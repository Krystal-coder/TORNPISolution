using System.Collections.Concurrent;
using TORNPISolution.Interface;
using TORNPISolution.Service.Devices;
using TORNPISolution.Service.Hardware;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// TestItem 分部：血压类测试模块 M01 初始化连接 / M03 产品充放气 / M05 静态压力精度 /
    /// M06 动态压力精度 / M04 压力控制功能（BP-006~008）。
    /// 模块执行包装与分派见 TestItem.cs 主文件，项名统一引用 <see cref="SpecItems"/>。
    /// </summary>
    public partial class TestItem
    {
        /// <summary>
        /// 血压仪初始化连接：按仪器链接配置中选择的血压模拟仪初始化（经共享会话租约，懒建立）。
        /// 会话未建立时租约首次打开即真实连接测试；已建立时发复位等回显做活性校验；
        /// 未选择模拟仪 → FAIL。连接成功写 1，失败/异常保持 0。
        /// </summary>
        //测试项目**************************---M01---**************************血压仪初始化连接
        public async Task BpDeviceInitModule(TestItemResultCollector collector, CancellationToken token)
        {
            if (NoInstrumentDemo("血压模拟仪初始化连接")) return;
            var config = _configService.Load();
            if (config.ActiveSimulator == SimulatorType.None)
            {
                _log.Error("血压仪初始化连接失败：未选择血压模拟仪（仪器链接配置 → 模拟仪类型）");
                return;
            }

            // 会话已建立：对共享连接做真实活性校验；未建立：租约懒打开（打开本身含复位等回显，即真实连接测试）
            if (_simSession.IsOpen)
            {
                await _simSession.VerifyConnectionAsync(token);
                _log.Info("血压模拟仪复用方案共享会话，连接校验通过");
            }
            else
            {
                await using var lease = await _simSession.RentAsync(token);
                if (lease.Simulator is BPA700BetaSimulator bpa)
                    await bpa.ApplySettingsAsync(
                        config.BPA700Simulator.Category,
                        config.BPA700Simulator.PressureUnit,
                        config.BPA700Simulator.AirReservoir,
                        token);
                else
                    _log.Info("康伟达 NIBP 模拟仪初始化成功");
            }

            await collector.SetAsync(SpecItems.BpSimulatorInit, TestItemResult.Of(1));
        }


        /// <summary>
        /// 产品充放气测试（BP-001 物理按键启动测量 / BP-002 测量过程中手动停止）：
        /// 产品通讯（进入产测模式/0x72/0x74）集中在 M16 产品通讯初始化，本模块不再问询；
        /// ① BP-001：经接口 RunBpMeasurementAsync 进入待测（血压/心率参数取血压模拟仪测试配置→当前启用设备→动态血压，
        ///    按仪器链接配置选择的模拟仪路由），提示测试员按产品物理[开始]键，
        ///    状态依次经历 Busy→Stable→Finish 为 PASS（BPA700=SDK 回调状态机；康伟达=AA 压力曲线判定），
        ///    状态一直停留在 Wait 说明产品未启动或气路未连通；
        /// ④ BP-002：同样进入待测，按[开始]键充气后于充气中按[停止]键，
        ///    状态变 Idle 并快速放气，测试值=快速放气时间（≤配置上限为 PASS，
        ///    上限在血压仪参数配置→充放气规格中设置）。超时/放气规格均取充放气规格配置。
        /// 充放气全程开启压力采样，压力曲线在 UI 实时展示（需连接储气瓶）。
        /// </summary>
        //测试项目**************************---M03---**************************产品充放气测试（BP-001/BP-002）
        public async Task BpAccuracyTestModule(TestItemResultCollector collector, CancellationToken token)
        {
            if (NoInstrumentDemo("产品充放气测试")) return;
            var config = _configService.Load();

            // 未选择模拟仪才跳过；康伟达/BPA700 均经接口 RunBpMeasurementAsync 判定
            if (config.ActiveSimulator == SimulatorType.None)
            {
                _log.Error("产品充放气测试跳过：未选择血压模拟仪（仪器链接配置 → 模拟仪类型）");
                return;
            }

            using (var product = _productFactory())
            {
                // 产品可用性以本轮 M16 产品通讯初始化记录为准（此处不再重复 0x72/0x74 问询）；
                // 产品不可用仅警告，不阻断后面的仪器控制流程（无产品也能调试 BPA700 侧逻辑）
                var cuff = config.ActiveBpTestParams.CuffTest;
                if (!GetProductAvailable(config.NoProductDemoMode))
                    _log.Warn($"产品不可用：BP-001/002 仍将等待产品物理按键（最长 {cuff.WaitStartTimeoutSeconds} 秒），模拟仪会停留在 Wait，两项判 FAIL，可随时点“停止”结束");

                // 经共享会话租约取模拟仪（接口调用：BPA700=SDK 回调状态机；康伟达=压力曲线判定）
                await using var lease = await _simSession.RentAsync(token);
                var simulator = lease.Simulator;

                // ③④ 血压参数取血压模拟仪测试配置（当前启用设备的动态血压 Default 参数）
                var dyn = config.ActiveBpTestParams.DynamicBloodPressure;
                var systolic = (int)dyn.Systolic;
                var diastolic = (int)dyn.Diastolic;
                var heartRate = dyn.HeartRate;

                // ③ BP-001 物理按键启动测量：限时等待测试员按产品[开始]键
                var bp1Wave = System.Diagnostics.Stopwatch.StartNew();
                void OnBp001Sample(double pressure) =>
                    PressureSampled?.Invoke(SpecItems.Bp001KeyStart,
                        new PressureSamplePoint(bp1Wave.Elapsed.TotalSeconds, pressure));

                BpTestRunResult bp1;
                try
                {
                    simulator.PressureSampled += OnBp001Sample;
                    await simulator.StartSamplingAsync(token);
                    _log.Info($"BP-001：请按下产品物理[开始]键启动测量（最长等待 {cuff.WaitStartTimeoutSeconds} 秒）...");
                    bp1 = await simulator.RunBpMeasurementAsync(systolic, diastolic, heartRate,
                        cuff.WaitStartTimeoutSeconds * 1000, cuff.TotalTimeoutSeconds * 1000, token);
                }
                finally
                {
                    await simulator.StopSamplingAsync();
                    simulator.PressureSampled -= OnBp001Sample;
                }

                if (bp1.CompletedFullSequence)
                {
                    await collector.SetAsync(SpecItems.Bp001KeyStart, TestItemResult.Of(1));
                    _log.Info($"BP-001 PASS：status 依次经历 Busy→Stable→Finish（峰值={bp1.PeakPressure:0.0} 总时间={bp1.TotalTime:0.0}s）");
                }
                else
                {
                    await collector.SetAsync(SpecItems.Bp001KeyStart, TestItemResult.Of(0));
                    _log.Warn($"BP-001 FAIL：status 序列 [{string.Join("→", bp1.StatusSequence)}] 未经历 Busy→Stable→Finish（最终 status={bp1.FinalStatus}，产品未启动或气路未连通）");
                }

                // ④ BP-002 测量过程中手动停止：按[开始]键充气后，充气中按[停止]键
                var bp2Wave = System.Diagnostics.Stopwatch.StartNew();
                void OnBp002Sample(double pressure) =>
                    PressureSampled?.Invoke(SpecItems.Bp002ManualStop,
                        new PressureSamplePoint(bp2Wave.Elapsed.TotalSeconds, pressure));

                BpTestRunResult bp2;
                try
                {
                    simulator.PressureSampled += OnBp002Sample;
                    await simulator.StartSamplingAsync(token);
                    _log.Info($"BP-002：请先按产品[开始]键，充气过程中再按[停止]键（启动最长等待 {cuff.WaitStartTimeoutSeconds} 秒）...");
                    bp2 = await simulator.RunBpMeasurementAsync(systolic, diastolic, heartRate,
                        cuff.WaitStartTimeoutSeconds * 1000, cuff.TotalTimeoutSeconds * 1000, token);
                }
                finally
                {
                    await simulator.StopSamplingAsync();
                    simulator.PressureSampled -= OnBp002Sample;
                }

                if (bp2.FinalStatus == 0)   // Idle：BPA700 收到产品停止，已快速放气
                {
                    // 动态规格：放气时间 0~配置上限（血压仪参数配置→充放气规格），≤上限视为放气完成
                    await collector.SetAsync(SpecItems.Bp002ManualStop, TestItemResult.WithDynamicSpec(Math.Round(bp2.DeflationTime, 2), 0, cuff.MaxDeflationSeconds));
                    _log.Info($"BP-002：快速放气时间 {bp2.DeflationTime:0.0}s（规格 ≤{cuff.MaxDeflationSeconds:0.#}s）");
                }
                else
                {
                    await collector.SetAsync(SpecItems.Bp002ManualStop, TestItemResult.Of(0));
                    _log.Warn($"BP-002 FAIL：未收到 status=Idle（最终 status={bp2.FinalStatus}），产品未按[停止]键手动停止");
                }
            }
        }

        /// <summary>
        /// 血压静态压力精度测试（BP-003）：
        /// 产品通讯集中在 M16 产品通讯初始化，本模块不再问询；
        /// ① 逐点加压（压力点/稳压时长/充气方式取血压模拟仪测试配置→当前启用设备→静态压力，
        ///    仅勾选的点参与），经模拟仪接口 RunStaticPressureSequenceAsync 执行：
        ///    BPA700=SDK 序列；康伟达=逐点压力源+AA 回读收敛；
        ///    每点到达稳压保持时（PressurePointStable 事件）并发回读产品上报压力（0x7E），
        ///    保证产品值取自稳压保持窗内（与仪器稳定值同时刻口径）；
        /// ③ 逐点判定 |产品上报值 − 仪器稳压值| ≤2mmHg，子节点（测试点1..N）随父项结果回传 UI 子表；
        ///    父项显示最差点的双值（仪器列=标准值，产品列=上报值，动态规格=标准值±2mmHg）；
        ///    产品未连接/无上报的点判 FAIL（不再假合格），任一点 FAIL 父项即 FAIL。
        /// 全程开启压力采样，静态压力曲线在 UI 实时展示（波形共用一张）。
        /// </summary>
        //测试项目**************************---M05---**************************血压静态压力精度测试（BP-003）
        public async Task BpStaticAccuracyModule(TestItemResultCollector collector, CancellationToken token)
        {
            if (NoInstrumentDemo("血压静态压力精度测试")) return;
            var config = _configService.Load();
            if (config.ActiveSimulator is not (SimulatorType.BPA700 or SimulatorType.Kangwenda))
            {
                _log.Error("血压静态压力精度测试跳过：未选择血压模拟仪（仪器链接配置 → 模拟仪类型）");
                return;
            }

            // 静态压力点按仪器链接配置选择的模拟仪路由读取（康伟达读康伟达节点、BPA700 读 BPA700 节点）
            var sp = config.ActiveBpTestParams.StaticPressure;
            var points = sp.Points.Where(p => p.Enabled).Select(p => p.Value).ToList();
            if (points.Count == 0)
            {
                _log.Warn($"BP-003 跳过：未勾选任何静态压力点（血压模拟仪测试配置 → {(config.ActiveSimulator == SimulatorType.Kangwenda ? "康伟达" : "BPA700")} → 静态压力）");
                return;
            }

            using (var product = _productFactory())
            {
                // 产品可用性以本轮 M16 产品通讯初始化记录为准（此处不再重复 0x72/0x74 问询）
                var productAvailable = GetProductAvailable(config.NoProductDemoMode);

                // 经共享会话租约取模拟仪（接口调用，BPA700=Beta SDK 序列 API；康伟达=逐点压力源+AA 收敛）
                await using var lease = await _simSession.RentAsync(token);
                var simulator = lease.Simulator;

                // ② 逐点静态加压，全程采样画曲线
                var waveWatch = System.Diagnostics.Stopwatch.StartNew();
                void OnSample(double pressure) =>
                    PressureSampled?.Invoke(SpecItems.Bp003StaticAccuracy,
                        new PressureSamplePoint(waveWatch.Elapsed.TotalSeconds, pressure));

                var pointValues = points.ToArray();
                var productValues = new double?[pointValues.Length];
                var readTasks = new ConcurrentBag<Task>();

                // 逐点稳定回调：在稳压保持窗内并发回读产品上报值（不阻塞仪器回调/序列线程）
                void OnPointStable(int index, double stable)
                {
                    if (!productAvailable || index < 0 || index >= productValues.Length) return;
                    readTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            productValues[index] = await product.ReadProductPressureAsync(token);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _log.Warn($"BP-003 测试点{index + 1} 产品上报值读失败：{ex.Message}");
                        }
                    }, token));
                }

                try
                {
                    simulator.PressureSampled += OnSample;
                    simulator.PressurePointStable += OnPointStable;
                    await simulator.StartSamplingAsync(token);

                    // 产品可用时才设置产品测试模式（0x73 mode=3）并读取产品上压力值
                    if (productAvailable)
                        await product.SetTestModeAsync(3, ct: token);
                    else
                        _log.Warn("BP-003：产品未连接或握手失败，各压力点将按“无产品上报值”判 FAIL");

                    // 一次性调用序列 API（接口成员，各实现自行逐点/一次下发），返回每个点的实际稳定压力值
                    var stablePressures = await simulator.RunStaticPressureSequenceAsync(
                        pointValues, sp.MeasureSeconds, sp.BpaInflation, token);

                    // 等保持窗内发起的产品回读收尾（正常单次串口往返远短于稳压保持时长）
                    await Task.WhenAll(readTasks);

                    // ③ 逐点判定 + 子节点 + 父项最差点双值
                    await ReportStaticAccuracyAsync(collector, pointValues, stablePressures, productValues);
                }
                finally
                {
                    await simulator.StopSamplingAsync();
                    simulator.PressureSampled -= OnSample;
                    simulator.PressurePointStable -= OnPointStable;
                }
            }
        }

        /// <summary>
        /// BP-003 逐点判定与上报：每点 |产品上报值 − 仪器稳压值| ≤2mmHg 为 PASS；
        /// 子节点（测试点1..N）随父项结果回传，UI 在 BP-003 行下子表逐点展示；
        /// 父项按最差点误差（FAIL 点优先，同类取误差最大）判定，双值列留空，任一点 FAIL 父项即 FAIL。
        /// </summary>
        private async Task ReportStaticAccuracyAsync(TestItemResultCollector collector,
            double[] pointValues, double[] stablePressures, double?[] productValues)
        {
            const double Tolerance = 2;   // mmHg，测试要求文档固定
            var pointResults = new List<TestPointResult>();
            TestPointResult? worst = null;

            for (var i = 0; i < pointValues.Length; i++)
            {
                var standard = stablePressures[i];
                var hasInstrument = !double.IsNaN(standard);
                var hasProduct = productValues[i].HasValue && !double.IsNaN(productValues[i]!.Value);
                var err = hasInstrument && hasProduct ? Math.Abs(productValues[i]!.Value - standard) : (double?)null;
                var pass = err.HasValue && err.Value <= Tolerance;
                var remark = pass
                    ? string.Empty
                    : !hasProduct ? "无产品上报值（产品未连接或无响应）"
                    : !hasInstrument ? "仪器未稳压到位"
                    : $"误差超过±{Tolerance:0}mmHg";

                var pr = new TestPointResult
                {
                    Name = $"测试点{i + 1}",
                    TargetPressure = pointValues[i],
                    InstrumentValue = hasInstrument ? standard : null,
                    ProductValue = hasProduct ? productValues[i] : null,
                    Error = err,
                    Pass = pass,
                    Remark = remark
                };
                pointResults.Add(pr);
                _log.Info($"BP-003 {pr.Name}：目标={pointValues[i]:0.#} 仪器稳压={pr.InstrumentText} 产品上报={pr.ProductText} 误差={pr.ErrorText} → {(pass ? "PASS" : "FAIL（" + remark + "）")}");

                // 最差点选择：FAIL 点优先，同类取误差大者（无误差的 FAIL 点取先出现的）
                if (worst == null
                    || (!pass && worst.Pass)
                    || (!pass == !worst.Pass && (err ?? 0) > (worst.Error ?? 0)))
                {
                    worst = pr;
                }
            }

            if (worst == null) return;

            // 父项：主值=最差点误差（规格为静态误差窗 -2~2mmHg），双值列不放值（逐点双值子表已展示）；
            // 无产品上报的 FAIL 点无误差，主值落到规格外保证判 FAIL
            var value = worst.Error ?? Tolerance + 1;
            await collector.SetAsync(SpecItems.Bp003StaticAccuracy, new TestItemResult
            {
                Value = value,
                Points = pointResults
            });
        }

        /// <summary>
        /// 血压动态压力精度测试（BP-004 血压精度/重复性 + BP-005 心率精度/重复性）：
        /// 产品通讯集中在 M16 产品通讯初始化，本模块不再问询；
        /// ① 经接口 RunBpMeasurementAsync 按测试要求固定 4 组参数（90/60、120/80、160/100、200/120 mmHg，
        ///    心率 60/80/100/120 BPM 同轮下发），每组重复 3 次完整充放气周期（需储气瓶产生脉搏波）；
        /// ② 每轮先发开始测量（0x79）产品才会自行充放气，模拟仪输出血压模拟波形，
        ///    产品测量结束后主动轮询 0x7B 拿收缩压/舒张压/平均压（实测当前固件不推送），心率由 0x7C 单独读回；
        ///    每轮结束发一次关闭测量（0x79 status=1），否则产品不接受下次开始：
        ///    精度（系统偏差）= 各组 |均值−设定值| 的最大值（血压 ≤3mmHg / 心率 ≤max(5,设定×5%)）；
        ///    重复性误差 = 各组 |最大−最小| 极差的最大值（血压 ≤2mmHg / 心率 ≤max(5,设定×5%)）；
        ///    每组“模拟仪设定值 vs 产品回读”随各结果项行内子表展示（分组行：设定/均值/最大/最小/偏差/极差/判定）。
        /// </summary>
        //测试项目**************************---M06---**************************血压动态压力精度测试（BP-004/BP-005）
        public async Task BpDynamicAccuracyModule(TestItemResultCollector collector, CancellationToken token)
        {
            if (NoInstrumentDemo("血压动态压力精度测试")) return;
            var config = _configService.Load();
            if (config.ActiveSimulator == SimulatorType.None)
            {
                _log.Error("血压动态压力精度测试跳过：未选择血压模拟仪（仪器链接配置 → 模拟仪类型）");
                return;
            }

            // BP-004/005 测试组（测试要求文档固定）：血压设定值与心率设定值同一轮下发同步执行
            var groups = new (int Systolic, int Diastolic, int HeartRate)[]
            {
                (90, 60, 60), (120, 80, 80), (160, 100, 100), (200, 120, 120)
            };
            const int repeats = 3;

            using (var product = _productFactory())
            {
                // 产品可用性以本轮 M16 产品通讯初始化记录为准（此处不再重复 0x72/0x74 问询）
                var productAvailable = GetProductAvailable(config.NoProductDemoMode);

                // 经共享会话租约取模拟仪（接口调用：BPA700=SDK 回调状态机；康伟达=压力曲线判定）
                await using var lease = await _simSession.RentAsync(token);
                var simulator = lease.Simulator;

                if (!productAvailable)
                {
                    if (!config.NoProductDemoMode)
                    {
                        _log.Warn("BP-004/005 跳过测量轮次：产品不可用，且全部结果项依赖产品上报值（协议预留），空跑无意义；本轮不会向模拟仪发血压模拟指令，仪器不进入血压模拟页面属预期，请先检查产品串口接线/端口配置");
                        return;
                    }

                    // 无产品演示模式：逐组驱动模拟仪走一遍血压模拟流程（验证仪器侧指令与波形显示）；
                    // 无产品对比值：结果项不报告，由收尾扫描按读回缺失统一判 FAIL
                    _log.Info("BP-004/005 无产品演示模式：逐组驱动模拟仪走血压模拟流程（短超时），结果项按产品读回缺失判 FAIL");
                    await simulator.StartSamplingAsync(token);
                    var demoWave = System.Diagnostics.Stopwatch.StartNew();
                    void OnDemoSample(double pressure) =>
                        PressureSampled?.Invoke(SpecItems.Bp004SystolicAccuracy,
                            new PressureSamplePoint(demoWave.Elapsed.TotalSeconds, pressure));
                    simulator.PressureSampled += OnDemoSample;
                    try
                    {
                        foreach (var g in groups)
                        {
                            _log.Info($"演示轮：组 SYS={g.Systolic} DIA={g.Diastolic} HR={g.HeartRate} 下发血压模拟参数并等测量曲线（短超时）...");
                            await simulator.RunBpMeasurementAsync(g.Systolic, g.Diastolic, g.HeartRate,
                                waitStartTimeoutMs: 8_000, totalTimeoutMs: 15_000, ct: token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _log.Warn($"演示轮异常（不影响收尾判定）：{ex.Message}");
                    }
                    finally
                    {
                        simulator.PressureSampled -= OnDemoSample;
                        await simulator.StopSamplingAsync();
                    }

                    return;
                }

                // ② 设置血压测试模式（0x73 mode=2）
                await product.SetTestModeAsync(2, ct: token);
                
                // 波形采样：模拟仪实时压力点上抛到 "BP-004收缩压精度测试" 行显示充放气压力曲线（全组共用一条时间轴）
                var bp4Wave = System.Diagnostics.Stopwatch.StartNew();
                void OnBp004Sample(double pressure) =>
                    PressureSampled?.Invoke(SpecItems.Bp004SystolicAccuracy,
                        new PressureSamplePoint(bp4Wave.Elapsed.TotalSeconds, pressure));
                simulator.PressureSampled += OnBp004Sample;
                try
                {
                    // ③ 逐组逐次执行：每组 3 次完整充放气周期，每次结束后读产品上报结果
                    var readings = new Dictionary<(int, int, int), List<ProductBpReading>>();
                    foreach (var g in groups)
                    {
                        var list = new List<ProductBpReading>();
                        readings[(g.Systolic, g.Diastolic, g.HeartRate)] = list;
                        for (var r = 1; r <= repeats; r++)
                        {
                            _log.Info($"BP-004/005 组 SYS={g.Systolic} DIA={g.Diastolic} HR={g.HeartRate} 第 {r}/{repeats} 次：启动产品测量并等测量曲线...");
                            // 不发开始测量指令（0x79）产品不会自行充放气：曲线判定等不到充气、也永远没有上报结果
                            await product.StartBpMeasureAsync(true, token);
                            await simulator.RunBpMeasurementAsync(g.Systolic, g.Diastolic, g.HeartRate, ct: token);
                            // 实测当前固件不主动推 0x7B（测量期间只周期推 0xCA，协议未定义），
                            // 曲线结束后主动轮询 0x7B 拿收缩压/舒张压/平均压，心率由 0x7C 单独读回
                            var reading = await PollBpResultAsync(product, token);
                            if (reading != null)
                            {
                                var hr = await product.ReadHeartRateAsync(token);
                                if (!double.IsNaN(hr)) reading = reading with { HeartRate = hr };
                                list.Add(reading);
                                _log.Info($"产品上报：SYS={reading.Systolic:0.0} DIA={reading.Diastolic:0.0} MAP={reading.MeanPressure:0.0} HR={reading.HeartRate:0.0}");
                            }
                            else
                            {
                                _log.Warn($"BP-004/005 组 SYS={g.Systolic} DIA={g.Diastolic} HR={g.HeartRate} 第 {r} 次：未取到产品测量结果（0x7B 轮询为空）");
                            }
                            // 关闭本次测量（0x79 status=1）：协议要求测完发一次关闭，否则下次开始不被接受
                            await product.StartBpMeasureAsync(false, token);
                        }
                    }
                
                    // ③ 统计：各组均值/极差 → 最大系统偏差与最大重复性误差；
                    //    同时把每组“设定值 vs 产品回读”写入各结果项子表（分组行：设定/回读均值/最大/最小/偏差/极差/判定）
                    if (readings.Values.All(l => l.Count == 0))
                    {
                        _log.Warn("BP-004/005 FAIL：未取到任何产品上报值（0x7B 轮询为空），请检查产品串口连接与测量周期是否完成");
                        return;
                    }

                    double sysDev = 0, sysRange = 0, diaDev = 0, diaRange = 0, hrDev = 0, hrRange = 0;
                    var sysAccPoints = new List<TestPointResult>();
                    var sysRepPoints = new List<TestPointResult>();
                    var diaAccPoints = new List<TestPointResult>();
                    var diaRepPoints = new List<TestPointResult>();
                    var hrAccPoints = new List<TestPointResult>();
                    var hrRepPoints = new List<TestPointResult>();
                    var hrAccFail = false;
                    var hrRepFail = false;
                    var gi = 0;

                    foreach (var g in groups)
                    {
                        gi++;
                        var list = readings[(g.Systolic, g.Diastolic, g.HeartRate)];
                        if (list.Count == 0) continue;
                        var sys = list.Select(x => x.Systolic).ToList();
                        var dia = list.Select(x => x.Diastolic).ToList();
                        var hr = list.Select(x => x.HeartRate).ToList();

                        var gSysDev = Math.Abs(sys.Average() - g.Systolic);
                        var gSysRange = sys.Max() - sys.Min();
                        var gDiaDev = Math.Abs(dia.Average() - g.Diastolic);
                        var gDiaRange = dia.Max() - dia.Min();
                        var gHrDev = Math.Abs(hr.Average() - g.HeartRate);
                        var gHrRange = hr.Max() - hr.Min();
                        var hrTol = Math.Max(5, g.HeartRate * 0.05);   // 心率容差按组 max(5, 设定×5%)

                        sysDev = Math.Max(sysDev, gSysDev);
                        sysRange = Math.Max(sysRange, gSysRange);
                        diaDev = Math.Max(diaDev, gDiaDev);
                        diaRange = Math.Max(diaRange, gDiaRange);
                        hrDev = Math.Max(hrDev, gHrDev);
                        hrRange = Math.Max(hrRange, gHrRange);
                        if (gHrDev > hrTol) hrAccFail = true;
                        if (gHrRange > hrTol) hrRepFail = true;

                        // 子表分组行：精度行仪器列=模拟仪设定值、产品列=回读均值；重复性行仪器列=回读最大、产品列=回读最小
                        var groupName = $"组{gi} {g.Systolic}/{g.Diastolic}@{g.HeartRate}";
                        sysAccPoints.Add(new TestPointResult { Name = groupName, TargetPressure = g.Systolic, InstrumentValue = g.Systolic, ProductValue = Math.Round(sys.Average(), 2), Error = Math.Round(gSysDev, 2), Pass = gSysDev <= 3 });
                        sysRepPoints.Add(new TestPointResult { Name = groupName, TargetPressure = g.Systolic, InstrumentValue = sys.Max(), ProductValue = sys.Min(), Error = Math.Round(gSysRange, 2), Pass = gSysRange <= 2 });
                        diaAccPoints.Add(new TestPointResult { Name = groupName, TargetPressure = g.Diastolic, InstrumentValue = g.Diastolic, ProductValue = Math.Round(dia.Average(), 2), Error = Math.Round(gDiaDev, 2), Pass = gDiaDev <= 3 });
                        diaRepPoints.Add(new TestPointResult { Name = groupName, TargetPressure = g.Diastolic, InstrumentValue = dia.Max(), ProductValue = dia.Min(), Error = Math.Round(gDiaRange, 2), Pass = gDiaRange <= 2 });
                        hrAccPoints.Add(new TestPointResult { Name = groupName, TargetPressure = g.HeartRate, InstrumentValue = g.HeartRate, ProductValue = Math.Round(hr.Average(), 2), Error = Math.Round(gHrDev, 2), Pass = gHrDev <= hrTol });
                        hrRepPoints.Add(new TestPointResult { Name = groupName, TargetPressure = g.HeartRate, InstrumentValue = hr.Max(), ProductValue = hr.Min(), Error = Math.Round(gHrRange, 2), Pass = gHrRange <= hrTol });
                    }

                    await collector.SetAsync(SpecItems.Bp004SystolicAccuracy, new TestItemResult { Value = Math.Round(sysDev, 2), Points = sysAccPoints });
                    await collector.SetAsync(SpecItems.Bp004SystolicRepeat, new TestItemResult { Value = Math.Round(sysRange, 2), Points = sysRepPoints });
                    await collector.SetAsync(SpecItems.Bp004DiastolicAccuracy, new TestItemResult { Value = Math.Round(diaDev, 2), Points = diaAccPoints });
                    await collector.SetAsync(SpecItems.Bp004DiastolicRepeat, new TestItemResult { Value = Math.Round(diaRange, 2), Points = diaRepPoints });
                    // 心率容差按组 max(5, 设定×5%)：静态规格 0~6 只等于最大容差组（120×5%），
                    // 任一组超自身容差时把父项主值顶出规格保证 FAIL（分组判定见子表）
                    var hrAccValue = hrAccFail ? Math.Max(Math.Round(hrDev, 2), 7) : Math.Round(hrDev, 2);
                    var hrRepValue = hrRepFail ? Math.Max(Math.Round(hrRange, 2), 7) : Math.Round(hrRange, 2);
                    await collector.SetAsync(SpecItems.Bp005HeartRateAccuracy, new TestItemResult { Value = hrAccValue, Points = hrAccPoints });
                    await collector.SetAsync(SpecItems.Bp005HeartRateRepeat, new TestItemResult { Value = hrRepValue, Points = hrRepPoints });
                    _log.Info($"BP-004/005 统计完成：收缩压偏差={sysDev:0.00} 极差={sysRange:0.00}；舒张压偏差={diaDev:0.00} 极差={diaRange:0.00}；心率偏差={hrDev:0.00} 极差={hrRange:0.00}");
                }
                finally
                {
                    simulator.PressureSampled -= OnBp004Sample;
                }
            }
        }

        /// <summary>
        /// 压力控制功能测试（BP-006~BP-008，测试要求文档编号）：三项均以产品串口回读压力（0x7E）判定产品行为，
        /// 未接产品/握手失败时三项直接 FAIL（0），不做模拟仪空跑。
        /// 产品通讯集中在 M16 产品通讯初始化，本模块不再问询；
        /// ① BP-006 匀速放气控制：产品自身血压测量周期（mode2 + 0x79 自充气，模拟仪并行提供脉搏波环境），
        ///    轮询回读峰值后的匀速放气段，计算平均放气速率 2~6mmHg/s（测试值=平均速率）；
        /// ② BP-007 快速放气控制：模拟仪充气，产品回读达到停止触发压力后发停止测量（0x7D），
        ///    回读压力归零耗时 ≤10s（测试值=归零耗时）；
        /// ④ BP-008 过压保护：mode3 静态压力充气过 300 冲至 320（参数取配置→超压），
        ///    产品应触发过压保护直接快速放气——回读骤降点即触发压力（动态规格 threshold1~threshold2）。
        /// </summary>
        //测试项目**************************---M04---**************************压力控制功能测试（BP-006~008）
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
                // 测试参数按仪器链接配置选择的模拟仪路由
                var testParams = config.ActiveBpTestParams;

                // 三项全部依赖产品串口回读压力判定，产品不可用直接三项 FAIL（0），不做无意义的模拟仪空跑；
                // 演示模式仅验证仪器侧行为，结果项按读回缺失判 FAIL；
                // 产品可用性以本轮 M16 产品通讯初始化记录为准（此处不再重复 0x72/0x74 问询）
                var productAvailable = GetProductAvailable(config.NoProductDemoMode);

                // 经共享会话租约取模拟仪（接口调用：步骤只用接口成员，BPA700=SDK 双路阀；康伟达=单阀脉宽合成）
                await using var lease = await _simSession.RentAsync(token);
                var simulator = lease.Simulator;

                if (!productAvailable)
                {
                    if (!config.NoProductDemoMode)
                    {
                        _log.Error("BP-006~008 判 FAIL：产品不可用（串口未连接或握手失败），三项均以产品回读压力判定，无法空跑；本轮不驱动模拟仪，仪器不进入血压模拟页面属预期");
                        var op0 = testParams.Overpressure;
                        await collector.SetAsync(SpecItems.Bp006UniformDeflation, TestItemResult.Of(0));
                        await collector.SetAsync(SpecItems.Bp007FastDeflation, TestItemResult.Of(0));
                        await collector.SetAsync(SpecItems.Bp008Overpressure, TestItemResult.WithDynamicSpec(0, op0.Threshold1, op0.Threshold2));
                        return;
                    }

                    // 无产品演示模式：逐项验证仪器侧充放气行为（匀速放气/快速放气/过压充气）；
                    // 无产品回读值：结果项不报告，由收尾扫描按读回缺失统一判 FAIL
                    _log.Info("BP-006~008 无产品演示模式：驱动模拟仪验证仪器侧充放气行为，结果项按产品读回缺失判 FAIL");

                    // 逐项演示包装：独立波形时间轴 + 异常隔离，结果由收尾扫描上报
                    async Task DemoStepAsync(string name, Func<Task> body)
                    {
                        var wave = System.Diagnostics.Stopwatch.StartNew();
                        void OnSample(double pressure) =>
                            PressureSampled?.Invoke(name, new PressureSamplePoint(wave.Elapsed.TotalSeconds, pressure));
                        simulator.PressureSampled += OnSample;
                        try
                        {
                            await body();
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _log.Warn($"演示步骤异常（{name}，不影响收尾判定）：{ex.Message}");
                        }
                        finally
                        {
                            simulator.PressureSampled -= OnSample;
                        }
                    }

                    var timeout = config.ActiveStaticPressureTimeoutSeconds;
                    await simulator.StartSamplingAsync(token);
                    try
                    {
                        // BP-006 演示：充到 150 后开慢释阀匀速泄放，观察泄放曲线
                        await DemoStepAsync(SpecItems.Bp006UniformDeflation, async () =>
                        {
                            await simulator.SetStaticPressureAsync(150, stableSeconds: 2, inflation: true, token, timeoutSeconds: timeout);
                            await simulator.SetSlowValveAsync(true, token);
                            await Task.Delay(12_000, token);
                            await simulator.SetSlowValveAsync(false, token);
                            await simulator.StopSimulationAsync(token);
                        });

                        // BP-007 演示：充到 250 后开快释阀归零，观察快速泄放曲线
                        await DemoStepAsync(SpecItems.Bp007FastDeflation, async () =>
                        {
                            await simulator.SetStaticPressureAsync(250, stableSeconds: 2, inflation: true, token, timeoutSeconds: timeout);
                            await simulator.SetFastValveAsync(true, token);
                            await Task.Delay(8_000, token);
                            await simulator.SetFastValveAsync(false, token);
                            await simulator.StopSimulationAsync(token);
                        });

                        // BP-008 演示：充到超压目标短时保持（无产品不会触发保护），再快释清场
                        await DemoStepAsync(SpecItems.Bp008Overpressure, async () =>
                        {
                            var op = testParams.Overpressure;
                            await simulator.SetStaticPressureAsync(op.TargetPressure, stableSeconds: 3, inflation: op.BpaInflation, token, timeoutSeconds: 60);
                            await simulator.SetFastValveAsync(true, token);
                            await Task.Delay(3_000, token);
                            await simulator.SetFastValveAsync(false, token);
                            await simulator.StopSimulationAsync(token);
                        });
                    }
                    finally
                    {
                        await simulator.StopSamplingAsync();
                    }
                    return;
                }

                // 逐项执行包装：每步独立异常隔离 + 立即上报（测一项显一项），
                // BP-006 失败不拖垮 BP-007/008；OperationCanceledException（用户点“停止”）向上层传递
                async Task RunStepAsync(string name, Func<Task<TestItemResult>> body)
                {
                    try
                    {
                        _log.Info($"步骤开始：{name}");
                        var result = await body();
                        await collector.SetAsync(name, result);
                        _log.Info($"步骤完成：{name}");
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"步骤失败：{name}，{ex.Message}");
                        await collector.SetAsync(name, TestItemResult.Of(0));
                    }
                }

                try
                {
                    await simulator.StartSamplingAsync(token);

                    await RunStepAsync(SpecItems.Bp006UniformDeflation, () => Bp006UniformDeflationAsync(simulator, product, testParams, token));
                    await RunStepAsync(SpecItems.Bp007FastDeflation, () => Bp007FastDeflationAsync(simulator, product, testParams, token));
                    await RunStepAsync(SpecItems.Bp008Overpressure, () => Bp008OverpressureAsync(simulator, product, testParams, token));
                }
                finally
                {
                    await simulator.StopSamplingAsync();
                }
            }
        }

        /// <summary>
        /// BP-006 匀速放气控制：产品自身血压测量周期（mode2 + 0x79 自充气+匀速放气），
        /// 模拟仪并行跑一轮血压模拟提供脉搏波环境；200ms 轮询 0x7E 回读产品压力，
        /// 取峰值后的匀速放气段计算平均放气速率（规格 2~6 mmHg/s，测试值=平均速率）。
        /// </summary>
        private async Task<TestItemResult> Bp006UniformDeflationAsync(
            IBloodPressureSimulator simulator, IProductController product, BpaTestParams testParams, CancellationToken token)
        {
            var pc = testParams.PressureControl;
            var dyn = testParams.DynamicBloodPressure;
            var cuff = testParams.CuffTest;

            await product.SetTestModeAsync(2, ct: token);

            var samples = new List<(double T, double P)>();
            var watch = System.Diagnostics.Stopwatch.StartNew();

            // 200ms 轮询产品回读压力（0x7E），采样与模拟仪测量并行
            using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var pollTask = PollProductPressureAsync(product, samples, 200, watch, pollCts.Token);

            void OnSample(double pressure) =>
                PressureSampled?.Invoke(SpecItems.Bp006UniformDeflation, new PressureSamplePoint(watch.Elapsed.TotalSeconds, pressure));

            simulator.PressureSampled += OnSample;
            try
            {
                await product.StartBpMeasureAsync(true, token);
                await simulator.RunBpMeasurementAsync((int)dyn.Systolic, (int)dyn.Diastolic, dyn.HeartRate,
                    cuff.WaitStartTimeoutSeconds * 1000, cuff.TotalTimeoutSeconds * 1000, token);
                await Task.Delay(500, token);
            }
            finally
            {
                pollCts.Cancel();
                await pollTask;
                simulator.PressureSampled -= OnSample;
                // 关闭本次测量（0x79 status=1），否则产品不接受下次开始
                await product.StartBpMeasureAsync(false, token);
            }

            var rate = ComputeUniformDeflationRate(samples, pc, out var segmentDesc);
            _log.Info($"BP-006：产品回读 {samples.Count} 点，匀速放气段 {segmentDesc}，平均放气速率 {rate:0.00} mmHg/s（规格 2~6）");
            return TestItemResult.Of(Math.Round(rate, 2));
        }

        /// <summary>
        /// BP-007 快速放气控制：mode2 发 0x79 后模拟仪充气，100ms 轮询产品回读压力，
        /// 达到停止触发压力（配置，默认 150mmHg）即发停止测量（0x7D）触发产品快速放气，
        /// 计时回读归零耗时（规格 ≤10s，测试值=归零耗时）。
        /// </summary>
        private async Task<TestItemResult> Bp007FastDeflationAsync(
            IBloodPressureSimulator simulator, IProductController product, BpaTestParams testParams, CancellationToken token)
        {
            var pc = testParams.PressureControl;
            var dyn = testParams.DynamicBloodPressure;

            await simulator.StopSimulationAsync(token);
            await simulator.SetFastValveAsync(false, token);
            await simulator.SetSlowValveAsync(false, token);
            await product.SetTestModeAsync(2, ct: token);
            await Task.Delay(500, token);

            var samples = new List<(double T, double P)>();
            var watch = System.Diagnostics.Stopwatch.StartNew();

            using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var pollTask = PollProductPressureAsync(product, samples, 100, watch, pollCts.Token);

            void OnSample(double pressure) =>
                PressureSampled?.Invoke(SpecItems.Bp007FastDeflation, new PressureSamplePoint(watch.Elapsed.TotalSeconds, pressure));

            simulator.PressureSampled += OnSample;
            double fastTime;
            try
            {
                await product.StartBpMeasureAsync(true, token);

                // 模拟仪充气：给产品袖带加压，产品回读随之上升（不等待收敛，边充边轮询）
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await simulator.SetStaticPressureAsync(pc.BP007StopTriggerPressure + 30, stableSeconds: 10, inflation: true, pollCts.Token);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _log.Warn($"BP-007 充气异常（不影响回读计时）：{ex.Message}");
                    }
                });

                // 等产品回读达到停止触发压力
                var stopAt = -1.0;
                while (watch.Elapsed.TotalSeconds < pc.BP007InflateWaitSeconds)
                {
                    token.ThrowIfCancellationRequested();
                    lock (samples)
                    {
                        if (samples.Count > 0 && samples[^1].P >= pc.BP007StopTriggerPressure)
                            stopAt = samples[^1].T;
                    }
                    if (stopAt >= 0) break;
                    await Task.Delay(100, token);
                }

                if (stopAt < 0)
                {
                    _log.Warn($"BP-007：{pc.BP007InflateWaitSeconds}s 内产品回读未达到停止触发压力 {pc.BP007StopTriggerPressure}mmHg，判 FAIL");
                    return TestItemResult.Of(0);
                }

                // 发停止测量（0x7D），产品触发快速放气，计时回读归零
                await product.StopBpMeasureAsync(token);
                _log.Info($"BP-007：产品回读 {pc.BP007StopTriggerPressure}mmHg 触发，已发停止测量（0x7D），等待快速放气归零");

                var zeroAt = -1.0;
                while (watch.Elapsed.TotalSeconds - stopAt < pc.BP007ZeroTimeoutSeconds)
                {
                    token.ThrowIfCancellationRequested();
                    lock (samples)
                    {
                        if (samples.Count > 0 && samples[^1].T >= stopAt && samples[^1].P <= pc.BP007ZeroPressure)
                            zeroAt = samples[^1].T;
                    }
                    if (zeroAt >= 0) break;
                    await Task.Delay(100, token);
                }

                if (zeroAt < 0)
                {
                    _log.Warn($"BP-007：{pc.BP007ZeroTimeoutSeconds}s 内产品回读未归零（≤{pc.BP007ZeroPressure}mmHg），按超时上报");
                    fastTime = pc.BP007ZeroTimeoutSeconds;
                }
                else
                {
                    fastTime = zeroAt - stopAt;
                }
            }
            finally
            {
                pollCts.Cancel();
                await pollTask;
                simulator.PressureSampled -= OnSample;
                await simulator.StopSimulationAsync(token);
                await simulator.SetFastValveAsync(false, token);
                await simulator.ResetAsync(token);
                // 关闭本次测量（0x79 status=1），否则产品不接受下次开始
                await product.StartBpMeasureAsync(false, token);
            }

            _log.Info($"BP-007：快速放气归零耗时 {fastTime:0.00}s（规格 ≤10s）");
            return TestItemResult.Of(Math.Round(fastTime, 2));
        }

        /// <summary>
        /// BP-008 过压保护：mode3 静态压力，模拟仪充气过 300 冲至 320（目标取配置→超压 TargetPressure），
        /// 100ms 轮询产品回读；产品触发过压保护直接快速放气——回读骤降点（600ms 窗内降≥25mmHg）
        /// 即触发压力（动态规格 threshold1~threshold2，默认 300~320）。
        /// </summary>
        private async Task<TestItemResult> Bp008OverpressureAsync(
            IBloodPressureSimulator simulator, IProductController product, BpaTestParams testParams, CancellationToken token)
        {
            var op = testParams.Overpressure;

            await simulator.StopSimulationAsync(token);
            await simulator.SetFastValveAsync(false, token);
            await simulator.SetSlowValveAsync(false, token);
            await product.SetTestModeAsync(3, ct: token);
            await Task.Delay(500, token);

            var samples = new List<(double T, double P)>();
            var watch = System.Diagnostics.Stopwatch.StartNew();

            using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var pollTask = PollProductPressureAsync(product, samples, 100, watch, pollCts.Token);

            void OnSample(double pressure) =>
                PressureSampled?.Invoke(SpecItems.Bp008Overpressure, new PressureSamplePoint(watch.Elapsed.TotalSeconds, pressure));

            simulator.PressureSampled += OnSample;
            using var inflateCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var inflateTask = Task.Run(() =>
                simulator.SetStaticPressureAsync(op.TargetPressure, stableSeconds: 0, inflation: op.BpaInflation, inflateCts.Token, timeoutSeconds: 60), inflateCts.Token);

            double? trigger = null;
            try
            {
                // 边充气边检测产品回读骤降（触发过压保护快速放气），75s 兜底
                var deadline = TimeSpan.FromSeconds(75);
                while (watch.Elapsed < deadline)
                {
                    token.ThrowIfCancellationRequested();
                    trigger = DetectProtectionCrash(samples, watch.Elapsed.TotalSeconds);
                    if (trigger != null) break;
                    if (inflateTask.IsCompleted)
                    {
                        // 充气已到位仍未触发，再观察 1s 后结束
                        await Task.Delay(1000, token);
                        trigger = DetectProtectionCrash(samples, watch.Elapsed.TotalSeconds);
                        break;
                    }
                    await Task.Delay(100, token);
                }
            }
            finally
            {
                if (trigger != null) inflateCts.Cancel();
                pollCts.Cancel();
                await pollTask;
                simulator.PressureSampled -= OnSample;
                try { await inflateTask; } catch (OperationCanceledException) { } catch (TimeoutException) { }

                // 清场：快释阀泄放残压，避免影响后续模块
                await simulator.SetFastValveAsync(true, token);
                await Task.Delay(1500, token);
                await simulator.SetFastValveAsync(false, token);
            }

            if (trigger == null)
            {
                _log.Warn($"BP-008：充气至 {op.TargetPressure}mmHg 全程未检测到产品过压保护动作（回读无骤降），判 FAIL");
                return TestItemResult.WithDynamicSpec(0, op.Threshold1, op.Threshold2);
            }

            _log.Info($"BP-008：检测到产品过压保护触发，触发压力 {trigger:0.0}mmHg（规格 {op.Threshold1}~{op.Threshold2}）");
            return TestItemResult.WithDynamicSpec(Math.Round(trigger.Value, 1), op.Threshold1, op.Threshold2);
        }

        /// <summary>
        /// 轮询血压测量结果（0x7B）：产品不主动推送，测量曲线结束后每 500ms 轮询一次，
        /// 收缩压 > 0 立即返回；10 次仍未取到有效结果返回最后一次响应（全零表示测量未完成，统计按 FAIL 判）。
        /// </summary>
        private static async Task<ProductBpReading?> PollBpResultAsync(IProductController product, CancellationToken token)
        {
            ProductBpReading? last = null;
            for (var i = 0; i < 10; i++)
            {
                token.ThrowIfCancellationRequested();
                last = await product.ReadBpResultAsync(token);
                if (last != null && last.Systolic > 0) return last;
                await Task.Delay(500, token);
            }
            return last;
        }

        /// <summary>
        /// 后台轮询产品回读压力（0x7E）：按 intervalMs 间隔采样入列（时间=秒表秒），
        /// 单次回读失败不中断采样；取消即退出。
        /// </summary>
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
                catch { /* 单次回读失败不中断采样 */ }

                try { await Task.Delay(intervalMs, ct); } catch (OperationCanceledException) { break; }
            }
        }

        /// <summary>
        /// BP-006 匀速放气速率：取回读峰值后的配置区间（默认 130~30mmHg）内的样本，
        /// 首尾点斜率即平均放气速率；区间内样本不足时退化为峰值-5 至峰值 20% 区间。
        /// </summary>
        private static double ComputeUniformDeflationRate(List<(double T, double P)> samples, BpaPressureControlParams pc, out string segmentDesc)
        {
            segmentDesc = "无";
            lock (samples)
            {
                if (samples.Count < 4) return 0;

                var peakIdx = 0;
                for (var i = 1; i < samples.Count; i++)
                    if (samples[i].P > samples[peakIdx].P) peakIdx = i;
                var peak = samples[peakIdx].P;

                var high = Math.Min(pc.BP006RateSegmentHigh, peak - 5);
                var low = Math.Max(pc.BP006RateSegmentLow, 10);
                var segment = samples.Skip(peakIdx).Where(s => s.P <= high && s.P >= low).ToList();
                if (segment.Count < 2 || high - low < 20)
                {
                    high = peak - 5;
                    low = Math.Max(10, peak * 0.2);
                    segment = samples.Skip(peakIdx).Where(s => s.P <= high && s.P >= low).ToList();
                }
                if (segment.Count < 2) return 0;

                var (t0, p0) = segment[0];
                var (t1, p1) = segment[^1];
                if (t1 <= t0) return 0;
                segmentDesc = $"{p0:0.0}→{p1:0.0} mmHg";
                return Math.Abs(p0 - p1) / (t1 - t0);
            }
        }

        /// <summary>
        /// BP-008 过压保护触发检测：最近 600ms 窗内回读压力骤降≥25mmHg（且降前压力≥50mmHg）
        /// 即视为产品触发保护快速放气，返回骤降前压力（=触发压力）；未检测到返回 null。
        /// </summary>
        private static double? DetectProtectionCrash(List<(double T, double P)> samples, double nowSeconds)
        {
            lock (samples)
            {
                if (samples.Count < 2) return null;
                var latest = samples[^1];
                for (var i = samples.Count - 2; i >= 0; i--)
                {
                    if (nowSeconds - samples[i].T > 0.6) break;   // 只看最近 600ms 窗
                    if (samples[i].P - latest.P >= 25 && samples[i].P >= 50) return samples[i].P;
                }
                return null;
            }
        }
    }
}
