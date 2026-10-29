using TORNPISolution.Service.Devices;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// TestItem 分部：血压计量检定——A19 JJG692 放气速率（听诊）/ A20 JJG692 气密性，含康伟达
    /// </summary>
    public partial class TestItem
    {
        //测试项目**************************---A19---**************************JJG692放气速率(听诊)（康伟达 0x04/0x12）
        /// <summary>
        /// JJG692-2010 放气速率（听诊）检测（康伟达专属：0x04 功能 + 子码 0x12）：设定压力/开始气压/停止气压/判定区间取血压模拟仪测试配置→康伟达→JJG692 段，设备自行升压后匀速放气；
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
        /// JJG692-2010 气密性检测（康伟达专属：0x04 功能 + 子码 0x13）：设定压力/稳压时间/漏气率上限取血压模拟仪测试配置→康伟达→JJG692 段，设备升压稳压后回传总时间/稳定压力/漏气率；
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

        /// <summary>JJG692 三项公共前置：必须是康伟达（BPA700 SDK 无 0x04 标准检测能力）并取出配置页的 JJG692 参数组；不满足时写错误日志并返回 false（调用方按项判 FAIL）。</summary>
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

        /// <summary>JJG692 数值类项（0x12/0x13 漏气率）结果构造：设备未完成/执行异常 → 测试值 -1（必落在判定区间外，判不合格），仪器列显示失败原因；
        /// 判定区间任一端有值 → 按区间判定，空缺的一端按 0/10 兜底；两端都留空（规程规格值尚未提供）→ 不判定，实测值以文本上报，项保持未测试。</summary>
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
