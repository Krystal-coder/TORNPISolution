using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// TestItem 分部：血压 A06 动态压力精度（BP-004）/ A21 血压心率精度及重复性（BP-005），
    /// 含两项共用的逐轮采集（CollectBpReadingsAsync / PollBpResultAsync）与子表行工厂。
    /// </summary>
    public partial class TestItem
    {
        /// <summary>
        /// 血压动态压力精度测试（BP-004），产品通讯统一在 A16 初始化：
        /// ① 压力组与轮数取 血压模拟仪测试配置→动态血压→BP-004 压力组（只跑勾选行），心率全模块一个值（作激励参数）；逐组逐轮经 CollectBpReadingsAsync 执行完整充放气周期（需储气瓶产生脉搏波），每轮之间按配置休息；
        /// ② 一轮一项（项名 SpecItems.Bp004Round，与 Spec 目录同源，改点位/轮数自动跟随）：测试值 = max(|收缩压回读−设定|, |舒张压回读−设定|) 判 ≤±2mmHg；子表两行=收缩压/舒张压 各自 设定/回读/偏差/判定；
        /// ③ 每组一项重复性（项名 SpecItems.Bp004Repeat）：测试值 = max(收缩压各轮极差, 舒张压各轮极差) 判 ≤±2mmHg；子表按测试要求文档第 3 条“每轮一行 + 统计行”展示（列=收缩压/舒张压，行=设定值/第N轮/均值/与设定偏差/极大/极小/极差）；
        /// ④ 心率判定在 A21 血压心率精度及重复性测试；某轮无有效回读 → 该项不报告，由收尾扫描按读回缺失判 FAIL；有效轮数 <2 的组其重复性项同理。
        /// </summary>
        //测试项目**************************---A06---**************************血压动态压力精度测试（BP-004）
        public async Task BpDynamicAccuracyModule(TestItemResultCollector collector, CancellationToken token)
        {
            if (NoInstrumentDemo("血压动态压力精度测试")) return;
            var config = _configService.Load();
            if (config.ActiveSimulator == SimulatorType.None)
            {
                _log.Error("血压动态压力精度测试跳过：未选择血压模拟仪（仪器链接配置 → 模拟仪类型）");
                return;
            }

            var (groups, repeats, restSeconds, ventDuringRest) =
                GetBpAccuracyGroups(config.ActiveBpTestParams.BpAccuracy, "血压动态压力精度测试");
            if (groups.Length == 0) return;

            // 本轮项名即波形归属项（采集层按轮重开时间轴，只画给这一项）
            string RoundItem((int Systolic, int Diastolic, int HeartRate) g, int r) => SpecItems.Bp004Round(g.Systolic, g.Diastolic, r);

            // 脉搏量取血压模拟仪测试配置→动态血压（康伟达入 0x02 帧驱动脉搏波幅；BPA700 SDK 忽略）
            var pulseVolume = config.ActiveBpTestParams.DynamicBloodPressure.PulseVolume;

            using (var product = _productFactory())
            {
                var productAvailable = GetProductAvailable(config.NoProductDemoMode);

                // 经共享会话取模拟仪（接口调用：BPA700=SDK 回调状态机；康伟达=压力曲线判定）
                await using var lease = await _simSession.RentAsync(token);
                var simulator = lease.Simulator;

                if (!productAvailable)
                {
                    if (!config.NoProductDemoMode)
                    {
                        _log.Warn("BP-004 跳过测量轮次：产品不可用，且全部结果项依赖产品上报值（协议预留），空跑无意义；本轮不会向模拟仪发血压模拟指令，仪器不进入血压模拟页面属预期，请先检查产品串口接线/端口配置");
                        return;
                    }

                    // 无产品演示模式：只验证仪器侧指令与波形显示；结果项不报告，由收尾扫描按读回缺失统一判 FAIL
                    _log.Info("BP-004 无产品演示模式：逐组驱动模拟仪走血压模拟流程（短超时），结果项按产品读回缺失判 FAIL");
                    await simulator.StartSamplingAsync(token);
                    try
                    {
                        await CollectBpReadingsAsync(simulator, null, groups, repeats, pulseVolume, RoundItem, 0, false, token);
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
                        await simulator.StopSamplingAsync();
                    }

                    return;
                }

                await product.SetTestModeAsync(2, ct: token);

                var readings = await CollectBpReadingsAsync(simulator, product, groups, repeats, pulseVolume,
                    RoundItem, restSeconds, ventDuringRest, token);

                if (readings.Values.All(l => !l.Any(x => x != null)))
                {
                    _log.Warn("BP-004 FAIL：未取到任何产品上报值（0x7B 轮询为空），请检查产品串口连接与测量周期是否完成");
                    return;
                }

                const double BpTolerance = 2;   // mmHg，测试要求文档 BP-004：重复测试的偏差与极差均 ≤±2mmHg
                foreach (var g in groups)
                {
                    var list = readings[(g.Systolic, g.Diastolic, g.HeartRate)];
                    var valid = new List<ProductBpReading>();

                    // 一轮一项：本轮回读与设定值的偏差（收缩/舒张取大者）；本轮无有效回读则不报告，收尾判 FAIL
                    for (var r = 1; r <= repeats; r++)
                    {
                        var rd = r <= list.Count ? list[r - 1] : null;
                        if (rd == null) continue;
                        valid.Add(rd);

                        var sysErr = Math.Abs(rd.Systolic - g.Systolic);
                        var diaErr = Math.Abs(rd.Diastolic - g.Diastolic);
                        var roundName = SpecItems.Bp004Round(g.Systolic, g.Diastolic, r);
                        _log.Info($"{roundName}：设定 {g.Systolic}/{g.Diastolic}（心率 {g.HeartRate}） 回读 {rd.Systolic:0.0}/{rd.Diastolic:0.0} " +
                                  $"偏差 收缩={sysErr:0.0} 舒张={diaErr:0.0} → {(Math.Max(sysErr, diaErr) <= BpTolerance ? "PASS" : "FAIL")}");

                        await collector.SetAsync(roundName, new TestItemResult
                        {
                            Value = Math.Round(Math.Max(sysErr, diaErr), 2),
                            InstrumentText = $"设定 {g.Systolic}/{g.Diastolic}",
                            ProductText = $"回读 {rd.Systolic:0.0}/{rd.Diastolic:0.0}",
                            Points = new List<TestPointResult>
                            {
                                BpChannelPoint("收缩压", g.Systolic, rd.Systolic, sysErr, BpTolerance, "mmHg"),
                                BpChannelPoint("舒张压", g.Diastolic, rd.Diastolic, diaErr, BpTolerance, "mmHg"),
                            },
                        });
                    }

                    // 每组一项重复性：判据只落在极差行（收缩/舒张取大者 ≤±2mmHg），中间统计行不判定，避免整屏 PASS 误读
                    if (valid.Count >= 2)
                    {
                        var sysVals = valid.Select(x => x.Systolic).ToList();
                        var diaVals = valid.Select(x => x.Diastolic).ToList();
                        var gSysRange = sysVals.Max() - sysVals.Min();
                        var gDiaRange = diaVals.Max() - diaVals.Min();
                        _log.Info($"BP-004重复性 {g.Systolic}/{g.Diastolic}（{valid.Count}轮）：" +
                                  $"收缩 均值={sysVals.Average():0.0} 极大={sysVals.Max():0.0} 极小={sysVals.Min():0.0} 极差={gSysRange:0.0}；" +
                                  $"舒张 均值={diaVals.Average():0.0} 极大={diaVals.Max():0.0} 极小={diaVals.Min():0.0} 极差={gDiaRange:0.0}");

                        await collector.SetAsync(SpecItems.Bp004Repeat(g.Systolic, g.Diastolic), new TestItemResult
                        {
                            Value = Math.Round(Math.Max(gSysRange, gDiaRange), 2),
                            InstrumentText = $"收缩压极差 {gSysRange:0.0}",
                            ProductText = $"舒张压极差 {gDiaRange:0.0}",
                            Points = BuildBpRepeatRows(g, list, repeats, valid, BpTolerance),
                        });
                    }
                    else
                        _log.Warn($"BP-004重复性 压力组 {g.Systolic}/{g.Diastolic}：有效回读仅 {valid.Count} 轮（<2），极差无统计意义，该项不报告由收尾判 FAIL");
                }
                _log.Info($"BP-004 统计完成：{groups.Length} 组×{repeats} 轮（轮间休息 {restSeconds}s），逐轮偏差与每组极差已按项上报");
            }
        }

        //测试项目**************************---A21---**************************血压心率精度及重复性测试（BP-005）
        /// <summary>
        /// 血压心率精度及重复性测试（BP-005）：压力固定一组、心率按配置逐点测，采集复用 BP-004 同一套流程（完整充放气周期 + 轮间休息 + 每轮独立曲线），判定看 0x7C 回读的心率；
        /// ① 一轮一项（SpecItems.Bp005Round）：测试值 = |本轮心率回读 − 设定|，判 ≤ max(5, 设定×5%)；
        /// ② 每点一项平均值（SpecItems.Bp005Average）：测试值 = 各轮心率均值，只展示不判定（文档第 2 条只要求“取平均值”，判定已在逐轮上），子表给 均值/与设定偏差/极大/极小/极差；
        /// ③ 某轮无有效心率回读 → 该项不报告，由收尾扫描按读回缺失判 FAIL；有效轮数 <2 的点其平均值项同理。
        /// </summary>
        public async Task BpHeartRateAccuracyModule(TestItemResultCollector collector, CancellationToken token)
        {
            if (NoInstrumentDemo("血压心率精度及重复性测试")) return;
            var config = _configService.Load();
            if (config.ActiveSimulator == SimulatorType.None)
            {
                _log.Error("血压心率精度及重复性测试跳过：未选择血压模拟仪（仪器链接配置 → 模拟仪类型）");
                return;
            }

            var (groups, repeats, restSeconds, ventDuringRest) =
                GetBpHeartRateGroups(config.ActiveBpTestParams.BpHeartRate, "血压心率精度及重复性测试");
            if (groups.Length == 0) return;

            string RoundItem((int Systolic, int Diastolic, int HeartRate) g, int r) => SpecItems.Bp005Round(g.HeartRate, r);

            var pulseVolume = config.ActiveBpTestParams.DynamicBloodPressure.PulseVolume;

            using (var product = _productFactory())
            {
                var productAvailable = GetProductAvailable(config.NoProductDemoMode);
                await using var lease = await _simSession.RentAsync(token);
                var simulator = lease.Simulator;

                if (!productAvailable)
                {
                    if (!config.NoProductDemoMode)
                    {
                        _log.Warn("BP-005 跳过测量轮次：心率判定完全依赖产品 0x7C 上报值，产品不可用时空跑无意义（不向模拟仪发血压模拟指令）");
                        return;
                    }

                    _log.Info("BP-005 无产品演示模式：逐点驱动模拟仪走血压模拟流程（短超时），结果项按产品读回缺失判 FAIL");
                    await simulator.StartSamplingAsync(token);
                    try
                    {
                        await CollectBpReadingsAsync(simulator, null, groups, repeats, pulseVolume, RoundItem, 0, false, token);
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
                        await simulator.StopSamplingAsync();
                    }
                    return;
                }

                // 血压测试模式（0x73 mode=2）：产品不自行充放气，靠 0x79 开始测量
                await product.SetTestModeAsync(2, ct: token);

                var readings = await CollectBpReadingsAsync(simulator, product, groups, repeats, pulseVolume,
                    RoundItem, restSeconds, ventDuringRest, token);

                if (readings.Values.All(l => !l.Any(x => x != null)))
                {
                    _log.Warn("BP-005 FAIL：未取到任何产品上报值（0x7B 轮询为空），请检查产品串口连接与测量周期是否完成");
                    return;
                }

                foreach (var g in groups)
                {
                    var tol = Math.Max(5, g.HeartRate * 0.05);   // 测试要求文档：±5 次/分 或 ±5% 取较大值
                    var list = readings[(g.Systolic, g.Diastolic, g.HeartRate)];
                    var hrs = new List<double>();

                    for (var r = 1; r <= repeats; r++)
                    {
                        var rd = r <= list.Count ? list[r - 1] : null;
                        if (rd == null || double.IsNaN(rd.HeartRate)) continue;
                        hrs.Add(rd.HeartRate);

                        var dev = Math.Abs(rd.HeartRate - g.HeartRate);
                        var roundName = SpecItems.Bp005Round(g.HeartRate, r);
                        _log.Info($"{roundName}：设定压力 {g.Systolic}/{g.Diastolic} 心率 {g.HeartRate} 回读 {rd.HeartRate:0.0} 偏差 {dev:0.0} → {(dev <= tol ? "PASS" : "FAIL")}");

                        await collector.SetAsync(roundName, new TestItemResult
                        {
                            Value = Math.Round(dev, 2),
                            InstrumentText = $"设定 {g.HeartRate}",
                            ProductText = $"回读 {rd.HeartRate:0.0}",
                            Points = new List<TestPointResult>
                            {
                                BpChannelPoint("心率", g.HeartRate, rd.HeartRate, dev, tol, "BPM"),
                            },
                        });
                    }

                    if (hrs.Count >= 2)
                    {
                        _log.Info($"BP-005平均值 心率 {g.HeartRate}（{hrs.Count}轮）：均值={hrs.Average():0.0} 与设定偏差={Math.Abs(hrs.Average() - g.HeartRate):0.0} 极大={hrs.Max():0.0} 极小={hrs.Min():0.0} 极差={hrs.Max() - hrs.Min():0.0}");
                        await collector.SetAsync(SpecItems.Bp005Average(g.HeartRate), new TestItemResult
                        {
                            Value = Math.Round(hrs.Average(), 2),
                            DisplayOnly = true,
                            InstrumentText = $"设定 {g.HeartRate}",
                            ProductText = $"{hrs.Count} 轮平均 {hrs.Average():0.0}",
                            Points = new List<TestPointResult> { BpRepeatPoint($"{g.HeartRate} BPM", g.HeartRate, hrs, tol) },
                        });
                    }
                    else
                        _log.Warn($"BP-005平均值 心率 {g.HeartRate}：有效回读仅 {hrs.Count} 轮（<2），均值无统计意义，该项不报告由收尾判 FAIL");
                }
                _log.Info($"BP-005 统计完成：{groups.Length} 个心率点×{repeats} 轮（轮间休息 {restSeconds}s）");
            }
        }

        /// <summary>BP-004 压力组取用（A06 项清单与执行轮次同一来源，与 Spec 目录构建同口径）：只取勾选行、取整后去重（两行完全相同时采集字典的键会互相覆盖）；心率全模块一个值；
        /// 轮数少于 3 抬到 3（少于 3 轮取极差统计意义不足）；未勾选任何行时记日志返回空数组（调用方直接结束、不发仪器指令）</summary>
        private ((int Systolic, int Diastolic, int HeartRate)[] Groups, int Repeats, int RestSeconds, bool VentDuringRest)
            GetBpAccuracyGroups(BpaBpAccuracyParams cfg, string moduleName)
        {
            var groups = cfg.Groups.Where(g => g.Enabled)
                .Select(g => (Systolic: (int)Math.Round(g.Systolic), Diastolic: (int)Math.Round(g.Diastolic), HeartRate: cfg.HeartRate))
                .Distinct()
                .ToArray();
            if (groups.Length == 0)
                _log.Warn($"{moduleName} 跳过：BP-004 压力组未勾选任何参与测试的行（血压模拟仪测试配置 → 动态血压 → BP-004 压力组）");
            return (groups, Math.Max(3, cfg.Repeats), Math.Max(0, cfg.RestSeconds), cfg.VentDuringRest);
        }

        /// <summary>BP-005 心率点取用（A21 项清单与执行轮次同一来源，与 Spec 目录同口径）：压力固定一组，心率只取勾选行并去重（同一心率重复一行没意义）</summary>
        private ((int Systolic, int Diastolic, int HeartRate)[] Groups, int Repeats, int RestSeconds, bool VentDuringRest)
            GetBpHeartRateGroups(BpaHeartRateAccuracyParams cfg, string moduleName)
        {
            var sys = (int)Math.Round(cfg.Systolic);
            var dia = (int)Math.Round(cfg.Diastolic);
            var groups = cfg.HeartRates.Where(p => p.Enabled).Select(p => p.Value).Distinct()
                .Select(hr => (Systolic: sys, Diastolic: dia, HeartRate: hr))
                .ToArray();
            if (groups.Length == 0)
                _log.Warn($"{moduleName} 跳过：BP-005 心率点未勾选任何参与测试的行（血压模拟仪测试配置 → 动态血压 → BP-005 心率点）");
            return (groups, Math.Max(3, cfg.Repeats), Math.Max(0, cfg.RestSeconds), cfg.VentDuringRest);
        }

        /// <summary>轮间休息（产品安全）：每轮测完休息 restSeconds 秒再开下一轮/下一组（含末轮收尾）；选「休息时泄压」先泄回零、选「不泄压」仪器保持待命；休息期间不绘波形，取消可立即打断</summary>
        private async Task RestAfterRoundAsync(IBloodPressureSimulator simulator, int round, int repeats,
            int restSeconds, bool ventDuringRest, CancellationToken token)
        {
            if (restSeconds <= 0) return;
            if (ventDuringRest)
            {
                try { await simulator.VentToZeroAsync(ct: token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { _log.Warn($"轮间泄压失败（继续休息）：{ex.Message}"); }
            }
            _log.Info($"第 {round}/{repeats} 轮测完，休息 {restSeconds}s…");
            await Task.Delay(restSeconds * 1000, token);
        }

        /// <summary>
        /// 血压测量轮次采集（A06 的 BP-004 轮次/重复性与 BP-005 心率共用同一套流程）：逐点位逐轮执行 产品 0x79 开始 → 模拟仪 RunBpMeasurementAsync（0x02 血压模拟帧带脉搏量，等完整充放气曲线）→ 轮询 0x7B 取收缩/舒张/平均压 + 0x7C 取心率 → 0x79 关闭。
        /// product 为 null（无产品演示模式）时不发产品指令、用短超时只验证仪器侧流程，回读格全部为空，也不休息。
        /// 波形按轮独立：每轮开始重开时间轴、只把本轮采样画给 waveItemName(g, r) 指定的那一项，轮一结束就退订，休息段（每轮后都等一次，含末轮）不进任何曲线。
        /// 返回：各点位的产品回读列表（键为点位三元组，与入参 groups 逐项对应）；列表按轮次占格（下标 i = 第 i+1 轮，null = 该轮没取到有效回读），一轮一项时下标才能与“第 N 轮”严格对上。
        /// </summary>
        private async Task<Dictionary<(int, int, int), List<ProductBpReading?>>> CollectBpReadingsAsync(
            IBloodPressureSimulator simulator, IProductController? product,
            (int Systolic, int Diastolic, int HeartRate)[] groups, int repeats, double pulseVolume,
            Func<(int Systolic, int Diastolic, int HeartRate), int, string?> waveItemName,
            int restSeconds, bool ventDuringRest, CancellationToken token)
        {
            var readings = new Dictionary<(int, int, int), List<ProductBpReading?>>();
            foreach (var g in groups)
            {
                var list = new List<ProductBpReading?>(repeats);
                readings[(g.Systolic, g.Diastolic, g.HeartRate)] = list;
                for (var r = 1; r <= repeats; r++)
                {
                    // 轮次级追踪上下文：本轮的 0x79/0x7B/0x7C 串口日志统一带 [A06…·BP@120/80/80#R1] 前缀
                    using var roundScope = TraceScope.Push($"BP@{g.Systolic}/{g.Diastolic}/{g.HeartRate}#R{r}");
                    _log.Info($"血压测量轮 组 SYS={g.Systolic} DIA={g.Diastolic} HR={g.HeartRate} 第 {r}/{repeats} 次：...");

                    // 本轮一条独立时间轴，采样只画给本轮那一项（waveItemName 返回 null 则本轮不画）
                    var roundItem = waveItemName(g, r);
                    var roundWatch = System.Diagnostics.Stopwatch.StartNew();
                    void OnRoundSample(double pressure)
                    {
                        if (roundItem != null)
                            PressureSampled?.Invoke(roundItem, new PressureSamplePoint(roundWatch.Elapsed.TotalSeconds, pressure));
                    }
                    simulator.PressureSampled += OnRoundSample;
                    var measureStarted = false;
                    var measureClosed = false;
                    try
                    {
                        if (product == null)
                        {
                            // 无产品演示：短超时只验证仪器侧指令与波形（异常由调用方按“不影响收尾判定”处理）
                            await simulator.RunBpMeasurementAsync(g.Systolic, g.Diastolic, g.HeartRate, pulseVolume,
                                waitStartTimeoutMs: 8_000, totalTimeoutMs: 15_000, ct: token);
                            list.Add(null);
                            continue;
                        }

                        // 不发开始测量指令（0x79）产品不会自行充放气：曲线判定等不到充气、也永远没有上报结果
                        await product.StartBpMeasureAsync(true, token);
                        measureStarted = true;
                        await simulator.RunBpMeasurementAsync(g.Systolic, g.Diastolic, g.HeartRate, pulseVolume, ct: token);
                        // 实测当前固件不主动推 0x7B（测量期间只周期推 0xCA，协议未定义），故曲线结束后主动轮询 0x7B 拿收缩压/舒张压/平均压，心率由 0x7C 单独读回
                        var reading = await PollBpResultAsync(product, token);
                        if (reading != null && reading.Systolic > 0)
                        {
                            var hr = await product.ReadHeartRateAsync(token);
                            if (!double.IsNaN(hr)) reading = reading with { HeartRate = hr };
                            list.Add(reading);
                            _log.Info($"产品上报：SYS={reading.Systolic:0.0} DIA={reading.Diastolic:0.0} MAP={reading.MeanPressure:0.0} HR={reading.HeartRate:0.0}");
                        }
                        else
                        {
                            // 全零/空响应 = 本轮测量未完成（收缩压 0 参与偏差计算会顶出天量误差），按无回读占格
                            list.Add(null);
                            _log.Warn($"血压测量轮 组 SYS={g.Systolic} DIA={g.Diastolic} HR={g.HeartRate} 第 {r} 次：未取到产品有效测量结果（0x7B 轮询为空或收缩压为 0）");
                        }
                        // 关闭本次测量（0x79 status=1）：协议要求测完发一次关闭，否则下次开始不被接受
                        await product.StartBpMeasureAsync(false, token);
                        measureClosed = true;
                    }
                    catch
                    {
                        // 取消或异常正好落在这一轮中间：开始测量发出去了却没走到关闭，补一次，否则产品挂着本次测量，后面每一轮/下一个模块的 0x79 开始都不被接受
                        // 都用 None 令牌：点“停止”时这一句恰恰最需要发得出去。
                        if (product != null && measureStarted && !measureClosed)
                        {
                            try { await product.StartBpMeasureAsync(false, CancellationToken.None); }
                            catch (Exception ex) { _log.Warn($"补发关闭测量（0x79 status=1）失败：{ex.Message}"); }
                        }

                        // 本轮被打断时袖带可能仍带压：尽力泄回零，避免设备带压进入下一个模块
                        try { await simulator.VentToZeroAsync(ct: CancellationToken.None); }
                        catch (Exception ex) { _log.Warn($"轮次中断后泄压失败：{ex.Message}"); }

                        throw;
                    }
                    finally
                    {
                        simulator.PressureSampled -= OnRoundSample;   // 本轮曲线到此为止，休息段不再绘制
                    }

                    // 每轮测完让产品休息（含末轮与换组之间；演示轮走上面的 continue 不休息）
                    await RestAfterRoundAsync(simulator, r, repeats, restSeconds, ventDuringRest, token);
                }
            }
            return readings;
        }

        /// <summary>
        /// 轮询血压测量结果（0x7B，产品不主动推送）：测量曲线结束后每 500ms 轮询一次，收缩压 > 0 立即返回；10 次仍未取到有效结果返回最后一次响应（全零表示测量未完成，统计按 FAIL 判）。
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

        /// <summary>BP-004/005 子表行（单值口径）：设定=目标值、回读=产品上报值、偏差=|回读−设定|（心率容差按组 max(5, 设定×5%)）；
        /// 口径 Channel 下 UI 列头为 设定值/回读值/偏差（行名列“通道”），BP-005 按点位分组时改传 Group（行名列“分组”）</summary>
        private static TestPointResult BpChannelPoint(string name, double target, double reading, double error, double tolerance, string unit,
            string schema = TestPointSchemas.Channel)
            => new()
            {
                Schema = schema,
                Unit = unit,
                Name = name,
                TargetPressure = target,
                InstrumentValue = Math.Round(target, 2),
                ProductValue = Math.Round(reading, 2),
                Error = Math.Round(error, 2),
                Pass = error <= tolerance,
                Remark = error <= tolerance ? string.Empty : $"偏差 {error:0.0} 超过 ±{tolerance:0.#}{unit}",
            };

        /// <summary>BP-005 心率平均值子表行（测试要求文档第 2 条“每组测量3次取平均值”）：列=设定值 / 均值 / 与设定偏差 / 极大与极小 / 极差，整行只展示不判定（合格与否看各轮项）；
        /// 极差超出容差时写进备注</summary>
        private static TestPointResult BpRepeatPoint(string name, double target, List<double> values, double tolerance)
        {
            var range = values.Max() - values.Min();
            var avg = values.Average();
            var dev = Math.Abs(avg - target);
            return new TestPointResult
            {
                Schema = TestPointSchemas.Repeat,
                Name = name,
                TargetPressure = target,
                Average = Math.Round(avg, 2),
                Max = Math.Round(values.Max(), 2),
                Min = Math.Round(values.Min(), 2),
                Range = Math.Round(range, 2),
                Error = Math.Round(dev, 2),
                ShowVerdict = false,
                Remark = range > tolerance ? $"极差 {range:0.0} 超 ±{tolerance:0.#}（仅统计）" : string.Empty,
            };
        }

        /// <summary>BP-004 重复性子表（每轮一行 + 统计行）：列=收缩压/舒张压，行=设定值 → 第1..N轮实测（按轮次占格，本轮没回读显 --，下标与“第 N 轮”严格对得上）→ 均值 → 与设定偏差 → 极大值 → 极小值 → 极差；
        /// 只极差行参与判定（判据同父项：收缩/舒张极差取大者 ≤±2），统计量只取有效轮</summary>
        private static List<TestPointResult> BuildBpRepeatRows((int Systolic, int Diastolic, int HeartRate) g,
            IReadOnlyList<ProductBpReading?> rounds, int repeats, IReadOnlyList<ProductBpReading> valid, double tolerance)
        {
            var sysVals = valid.Select(x => x.Systolic).ToList();
            var diaVals = valid.Select(x => x.Diastolic).ToList();
            var sysRange = sysVals.Max() - sysVals.Min();
            var diaRange = diaVals.Max() - diaVals.Min();
            var rangePass = Math.Max(sysRange, diaRange) <= tolerance;

            var rows = new List<TestPointResult>
            {
                BpRepeatRow("设定值", g.Systolic.ToString("0.0"), g.Diastolic.ToString("0.0")),
            };
            for (var r = 1; r <= repeats; r++)
            {
                var rd = r <= rounds.Count ? rounds[r - 1] : null;
                rows.Add(BpRepeatRow($"第{r}轮",
                    rd == null ? "--" : NumOr(rd.Systolic),
                    rd == null ? "--" : NumOr(rd.Diastolic)));
            }

            rows.Add(BpRepeatRow("均值", NumOr(sysVals.Average()), NumOr(diaVals.Average())));
            rows.Add(BpRepeatRow("与设定偏差",
                NumOr(Math.Abs(sysVals.Average() - g.Systolic)),
                NumOr(Math.Abs(diaVals.Average() - g.Diastolic))));
            rows.Add(BpRepeatRow("极大值", NumOr(sysVals.Max()), NumOr(diaVals.Max())));
            rows.Add(BpRepeatRow("极小值", NumOr(sysVals.Min()), NumOr(diaVals.Min())));
            rows.Add(BpRepeatRow("极差",
                NumOr(sysRange), NumOr(diaRange),
                rangePass,
                Math.Round(Math.Max(sysRange, diaRange), 2),
                rangePass ? string.Empty
                    : $"{(sysRange > tolerance ? "收缩压" : "舒张压")}极差 {Math.Max(sysRange, diaRange):0.0} 超 ±{tolerance:0.#}"));
            return rows;
        }

        /// <summary>重复性子表行：pass 为 null 时只展示不判定（均值/极大/极小等中间行末列留空）</summary>
        private static TestPointResult BpRepeatRow(string name, string systolic, string diastolic,
            bool? pass = null, double? error = null, string remark = "")
            => new()
            {
                Schema = TestPointSchemas.RepeatMatrix,
                Name = name,
                Unit = "mmHg",
                Cells = new List<string> { systolic, diastolic },
                ShowVerdict = pass.HasValue,
                Pass = pass ?? true,
                Error = error,
                Remark = remark,
            };

        private static string NumOr(double value) => value.ToString("0.0");

    }
}
