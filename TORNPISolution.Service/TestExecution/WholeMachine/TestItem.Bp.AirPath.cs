using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// TestItem 分部：气路类测试——A18 放气速率（仪器充气—缓降两段时长）、A22 气路泄露（高低压段起止压差）。
    /// 两项都由产品关闭泄气阀（0x6B）封闭气路、进入静态压力模式（0x73 mode=3），读压力统一用 0x76，收尾退出静态压力模式并打开泄气阀（0x6A）。
    /// A18 用正式版 SDK 的过压测试（仪器充气到目标压后转入缓降、降到终点即停），A22 用 1.0.4.0-BETA 句柄版 SDK 的静态压力序列（点间直接切压不泄回零）。
    /// </summary>
    public partial class TestItem
    {
        //测试项目**************************---A18---**************************放气速率测试（BP-007）
        /// <summary>
        /// 放气速率测试（A18）：血压计气路与模拟仪接通、不封堵毛细气孔，产品关闭泄气阀（0x6B）后进入静态压力模式（0x73 mode=3），整轮压力由仪器给。
        /// ① 仪器跑一轮"充气到 230 → 转入缓降 → 降到 40 即停"（正式版 SDK 的过压测试），t1/t2 取仪器自己上报的充气段与泄气段时长；
        ///    回读（采样曲线）只用来分两段曲线、取该点仪器均值与门控产品 0x76 采样，两项都在本轮结束后一起上报，逐项耗时按回读时刻给。
        /// ② 缓降走的就是仪器自己的放气阀，所以本项不下发仪器阀指令：阀态要下一次测试调用才生效，此处关阀会把缓降堵死。
        /// ③ 收尾退出静态压力模式（0x73 status=1）+ 打开泄气阀（0x6A）+ 仪器泄放回零，停止/异常路径同样执行。
        /// </summary>
        public async Task DeflateRateModule(TestItemResultCollector collector, CancellationToken token)
        {
            const string ModuleName = "放气速率测试";
            // 阈值 2：仪器充过该线即转入缓降，取厂商上位机超压测试页的默认值（临时值，后续按 SOP 调整）
            const double VentStartThreshold = 210;
            const int InflateArriveSeconds = 60;   // 加压段等回读升到 230 的观察上限
            const int DescentObserveSeconds = 120; // 泄压段等回读降到 40 的观察上限（缓降约 4.7mmHg/s，190mmHg 要 40s 上下）
            const int RoundTimeoutSeconds = 180;   // 整轮（充气 + 缓降）兜底超时
            // 平台离开判定余量：回读跌破"高压设定点 - 该余量"才算离开 230 平台，避开平台抖动；只用来在日志里说明段内何时起降
            const double DescentStartMargin = 10;

            if (NoInstrumentDemo(ModuleName)) return;
            var config = _configService.Load();
            if (config.ActiveSimulator != SimulatorType.BPA700)
            {
                _log.Error($"{ModuleName}跳过：充气—缓降这一轮流程用 BPA700 正式版 SDK 的过压测试，康伟达未做验证");
                return;
            }

            using var product = _productFactory();
            if (!await OpenProductAsync(product, "A18", token))
            {
                _log.Error("A18 判 FAIL：产品不可用（串口未连接或握手失败），关阀与静态压力模式指令无法下发，本模块不驱动模拟仪");
                return;
            }

            await using var lease = await _simSession.RentAsync(token);
            var simulator = lease.Simulator;
            var testParams = config.BPA700Simulator.TestParams;
            var airPath = testParams.AirPath;
            var inflation = testParams.Overpressure.BpaInflation;

            var rig = new AirPathRig(simulator, _log, PressureSampled);
            var staticModeEntered = false;
            try
            {
                await rig.StartAsync(token);

                // ① 关闭泄气阀（0x6B）：产品侧封闭气路，整轮压力都由仪器给，收尾再打开
                await product.SetVentValveAsync(false, token);

                // ② 进入静态压力测试模式（0x73 mode=3），产品在该模式下才上报静态压力
                await product.SetTestModeAsync(3, ct: token);
                staticModeEntered = true;

                // ③ 整轮只发一次仪器指令，边跑边按回读切两段曲线、取该点压力
                rig.BeginPhase(SpecItems.Deflate007InflateTime);
                var run = simulator.RunOverpressureTestAsync(SpecItems.DeflateInflatePressure,
                    SpecItems.DeflateEndPressure, VentStartThreshold, inflation, RoundTimeoutSeconds * 1000, token);

                var arriveHigh = await rig.ArrivalSecondsAsync(SpecItems.DeflateInflatePressure, InflateArriveSeconds, token);
                if (double.IsNaN(arriveHigh))
                {
                    _log.Warn($"{SpecItems.Deflate007InflateTime}：{InflateArriveSeconds}s 内回读没升到 {SpecItems.DeflateInflatePressure:0}mmHg" +
                              $"（观察末 {Format(rig.LatestPressure())}），t1 与 t2 都不报告（由收尾判 FAIL）");
                    // 本轮由收尾的停止指令结束，这里只把稍后可能抛出的异常观察掉，不留到 GC 时才炸
                    _ = run.ContinueWith(static t => { _ = t.Exception; }, TaskScheduler.Default);
                    return;
                }
                var pHigh = rig.WindowAverage();
                var productHigh = await ReadProductPressureAsync(product, $"{SpecItems.DeflateInflatePressure:0}mmHg 采样", token);

                rig.BeginPhase(SpecItems.Deflate007DeflateTime);
                var (arriveLow, departAt) = await rig.DeflateSpanAsync(SpecItems.DeflateInflatePressure - DescentStartMargin,
                    SpecItems.DeflateEndPressure, DescentObserveSeconds, token);
                var pLow = rig.WindowAverage();
                var productLow = await ReadProductPressureAsync(product, $"{SpecItems.DeflateEndPressure:0}mmHg 采样", token);

                OverpressureRunResult round;
                try
                {
                    round = await run;
                }
                catch (TimeoutException ex)
                {
                    _log.Warn($"A18：{ex.Message}，t1 与 t2 都不报告（由收尾判 FAIL）");
                    return;
                }
                if (double.IsNaN(arriveLow))
                    _log.Warn($"{SpecItems.Deflate007DeflateTime}：{DescentObserveSeconds}s 内回读没降到 {SpecItems.DeflateEndPressure:0}mmHg" +
                              $"（段内最低 {Format(rig.PhaseMinimum())}、观察末 {Format(rig.LatestPressure())}），t2 的逐项耗时按观察末给");

                // 测试值=仪器上报的两段时长（单位 s 与列头一致），判定区间取配置页（气路测试）；该点仪器均值与产品 0x76 采样进子表行
                await collector.SetAsync(SpecItems.Deflate007InflateTime,
                    AirPathTimeResult(round.InflateSeconds, SpecItems.DeflateInflatePressure, pHigh, productHigh,
                        airPath.InflateTimeMin, airPath.InflateTimeMax, 30, arriveHigh));
                await collector.SetAsync(SpecItems.Deflate007DeflateTime,
                    AirPathTimeResult(round.DeflateSeconds, SpecItems.DeflateEndPressure, pLow, productLow,
                        airPath.DeflateTimeMin, airPath.DeflateTimeMax, 60, double.IsNaN(arriveLow) ? DescentObserveSeconds : arriveLow));

                var deflateRate = round.DeflateSeconds > 0
                    ? (SpecItems.DeflateInflatePressure - SpecItems.DeflateEndPressure) / round.DeflateSeconds : double.NaN;
                _log.Info($"BP-007：t1={round.InflateSeconds:0.0}s（回读 {arriveHigh:0.0}s 时升到 {SpecItems.DeflateInflatePressure:0}，" +
                          $"峰值={Format(round.PeakPressure)}）{SpecItems.DeflateInflatePressure:0} 点仪器={Format(pHigh)} 产品={Format(productHigh)}；" +
                          $"t2={round.DeflateSeconds:0.0}s（回读 {departAt:0.0}s 起降、{arriveLow:0.0}s 降到 {SpecItems.DeflateEndPressure:0}，均速 {deflateRate:0.00}mmHg/s）" +
                          $"{SpecItems.DeflateEndPressure:0} 点仪器={Format(pLow)} 产品={Format(productLow)}");
            }
            finally
            {
                await FinishAirPathAsync(product, simulator, rig, staticModeEntered, "A18");
            }
        }

        //测试项目**************************---A22---**************************气路泄露测试（BP-010）
        /// <summary>
        /// 气路泄露测试（A22）：先弹模态提示让测试员封堵泄气阀毛细气孔，点确定才开始流程。
        /// ① 关闭泄气阀（0x6B）+ 进入静态压力模式（0x73 mode=3）；
        /// ② 仪器用静态压力序列一次下发 [50, 250]：低压段回读升到 50 后稳压 3s 读 P_L_start、再保压 5s 读 P_L_end，起止差=低压泄气量；
        /// ③ 50 这点回读稳压后满 PointHoldSeconds 设备直接升到 250（点间不泄回零），高压段同样在升到 250 后 3s 读起点、再 5s 读终点，起止差=高压泄气量；两段都由仪器泵压驱动，没有硬泄压带来的过冲；
        ///    每段仪器值取窗口末端 1s 采样均值、产品值走 0x76（与校准、静态压力精度检测同一条指令）。
        /// ④ 收尾结束本轮 + 退出静态压力模式 + 打开泄气阀（0x6A）+ 仪器泄放回零。
        /// 起止四点只展示不判定；某段在 StageArriveSeconds 内没到点就读不出起止，该段及其后不报告，由收尾判 FAIL。
        /// 仅 BPA700：点之间直接切压不泄回零只有 1.0.4.0-BETA 句柄版 SDK 的序列模式提供，康伟达未做验证。
        /// </summary>
        public async Task AirPathLeakModule(TestItemResultCollector collector, CancellationToken token)
        {
            const string ModuleName = "气路泄露测试";
            const int SettleSeconds = 3;      // 起点读取前的稳压秒数（文档：到点稳压后等 3s）
            const int HoldSeconds = 5;        // 保压时长（文档：读完起点再保压 5s 读终点）
            // 读数窗从首次进带起算（约比设备 Stable 早 5s），终点读完在进带+8.35 ≈ Stable+3.05；设备每点保持秒数从该点 Stable 起算、到点直接切下一个压力点，SDK 没有提前切点的 API，所以只能把保持窗压到刚盖住读数窗：4s 时离切压约 0.9s（临时值，后续按 SOP 调整）
            const int PointHoldSeconds = 4;
            const int StageArriveSeconds = 60; // 序列每个压力点的到点观察上限

            if (NoInstrumentDemo(ModuleName)) return;
            var config = _configService.Load();
            if (config.ActiveSimulator != SimulatorType.BPA700)
            {
                _log.Error($"{ModuleName}跳过：气路类两项只按鲸扬 BPA700 的流程实现，康伟达未做验证");
                return;
            }

            using var product = _productFactory();
            if (!await OpenProductAsync(product, "A22", token))
            {
                _log.Error("A22 判 FAIL：产品不可用（串口未连接或握手失败），关阀与静态压力模式指令无法下发，本模块不驱动模拟仪");
                return;
            }

            await PromptOperatorAsync(ModuleName,
                "请用堵头封堵血压计泄气阀的毛细气孔，使气路完全封闭（压力传感器、阀、泵都接入气路），封堵完成后点「确定」开始测试。", token);

            await using var lease = await _simSession.RentSequenceSdkAsync(token);
            var simulator = lease.Simulator;
            var inflation = config.ActiveBpTestParams.StaticPressure.BpaInflation;
            var airPath = config.BPA700Simulator.TestParams.AirPath;

            var rig = new AirPathRig(simulator, _log, PressureSampled);
            var staticModeEntered = false;
            try
            {
                await rig.StartAsync(token);
                await simulator.SetFastValveAsync(false);
                await simulator.SetSlowValveAsync(false);
                await product.SetVentValveAsync(false, token);
                await product.SetTestModeAsync(3, ct: token);
                staticModeEntered = true;

                await simulator.StartStaticPressureSequenceAsync(
                    new[] { SpecItems.LeakLowPressure, SpecItems.LeakHighPressure }, PointHoldSeconds, inflation, token);
                _log.Info($"BP-010 升压段：{SpecItems.LeakLowPressure:0} 这点回读稳压后再保持 {PointHoldSeconds}s，到时设备直接升向 {SpecItems.LeakHighPressure:0}mmHg，期间请勿停止");

                // 一段：回读升到点 -> 稳压满 3s 读起点 -> 再保压 5s 读终点；到点判定用采样回读（回调只在点状态变化时来一次，给不到时刻精度）
                async Task<LeakStage?> RunStageAsync(double target, string curveItem)
                {
                    rig.BeginPhase(curveItem);
                    var crossed = await rig.ArrivalSecondsAsync(target, StageArriveSeconds, token);
                    if (double.IsNaN(crossed))
                    {
                        _log.Warn($"{target:0}mmHg 段：{StageArriveSeconds}s 内回读未升到 {target:0}mmHg" +
                                  $"（观察末 {Format(rig.LatestPressure())}），该段起止值不报告（由收尾判 FAIL）");
                        return null;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(SettleSeconds), token);
                    var startInstrument = rig.WindowAverage();
                    var startProduct = await ReadProductPressureAsync(product, $"{target:0}mmHg 起点", token);

                    await Task.Delay(TimeSpan.FromSeconds(HoldSeconds), token);
                    var endInstrument = rig.WindowAverage();
                    var endProduct = await ReadProductPressureAsync(product, $"{target:0}mmHg 终点", token);

                    _log.Info($"BP-010 {target:0}mmHg 段：起点 仪器={Format(startInstrument)} 产品={Format(startProduct)}，" +
                              $"终点 仪器={Format(endInstrument)} 产品={Format(endProduct)}");
                    return new LeakStage(Measured(startInstrument), Measured(startProduct),
                        Measured(endInstrument), Measured(endProduct));
                }

                var low = await RunStageAsync(SpecItems.LeakLowPressure, SpecItems.Leak010LowStart);
                if (low == null) return;
                await ReportLeakPointAsync(collector, SpecItems.Leak010LowStart, low.StartInstrument, low.StartProduct);
                await ReportLeakPointAsync(collector, SpecItems.Leak010LowEnd, low.EndInstrument, low.EndProduct);
                await ReportLeakLossAsync(collector, SpecItems.Leak010LowLoss, low, "低压", airPath.LowLossMin, airPath.LowLossMax);

                var high = await RunStageAsync(SpecItems.LeakHighPressure, SpecItems.Leak010HighStart);
                if (high == null) return;
                await ReportLeakPointAsync(collector, SpecItems.Leak010HighStart, high.StartInstrument, high.StartProduct);
                await ReportLeakPointAsync(collector, SpecItems.Leak010HighEnd, high.EndInstrument, high.EndProduct);
                await ReportLeakLossAsync(collector, SpecItems.Leak010HighLoss, high, "高压", airPath.HighLossMin, airPath.HighLossMax);
            }
            finally
            {
                await FinishAirPathAsync(product, simulator, rig, staticModeEntered, "A22");
            }
        }

        /// <summary>一段泄露测量的起止读数（null = 该值没读到）</summary>
        private sealed record LeakStage(double? StartInstrument, double? StartProduct, double? EndInstrument, double? EndProduct);

        /// <summary>气路类模块开产品串口：可用性以本轮 A16 握手记录为准，本模块自建实例仍需打开串口才能下发关阀/模式/读压指令</summary>
        private async Task<bool> OpenProductAsync(IProductController product, string moduleTag, CancellationToken token)
        {
            if (!GetProductAvailable(_configService.Load().NoProductDemoMode)) return false;
            try
            {
                await product.OpenAsync(token);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Warn($"{moduleTag}：产品串口打开失败：{ex.Message}");
                return false;
            }
        }

        /// <summary>读产品采样压力（0x76）：失败或无响应只记 Warn 回 NaN，由调用方按缺值处理，不打断正在保压的流程</summary>
        private async Task<double> ReadProductPressureAsync(IProductController product, string label, CancellationToken token)
        {
            try
            {
                return await product.ReadProductPressureAsync(token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Warn($"产品采样压力读取失败（{label}）：{ex.Message}");
                return double.NaN;
            }
        }

        /// <summary>气路泄露起止点结果：只展示不判定，缺值列显示 --（仪器值缺失时测试值退回产品值，不展示成 0）</summary>
        private static async Task ReportLeakPointAsync(TestItemResultCollector collector, string itemName, double? instrument, double? product)
            => await collector.SetAsync(itemName, new TestItemResult
            {
                Value = instrument ?? product ?? 0,
                InstrumentValue = instrument,
                ProductValue = product,
                DisplayOnly = true,
            });

        /// <summary>
        /// 气路类时长项结果（A18 的 t1/t2）：测试值与仪器列都是实测时长（s），判定区间取配置页（气路测试）；两端都留空则不判 PASS/FAIL，只报实测值。
        /// 该点的两路压力读进子表行，不写成项文本：测试值列文本优先（报告与界面同口径），写进文本会把判定用的时长顶掉。
        /// </summary>
        /// <param name="measuredSeconds">本项的实测时刻（s，从该段曲线起点算）：两项都在仪器整轮结束后一起上报，不填就会让首项吃掉整段采集</param>
        private static TestItemResult AirPathTimeResult(double seconds, double target, double? instrumentPressure,
            double? productPressure, double? min, double? max, double fallbackMax, double measuredSeconds)
        {
            var value = Math.Round(seconds, 1);
            var result = min == null && max == null
                ? new TestItemResult { NotJudged = true, InstrumentText = $"{value:0.0} s" }
                : new TestItemResult
                {
                    Value = value,
                    InstrumentValue = value,
                    SpecMin = min ?? 0,
                    SpecMax = max ?? fallbackMax,
                };

            if (!double.IsNaN(measuredSeconds)) result.MeasuredSeconds = Math.Round(measuredSeconds, 1);

            result.Points = new List<TestPointResult>
            {
                new()
                {
                    Schema = TestPointSchemas.Point,
                    Name = $"{target:0}mmHg 点采样",
                    Unit = "mmHg",
                    TargetPressure = target,
                    InstrumentValue = Measured(instrumentPressure ?? double.NaN),
                    ProductValue = Measured(productPressure ?? double.NaN),
                    ShowVerdict = false,
                },
            };
            return result;
        }

        /// <summary>判定区间文本（日志用）：上限留空时写明兜底值</summary>
        private static string SpecRange(double? min, double? max, double fallbackMax, string unit)
            => min == null && max == null
                ? "未配置，不判定"
                : $"{min?.ToString("0.###") ?? "不限"} ~ {(max ?? fallbackMax):0.###} {unit}";

        /// <summary>气路泄露起止差结果：判定值=产品侧差值、仪器列=仪器侧差值，判定区间取配置页（气路测试）；起止值有缺失则不报告，由收尾判 FAIL</summary>
        private async Task ReportLeakLossAsync(TestItemResultCollector collector, string itemName, LeakStage stage,
            string label, double? min, double? max)
        {
            if (stage.StartInstrument == null || stage.EndInstrument == null ||
                stage.StartProduct == null || stage.EndProduct == null)
            {
                _log.Warn($"{itemName}：{label}段起止值有缺失，算不出泄气量，不报告（由收尾判 FAIL）");
                return;
            }

            var instrumentLoss = Math.Round(stage.StartInstrument.Value - stage.EndInstrument.Value, 1);
            var productLoss = Math.Round(stage.StartProduct.Value - stage.EndProduct.Value, 1);
            _log.Info($"{itemName}：{label}泄气值 产品（判定用）={productLoss:0.0} 仪器={instrumentLoss:0.0}mmHg，判定区间 {SpecRange(min, max, 2, "mmHg")}");
            if (min == null && max == null)
            {
                await collector.SetAsync(itemName, new TestItemResult
                {
                    NotJudged = true,
                    InstrumentText = $"{instrumentLoss:0.0} mmHg",
                    ProductText = $"{productLoss:0.0} mmHg",
                });
                return;
            }

            await collector.SetAsync(itemName, TestItemResult.WithDualValues(instrumentLoss, productLoss, min ?? 0, max ?? 2));
        }

        /// <summary>
        /// 气路类模块收尾（A18/A22 共用，停止与异常路径也要走到）：主动 BPAStopTest 结束仍激活的稳压轮（不结束仪器会一直补压，泄放回零判不到）→
        /// 退出静态压力模式（0x73 mode=3 status=1）→ 打开泄气阀（0x6A）→ 仪器泄放回零（排在停采样之前，BPA700 要靠采样回读判零）→ 退订并停采样。
        /// 每句各自兜异常，前一句失败不能顶掉后面的开阀。
        /// </summary>
        private async Task FinishAirPathAsync(IProductController product, IBloodPressureSimulator simulator,
            AirPathRig rig, bool staticModeEntered, string moduleTag)
        {
            try { await simulator.StopSimulationAsync(CancellationToken.None); }
            catch (Exception ex) { _log.Warn($"{moduleTag}：结束稳压轮失败：{ex.Message}"); }

            if (staticModeEntered)
            {
                try { await product.SetTestModeAsync(3, 1, CancellationToken.None); }
                catch (Exception ex) { _log.Warn($"{moduleTag}：退出静态压力模式（0x73 mode=3 status=1）失败：{ex.Message}"); }
            }

            try { await product.SetVentValveAsync(true, CancellationToken.None); }
            catch (Exception ex) { _log.Warn($"{moduleTag}：打开泄气阀（0x6A）失败：{ex.Message}"); }

            try { await simulator.VentToZeroAsync(ct: CancellationToken.None); }
            catch (Exception ex) { _log.Warn($"{moduleTag}：模块结束泄气失败：{ex.Message}"); }

            await rig.StopAsync();
        }

        /// <summary>实测值可用时保留一位小数，缺值回 null（UI 与入库按 -- 显示，不把 NaN 当数据）</summary>
        private static double? Measured(double value) =>
            double.IsNaN(value) || double.IsInfinity(value) ? null : Math.Round(value, 1);

        private static string Format(double value) => Measured(value) is double v ? v.ToString("0.0") : "--";

        /// <summary>
        /// 气路类测试的采样旁路：把仪器压力采样按当前阶段项名上抛给 UI 画曲线，同时留最近窗口用于读起点/终点。
        /// 换阶段时清窗并重置时间轴（一段一条独立曲线，与 BP-003 逐点独立时间轴同口径）。
        /// </summary>
        private sealed class AirPathRig
        {
            /// <summary>到压判定容差（mmHg）：回读在目标附近抖动，取进入目标 ±该容差的首个采样时刻</summary>
            private const double ArrivalTolerance = 1.0;

            /// <summary>读值窗口（秒）：单点采样有毛刺，取窗口末端这段时间的均值</summary>
            private const double WindowSeconds = 1.0;

            private readonly IBloodPressureSimulator _simulator;
            private readonly ILogService _log;
            private readonly Action<string, PressureSamplePoint>? _sink;
            private readonly List<(double T, double P)> _samples = new();
            private readonly object _lock = new();
            private string _item = string.Empty;
            private System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();

            public AirPathRig(IBloodPressureSimulator simulator, ILogService log, Action<string, PressureSamplePoint>? sink)
            {
                _simulator = simulator;
                _log = log;
                _sink = sink;
            }

            /// <summary>订采样并按需开采样：只有回读依赖采样的仪器（BPA700）才发开采样指令，康伟达常态回传 AA 帧</summary>
            public async Task StartAsync(CancellationToken token)
            {
                _simulator.PressureSampled += OnSample;
                if (!_simulator.RequiresSamplingForReadback) return;
                try
                {
                    await _simulator.StartSamplingAsync(token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.Warn($"气路测试：开启压力采样失败，压力值可能无回读：{ex.Message}");
                }
            }

            /// <summary>换到下一段：曲线改挂到本段项名，时间轴与读值窗口重新开始</summary>
            public void BeginPhase(string itemName)
            {
                _item = itemName;
                lock (_lock) _samples.Clear();
                _watch = System.Diagnostics.Stopwatch.StartNew();
            }

            /// <summary>等本段回读首次升到目标（含容差）并回该时刻（秒），A22 用它作读数窗起点：泵会先冲过设定值再回落，所以这个时刻比序列的 Stable 回调早约 5s，只作段内读数的门控、不作时长判定；观察满 timeoutSeconds 仍未到压回 NaN，由调用方按缺值处理</summary>
            public async Task<double> ArrivalSecondsAsync(double target, int timeoutSeconds, CancellationToken token)
                => await CrossSecondsAsync(target, timeoutSeconds, token).ConfigureAwait(false);

            /// <summary>
            /// 本段降压读数：回读首次降到 target（含容差）的段内时刻作到达时刻，另回该段回读首次跌破 leaveBelow（离开高压平台）的时刻。
            /// 段起点即曲线 0s（压力停在高压平台），所以到达时刻就是"从高压降到低压用了多少"，离开时刻只用来说明段内何时起降。
            /// 观察满 timeoutSeconds 仍未降到 target 两项都回 NaN，由调用方按缺值处理。
            /// </summary>
            public async Task<(double Arrival, double Departure)> DeflateSpanAsync(double leaveBelow, double target, int timeoutSeconds, CancellationToken token)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (watch.Elapsed.TotalSeconds < timeoutSeconds)
                {
                    var crossed = ScanDeflate(leaveBelow, target);
                    if (crossed.HasValue) return crossed.Value;
                    await Task.Delay(100, token).ConfigureAwait(false);
                }
                return ScanDeflate(leaveBelow, target) ?? (double.NaN, double.NaN);
            }

            private (double Arrival, double Departure)? ScanDeflate(double leaveBelow, double target)
            {
                lock (_lock)
                {
                    double? departure = null;
                    for (var i = 0; i < _samples.Count; i++)
                    {
                        var p = _samples[i].P;
                        if (departure == null && p <= leaveBelow) departure = _samples[i].T;
                        if (p > target + ArrivalTolerance) continue;
                        // 降压不一定先经过 leaveBelow（平台本就低于该值时第一点就是终压），此时离开时刻取到达时刻，段内只算了降到 target 那一段
                        return (_samples[i].T, departure ?? _samples[i].T);
                    }
                }
                return null;
            }

            private async Task<double> CrossSecondsAsync(double target, int timeoutSeconds, CancellationToken token)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (watch.Elapsed.TotalSeconds < timeoutSeconds)
                {
                    double? crossed = null;
                    lock (_lock)
                    {
                        for (var i = 0; i < _samples.Count; i++)
                        {
                            if (_samples[i].P < target - ArrivalTolerance) continue;
                            crossed = _samples[i].T;
                            break;
                        }
                    }
                    if (crossed.HasValue) return crossed.Value;
                    await Task.Delay(100, token).ConfigureAwait(false);
                }
                return double.NaN;
            }

            /// <summary>本段最近一次采样回读（无采样时退回仪器最近一次稳压读回）</summary>
            public double LatestPressure()
            {
                lock (_lock)
                {
                    if (_samples.Count > 0) return _samples[^1].P;
                }
                return _simulator.LastStaticPressure;
            }

            /// <summary>本段末端窗口（WindowSeconds 秒）内采样的均值，用于读一段的起点/终点；无采样时退回仪器最近一次稳压读回</summary>
            public double WindowAverage()
            {
                lock (_lock)
                {
                    var now = _watch.Elapsed.TotalSeconds;
                    var recent = _samples.Where(s => now - s.T <= WindowSeconds).Select(s => s.P).ToList();
                    if (recent.Count > 0) return recent.Average();
                }
                return _simulator.LastStaticPressure;
            }

            /// <summary>本段采样的最低压力（无采样时回 NaN）：泄压段没泄到位时用来说明停在多少</summary>
            public double PhaseMinimum()
            {
                lock (_lock)
                {
                    if (_samples.Count == 0) return double.NaN;
                    return _samples.Min(s => s.P);
                }
            }

            /// <summary>退订并停采样（停采样只在开过的仪器上做）</summary>
            public async Task StopAsync()
            {
                _simulator.PressureSampled -= OnSample;
                if (!_simulator.RequiresSamplingForReadback) return;
                try { await _simulator.StopSamplingAsync(); }
                catch (Exception ex) { _log.Warn($"气路测试：停止压力采样失败：{ex.Message}"); }
            }

            private void OnSample(double pressure)
            {
                var at = _watch.Elapsed.TotalSeconds;
                lock (_lock) _samples.Add((at, pressure));
                if (_item.Length > 0) _sink?.Invoke(_item, new PressureSamplePoint(at, pressure));
            }
        }

        /// <summary>
        /// 静态压力序列的逐点驱动：全部压力点一次下发（点之间直接切压不泄回零），按序列回调的每点 Stable 信号逐点交回上层处理。
        /// 回调线程只置信号，仪器读值与产品指令都在消费循环里做；某点在 arriveTimeoutSeconds 内没到压，本轮后续点也测不到，抛 TimeoutException 由调用方收尾。
        /// </summary>
        /// <param name="holdSeconds">每点保持秒数（从该点回读稳压起计时），上层要在这段窗口内读完并下发指令</param>
        /// <param name="onPointBegin">等该点到压前调用（开本点曲线时间轴用）</param>
        /// <param name="onPointStable">该点回读稳压后调用（读值与指令窗口就是 holdSeconds）</param>
        private static async Task RunStaticPressureSequenceByPointsAsync(IBloodPressureSimulator simulator, double[] points,
            int holdSeconds, bool inflation, int arriveTimeoutSeconds,
            Func<int, Task> onPointBegin, Func<int, Task> onPointStable, CancellationToken token)
        {
            var arrived = points.Select(_ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
            void OnStable(int index, double pressure)
            {
                if (index >= 0 && index < arrived.Length) arrived[index].TrySetResult(true);
            }

            simulator.PressurePointStable += OnStable;
            try
            {
                await simulator.StartStaticPressureSequenceAsync(points, holdSeconds, inflation, token).ConfigureAwait(false);
                for (var i = 0; i < points.Length; i++)
                {
                    await onPointBegin(i).ConfigureAwait(false);
                    try
                    {
                        await arrived[i].Task.WaitAsync(TimeSpan.FromSeconds(arriveTimeoutSeconds), token).ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        throw new TimeoutException($"静态压力序列第 {i + 1} 点（{points[i]:0.#}）{arriveTimeoutSeconds}s 内没有回读稳压，本轮后续点无法测量");
                    }
                    await onPointStable(i).ConfigureAwait(false);
                }
            }
            finally
            {
                simulator.PressurePointStable -= OnStable;
            }
        }
    }
}
