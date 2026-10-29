using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// TestItem 分部：血压 A06 动态压力精度（BP-004）/ A21 血压心率精度及重复性（BP-005），
    /// 两项共用的逐轮采集（CollectBpReadingsAsync：产测态 0xCA 开始 → 等 0xCA 结果帧 → 0xCC 结束）与子表行工厂。
    /// </summary>
    public partial class TestItem
    {
        /// <summary>模拟仪周期等待相对产品预算的余量（毫秒）：让结果帧超时成为唯一判据，避免模拟仪先超时自行停输出、把产品未测完记成仪器问题</summary>
        private const int CycleWaitMarginMs = 10_000;

        /// <summary>
        /// 血压动态压力精度测试（BP-004），产品通讯统一在 A16 初始化：
        /// ① 压力组与轮数取 血压模拟仪测试配置→动态血压→BP-004 压力组（只跑勾选行，每组自带心率作激励参数）；逐组逐轮经 CollectBpReadingsAsync 执行（产测态 0xCA 开始 → 等 0xCA 结果帧 → 0xCC 结束 + 停仪器输出，每轮之间按配置休息）；
        /// ② 一轮一项（项名 SpecItems.Bp004Round，与 Spec 目录同源，改点位/轮数自动跟随）：测试值 = max(|收缩压回读−设定|, |舒张压回读−设定|) 判 ≤±2mmHg；子表两行=收缩压/舒张压 各自 设定/回读/偏差/判定；
        /// ③ 每组一项重复性（项名 SpecItems.Bp004Repeat，仅 2 轮起建项）：测试值 = max(收缩压各轮极差, 舒张压各轮极差) 判 ≤±2mmHg；子表列=收缩压/舒张压，行=设定值/第N轮/均值/与设定偏差/极差；
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

            var (groups, repeats, restSeconds, ventDuringRest, singleMeasureTimeoutMs) =
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
                    try
                    {
                        await CollectBpReadingsAsync(simulator, null, groups, repeats, pulseVolume, RoundItem, 0, false, singleMeasureTimeoutMs, token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _log.Warn($"演示轮异常（不影响收尾判定）：{ex.Message}");
                    }

                    return;
                }

                if (!await PrepareProductForBpMeasureAsync(product, token)) return;

                var (readings, roundSeconds) = await CollectBpReadingsAsync(simulator, product, groups, repeats, pulseVolume,
                    RoundItem, restSeconds, ventDuringRest, singleMeasureTimeoutMs, token);

                if (readings.Values.All(l => !l.Any(x => x != null)))
                {
                    _log.Warn("BP-004 FAIL：未取到任何产品上报值（0xCA 结果帧全缺），请检查产品串口连接与测量周期是否完成");
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
                            InstrumentText = $"{g.Systolic}/{g.Diastolic}",
                            ProductText = $"{rd.Systolic:0.##}/{rd.Diastolic:0.##}/{rd.MeanPressure:0.##}/{rd.HeartRate:0.##}",
                            MeasuredSeconds = BpRoundSeconds(roundSeconds, g, r),
                            Points = new List<TestPointResult>
                            {
                                BpChannelPoint("收缩压", g.Systolic, rd.Systolic, sysErr, BpTolerance, "mmHg"),
                                BpChannelPoint("舒张压", g.Diastolic, rd.Diastolic, diaErr, BpTolerance, "mmHg"),
                            },
                        });
                    }

                    // 每组一项重复性：判据只落在极差行（收缩/舒张取大者 ≤±2mmHg），中间统计行不判定，避免整屏 PASS 误读
                    if (repeats < 2)
                        _log.Info($"BP-004重复性 压力组 {g.Systolic}/{g.Diastolic}：配置 {repeats} 轮，无极差可算，本组不测重复性（目录也未建该项）");
                    else if (valid.Count >= 2)
                    {
                        var sysVals = valid.Select(x => x.Systolic).ToList();
                        var diaVals = valid.Select(x => x.Diastolic).ToList();
                        var sys = RepeatStats.Of(sysVals);
                        var dia = RepeatStats.Of(diaVals);
                        _log.Info($"BP-004重复性 {g.Systolic}/{g.Diastolic}（{valid.Count}轮）：" +
                                  $"收缩 均值={sys.Average:0.0} 极大={sys.Max:0.0} 极小={sys.Min:0.0} 极差={sys.Range:0.0}；" +
                                  $"舒张 均值={dia.Average:0.0} 极大={dia.Max:0.0} 极小={dia.Min:0.0} 极差={dia.Range:0.0}");

                        await collector.SetAsync(SpecItems.Bp004Repeat(g.Systolic, g.Diastolic), new TestItemResult
                        {
                            Value = Math.Round(Math.Max(sys.Range, dia.Range), 2),
                            InstrumentText = $"收缩压极差 {sys.Range:0.0}",
                            ProductText = $"舒张压极差 {dia.Range:0.0}",
                            // 文本统计行（界面子表按 RepeatMatrix 口径显示）+ 数值统计行（RepeatStats：只入库/出报表，
                            // 保存服务与报表图表直接读数值列，不解析文本行）
                            Points = BuildBpRepeatRows(g, list, repeats, sys, dia, BpTolerance)
                                .Concat(new[]
                                {
                                    BpRepeatStatsRow("收缩压", g.Systolic, sys),
                                    BpRepeatStatsRow("舒张压", g.Diastolic, dia),
                                }).ToList(),
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
        /// 血压心率精度及重复性测试（BP-005）：压力固定一组、心率按配置逐点测，采集复用 BP-004 同一套流程（产测态 0xCA 开始/0xCC 结束 + 轮间休息 + 每轮独立曲线），判定看 0xCA 结果帧里的心率；
        /// ① 一轮一项（SpecItems.Bp005Round）：测试值 = |本轮心率回读 − 设定|，判 ≤ max(5, 设定×5%)；
        /// ② 每点一项平均值（SpecItems.Bp005Average）：测试值 = 各轮心率均值（1 轮时即该轮自己），只展示不判定（文档第 2 条只要求“取平均值”，判定已在逐轮上），子表给 均值/与设定偏差，2 轮起才有极大/极小/极差；
        /// ③ 某轮无有效心率回读 → 该项不报告，由收尾扫描按读回缺失判 FAIL；某点一轮有效回读都没有时其平均值项同理。
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

            var (groups, repeats, restSeconds, ventDuringRest, singleMeasureTimeoutMs) =
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
                        _log.Warn("BP-005 跳过测量轮次：心率判定完全依赖产品 0xCA 结果帧上报值，产品不可用时空跑无意义（不向模拟仪发血压模拟指令）");
                        return;
                    }

                    _log.Info("BP-005 无产品演示模式：逐点驱动模拟仪走血压模拟流程（短超时），结果项按产品读回缺失判 FAIL");
                    try
                    {
                        await CollectBpReadingsAsync(simulator, null, groups, repeats, pulseVolume, RoundItem, 0, false, singleMeasureTimeoutMs, token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _log.Warn($"演示轮异常（不影响收尾判定）：{ex.Message}");
                    }
                    return;
                }

                if (!await PrepareProductForBpMeasureAsync(product, token)) return;

                var (readings, roundSeconds) = await CollectBpReadingsAsync(simulator, product, groups, repeats, pulseVolume,
                    RoundItem, restSeconds, ventDuringRest, singleMeasureTimeoutMs, token);

                if (readings.Values.All(l => !l.Any(x => x != null)))
                {
                    _log.Warn("BP-005 FAIL：未取到任何产品上报值（0xCA 结果帧全缺），请检查产品串口连接与测量周期是否完成");
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
                            InstrumentText = $"{g.HeartRate}",
                            ProductText = $"{rd.HeartRate:0.##}",
                            MeasuredSeconds = BpRoundSeconds(roundSeconds, g, r),
                            Points = new List<TestPointResult>
                            {
                                BpChannelPoint("心率", g.HeartRate, rd.HeartRate, dev, tol, "BPM"),
                            },
                        });
                    }

                    if (hrs.Count >= 1)
                    {
                        _log.Info($"BP-005平均值 心率 {g.HeartRate}（{hrs.Count}轮）：均值={hrs.Average():0.0} 与设定偏差={Math.Abs(hrs.Average() - g.HeartRate):0.0}" +
                                  (hrs.Count >= 2 ? $" 极差={hrs.Max() - hrs.Min():0.0}" : "（单轮，无极差）"));
                        await collector.SetAsync(SpecItems.Bp005Average(g.HeartRate), new TestItemResult
                        {
                            Value = Math.Round(hrs.Average(), 2),
                            DisplayOnly = true,
                            InstrumentText = $"{g.HeartRate}",
                            ProductText = $"{hrs.Count} 轮平均 {hrs.Average():0.0}",
                            Points = new List<TestPointResult> { BpRepeatPoint($"{g.HeartRate} BPM", g.HeartRate, hrs, tol) },
                        });
                    }
                    else
                        _log.Warn($"BP-005平均值 心率 {g.HeartRate}：无有效回读轮，均值算不出，该项不报告由收尾判 FAIL");
                }
                _log.Info($"BP-005 统计完成：{groups.Length} 个心率点×{repeats} 轮（轮间休息 {restSeconds}s）");
            }
        }

        /// <summary>BP-004 压力组取用（A06 项清单与执行轮次同一来源，与 Spec 目录构建同口径）：只取勾选行、血压取整后按收缩/舒张去重（项名只带血压，同血压多行会撞名，心率取先出现的一行并记日志）；
        /// 轮数按配置现算（SpecItems.EffectiveRepeats，最少 1 轮）；单次测量超时取配置秒数（最少 1 秒）；未勾选任何行时记日志返回空数组（调用方直接结束、不发仪器指令）</summary>
        private ((int Systolic, int Diastolic, int HeartRate)[] Groups, int Repeats, int RestSeconds, bool VentDuringRest, int SingleMeasureTimeoutMs)
            GetBpAccuracyGroups(BpaBpAccuracyParams cfg, string moduleName)
        {
            var pairs = cfg.Groups.Where(g => g.Enabled)
                .GroupBy(g => ((int)Math.Round(g.Systolic), (int)Math.Round(g.Diastolic)));

            var groups = new List<(int Systolic, int Diastolic, int HeartRate)>();
            foreach (var pair in pairs)
            {
                var heartRates = pair.Select(g => g.HeartRate).Distinct().ToArray();
                if (heartRates.Length > 1)
                    _log.Warn($"{moduleName}：压力组 {pair.Key.Item1}/{pair.Key.Item2} 有 {pair.Count()} 行且心率不同（{string.Join("/", heartRates)}），"
                              + $"项名只带血压、两轮次会撞名，只跑先出现的一行（心率 {heartRates[0]}），其余行忽略");
                groups.Add((pair.Key.Item1, pair.Key.Item2, heartRates[0]));
            }

            if (groups.Count == 0)
                _log.Warn($"{moduleName} 跳过：BP-004 压力组未勾选任何参与测试的行（血压模拟仪测试配置 → 动态血压 → BP-004 压力组）");
            return (groups.ToArray(), SpecItems.EffectiveRepeats(cfg.Repeats), Math.Max(0, cfg.RestSeconds), cfg.VentDuringRest,
                Math.Max(1, cfg.SingleMeasureTimeoutSeconds) * 1000);
        }

        /// <summary>BP-005 心率点取用（A21 项清单与执行轮次同一来源，与 Spec 目录同口径）：压力固定一组，心率只取勾选行并去重（同一心率重复一行没意义）；轮数按配置现算（最少 1 轮）；单次测量超时取配置秒数（最少 1 秒）</summary>
        private ((int Systolic, int Diastolic, int HeartRate)[] Groups, int Repeats, int RestSeconds, bool VentDuringRest, int SingleMeasureTimeoutMs)
            GetBpHeartRateGroups(BpaHeartRateAccuracyParams cfg, string moduleName)
        {
            var sys = (int)Math.Round(cfg.Systolic);
            var dia = (int)Math.Round(cfg.Diastolic);
            var groups = cfg.HeartRates.Where(p => p.Enabled).Select(p => p.Value).Distinct()
                .Select(hr => (Systolic: sys, Diastolic: dia, HeartRate: hr))
                .ToArray();
            if (groups.Length == 0)
                _log.Warn($"{moduleName} 跳过：BP-005 心率点未勾选任何参与测试的行（血压模拟仪测试配置 → 动态血压 → BP-005 心率点）");
            return (groups, SpecItems.EffectiveRepeats(cfg.Repeats), Math.Max(0, cfg.RestSeconds), cfg.VentDuringRest,
                Math.Max(1, cfg.SingleMeasureTimeoutSeconds) * 1000);
        }

        /// <summary>血压测量轮的产测态前置（BP-004/BP-005 共用）：打开产品串口 → 发进入产测模式（0x07/0x01）；A16 已发过一次，这里重发是为了单独跑本模块时也能起测，兼作防休眠。返回 false 表示串口打不开，调用方直接结束（未报项由收尾扫描判 FAIL）</summary>
        private async Task<bool> PrepareProductForBpMeasureAsync(IProductController product, CancellationToken token)
        {
            try
            {
                await product.OpenAsync(token);
                await product.EnterProductionTestModeAsync(token);
                _log.Info("产品串口已打开并进入产测模式（0x07/0x01），可开始完整血压测量");
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"血压测量前置失败：产品串口打开或进入产测模式异常（{ex.Message}），本模块不驱动模拟仪，未报项按读回缺失判 FAIL");
                return false;
            }
        }

        /// <summary>轮间休息（产品安全）：每轮测完休息 restSeconds 秒再开下一轮/下一组；本模块末轮只泄压不休息（后面没有下一轮要护）；选「休息时泄压」泄回零与休息计时并行、选「不泄压」仪器保持待命；休息期间不绘波形，取消可立即打断</summary>
        private async Task RestAfterRoundAsync(IBloodPressureSimulator simulator, int round, int repeats,
            int restSeconds, bool ventDuringRest, bool lastRound, CancellationToken token)
        {
            if (restSeconds <= 0) return;

            // 末轮省的是等待秒数，泄压照做：设备不能带压进入下一个模块
            var ventTask = VentForRestAsync(simulator, ventDuringRest, token);
            try
            {
                if (lastRound)
                    _log.Info($"第 {round}/{repeats} 轮测完（本模块末轮）：不再休息，仅泄压回零");
                else
                {
                    _log.Info($"第 {round}/{repeats} 轮测完，休息 {restSeconds}s…");
                    await Task.Delay(restSeconds * 1000, token).ConfigureAwait(false);
                }
            }
            finally
            {
                // 取消时也要等泄压任务结束（它随 token 一起中止），否则它与本轮异常路径里的补泄压并发操作同一气路
                await ventTask.ConfigureAwait(false);
            }
        }

        /// <summary>休息段泄压：异常只记日志，不中断休息（泄压失败原本也不该让本轮判不出来）</summary>
        private async Task VentForRestAsync(IBloodPressureSimulator simulator, bool ventDuringRest, CancellationToken token)
        {
            if (!ventDuringRest) return;
            try { await simulator.VentToZeroAsync(ct: token).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _log.Warn($"轮间泄压失败（继续休息）：{ex.Message}"); }
        }

        /// <summary>
        /// 血压测量轮次采集（A06 的 BP-004 轮次/重复性与 BP-005 心率共用同一套流程）：整轮采集期间常开压力采样（界面波形的唯一数据源），逐轮流程见 <see cref="CollectBpRoundsAsync"/>。
        /// 回读格与逐轮用时一起返回：模块是采完全部轮次才批量上报，逐项耗时只能由采集层给出。
        /// </summary>
        private async Task<(Dictionary<(int, int, int), List<ProductBpReading?>> Readings,
            Dictionary<(int, int, int), List<double>> RoundSeconds)> CollectBpReadingsAsync(
            IBloodPressureSimulator simulator, IProductController? product,
            (int Systolic, int Diastolic, int HeartRate)[] groups, int repeats, double pulseVolume,
            Func<(int Systolic, int Diastolic, int HeartRate), int, string?> waveItemName,
            int restSeconds, bool ventDuringRest, int singleMeasureTimeoutMs, CancellationToken token)
        {
            var readings = new Dictionary<(int, int, int), List<ProductBpReading?>>();
            var roundSeconds = new Dictionary<(int, int, int), List<double>>();

            // 不开采样则 PressureSampled 一条也不上抛，界面每轮曲线全空（BPA700 的曲线来自 SDK 采样回调）
            await simulator.StartSamplingAsync(token);
            try
            {
                await CollectBpRoundsAsync(readings, roundSeconds, simulator, product, groups, repeats, pulseVolume,
                    waveItemName, restSeconds, ventDuringRest, singleMeasureTimeoutMs, token);
            }
            finally
            {
                await simulator.StopSamplingAsync();
            }
            return (readings, roundSeconds);
        }

        /// <summary>
        /// 逐点位逐轮执行：模拟仪按点位出脉搏波并进入待测 → 产测态 0xCA 开始完整血压测量 → 等 0xCA 结果帧（状态码/收缩/舒张/平均压/心率）→ 0xCC 结束 + 关闭模拟仪输出。
        /// 仪器先输出、产品后开始是现场实测确认的顺序：反过来产品在没有脉搏波的窗口里充气，测完只回 0/0/0/0。
        /// 单轮从 0xCA 起算超过 singleMeasureTimeoutMs（配置项）没等到结果帧：发 0xCC + 停仪器输出后抛 TimeoutException 中止本模块（未报项由收尾扫描判 FAIL）。
        /// product 为 null（无产品演示模式）时不发产品指令、用短超时只验证仪器侧流程，回读格全部为空，也不休息。
        /// 波形按轮独立：每轮开始重开时间轴、只把本轮采样画给 waveItemName(g, r) 指定的那一项，轮一结束就退订，休息段（每轮后都泄压，非末轮再各等 restSeconds）不进任何曲线。
        /// 写 readings：键为点位三元组（与入参 groups 逐项对应），值按轮次占格（下标 i = 第 i+1 轮，null = 该轮没取到有效回读），一轮一项时下标才能与“第 N 轮”严格对上。
        /// 写 roundSeconds：与 readings 同键同下标，值是本轮实际占用的秒数（从本轮开始到其后的轮间休息结束）。
        /// </summary>
        private async Task CollectBpRoundsAsync(
            Dictionary<(int, int, int), List<ProductBpReading?>> readings,
            Dictionary<(int, int, int), List<double>> roundSeconds,
            IBloodPressureSimulator simulator, IProductController? product,
            (int Systolic, int Diastolic, int HeartRate)[] groups, int repeats, double pulseVolume,
            Func<(int Systolic, int Diastolic, int HeartRate), int, string?> waveItemName,
            int restSeconds, bool ventDuringRest, int singleMeasureTimeoutMs, CancellationToken token)
        {
            for (var gi = 0; gi < groups.Length; gi++)
            {
                var g = groups[gi];
                var list = new List<ProductBpReading?>(repeats);
                var seconds = new List<double>(repeats);
                readings[(g.Systolic, g.Diastolic, g.HeartRate)] = list;
                roundSeconds[(g.Systolic, g.Diastolic, g.HeartRate)] = seconds;
                for (var r = 1; r <= repeats; r++)
                {
                    // 轮次级追踪上下文：本轮的 0xCA/0xCC 串口日志统一带 [A06…·BP@120/80/80#R1] 前缀
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
                    var measureEnded = false;
                    Task<BpTestRunResult>? cycleTask = null;
                    try
                    {
                        if (product == null)
                        {
                            // 无产品演示：短超时只验证仪器侧指令与波形（异常由调用方按“不影响收尾判定”处理）
                            await simulator.RunBpMeasurementAsync(g.Systolic, g.Diastolic, g.HeartRate, pulseVolume,
                                waitStartTimeoutMs: 8_000, totalTimeoutMs: 15_000, ct: token);
                            list.Add(null);
                            seconds.Add(roundWatch.Elapsed.TotalSeconds);
                            continue;
                        }

                        // 仪器先按点位输出脉搏波并进入待测（BPA700 停在 Wait 等产品充气），再让产品开始测量：现场实测这个顺序才测得出数值
                        cycleTask = simulator.RunBpMeasurementAsync(g.Systolic, g.Diastolic, g.HeartRate, pulseVolume,
                            totalTimeoutMs: singleMeasureTimeoutMs + CycleWaitMarginMs, ct: token);

                        // 产测态开始一次完整血压测量（0xCA 无参帧）：不发这条产品不自行充放气，也就永远没有结果帧
                        await product.StartFullBpMeasureAsync(token);
                        measureStarted = true;

                        // 结果帧在产品整个充放气周期末尾才到，与模拟仪的周期判定并行等（模拟仪只出脉搏波并等 Finish，不参与计时判定）
                        var (timedOut, reading) = await product.WaitFullBpResultAsync(singleMeasureTimeoutMs, token);

                        if (timedOut)
                        {
                            _log.Error($"血压测量轮 组 SYS={g.Systolic} DIA={g.Diastolic} HR={g.HeartRate} 第 {r} 次：" +
                                       $"{singleMeasureTimeoutMs / 1000}s 内未收到产品 0xCA 结果帧 → 发结束指令（0xCC）、关闭模拟仪输出，本模块中止");
                            await EndBpMeasureRoundAsync(product, simulator, cycleTask, sendEndCommand: true);
                            measureEnded = true;
                            throw new TimeoutException($"单次血压测量超过 {singleMeasureTimeoutMs / 1000}s 未收到产品结果帧（0xCA），本模块中止");
                        }

                        var cycle = await cycleTask;
                        cycleTask = null;
                        // 仪器本轮周期与产品回读并排看：区分“仪器没出波”和“仪器出了波、产品没测出”
                        _log.Info($"仪器本轮：峰值 {cycle.PeakPressure:0.0} mmHg 充气 {cycle.InflatableTime:0.0}s 放气 {cycle.DeflationTime:0.0}s 合计 {cycle.TotalTime:0.0}s 末态 status={cycle.FinalStatus}");

                        if (reading != null)
                        {
                            list.Add(reading);
                            _log.Info($"产品上报：SYS={reading.Systolic:0.##} DIA={reading.Diastolic:0.##} MAP={reading.MeanPressure:0.##} HR={reading.HeartRate:0.##}");
                        }
                        else
                        {
                            // 结果帧状态码非 0 / 长度不足 = 本轮没测出结果（收缩压 0 参与偏差计算会顶出天量误差），按无回读占格
                            list.Add(null);
                            _log.Warn($"血压测量轮 组 SYS={g.Systolic} DIA={g.Diastolic} HR={g.HeartRate} 第 {r} 次：本轮无有效测量结果（0xCA 结果帧状态码非 0 或长度不足）");
                        }

                        // 每轮测完都必须发结束（0xCC）并关闭模拟仪输出，再进休息段
                        await EndBpMeasureRoundAsync(product, simulator, cycleTask, sendEndCommand: true);
                        measureEnded = true;
                    }
                    catch
                    {
                        // 取消或异常正好落在这一轮中间：开始测量发出去了却没走到结束，补一次结束指令；仪器周期任务已起的话一并停输出收掉，
                        // 否则产品挂着本次测量，后面每一轮/下一个模块的开始都不被接受。都用 None 令牌：点“停止”时这几句恰恰最需要发得出去。
                        if (cycleTask != null || measureStarted)
                            await EndBpMeasureRoundAsync(product!, simulator, cycleTask, sendEndCommand: measureStarted && !measureEnded);

                        // 本轮被打断时袖带可能仍带压：尽力泄回零，避免设备带压进入下一个模块
                        try { await simulator.VentToZeroAsync(ct: CancellationToken.None); }
                        catch (Exception ex) { _log.Warn($"轮次中断后泄压失败：{ex.Message}"); }

                        throw;
                    }
                    finally
                    {
                        simulator.PressureSampled -= OnRoundSample;   // 本轮曲线到此为止，休息段不再绘制
                    }

                    // 每轮测完让产品休息（换组之间也算一轮的间隔；演示轮走上面的 continue 不休息；本模块最后一轮不休息）
                    await RestAfterRoundAsync(simulator, r, repeats, restSeconds, ventDuringRest,
                        gi == groups.Length - 1 && r == repeats, token);

                    // 本轮用时到休息结束为止：休息是这一轮换来的，算进这一项，逐项之和才对得上大项用时
                    seconds.Add(roundWatch.Elapsed.TotalSeconds);
                }
            }
        }

        /// <summary>
        /// 一轮血压测量的收尾（正常、超时、取消/异常三条路径都走这里）：按需发结束指令（0xCC）→ 关闭模拟仪动态输出 → 收掉并行的模拟仪周期任务。
        /// 三步都用 <see cref="CancellationToken.None"/>（点“停止”时最需要发得出去），异常只记日志不外抛，避免盖掉本轮真正的失败原因。
        /// </summary>
        /// <param name="sendEndCommand">本轮 0xCA 是否真的发出去了：没发出过就不该发 0xCC（产品会把它当一次没有对应的结束）</param>
        private async Task EndBpMeasureRoundAsync(IProductController product, IBloodPressureSimulator simulator,
            Task<BpTestRunResult>? cycleTask, bool sendEndCommand)
        {
            if (sendEndCommand)
            {
                try { await product.StopFullBpMeasureAsync(CancellationToken.None); }
                catch (Exception ex) { _log.Warn($"补发结束完整血压测量（0xCC）失败：{ex.Message}"); }
            }

            try { await simulator.StopSimulationAsync(CancellationToken.None); }
            catch (Exception ex) { _log.Warn($"停止模拟仪输出失败：{ex.Message}"); }

            if (cycleTask == null) return;
            try { await cycleTask; }
            catch (Exception ex) { _log.Debug($"模拟仪周期任务收尾异常（输出已停）：{ex.Message}"); }
        }

        /// <summary>本轮实测用时（秒）：BP-004/BP-005 采完全部轮次才批量上报，逐项耗时取采集层记录，不用上报时刻差</summary>
        private static double? BpRoundSeconds(Dictionary<(int, int, int), List<double>> roundSeconds,
            (int Systolic, int Diastolic, int HeartRate) g, int r)
            => roundSeconds.TryGetValue(g, out var list) && r <= list.Count ? Math.Round(list[r - 1], 1) : null;

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

        /// <summary>BP-005 心率平均值子表行：列=设定值 / 均值 / 与设定偏差 / 极大与极小 / 极差，整行只展示不判定（合格与否看各轮项）；单轮没有散布，极大/极小/极差留空显示 --；
        /// 极差超出容差时写进备注</summary>
        private static TestPointResult BpRepeatPoint(string name, double target, List<double> values, double tolerance)
        {
            var avg = values.Average();
            var dev = Math.Abs(avg - target);
            var hasSpread = values.Count >= 2;
            var range = hasSpread ? values.Max() - values.Min() : (double?)null;
            return new TestPointResult
            {
                Schema = TestPointSchemas.Repeat,
                Unit = "BPM",
                Name = name,
                TargetPressure = target,
                Average = Math.Round(avg, 2),
                Max = hasSpread ? Math.Round(values.Max(), 2) : null,
                Min = hasSpread ? Math.Round(values.Min(), 2) : null,
                Range = range.HasValue ? Math.Round(range.Value, 2) : null,
                Error = Math.Round(dev, 2),
                ShowVerdict = false,
                Remark = range > tolerance ? $"极差 {range:0.0} 超 ±{tolerance:0.#}（仅统计）" : string.Empty,
            };
        }

        /// <summary>一通道多轮回读的统计量：算一次给日志、子表文本行与数值统计行共用（调用方保证列表非空）</summary>
        private readonly record struct RepeatStats(double Average, double Max, double Min, double Range)
        {
            public static RepeatStats Of(IReadOnlyList<double> values)
            {
                var max = values.Max();
                var min = values.Min();
                return new RepeatStats(values.Average(), max, min, max - min);
            }
        }

        /// <summary>BP-004 重复性子表（每轮一行 + 统计行）：列=收缩压/舒张压，行=设定值 → 第1..N轮实测（按轮次占格，本轮没回读显 --，下标与“第 N 轮”严格对得上）→ 均值 → 与设定偏差 → 极差；
        /// 只极差行参与判定（判据同父项：收缩/舒张极差取大者 ≤±2），统计量只取有效轮；极大/极小不单列（两轮时它就是两轮回读值本身，多轮也只是极差的构成）</summary>
        private static List<TestPointResult> BuildBpRepeatRows((int Systolic, int Diastolic, int HeartRate) g,
            IReadOnlyList<ProductBpReading?> rounds, int repeats, RepeatStats sys, RepeatStats dia, double tolerance)
        {
            var rangePass = Math.Max(sys.Range, dia.Range) <= tolerance;

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

            rows.Add(BpRepeatRow("均值", NumOr(sys.Average), NumOr(dia.Average)));
            rows.Add(BpRepeatRow("与设定偏差",
                NumOr(Math.Abs(sys.Average - g.Systolic)),
                NumOr(Math.Abs(dia.Average - g.Diastolic))));
            rows.Add(BpRepeatRow("极差",
                NumOr(sys.Range), NumOr(dia.Range),
                rangePass,
                Math.Round(Math.Max(sys.Range, dia.Range), 2),
                rangePass ? string.Empty
                    : $"{(sys.Range > tolerance ? "收缩压" : "舒张压")}极差 {Math.Max(sys.Range, dia.Range):0.0} 超 ±{tolerance:0.#}"));
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

        /// <summary>BP-004 重复性数值统计行（收缩/舒张各一行）：把文本统计行里的统计量落成数值列
        /// （Average/Max/Min/Range/Error），Schema=RepeatStats——入库与报表图表（折线图/统计卡）直接读列、
        /// 不解析文本；界面子表渲染时跳过本口径（见 TestItemViewModel.BuildSubTable）</summary>
        private static TestPointResult BpRepeatStatsRow(string channel, double target, RepeatStats stats)
            => new()
            {
                Schema = TestPointSchemas.RepeatStats,
                Name = $"{channel}统计",
                Unit = "mmHg",
                TargetPressure = target,
                Average = Math.Round(stats.Average, 2),
                Max = Math.Round(stats.Max, 2),
                Min = Math.Round(stats.Min, 2),
                Range = Math.Round(stats.Range, 2),
                Error = Math.Round(Math.Abs(stats.Average - target), 2),
                ShowVerdict = false,
            };

    }
}
