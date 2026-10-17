using TORNPISolution.Interface;
using TORNPISolution.Service.Devices;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// TestItem 分部：血压校准与计量检定——A17 产品静态压力校准 / A19 JJG692 放气速率（听诊）/ A20 JJG692 气密性，
    /// 含三项各自的辅助（轮次间隔休息、康伟达前置检查、检定结果上报）。
    /// 模块执行包装与分派见 TestItem.cs 主文件，项名统一引用 <see cref="SpecItems"/>。
    /// </summary>
    public partial class TestItem
    {
        /// <summary>
        /// 产品静态压力校准（A17）：产品压力传感器逐点校准，校准点固定 60/120/180/240mmHg，时序与手动校准一致（无需先进静态压力模式）：
        /// ① 启动压力传感器（0x65）前先泄气归零（零点在常压取），有响应即通过；
        /// ② 逐点：先泄压（终止压力源调压再放气）-> 模拟仪稳压 3s（校准无需长稳压）-> |稳压值-校准点| ≤3mmHg 才发校准（0x66 + uint16 LE）
        ///    -> 有 ACK 判 PASS（测试值=稳压误差，仪器列=稳压值）-> 延时 2s -> 点后泄气 -> 下一点；稳压超限或无 ACK 判 FAIL 并跳过该点指令；
        /// ③ 四个点完成后关闭压力传感器（0x67）：finally + None 令牌，停止/异常路径也发送。
        /// 产品不可用（未连接/握手失败）：全项判 FAIL，不驱动模拟仪。
        /// </summary>
        //测试项目**************************---A17---**************************产品静态压力校准
        public async Task ProductStaticPressureCalibrationModule(TestItemResultCollector collector, CancellationToken token)
        {
            const double Tolerance = 3;   // mmHg，稳压误差规格，与 BP-003 同口径
            const int StableSeconds = 3;  // A17 稳压时间固定 3s（校准无需长稳压，不用配置里的 BP-003 稳压时长）

            if (NoInstrumentDemo("产品静态压力校准")) return;
            var config = _configService.Load();
            if (config.ActiveSimulator is not (SimulatorType.BPA700 or SimulatorType.Kangwenda))
            {
                _log.Error("产品静态压力校准跳过：未选择血压模拟仪（仪器链接配置 → 模拟仪类型）");
                return;
            }

            using (var product = _productFactory())
            {
                // 产品可用性以本轮 A16 产品通讯初始化记录为准；本模块自建实例仍需打开产品串口才能下发校准指令
                var productAvailable = GetProductAvailable(config.NoProductDemoMode);
                if (productAvailable)
                {
                    try
                    {
                        await product.OpenAsync(token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        productAvailable = false;
                        _log.Warn($"A17：产品串口打开失败：{ex.Message}");
                    }
                }

                if (!productAvailable)
                {
                    _log.Error("A17 判 FAIL：产品不可用（串口未连接或握手失败），校准指令无法下发，本模块不驱动模拟仪");
                    await collector.SetAsync(SpecItems.PcalStartSensor, TestItemResult.Of(0));
                    foreach (var value in SpecItems.PcalPoints)
                        await collector.SetAsync(SpecItems.PcalPoint(value), TestItemResult.Of(Tolerance + 1));
                    await collector.SetAsync(SpecItems.PcalCloseSensor, TestItemResult.Of(0));
                    return;
                }

                await using var lease = await _simSession.RentAsync(token);
                var simulator = lease.Simulator;
                var sp = config.ActiveBpTestParams.StaticPressure;

                // ① 启动压力传感器（0x65）：校准流程前置命令，有响应即通过
                var started = false;
                try
                {
                    // ① 启动压力传感器（0x65）：前置命令，有响应即通过；启动前先泄气归零，传感器零点在常压取
                    try
                    {
                        await simulator.VentToZeroAsync(ct: token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _log.Warn($"A17：启动前泄气失败，传感器零点可能带残压：{ex.Message}");
                    }

                    try
                    {
                        await product.StartPressureSensorAsync(token);
                        started = true;
                        await collector.SetAsync(SpecItems.PcalStartSensor, TestItemResult.Of(1));
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _log.Warn($"A17 启动压力传感器（0x65）无响应，判 FAIL：{ex.Message}");
                        await collector.SetAsync(SpecItems.PcalStartSensor, TestItemResult.Of(0));
                    }

                    if (!started)
                    {
                        // 前置命令失败：产品未进入压力静态测试态，各校准点无需空等稳压
                        foreach (var value in SpecItems.PcalPoints)
                            await collector.SetAsync(SpecItems.PcalPoint(value), TestItemResult.Of(Tolerance + 1));
                    }
                    else
                    {
                        // ② 逐点：稳压 -> 校准 -> 延时 2s -> 下一点
                        foreach (var value in SpecItems.PcalPoints)
                        {
                            token.ThrowIfCancellationRequested();
                            var itemName = SpecItems.PcalPoint(value);

                            // 切换前泄压（手动实测：先泄到 0 再切下一点更准）；保压顶阀时驱动会坚持开阀等设备放弃，
                            // 失败再补泄一次；仍带残压也不硬断，后续稳压误差 >3mmHg 会拦住校准指令
                            try
                            {
                                if (!await simulator.VentToZeroAsync(ct: token))
                                {
                                    _log.Warn($"A17 {itemName}：切换前泄压未归零，补泄一次");
                                    await simulator.VentToZeroAsync(ct: token);
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                _log.Warn($"A17 {itemName}：切换前泄压失败（清场仪式会补偿）：{ex.Message}");
                            }

                            // 模拟仪单点稳压（序列 API 含清场/调零与收敛保持），返回实际稳压值
                            double stable;
                            try
                            {
                                stable = (await simulator.RunStaticPressureSequenceAsync(
                                    new[] { value }, StableSeconds, sp.BpaInflation, token))[0];
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                _log.Warn($"A17 {itemName}：模拟仪稳压失败，判 FAIL：{ex.Message}");
                                await collector.SetAsync(itemName, TestItemResult.Of(Tolerance + 1));
                                continue;
                            }

                            var hasInstrument = !double.IsNaN(stable);
                            var err = hasInstrument ? Math.Abs(stable - value) : (double?)null;
                            var ack = false;
                            if (err.HasValue && err.Value <= Tolerance)
                            {
                                try
                                {
                                    await product.CalibratePressureSensorAsync((ushort)value, token);
                                    ack = true;
                                }
                                catch (OperationCanceledException)
                                {
                                    throw;
                                }
                                catch (Exception ex)
                                {
                                    _log.Warn($"A17 {itemName}：校准指令（0x66）无响应，判 FAIL：{ex.Message}");
                                }
                            }
                            else
                            {
                                _log.Warn($"A17 {itemName}：稳压误差超限（稳压={(hasInstrument ? stable.ToString("0.0") : "--")}），跳过校准指令，判 FAIL");
                            }

                            var pass = ack && err.HasValue && err.Value <= Tolerance;
                            _log.Info($"A17 {itemName}：稳压={(hasInstrument ? stable.ToString("0.0") : "--")} 误差={(err?.ToString("0.0") ?? "--")} 校准ACK={(ack ? "有" : "无")} → {(pass ? "PASS" : "FAIL")}");
                            await collector.SetAsync(itemName, new TestItemResult
                            {
                                Value = pass ? err!.Value : Tolerance + 1,
                                InstrumentValue = hasInstrument ? stable : null
                            });

                            // 每发一个校准指令延时 2s 再进行下一步（产品侧校准写入需间隔）
                            await Task.Delay(2000, token);

                            // 每点校准完成后立即泄放回零：接大容积容器时憋压会让泵补载持续发热，
                            // 点后泄气让泵早歇；下一点清场仪式仍会按仪式泄残压并重新调零
                            try
                            {
                                await simulator.VentToZeroAsync(ct: token);
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                _log.Warn($"A17 {itemName}：点间泄气失败（下一点清场会补偿）：{ex.Message}");
                            }
                        }
                    }
                }
                finally
                {
                    // ③ 全部测试项结束后关闭压力传感器；None 令牌保证停止/异常路径也执行
                    try
                    {
                        await product.ClosePressureSensorAsync(CancellationToken.None);
                        await collector.SetAsync(SpecItems.PcalCloseSensor, TestItemResult.Of(1));
                    }
                    catch (Exception ex)
                    {
                        _log.Warn($"A17 关闭压力传感器（0x67）失败，判 FAIL：{ex.Message}");
                        await collector.SetAsync(SpecItems.PcalCloseSensor, TestItemResult.Of(0));
                    }

                    // 模块结束泄气：容器残压放掉，避免气体憋到下一模块（尽力泄，停/异常路径也泄）
                    try
                    {
                        await simulator.VentToZeroAsync(ct: CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _log.Warn($"A17：模块结束泄气失败：{ex.Message}");
                    }
                }
            }
        }

        /// <summary>BP-004 压力组取用（A06 项清单与执行轮次同一来源，与 Spec 目录构建同口径）：
        /// 只取勾选行、取整后去重（两行完全相同时采集字典的键会互相覆盖）；心率全模块一个值；
        /// 轮数少于 3 抬到 3（少于 3 轮取极差统计意义不足）；
        /// 未勾选任何行时记日志返回空数组（调用方直接结束、不发仪器指令）</summary>
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

        /// <summary>BP-005 心率点取用（A21 项清单与执行轮次同一来源，与 Spec 目录同口径）：
        /// 压力固定一组，心率只取勾选行并去重（同一心率重复一行没意义）</summary>
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

        /// <summary>轮间休息（产品安全）：每轮测完休息 restSeconds 秒再开下一轮/下一组（含末轮收尾）；
        /// 选「休息时泄压」先泄回零，选「不泄压」则仪器保持待命；休息期间不绘波形，取消可立即打断</summary>
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

        //测试项目**************************---A19---**************************JJG692放气速率(听诊)（康伟达 0x04/0x12）
        /// <summary>
        /// JJG692-2010 放气速率（听诊）检测（康伟达专属：0x04 功能 + 子码 0x12）：
        /// 设定压力/开始气压/停止气压/判定区间取血压模拟仪测试配置→康伟达→JJG692 段，设备自行升压后匀速放气；
        /// 测试值 = 设备回传漏气率（mmHg/s），按配置区间判定；区间两端都留空时不判定（只以文本报实测值，项保持未测试）。
        /// </summary>
        public async Task Jjg692DeflationRateModule(TestItemResultCollector collector, CancellationToken token)
        {
            const string ModuleName = "JJG692放气速率(听诊)检测";
            if (NoInstrumentDemo(ModuleName)) return;

            if (!TryRequireKangwenda(ModuleName, _configService.Load(), out var jjg))
            {
                await collector.SetAsync(SpecItems.Jjg692DeflationRate, Jjg692RateResult(null, null, "未选择康伟达模拟仪，检测未执行", null, null));
                return;
            }

            var dr = jjg!.DeflationRate;
            KangwendaNIBPSimulator.Jjg692Result? result = null;
            string? failure = null;
            await using (var lease = await _simSession.RentAsync(token))
            {
                if (lease.Simulator is not KangwendaNIBPSimulator kw)
                {
                    _log.Error($"{ModuleName} 判 FAIL：租到的实例不是康伟达驱动（实际 {lease.Simulator.GetType().Name}）");
                    await collector.SetAsync(SpecItems.Jjg692DeflationRate, Jjg692RateResult(null, null, "驱动实例类型不符", dr.MinRate, dr.MaxRate));
                    return;
                }

                _log.Info($"{ModuleName} 开始：设定压力={dr.SetPressure} 开始气压={dr.StartPressure} 停止气压={dr.StopPressure} mmHg，判定区间=[{dr.MinRate?.ToString() ?? "不限"}, {dr.MaxRate?.ToString() ?? "不限"}] mmHg/s");
                try
                {
                    result = await kw.RunJjg692Async(KangwendaNIBPSimulator.Jjg692Check.DeflationRate,
                        (byte)jjg.MeasureType, (byte)jjg.CuffType,
                        setPressure: dr.SetPressure, startPressure: dr.StartPressure, stopPressure: dr.StopPressure,
                        timeoutMs: jjg.TimeoutSeconds * 1000, ct: token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failure = ex.Message;
                    _log.Error($"{ModuleName} 执行失败：{ex.Message}");
                }
            }

            await collector.SetAsync(SpecItems.Jjg692DeflationRate,
                Jjg692RateResult(result, result?.LeakRate, failure, dr.MinRate, dr.MaxRate));
        }

        //测试项目**************************---A20---**************************JJG692气密性（康伟达 0x04/0x13）
        /// <summary>
        /// JJG692-2010 气密性检测（康伟达专属：0x04 功能 + 子码 0x13）：
        /// 设定压力/稳压时间/漏气率上限取血压模拟仪测试配置→康伟达→JJG692 段，设备升压稳压后回传总时间/稳定压力/漏气率；
        /// 测试值 = 漏气率（mmHg/s），按上限判定；上限留空时不判定（只以文本报实测值，项保持未测试）。
        /// </summary>
        public async Task Jjg692AirtightnessModule(TestItemResultCollector collector, CancellationToken token)
        {
            const string ModuleName = "JJG692气密性检测";
            if (NoInstrumentDemo(ModuleName)) return;

            if (!TryRequireKangwenda(ModuleName, _configService.Load(), out var jjg))
            {
                await collector.SetAsync(SpecItems.Jjg692Airtightness, Jjg692RateResult(null, null, "未选择康伟达模拟仪，检测未执行", null, null));
                return;
            }

            var at = jjg!.Airtightness;
            KangwendaNIBPSimulator.Jjg692Result? result = null;
            string? failure = null;
            await using (var lease = await _simSession.RentAsync(token))
            {
                if (lease.Simulator is not KangwendaNIBPSimulator kw)
                {
                    _log.Error($"{ModuleName} 判 FAIL：租到的实例不是康伟达驱动（实际 {lease.Simulator.GetType().Name}）");
                    await collector.SetAsync(SpecItems.Jjg692Airtightness, Jjg692RateResult(null, null, "驱动实例类型不符", null, at.MaxLeakRate));
                    return;
                }

                _log.Info($"{ModuleName} 开始：设定压力={at.SetPressure} mmHg 稳压={at.StabilizeSeconds}s，漏气率上限={(at.MaxLeakRate?.ToString() ?? "不限")} mmHg/s");
                try
                {
                    result = await kw.RunJjg692Async(KangwendaNIBPSimulator.Jjg692Check.Airtightness,
                        (byte)jjg.MeasureType, (byte)jjg.CuffType,
                        setPressure: at.SetPressure, stabilizeSeconds: at.StabilizeSeconds,
                        timeoutMs: jjg.TimeoutSeconds * 1000, ct: token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failure = ex.Message;
                    _log.Error($"{ModuleName} 执行失败：{ex.Message}");
                }
            }

            await collector.SetAsync(SpecItems.Jjg692Airtightness,
                Jjg692RateResult(result, result?.LeakRate, failure, null, at.MaxLeakRate));
        }

        /// <summary>JJG692 三项公共前置：必须是康伟达（BPA700 SDK 无 0x04 标准检测能力），
        /// 并取出配置页的 JJG692 参数组；不满足时写错误日志并返回 false（调用方按项判 FAIL）。</summary>
        private bool TryRequireKangwenda(string moduleName, InstrumentConnectionConfig config, out Jjg692Params? jjg)
        {
            if (config.ActiveSimulator != SimulatorType.Kangwenda)
            {
                _log.Error($"{moduleName} 跳过：JJG692 标准检测是康伟达 0x04 功能专属，当前未选择康伟达（仪器链接配置 → 模拟仪类型）");
                jjg = null;
                return false;
            }
            jjg = config.ActiveBpTestParams.Jjg692;
            return true;
        }

        /// <summary>JJG692 数值类项（0x12/0x13 漏气率）结果构造：
        /// 设备未完成/执行异常 → 测试值 -1（必落在判定区间外，判不合格），仪器列显示失败原因；
        /// 判定区间任一端有值 → 按区间判定，空缺的一端按 0/10 兜底；
        /// 两端都留空（规程规格值尚未提供）→ 不判定，实测值以文本上报，项保持未测试。</summary>
        private static TestItemResult Jjg692RateResult(KangwendaNIBPSimulator.Jjg692Result? r, double? rate,
            string? failure, double? min, double? max)
        {
            var specMin = min ?? 0;
            var specMax = max ?? 10;
            if (failure != null || r == null || !r.Completed)
                return new TestItemResult
                {
                    Value = -1,
                    SpecMin = specMin,
                    SpecMax = specMax,
                    InstrumentText = failure ?? (r == null ? "未收到设备状态帧" : "设备回报检测失败（状态 3）"),
                };

            var summary = KangwendaNIBPSimulator.DescribeJjg692Result(r);
            if (min == null && max == null)
                return new TestItemResult { NotJudged = true, InstrumentText = $"{summary}｜未配置判定区间" };

            return new TestItemResult
            {
                Value = rate ?? 0,
                SpecMin = specMin,
                SpecMax = specMax,
                InstrumentText = summary,
            };
        }

    }
}
