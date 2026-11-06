using TORNPISolution.Interface;
using TORNPISolution.Service.Communication;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// TestItem 分部：血压校准——A17 产品静态压力传感器逐点校准。
    /// </summary>
    public partial class TestItem
    {
        /// <summary>A17 校准点的稳压误差规格（mmHg）：必须稳到目标 ±1.5 内才允许下发 0x66（与康伟达收敛判据一致），与 BP-003 的到点容差同口径</summary>
        private const double PcalTolerance = 1.5;

        /// <summary>
        /// 产品静态压力校准（A17）：产品压力传感器逐点校准，校准点固定 50/150/250mmHg，时序与手动校准一致（无需先进静态压力模式）。
        /// ① 启动压力传感器（0x65）前先泄气归零（常压下启动传感器），有响应即通过；
        /// ② 逐点在带压下下发校准（0x66 + uint16 LE）：|稳压值-校准点| ≤1.5mmHg 才发指令，有 ACK 判 PASS（测试值=稳压误差，仪器列=稳压值），
        ///    稳压超限或无 ACK 判 FAIL 并跳过该点指令。两台仪器统一走逐点单轮（到稳压后发指令，等本轮 Finish 再升下一点）：
        ///    BPA700=BPARunStaticPressure(校准点, 量测时间)，等 Stable 后取采样均值下发，再等 Finish；康伟达=压力源逐点稳压后下发。
        ///    不用 BETA 序列点间直切：保持窗太短，产品会把上一档的 ADC 标成新校准点，KB 截距乱跳、后续压力读数整体偏。
        /// ③ 三个点完成后关闭压力传感器（0x67）：finally + None 令牌，停止/异常路径也发送；产品不可用（未连接/握手失败）时全项判 FAIL、不驱动模拟仪。
        /// </summary>
        //测试项目**************************---A17---**************************产品静态压力校准
        public async Task ProductStaticPressureCalibrationModule(TestItemResultCollector collector, CancellationToken token)
        {
            const int SequenceHoldSeconds = 3;  // 序列模式每点保持窗（当前 A17 已不走序列，保留常量供回退）：从该点回读稳压起计时
            const int PointArriveSeconds = 60;  // 序列每个压力点的到压观察上限
            const int StableRoundEndMs = 15_000;   // 单轮路径的本轮收尾等待上限：正常几秒内 Finish，超时由 WaitStaticPressureFinishAsync 主动结束，不拖住下一点

            var config = _configService.Load();
            if (config.ActiveSimulator is not (SimulatorType.BPA700 or SimulatorType.Kangwenda))
            {
                _log.Error("产品静态压力校准跳过：未选择血压模拟仪（仪器链接配置 → 模拟仪类型）");
                return;
            }

            using (var product = _productFactory())
            {
                // 产品可用性以本轮 A16 产品通讯初始化记录为准；本模块自建实例仍需打开产品串口才能下发校准指令
                var productAvailable = GetProductAvailable();
                if (productAvailable)
                {
                    try
                    {
                        await product.OpenAsync(token);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        productAvailable = false;
                        _log.Warn($"A17：产品串口打开失败：{ex.Message}");
                    }
                }

                if (!productAvailable)
                {
                    _log.Error("A17 判 FAIL：产品不可用（串口未连接或握手失败），校准指令无法下发，本模块不驱动模拟仪");
                    await collector.SetAsync(SpecItems.PcalStartSensor, TestItemResult.Of(0));
                    await ReportPcalPointsAsFailAsync(collector);
                    await collector.SetAsync(SpecItems.PcalCloseSensor, TestItemResult.Of(0));
                    return;
                }

                // 校准写入依赖产品内部 ADC 基准，必须让 BPA700 自己按"设定压力 + 量测时间"稳到位再写。
                // BETA 序列点间直切、每点只保持 3s，实测会把上一档的 ADC 标成新点：KB 截距在 -144 / +74 / +3185 之间乱跳，
                // 产品读数随之整体偏 5mmHg。因此 A17 统一走逐点单轮（正式版 BPARunStaticPressure(pressure, MeasureSeconds, cb, inflation)），
                // 与现场用 BPA700 手动校准的口径一致：仪器自己稳压 → 量测时间窗口内写入 → 等 Finish 再下一点。
                var useSequenceSdk = false;
                await using var lease = useSequenceSdk
                    ? await _simSession.RentSequenceSdkAsync(token)
                    : await _simSession.RentAsync(token);
                var simulator = lease.Simulator;
                var sp = config.ActiveBpTestParams.StaticPressure;

                // 序列路径用采样旁路管开/停采样与读值窗口；单轮路径沿用原有显式开/停采样
                // 回读依赖采样的仪器（BPA700）必须先开采样，否则稳压值恒为 NaN、三个校准点全判 FAIL；
                // 康伟达常态回传 AA 帧，此处不发任何指令，指令流保持原样
                var rig = useSequenceSdk ? new AirPathRig(simulator, _log, null) : null;
                if (rig != null)
                {
                    await rig.StartAsync(token);
                }
                else if (simulator.RequiresSamplingForReadback)
                {
                    try
                    {
                        await simulator.StartSamplingAsync(token);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _log.Warn($"A17：开启压力采样失败，稳压值可能无回读：{ex.Message}");
                    }
                }

                var started = false;
                try
                {
                    // ① 启动压力传感器（0x65）：前置命令，有响应即通过；启动前先泄气归零，传感器零点在常压取
                    try
                    {
                        await simulator.VentToZeroAsync(ct: token);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _log.Warn($"A17：启动前泄气失败，传感器零点可能带残压：{ex.Message}");
                    }

                    try
                    {
                        await product.StartPressureSensorAsync(token);
                        started = true;
                        await collector.SetAsync(SpecItems.PcalStartSensor, TestItemResult.Of(1));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _log.Warn($"A17 启动压力传感器（0x65）无响应，判 FAIL：{ex.Message}");
                        await collector.SetAsync(SpecItems.PcalStartSensor, TestItemResult.Of(0));
                    }

                    if (!started)
                    {
                        // 前置命令失败：产品未进入压力静态测试态，各校准点无需空等稳压
                        await ReportPcalPointsAsFailAsync(collector);
                        await collector.SetAsync(SpecItems.PcalReadKb, TestItemResult.OfProductText(0, "压力传感器未启动，未读取"));
                    }
                    else if (rig != null)
                    {
                        await CalibrateBySequenceAsync(product, simulator, rig, collector, sp.BpaInflation,
                            SequenceHoldSeconds, PointArriveSeconds, token);
                        await ReportPcalKbAsync(product, collector, token);
                    }
                    else
                    {
                        await CalibrateBySingleRoundsAsync(product, simulator, collector, sp.BpaInflation,
                            Math.Max(sp.MeasureSeconds, 2), StableRoundEndMs, token);
                        await ReportPcalKbAsync(product, collector, token);
                    }
                }
                finally
                {
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

                    // 序列路径收尾时本轮可能还停在最后一个点的保持窗内：先主动结束本轮，否则仪器继续带压、下面的泄放判不到零
                    if (rig != null)
                    {
                        try
                        {
                            await simulator.StopSimulationAsync(CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            _log.Warn($"A17：结束静态压力序列失败：{ex.Message}");
                        }
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

                    // 排在泄气之后：BPA700 要靠采样回读判断是否已归零（与 BP-003 收尾同序）
                    if (rig != null)
                    {
                        await rig.StopAsync();
                    }
                    else if (simulator.RequiresSamplingForReadback)
                    {
                        try
                        {
                            await simulator.StopSamplingAsync();
                        }
                        catch (Exception ex)
                        {
                            _log.Warn($"A17：停止压力采样失败：{ex.Message}");
                        }
                    }
                }
            }
        }

        /// <summary>A17 三个校准点全判 FAIL（产品不可用或前置命令失败，指令发不出去）：测试值顶到规格外，保证必判不合格</summary>
        private static async Task ReportPcalPointsAsFailAsync(TestItemResultCollector collector)
        {
            foreach (var value in SpecItems.PcalPoints)
                await collector.SetAsync(SpecItems.PcalPoint(value), TestItemResult.Of(PcalTolerance + 1));
        }

        /// <summary>
        /// 校准完成后回读产品压力校准 K/B（0x07 0xC3 无参帧，应答参数区 = float K + float B 小端）。
        /// 判定：读回的 K、B 只要有一个不等于出厂默认值，即认为校准已写入（1=PASS，0=FAIL）；
        /// 无响应或参数区不足 8 字节按 FAIL，文本列写明原因，便于现场区分"没校准上"和"读不到"。
        /// </summary>
        private async Task ReportPcalKbAsync(IProductController product, TestItemResultCollector collector, CancellationToken token)
        {
            byte[]? payload = null;
            try
            {
                payload = await product.SendRawCommandAsync(ProductProtocol.ReadPressureKb, null, 2000, token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Warn($"A17：读取压力校准 K/B（0x07 0xC3）失败：{ex.Message}");
            }

            var kb = ProductProtocol.ParsePressureKb(payload);
            if (kb == null)
            {
                _log.Warn("A17：压力校准 K/B 回读无响应或参数区不足 8 字节，判 FAIL");
                await collector.SetAsync(SpecItems.PcalReadKb, TestItemResult.OfProductText(0, "读取失败/无响应"));
                return;
            }

            // 浮点比较容差：默认值保留 4 位小数，未校准时读回值与默认值差异应远小于该量级
            const double Epsilon = 1e-3;
            var (k, b) = kb.Value;
            var changed = Math.Abs(k - SpecItems.PcalKbDefaultK) > Epsilon
                          || Math.Abs(b - SpecItems.PcalKbDefaultB) > Epsilon;
            _log.Info($"A17 读取压力校准KB值：K={k:0.####} B={b:0.####}" +
                      $"（默认 {SpecItems.PcalKbDefaultK:0.####}/{SpecItems.PcalKbDefaultB:0.####}）→ {(changed ? "PASS" : "FAIL（仍是出厂默认值，校准未写入）")}");
            await collector.SetAsync(SpecItems.PcalReadKb,
                TestItemResult.OfProductText(changed ? 1 : 0, $"K={k:0.####} B={b:0.####}"));
        }

        /// <summary>
        /// A17 序列路径（BPA700 的 1.0.4.0-BETA 句柄版 SDK）：三点一次下发、点间直接切压不泄回零，每点在保持窗内读仪器均值并下发校准。
        /// 某点在 arriveTimeoutSeconds 内没到压，该点及其后都不报告（由收尾判 FAIL）；末点校准写入留 2s 间隔后再进收尾。
        /// </summary>
        private async Task CalibrateBySequenceAsync(IProductController product, IBloodPressureSimulator simulator,
            AirPathRig rig, TestItemResultCollector collector, bool inflation, int holdSeconds, int arriveTimeoutSeconds, CancellationToken token)
        {
            try
            {
                await RunStaticPressureSequenceByPointsAsync(simulator, SpecItems.PcalPoints, holdSeconds, inflation,
                    arriveTimeoutSeconds,
                    _ => Task.CompletedTask,
                    async i =>
                    {
                        // 与单轮路径同口径：稳压后再稍等取窗口末端均值，避免读到爬坡段（采样 200Hz，1s 均值窗在稳压时已积满，这段只是躲回落锯齿）
                        await Task.Delay(400, token);
                        await CalibratePcalPointAsync(product, collector, SpecItems.PcalPoints[i], rig.WindowAverage(), token);
                    },
                    token);
            }
            catch (TimeoutException ex)
            {
                _log.Warn($"A17：{ex.Message}（未报告的校准点由收尾判 FAIL）");
                return;
            }

            await Task.Delay(2000, token);
        }

        /// <summary>
        /// A17 单轮路径（康伟达）：逐点各起一轮静态压力，到稳压后下发校准，再等本轮走完升下一点（仪器要上一轮 Finish 才接受下一次调用）。
        /// 点间不额外泄压：康伟达每点开头的清场仪式自带泄残压+调零；残压若把稳压带偏，误差超 1.5mmHg 会拦住该点指令并判 FAIL。
        /// </summary>
        private async Task CalibrateBySingleRoundsAsync(IProductController product, IBloodPressureSimulator simulator,
            TestItemResultCollector collector, bool inflation, int stableSeconds, int stableRoundEndMs, CancellationToken token)
        {
            foreach (var value in SpecItems.PcalPoints)
            {
                token.ThrowIfCancellationRequested();
                var itemName = SpecItems.PcalPoint(value);
                try
                {
                    await simulator.SetStaticPressureAsync(value, stableSeconds, inflation, token);
                    await CalibratePcalPointAsync(product, collector, value, simulator.LastStaticPressure, token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.Warn($"A17 {itemName}：本点异常，判 FAIL：{ex.Message}");
                    await collector.SetAsync(itemName, TestItemResult.Of(PcalTolerance + 1));
                }
                finally
                {
                    // 只等本轮 Finish（设备测试态结束就能接受下一次调用）；Stable+量测时间才 Finish，
                    // 而前面 settle 0.8s + 写校准 0.2s 已占掉约 1s，这里实际只再等约 1s。
                    // 用 None 令牌：停止/取消时也要把设备收干净，不能把激活态留给下一点。
                    await simulator.WaitStaticPressureFinishAsync(stableRoundEndMs, CancellationToken.None);
                }
            }
        }

        /// <summary>A17 单个校准点的下发与上报（两条驱动路径共用）：稳压误差 ≤1.5mmHg 才在带压下发 0x66，有 ACK 判 PASS；超限或无 ACK 判 FAIL 并跳过指令</summary>
        private async Task CalibratePcalPointAsync(IProductController product, TestItemResultCollector collector,
            double value, double stable, CancellationToken token)
        {
            var itemName = SpecItems.PcalPoint(value);
            var hasInstrument = !double.IsNaN(stable);
            var err = hasInstrument ? Math.Abs(stable - value) : (double?)null;
            var ack = false;
            if (err.HasValue && err.Value <= PcalTolerance)
            {
                // 校准帧不带压力值，产品以"当前 ADC/内部压力"为基准；这里不再额外读 0x76：
                // BPA700 的稳压保持窗只有 2s，多一次 0x76 往返就可能把 0x66 挤到降压段。
                // 校准基准直接从仪器采样回读（仪器列），产品侧读数留到 READ KB 一项统一验证。

                try
                {
                    await product.CalibratePressureSensorAsync((ushort)value, token);
                    ack = true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.Warn($"A17 {itemName}：校准指令（0x66）无响应，判 FAIL：{ex.Message}");
                }
            }
            else
            {
                _log.Warn(hasInstrument
                    ? $"A17 {itemName}：稳压误差超限（稳压={stable:0.0}），跳过校准指令，判 FAIL"
                    : $"A17 {itemName}：模拟仪无稳压回读，跳过校准指令，判 FAIL");
            }

            var pass = ack && err.HasValue && err.Value <= PcalTolerance;
            _log.Info($"A17 {itemName}：稳压={(hasInstrument ? stable.ToString("0.0") : "--")} 误差={(err?.ToString("0.0") ?? "--")} 校准ACK={(ack ? "有" : "无")} → {(pass ? "PASS" : "FAIL")}");
            await collector.SetAsync(itemName, new TestItemResult
            {
                Value = pass ? err!.Value : PcalTolerance + 1,
                InstrumentValue = hasInstrument ? stable : null
            });
        }
    }
}
