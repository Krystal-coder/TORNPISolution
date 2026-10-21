using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// TestItem 分部：血压 A04 压力控制功能（BP-006~009，各项由 0x79/0x75 驱动、以 0x76/0x80 回读判定），含本模块专用的回读轮询与放气曲线分析。
    /// </summary>
    public partial class TestItem
    {
        /// <summary>
        /// 压力控制功能测试（BP-006~009）：四项均以产品回读压力（0x76）判定，未接产品/握手失败不做模拟仪空跑；产品通讯统一在 A16 初始化；充气一律由产品气泵完成，模拟仪只当手臂提供脉搏波。
        /// ① BP-006 最高充气压力 + ② BP-007 匀速放气控制：同一轮 0x79 完整测量，回读峰值判 ≤300mmHg、匀速段平均速率判 2~6mmHg/s（分段速率只展示）；
        /// ③ BP-008 快速放气功能：0x79 充气，回读达停止触发压力（或已从峰值回落）即发 0x79 status=1 关闭测量，从峰值降至终点压力耗时 ≤10s；
        /// ④ BP-009 过压保护：0x75 充气（目标压与超时取配置，到点产品底层自行放气），首次超响应起算压力到 0x80 过压状态置位的间隔 ≤15s、置位持续时长 ≤3s，触发点与峰值只记录。
        /// </summary>
        //测试项目**************************---A04---**************************压力控制功能测试（BP-006~009）
        public async Task PressureControlModule(TestItemResultCollector collector, CancellationToken token)
        {
            if (NoInstrumentDemo("压力控制功能测试")) return;
            var config = _configService.Load();
            if (config.ActiveSimulator == SimulatorType.None)
            {
                _log.Error("压力控制功能测试跳过：未选择血压模拟仪（仪器链接配置 → 模拟仪类型）");
                return;
            }

            using (var product = _productFactory())
            {
                var testParams = config.ActiveBpTestParams;

                // 产品不可用时四项结果一律不报告、由收尾判 FAIL：报 0 对“≤300mmHg”“≤10s”这类上限规格会误判成 PASS。
                var productAvailable = GetProductAvailable(config.NoProductDemoMode);

                // 经共享会话取模拟仪（接口调用：步骤只用接口成员，本模块只让它跑血压模拟）
                await using var lease = await _simSession.RentAsync(token);
                var simulator = lease.Simulator;

                if (!productAvailable)
                {
                    var pc0 = testParams.PressureControl;
                    if (!config.NoProductDemoMode)
                    {
                        _log.Error($"BP-006~009 判 FAIL：产品不可用（串口未连接或握手失败），四项均以产品回读压力判定，无法空跑；本轮不驱动模拟仪，仪器不进入血压模拟页面属预期");
                        return;
                    }

                    // 无产品演示模式：模拟仪只当手臂、不加压，没有充气方就无法复现充放气，只跑一轮血压模拟验证仪器侧能被驱动；四项结果均不报告，由收尾按读回缺失判 FAIL
                    _log.Info("BP-006~009 无产品演示模式：驱动模拟仪跑一轮血压模拟验证仪器侧，四项结果均不报告、由收尾判 FAIL");

                    var demoWatch = System.Diagnostics.Stopwatch.StartNew();
                    void OnDemoSample(double pressure) =>
                        PressureSampled?.Invoke(SpecItems.Bp006MaxInflatePressure, new PressureSamplePoint(demoWatch.Elapsed.TotalSeconds, pressure));

                    await simulator.StartSamplingAsync(token);
                    simulator.PressureSampled += OnDemoSample;
                    try
                    {
                        await simulator.RunBpMeasurementAsync((int)pc0.InflateSystolic, (int)pc0.InflateDiastolic,
                            (int)pc0.InflateHeartRate, pc0.InflatePulseVolume,
                            waitStartTimeoutMs: 8_000, totalTimeoutMs: 15_000, ct: token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _log.Warn($"演示步骤异常（不影响收尾判定）：{ex.Message}");
                    }
                    finally
                    {
                        simulator.PressureSampled -= OnDemoSample;
                        await simulator.StopSamplingAsync();
                    }
                    return;
                }

                // 分组执行包装：每组独立异常隔离，组内结果由方法自己边测边报（一次采集可报多项）；异常或未测得时不报告，由收尾判 FAIL
                async Task RunGroupAsync(string label, Func<Task> body, params string[] names)
                {
                    try
                    {
                        _log.Info($"步骤开始：{label}");
                        await body();
                        _log.Info($"步骤完成：{label}");
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"步骤失败：{label}，{ex.Message}；本组结果项不报告，由收尾判 FAIL：{string.Join("、", names)}");
                    }
                }

                try
                {
                    await simulator.StartSamplingAsync(token);

                    // BP-006/007 同一轮采集：一次充放气出两个结果，省掉一整轮重复充气
                    await RunGroupAsync("BP-006 最高充气压力 + BP-007 匀速放气控制",
                        () => Bp006007FullMeasureAsync(simulator, product, testParams, collector, token),
                        SpecItems.Bp006MaxInflatePressure, SpecItems.Bp007UniformDeflation);
                    await RunGroupAsync("BP-008 快速放气功能",
                        () => Bp008FastDeflationAsync(simulator, product, testParams, collector, token),
                        SpecItems.Bp008FastDeflation);
                    await RunGroupAsync("BP-009 过压保护",
                        () => Bp009OverpressureAsync(simulator, product, testParams, collector, token),
                        SpecItems.Bp009ResponseTime, SpecItems.Bp009ReliefDuration);
                }
                finally
                {
                    await simulator.StopSamplingAsync();
                }
            }
        }

        /// <summary>
        /// BP-006 最高充气压力 + BP-007 匀速放气控制（同一轮采集）：mode2 + 0x79 让产品自己充放气，模拟仪并行跑一轮血压模拟只提供脉搏波（不加压），轮询产品回读压力记录整条曲线。
        /// 回读峰值报 BP-006（≤300mmHg），峰值后匀速段平均速率报 BP-007（2~6mmHg/s，分段速率只展示不判定）；一轮没采到有效充气（峰值过低/样本过少）时两项都不报告，由收尾判 FAIL。
        /// </summary>
        private async Task Bp006007FullMeasureAsync(
            IBloodPressureSimulator simulator, IProductController product, BpaTestParams testParams,
            TestItemResultCollector collector, CancellationToken token)
        {
            var pc = testParams.PressureControl;
            var cuff = testParams.CuffTest;

            await product.SetTestModeAsync(2, ct: token);

            var samples = new List<(double T, double P)>();
            var watch = System.Diagnostics.Stopwatch.StartNew();

            // 200ms 轮询产品回读压力（0x76），采样与模拟仪血压模拟并行
            using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var pollTask = PollProductPressureAsync(product, samples, 200, watch, pollCts.Token);

            // 一条曲线同时是两项的证据，画给 BP-006（峰值就是这项的测试值）
            void OnSample(double pressure) =>
                PressureSampled?.Invoke(SpecItems.Bp006MaxInflatePressure, new PressureSamplePoint(watch.Elapsed.TotalSeconds, pressure));

            simulator.PressureSampled += OnSample;
            try
            {
                await product.StartBpMeasureAsync(true, token);
                await simulator.RunBpMeasurementAsync((int)pc.InflateSystolic, (int)pc.InflateDiastolic,
                    (int)pc.InflateHeartRate, pc.InflatePulseVolume,
                    cuff.WaitStartTimeoutSeconds * 1000, cuff.TotalTimeoutSeconds * 1000, token);

                // 放气尾部兜底：血压模拟曲线结束不等于放气到底，继续等回读降至采样结束压力或最长等待秒数，免得匀速段下半截（近 30mmHg）被截掉；轮询仍在跑
                var deflateDeadline = watch.Elapsed.TotalSeconds + pc.BP007MaxDeflateSeconds;
                while (watch.Elapsed.TotalSeconds < deflateDeadline)
                {
                    token.ThrowIfCancellationRequested();
                    double last;
                    lock (samples) last = samples.Count > 0 ? samples[^1].P : 0;
                    if (last <= pc.BP007DeflateEndPressure) break;
                    await Task.Delay(200, token);
                }
            }
            finally
            {
                // 先退订再收尾，每句各自兜异常：前一句抛了不能把后面几句顶掉，否则曲线还在往已结束的项画点、产品挂着本次测量（下次 0x79 开始不被接受）
                simulator.PressureSampled -= OnSample;
                pollCts.Cancel();
                try { await pollTask; }
                catch (Exception ex) { _log.Warn($"BP-006/007：压力轮询收尾异常：{ex.Message}"); }

                // 关闭本次测量（0x79 status=1），否则产品不接受下次开始；None 令牌保证停止路径也发得出去
                try { await product.StartBpMeasureAsync(false, CancellationToken.None); }
                catch (Exception ex) { _log.Warn($"BP-006/007：关闭本次测量（0x79 status=1）失败：{ex.Message}"); }
            }

            var analysis = AnalyzeDeflationCurve(samples, pc);
            if (analysis == null)
            {
                lock (samples) _log.Warn($"BP-006/007：产品回读 {samples.Count} 点，不足以定出峰值与匀速段，两项均不报告（由收尾判 FAIL）");
                return;
            }
            var (peak, rate, sampleCount, segmentDesc, slices) = analysis.Value;

            await collector.SetAsync(SpecItems.Bp006MaxInflatePressure, new TestItemResult
            {
                Value = Math.Round(peak, 1),
                InstrumentText = $"设定 {pc.InflateSystolic:0}/{pc.InflateDiastolic:0}",
                ProductText = $"回读峰值 {peak:0.0}",
            });
            _log.Info($"BP-006：产品回读峰值 {peak:0.0}mmHg（规格 ≤300）");

            await collector.SetAsync(SpecItems.Bp007UniformDeflation, new TestItemResult
            {
                Value = Math.Round(rate, 2),
                InstrumentText = $"匀速段 {segmentDesc}",
                ProductText = $"平均 {rate:0.00} mmHg/s",
                Points = slices.Count > 0 ? slices : null,
            });
            _log.Info($"BP-007：匀速段取 {sampleCount} 点，放气段 {segmentDesc}，平均放气速率 {rate:0.00} mmHg/s（规格 2~6，分段 {slices.Count} 段仅展示）");
        }

        /// <summary>
        /// BP-008 快速放气功能：mode2 + 0x79 让产品气泵自己充气（模拟仪只当手臂提供脉搏波、不加压），100ms 轮询 0x76，回读达停止触发压力（配置，文档值 260）或已从峰值回落指定幅度（产品自己转入放气的兜底）即发 0x79 status=1 关闭测量触发快速放气；
        /// 计时起点取回读峰值而非发停止的时刻（文档“从最高压降至15mmHg”），从峰值降至终点压力（配置，文档值 15mmHg）耗时 ≤10s；没形成有效充气则不报告，由收尾判 FAIL。
        /// </summary>
        private async Task Bp008FastDeflationAsync(
            IBloodPressureSimulator simulator, IProductController product, BpaTestParams testParams,
            TestItemResultCollector collector, CancellationToken token)
        {
            var pc = testParams.PressureControl;
            var cuff = testParams.CuffTest;

            await product.SetTestModeAsync(2, ct: token);
            await Task.Delay(500, token);

            var samples = new List<(double T, double P)>();
            var watch = System.Diagnostics.Stopwatch.StartNew();

            using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var pollTask = PollProductPressureAsync(product, samples, 100, watch, pollCts.Token);

            void OnSample(double pressure) =>
                PressureSampled?.Invoke(SpecItems.Bp008FastDeflation, new PressureSamplePoint(watch.Elapsed.TotalSeconds, pressure));

            simulator.PressureSampled += OnSample;

            // 模拟仪只当手臂：与产品测量并行跑一轮血压模拟提供脉搏波，发停止时连它一起取消
            using var simCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var simTask = Task.Run(() => simulator.RunBpMeasurementAsync((int)pc.InflateSystolic, (int)pc.InflateDiastolic,
                (int)pc.InflateHeartRate, pc.InflatePulseVolume,
                cuff.WaitStartTimeoutSeconds * 1000, cuff.TotalTimeoutSeconds * 1000, simCts.Token), simCts.Token);

            try
            {
                await product.StartBpMeasureAsync(true, token);

                // 阶段 1：等产品充气到停止触发压力，或产品已自行转入放气（从峰值回落指定幅度）
                var reached = false;
                while (watch.Elapsed.TotalSeconds < pc.BP008InflateWaitSeconds)
                {
                    token.ThrowIfCancellationRequested();
                    double last, max;
                    lock (samples)
                    {
                        last = samples.Count > 0 ? samples[^1].P : 0;
                        max = 0;
                        foreach (var s in samples) if (s.P > max) max = s.P;
                    }
                    if (last >= pc.BP008StopTriggerPressure || (max >= 60 && max - last >= pc.BP008PeakFallEscapeMmHg))
                    {
                        reached = true;
                        break;
                    }
                    await Task.Delay(100, token);
                }
                if (!reached)
                {
                    _log.Warn($"BP-008：{pc.BP008InflateWaitSeconds}s 内产品回读既未达停止触发压力 {pc.BP008StopTriggerPressure:0}mmHg、也未自行转入放气，本项不报告（由收尾判 FAIL）");
                    return;
                }

                // 发 0x79 status=1 关闭本次测量触发快速放气（无论如何都要发，否则产品不接受下次开始）
                await product.StartBpMeasureAsync(false, token);
                _log.Info($"BP-008：已发关闭测量（0x79 status=1）触发快速放气，等压力停止上升后从回读峰值计时至 ≤{pc.BP008EndPressure:0}mmHg");

                // 发停止后压力可能还涨一小截，先等它稳定再定峰值；随后从峰值时刻往后找首次降到终点的时刻
                await Task.Delay(1500, token);
                var deadline = watch.Elapsed.TotalSeconds + pc.BP008ZeroTimeoutSeconds;
                var peak = 0.0;
                var endAt = -1.0;
                var fastTime = 0.0;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var peakAt = 0.0;
                    lock (samples)
                    {
                        var peakIdx = 0;
                        for (var i = 1; i < samples.Count; i++)
                            if (samples[i].P > samples[peakIdx].P) peakIdx = i;
                        if (samples.Count > 0) { peak = samples[peakIdx].P; peakAt = samples[peakIdx].T; }
                        for (var i = peakIdx + 1; i < samples.Count; i++)
                            if (samples[i].P <= pc.BP008EndPressure) { endAt = samples[i].T; break; }
                    }
                    if (endAt >= 0) { fastTime = endAt - peakAt; break; }
                    if (watch.Elapsed.TotalSeconds >= deadline) break;
                    await Task.Delay(100, token);
                }

                if (peak < 60)
                {
                    _log.Warn($"BP-008：回读峰值仅 {peak:0.0}mmHg，未形成有效充气，本项不报告（由收尾判 FAIL）");
                    return;
                }

                await collector.SetAsync(SpecItems.Bp008FastDeflation, new TestItemResult
                {
                    // 超时未降到终点：测试值给到超时点（必然超规格判 FAIL），产品列写明是超时不是实测耗时
                    Value = Math.Round(endAt >= 0 ? fastTime : pc.BP008ZeroTimeoutSeconds, 2),
                    InstrumentText = $"触发 {pc.BP008StopTriggerPressure:0}mmHg",
                    ProductText = endAt >= 0
                        ? $"峰值 {peak:0.0} 降至 {pc.BP008EndPressure:0}mmHg 用时 {fastTime:0.00}s"
                        : $"峰值 {peak:0.0}mmHg，{pc.BP008ZeroTimeoutSeconds}s 内未降至 {pc.BP008EndPressure:0}mmHg",
                });
            }
            finally
            {
                // 逐句各自兜异常且用 None 令牌：点“停止”或上一句抛异常时后面的收尾照样要做完，否则产品还挂着本次测量（下次 0x79 开始不被接受）、模拟仪还停在血压模拟页面
                simulator.PressureSampled -= OnSample;
                pollCts.Cancel();
                try { await pollTask; }
                catch (Exception ex) { _log.Warn($"BP-008：压力轮询收尾异常：{ex.Message}"); }

                simCts.Cancel();
                try { await simTask; }
                catch (OperationCanceledException) { }
                catch (Exception ex) { _log.Warn($"BP-008：血压模拟任务收尾异常（中途关闭属预期）：{ex.Message}"); }
                try { await simulator.StopSimulationAsync(CancellationToken.None); }
                catch (Exception ex) { _log.Warn($"BP-008：停止血压模拟失败：{ex.Message}"); }
                try { await product.StartBpMeasureAsync(false, CancellationToken.None); }
                catch (Exception ex) { _log.Warn($"BP-008：关闭本次测量（0x79 status=1）失败：{ex.Message}"); }
            }

            _log.Info("BP-008：快速放气完成（规格 ≤10s）");
        }

        /// <summary>
        /// BP-009 过压保护：mode3 + 0x75 让产品气泵自己充到目标压（模拟仪只当手臂、不参与加压），100ms 并行轮询 0x76 回读压力与 0x80 过压状态。
        /// 判据只认 0x80：0x75 超时后产品底层同样会自行放气，把“压力骤降”当成保护动作的证据会让保护失效的产品拿到 PASS。
        /// 结果拆两行：① 响应时限 = 首次超压门限到 0x80 置位的间隔（≤15s），全程未置位则把观察总时长报出去（超规格必然 FAIL）；② 泄压时长 = 0x80 置位持续时长（≤3s），未置位时无法评估、不报告、由收尾判 FAIL。
        /// 两行的产品列都写真实峰值（不论 PASS/FAIL）；超压点/置位/回落/峰值四个关键时刻记在行详情，只记录不判定。
        /// </summary>
        private async Task Bp009OverpressureAsync(
            IBloodPressureSimulator simulator, IProductController product, BpaTestParams testParams,
            TestItemResultCollector collector, CancellationToken token)
        {
            var pc = testParams.PressureControl;
            var target = (ushort)Math.Clamp(pc.BP009InflateTargetPressure, 0, ushort.MaxValue);
            var timeout = (ushort)Math.Clamp(pc.BP009InflateTimeoutSeconds, 1, ushort.MaxValue);

            await product.SetTestModeAsync(3, ct: token);
            await Task.Delay(500, token);

            var samples = new List<(double T, double P)>();
            var states = new List<(double T, bool Over)>();
            var watch = System.Diagnostics.Stopwatch.StartNew();

            // 两条轮询共用一个产品串口：ProductController 内部互斥门把“清缓冲→发送→收帧”整段串行化，
            // 100ms × 2 路仍在单串口吞吐范围内（0x80 是实时状态，轮询间隔就是时长测量的精度）
            using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var pressTask = PollProductPressureAsync(product, samples, 100, watch, pollCts.Token);
            var stateTask = PollOverPressureStateAsync(product, states, 100, watch, pollCts.Token);

            void OnSample(double pressure) =>
                PressureSampled?.Invoke(SpecItems.Bp009ResponseTime, new PressureSamplePoint(watch.Elapsed.TotalSeconds, pressure));

            simulator.PressureSampled += OnSample;
            var elapsed = 0.0;
            try
            {
                // 基线核对：进本轮前 0x80 若已置位，说明上一轮状态没回干净，本轮时长会把残留算进来
                if (await product.ReadOverPressureStateAsync(token) == true)
                    _log.Warn("BP-009：充气前 0x80 已处于过压状态，本轮置位时长可能含上一轮残留，结果需人工核对");

                _log.Info($"BP-009：0x75 强制充气 目标 {target}mmHg / 最长 {timeout}s（判据：首次超 {pc.BP009ResponseStartPressure:0}mmHg 起算，只认 0x80 置位）");
                await product.StartInflateAsync(target, timeout, token);

                // 观察窗 = 充气超时上限 + 15s 泄压余量（超时后产品底层自行放气）；已置位且已回落、回落后再多看 2s 就提前收尾
                var windowSeconds = timeout + 15.0;
                while (watch.Elapsed.TotalSeconds < windowSeconds)
                {
                    token.ThrowIfCancellationRequested();
                    var (on0, off0) = ReadOverPressureWindow(states);
                    if (on0 >= 0 && off0 >= 0 && watch.Elapsed.TotalSeconds - off0 >= 2) break;
                    await Task.Delay(100, token);
                }
                elapsed = watch.Elapsed.TotalSeconds;
            }
            finally
            {
                // 先退订再收尾，每句各自兜异常且用 None 令牌：中断时也要停掉两条轮询、退出模式、把残压泄掉
                simulator.PressureSampled -= OnSample;
                pollCts.Cancel();
                try { await pressTask; }
                catch (Exception ex) { _log.Warn($"BP-009：压力轮询收尾异常：{ex.Message}"); }
                try { await stateTask; }
                catch (Exception ex) { _log.Warn($"BP-009：过压状态轮询收尾异常：{ex.Message}"); }

                // 退出静态压力模式（0x73 mode=3 status=1），与 BP-003 同一口径，不让产品带着模式进下一个模块
                try { await product.SetTestModeAsync(3, 1, CancellationToken.None); }
                catch (Exception ex) { _log.Warn($"BP-009：退出静态压力模式（0x73 mode=3 status=1）失败：{ex.Message}"); }

                // 清场：保护未必动作，气路可能还顶着高压，用模拟仪快排阀泄掉残压，别让后续模块带压
                try
                {
                    await simulator.SetFastValveAsync(true, CancellationToken.None);
                    await Task.Delay(1500, CancellationToken.None);
                    await simulator.SetFastValveAsync(false, CancellationToken.None);
                }
                catch (Exception ex) { _log.Warn($"BP-009：清场泄压失败：{ex.Message}"); }
            }

            if (samples.Count == 0)
            {
                _log.Warn($"BP-009：产品回读 0 点（0x76 无响应），两条判据均无法评估，两项都不报告（由收尾判 FAIL）");
                return;
            }

            var (onAt, offAt) = ReadOverPressureWindow(states);
            var peak = 0.0;
            var peakAt = 0.0;
            var overAt = -1.0;
            lock (samples)
            {
                foreach (var s in samples)
                    if (s.P > peak) { peak = s.P; peakAt = s.T; }
                foreach (var s in samples)
                    if (s.P >= pc.BP009ResponseStartPressure) { overAt = s.T; break; }
            }
            var triggerPressure = PressureAt(samples, onAt);

            // 关键时刻只记录不判定：挂在先报告的那一行上（② 在未置位时不报告，事件得有落脚处）
            var events = new List<TestPointResult>();
            void AddEvent(string label, double t, string remark)
            {
                var p = PressureAt(samples, t);
                if (t < 0 || double.IsNaN(p)) return;
                events.Add(new TestPointResult
                {
                    Name = label,
                    Schema = TestPointSchemas.Event,
                    Unit = "mmHg",
                    ShowVerdict = false,
                    Cells = new List<string> { t.ToString("0.0"), p.ToString("0.0"), remark },
                });
            }
            AddEvent("首次超压", overAt, $"首次 ≥{pc.BP009ResponseStartPressure:0}mmHg（响应时限起点）");
            AddEvent("0x80 置位", onAt, "过压状态置位（保护动作）");
            AddEvent("0x80 回落", offAt, "过压状态回落（保护解除）");
            AddEvent("回读峰值", peakAt, "本轮充气最高压");
            List<TestPointResult>? eventPoints = events.Count > 0 ? events : null;

            // ① 响应时限
            if (onAt >= 0 && overAt >= 0)
            {
                var responseSeconds = onAt - overAt;
                await collector.SetAsync(SpecItems.Bp009ResponseTime, new TestItemResult
                {
                    Value = Math.Round(responseSeconds, 2),
                    InstrumentText = $"0x75 {target}mmHg/{timeout}s",
                    ProductText = $"超 {pc.BP009ResponseStartPressure:0}mmHg 起 {responseSeconds:0.00}s 置位，触发点 {triggerPressure:0.0}mmHg，峰值 {peak:0.0}mmHg",
                    Points = eventPoints,
                });
                eventPoints = null;
                _log.Info($"BP-009①：首次超 {pc.BP009ResponseStartPressure:0}mmHg（{overAt:0.0}s）到 0x80 置位（{onAt:0.0}s）间隔 {responseSeconds:0.00}s（规格 ≤15）");
            }
            else if (onAt < 0)
            {
                // 全程未置位 = 保护没动作：观察总时长就是这项的测试值（超规格必然 FAIL），峰值照实报出
                await collector.SetAsync(SpecItems.Bp009ResponseTime, new TestItemResult
                {
                    Value = Math.Round(elapsed, 1),
                    InstrumentText = $"0x75 {target}mmHg/{timeout}s",
                    ProductText = $"观察 {elapsed:0.0}s 全程 0x80 未置位，峰值 {peak:0.0}mmHg（未超 {pc.BP009ResponseStartPressure:0}mmHg）",
                    Points = eventPoints,
                });
                eventPoints = null;
                _log.Warn($"BP-009①：充气观察 {elapsed:0.0}s 全程 0x80 未置位（过压保护未动作），回读峰值 {peak:0.0}mmHg，按观察总时长判 FAIL");
            }
            else
                _log.Warn($"BP-009①：0x80 已置位但回读全程未超 {pc.BP009ResponseStartPressure:0}mmHg（峰值 {peak:0.0}），起点定不出，响应时限不报告（由收尾判 FAIL）");

            // ② 泄压时长 = 0x80 置位持续时长（产品自己报告的保护状态窗口）
            if (onAt >= 0)
            {
                var relief = offAt >= 0 ? offAt - onAt : elapsed - onAt;
                // 文档判据①的“超压”工况是回读真到过 330：峰值没上去时置位仍成立，但时长仅供参考（默认 260 就是这种情形）
                var note = peak >= pc.BP009SustainedPressure ? ""
                    : $"（峰值未达 {pc.BP009SustainedPressure:0}mmHg，非文档超压工况，仅供参考）";
                await collector.SetAsync(SpecItems.Bp009ReliefDuration, new TestItemResult
                {
                    Value = Math.Round(relief, 2),
                    InstrumentText = "0x80 轮询 100ms",
                    ProductText = (offAt >= 0
                        ? $"置位 {onAt:0.0}s→回落 {offAt:0.0}s，触发点 {triggerPressure:0.0}mmHg，峰值 {peak:0.0}mmHg"
                        : $"观察至 {elapsed:0.0}s 仍未回落，触发点 {triggerPressure:0.0}mmHg，峰值 {peak:0.0}mmHg") + note,
                    Points = eventPoints,
                });
                _log.Info($"BP-009②：0x80 置位持续 {relief:0.00}s（规格 ≤3）{note}");
            }
            else
                _log.Warn("BP-009②：0x80 全程未置位，泄压时长无法评估，不报告（由收尾判 FAIL）");
        }

        /// <summary>后台轮询产品回读压力（0x76）：按 intervalMs 间隔采样入列（时间=秒表秒），单次回读失败不中断采样；取消即退出。</summary>
        private static async Task PollProductPressureAsync(
            IProductController product, List<(double T, double P)> samples,
            int intervalMs, System.Diagnostics.Stopwatch watch, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var p = await product.ReadProductPressureAsync(ct);
                    if (!double.IsNaN(p))
                        lock (samples) samples.Add((watch.Elapsed.TotalSeconds, p));
                }
                catch (OperationCanceledException) { break; }
                catch { }

                try { await Task.Delay(intervalMs, ct); } catch (OperationCanceledException) { break; }
            }
        }

        /// <summary>
        /// BP-006/007 同一轮曲线拆解：回读峰值（BP-006 测试值）+ 峰值后匀速放气段的平均速率（BP-007 测试值），匀速段取配置区间（默认 130~30mmHg）内样本、首尾点斜率即平均速率，区间内样本不足时退化为“峰值−5 至峰值 20%”，再按 BP007RateSliceMmHg 切段给分段速率（只展示不判定）。
        /// 没形成有效充气（峰值过低/样本过少/定不出放气段）返回 null，调用方两项都不报告。
        /// </summary>
        private static (double Peak, double Rate, int SampleCount, string SegmentDesc, List<TestPointResult> Slices)?
            AnalyzeDeflationCurve(List<(double T, double P)> samples, BpaPressureControlParams pc)
        {
            lock (samples)
            {
                if (samples.Count < 4) return null;

                var peakIdx = 0;
                for (var i = 1; i < samples.Count; i++)
                    if (samples[i].P > samples[peakIdx].P) peakIdx = i;
                var peak = samples[peakIdx].P;
                if (peak < 60) return null;      // 没形成有效充气，峰值与放气速率都没有意义

                // 匀速段：优先用配置区间，区间内样本太少（不足 4 点或跨度不足 20mmHg）就退化成峰值的相对区间
                var high = Math.Min(pc.BP007RateSegmentHigh, peak - 5);
                var low = Math.Max(pc.BP007RateSegmentLow, 10);
                var segment = samples.Skip(peakIdx).Where(s => s.P <= high && s.P >= low).ToList();
                if (segment.Count < 4 || high - low < 20)
                {
                    high = peak - 5;
                    low = Math.Max(10, peak * 0.2);
                    segment = samples.Skip(peakIdx).Where(s => s.P <= high && s.P >= low).ToList();
                }
                if (segment.Count < 2) return null;

                var (t0, p0) = segment[0];
                var (t1, p1) = segment[^1];
                if (t1 <= t0) return null;

                var rate = Math.Abs(p0 - p1) / (t1 - t0);
                return (peak, rate, segment.Count, $"{p0:0.0}→{p1:0.0} mmHg", BuildRateSlices(segment, pc.BP007RateSliceMmHg));
            }
        }

        /// <summary>匀速段按固定压力跨度切段，逐段给速率/时长/压力区间（文档只说“匀速”、未量化“稳定”，所以分段只展示不判定）；最多 30 段，避免切段跨度配得过小时行数过多。</summary>
        private static List<TestPointResult> BuildRateSlices(List<(double T, double P)> segment, double sliceMmHg)
        {
            var rows = new List<TestPointResult>();
            if (segment.Count < 2 || sliceMmHg <= 0) return rows;

            var bottom = segment[^1].P;
            for (var segHigh = segment[0].P; segHigh - sliceMmHg >= bottom; segHigh -= sliceMmHg)
            {
                if (rows.Count >= 30) break;
                var segLow = segHigh - sliceMmHg;
                var pts = segment.Where(s => s.P <= segHigh && s.P >= segLow).ToList();
                if (pts.Count < 2 || pts[^1].T <= pts[0].T) continue;
                var r = (pts[0].P - pts[^1].P) / (pts[^1].T - pts[0].T);
                rows.Add(new TestPointResult
                {
                    Name = $"{segHigh:0}→{segLow:0}",
                    Schema = TestPointSchemas.Slice,
                    Unit = "mmHg/s",
                    ShowVerdict = false,
                    Cells = new List<string>
                    {
                        r.ToString("0.00"),
                        (pts[^1].T - pts[0].T).ToString("0.0"),
                        $"{pts[0].P:0.0}→{pts[^1].P:0.0}",
                    },
                });
            }
            return rows;
        }

        /// <summary>后台轮询产品过压状态（0x80）：按 intervalMs 采样入列（时间=秒表秒），单次无响应不中断采样（空列 = 产品不支持本命令，由调用方按无法评估处理）；取消即退出。</summary>
        private static async Task PollOverPressureStateAsync(
            IProductController product, List<(double T, bool Over)> states,
            int intervalMs, System.Diagnostics.Stopwatch watch, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var over = await product.ReadOverPressureStateAsync(ct);
                    if (over.HasValue)
                        lock (states) states.Add((watch.Elapsed.TotalSeconds, over.Value));
                }
                catch (OperationCanceledException) { break; }
                catch { }

                try { await Task.Delay(intervalMs, ct); } catch (OperationCanceledException) { break; }
            }
        }

        /// <summary>从 0x80 采样列取首次置位时刻与置位后首次回落时刻（未置位/未回落返回 -1）</summary>
        private static (double OnAt, double OffAt) ReadOverPressureWindow(List<(double T, bool Over)> states)
        {
            lock (states)
            {
                var onAt = -1.0;
                var offAt = -1.0;
                for (var i = 0; i < states.Count; i++)
                {
                    if (onAt < 0)
                    {
                        if (states[i].Over) onAt = states[i].T;
                    }
                    else if (!states[i].Over) { offAt = states[i].T; break; }
                }
                return (onAt, offAt);
            }
        }

        /// <summary>取回读曲线上 t 时刻（含）之前最后一点的压力；t&lt;0 或无样本返回 NaN</summary>
        private static double PressureAt(List<(double T, double P)> samples, double t)
        {
            if (t < 0) return double.NaN;
            var p = double.NaN;
            lock (samples)
                foreach (var s in samples)
                {
                    if (s.T > t) break;
                    p = s.P;
                }
            return p;
        }
    }
}
