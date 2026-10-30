using TORNPISolution.Interface;
using TORNPISolution.Service.Devices;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// TestItem 分部：B04 综测仪上电（单板工位）。
    /// 一台已启用综测仪的四个通道各占一项，每个通道三步（槽位号口径与目录构建同一个函数 <see cref="SpecItems.BoardPowerSlots"/>）：
    ///   ① 缓输出打开通路（OUTPUT:RAMP 带线制，带了线制通路就已经打开，不必再发 OUTPUT:STA）；
    ///   ② 切 po 测量通路（手册 4.2.2 要求先切再读本通道输出电压）；
    ///   ③ 回读电压换算成 mV，与设定值比，落在配置给的 ±容差内判 PASS。
    /// 台与台并发（各自四条独立 TCP 连接），同一台内四个通道串行，采集结果串行上报（结果字典不是线程安全的）；结束时只断连接：CloseAsync 不动输出状态，产品要带电跑后续模块，下电是另一个动作。
    /// </summary>
    public partial class TestItem
    {
        //测试项目**************************---B04---**************************综测仪上电（单板）
        public async Task BoardPowerOnModule(TestItemResultCollector collector, CancellationToken token)
        {

            var config = _configService.Load();
            // 按本轮范围筛槽位：单板页右键“只跑通道 3”时，其余七路既不下发输出也不上报，避免拿旧值充本轮结果
            var slots = WithinSlotScope(SpecItems.BoardPowerSlots(config), s => s.Slot);
            if (slots.Count == 0)
            {
                _log.Warn("综测仪上电：本轮要的槽位里没有可对上的已启用综测仪通道（仪器连接配置→综测仪），不上电");
                return;
            }

            var testers = slots.Select(s => s.TesterIndex).Distinct().ToList();
            var probed = await Task.WhenAll(testers.Select(tester =>
                PowerOnTesterAsync(config.Cry5242Testers[tester],
                    slots.Where(s => s.TesterIndex == tester).ToList(), token))).ConfigureAwait(false);
            var bySlot = probed.SelectMany(list => list).ToDictionary(r => r.Slot);

            // 上报串行：各台并发采集
            var failed = 0;
            string? firstFailure = null;
            foreach (var (slot, testerIndex, channel) in slots)
            {
                var setting = config.Cry5242Testers[testerIndex];
                var read = bySlot.TryGetValue(slot, out var one) ? one : null;
                var measured = read?.MeasuredMilliVolts;

                if (measured.HasValue)
                {
                    // 判定交给目录里的静态规格（设定 ±容差），这里只交出实测值和仪器原文
                    await collector.SetAsync(SpecItems.PowerVoltage(slot), new TestItemResult
                    {
                        Value = Math.Round(measured.Value, 1),
                        InstrumentText = $"回读 {measured.Value:0.#}mV（仪器原文 {read!.RawReading}）"
                    });
                }
                else
                {
                    await collector.SetAsync(SpecItems.PowerVoltage(slot),
                        TestItemResult.OfText(0, read?.Failure ?? "本路没有回读到电压"));
                    failed++;
                    firstFailure ??= read?.Failure ?? $"{setting.Name} CH{channel} 没有回读到电压";
                }

                _log.Info($"供电电压-{slot}（{setting.Name} CH{channel}）：设定 {setting.OutputMilliVolts}mV，" +
                          $"回读 {(measured.HasValue ? $"{measured:0.#}mV" : read?.Failure ?? "无")}");
            }

            // 一次模块只弹一条通知：几台仪器同时没连上时不该刷出同样多个 Growl
            if (failed > 0)
                DeviceConnectionFailed?.Invoke("综测仪上电", $"{failed}/{slots.Count} 路没读到电压（{firstFailure}）");

            _log.Info($"综测仪上电完成：{slots.Count - failed}/{slots.Count} 路回读到电压，输出保持（本模块只断网络连接，下电另做）");
        }

        /// <summary>
        /// 一台综测仪负责的那几个通道：建链 →（可选）整机复位清残留 → 逐通道缓输出并回读；建链、复位或参数校验没过就把这台负责的通道整批判 FAIL，单个通道失败只影响它自己那一路。
        /// 取消照常上抛，已经打开的输出不动它（产品保持带电，交给现场或下电动作处理）。
        /// </summary>
        private async Task<IReadOnlyList<ChannelPower>> PowerOnTesterAsync(
            Cry5242Settings settings,
            IReadOnlyList<(int Slot, int TesterIndex, int Channel)> slots,
            CancellationToken token)
        {
            var results = slots.ToDictionary(s => s.Channel, s => new ChannelPower { Slot = s.Slot, Channel = s.Channel });

            using ICry5242Tester tester = new Cry5242Tester(settings, _log);
            try
            {
                // 电压没配就不下发任何输出指令：这台是给主板供电的，宁可整批判 FAIL 也不猜一个电压上去
                if (settings.OutputMilliVolts <= 0)
                    throw new InvalidOperationException(
                        $"{settings.Name} 没配输出电压（{nameof(Cry5242Settings.OutputMilliVolts)} 要填大于 0 的 mV 值），本台不上电");

                await tester.OpenAsync(token).ConfigureAwait(false);

                if (settings.ResetBeforePowerOn)
                {
                    // 过流保护一旦动作就只能靠 *RST 恢复，复位同时把上一轮残留的输出状态清干净再设新的
                    _log.Info($"{settings.Name} 上电前整机复位：清掉残留输出与已经动作的过流保护");
                    await tester.ResetAsync(token).ConfigureAwait(false);
                }

                foreach (var (_, _, channel) in slots)
                {
                    var one = results[channel];
                    try
                    {
                        // 线制留空，由驱动回落到这台仪器配置里的 FourWire
                        await tester.SetRampOutputAsync(channel, settings.OutputMilliVolts,
                            settings.ProtectionMilliAmps, settings.RampMilliSeconds, ct: token).ConfigureAwait(false);

                        await tester.SwitchFunctionAsync(channel, Cry5242Function.Po, ct: token).ConfigureAwait(false);
                        var reading = await tester.MeasureOutputVoltageAsync(channel, ct: token).ConfigureAwait(false);

                        one.RawReading = reading.ToString();
                        one.MeasuredMilliVolts = ToMilliVolts(reading, $"{settings.Name} CH{channel}");
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        one.Failure = ex.Message;
                        _log.Error($"{settings.Name} CH{channel} 上电或回读失败：{ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 这台一路都没走通（建链失败多半是 IP 与通道端口没配对，或仪器侧 TCP 服务没起）
                foreach (var one in results.Values.Where(r => r.Failure == null && !r.MeasuredMilliVolts.HasValue))
                    one.Failure = ex.Message;
                _log.Error($"{settings.Name} 上电流程中断，本台 {results.Count} 个通道全部判 FAIL：{ex.Message}");
            }
            finally
            {
                // 只关连接：CloseAsync 明确不改输出状态，产品要保持带电
                try { await tester.CloseAsync().ConfigureAwait(false); }
                catch (Exception ex) { _log.Warn($"{settings.Name} 关闭连接异常（输出状态不受影响）：{ex.Message}"); }
            }

            return slots.Select(s => results[s.Channel]).ToList();
        }

        /// <summary>回读换算成 mV：驱动把数值和单位原样带回（它不替现场猜量纲），换算在这里按单位词做；单位认不出来时按数值量级兜底猜一次并记警告——把 5V 当 5mV 判会整批误判，必须在日志里留下痕迹。</summary>
        private double ToMilliVolts(Cry5242Reading reading, string where)
        {
            var unit = reading.Unit.Trim().ToUpperInvariant();
            if (unit == "V") return reading.Value * 1000;
            if (unit == "MV") return reading.Value;

            var isVolts = reading.Value <= 8;
            var guessed = isVolts ? reading.Value * 1000 : reading.Value;
            _log.Warn($"{where} 回读单位认不出来（原文 {reading}），按量级当 {(isVolts ? "V" : "mV")} 折成 {guessed:0.#}mV，判定前请核对仪器给的单位");
            return guessed;
        }

        /// <summary>一个通道的上电结果：并发采集阶段各写各的这份，上报阶段串行取用</summary>
        private sealed class ChannelPower
        {
            public int Slot { get; init; }
            public int Channel { get; init; }

            /// <summary>回读换算出的实测电压（mV）；没读到为 null</summary>
            public double? MeasuredMilliVolts { get; set; }

            /// <summary>仪器回读原文（带它自己给的单位），出问题时现场要能对上</summary>
            public string RawReading { get; set; } = string.Empty;

            /// <summary>本路失败的原因；null 表示这一路走通了</summary>
            public string? Failure { get; set; }
        }
    }
}
