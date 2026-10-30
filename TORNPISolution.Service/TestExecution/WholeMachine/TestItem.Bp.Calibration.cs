using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// TestItem 分部：血压校准——A17 产品静态压力传感器逐点校准。
    /// </summary>
    public partial class TestItem
    {
        /// <summary>A17 校准点的稳压误差规格（mmHg），与 BP-003 同口径</summary>
        private const double PcalTolerance = 3;

        /// <summary>
        /// 产品静态压力校准（A17）：产品压力传感器逐点校准，校准点固定 50/150/250mmHg，时序与手动校准一致（无需先进静态压力模式）。
        /// ① 启动压力传感器（0x65）前先泄气归零（常压下启动传感器），有响应即通过；
        /// ② 逐点在带压下下发校准（0x66 + uint16 LE）：|稳压值-校准点| ≤3mmHg 才发指令，有 ACK 判 PASS（测试值=稳压误差，仪器列=稳压值），
        ///    稳压超限或无 ACK 判 FAIL 并跳过该点指令。驱动按仪器分两条路：
        ///    BPA700=静态压力序列一次下发三点（点间直接切压不泄回零，省掉每轮约 10s 的设备恢复等待与从零重新充气）；
        ///    康伟达=逐点单轮（到稳压后发指令，等本轮走完再升下一点，仪器要 Finish 才接受下一次调用）。
        /// ③ 三个点完成后关闭压力传感器（0x67）：finally + None 令牌，停止/异常路径也发送；产品不可用（未连接/握手失败）时全项判 FAIL、不驱动模拟仪。
        /// </summary>
        //测试项目**************************---A17---**************************产品静态压力校准
        public async Task ProductStaticPressureCalibrationModule(TestItemResultCollector collector, CancellationToken token)
        {
            const int StableSeconds = 2;        // 正式版单点轮的保持窗：0x66 落在 Stable+1.2s、产品侧校准写入需 2s，Finish 在保持窗末端再多约 1.6s，2s 窗口内写入全程带压（临时值，后续按 SOP 调整）
            const int SequenceHoldSeconds = 3;  // 序列模式每点保持窗：从该点回读稳压起计时。SDK 只给所有点同一个秒数，所以下限由末点定（读值 0.4s + 0x66 往返 0.15s + 上报 0.15s + 校准写入需带压 2s ≈ 2.7s）；前两点各自只用 0.7s，保持窗剩下的就是等设备切点的白等（临时值，后续按 SOP 调整）
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

                var useSequenceSdk = config.ActiveSimulator == SimulatorType.BPA700;
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
                    }
                    else if (rig != null)
                    {
                        await CalibrateBySequenceAsync(product, simulator, rig, collector, sp.BpaInflation,
                            SequenceHoldSeconds, PointArriveSeconds, token);
                    }
                    else
                    {
                        await CalibrateBySingleRoundsAsync(product, simulator, collector, sp.BpaInflation,
                            StableSeconds, StableRoundEndMs, token);
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
        /// 点间不额外泄压：康伟达每点开头的清场仪式自带泄残压+调零；残压若把稳压带偏，误差超 3mmHg 会拦住该点指令并判 FAIL。
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
                    // 等本轮稳压窗走完（不等 Finish 下一点会被仪器拒绝）；产品侧校准写入需 2s 间隔：
                    // 该间隔落在稳压窗内不额外耗时，无异步测试态的仪器由这条延时补足，两者并发等待取较长者
                    await Task.WhenAll(
                        simulator.WaitStaticPressureFinishAsync(stableRoundEndMs, token),
                        Task.Delay(2000, token));
                }
            }
        }

        /// <summary>A17 单个校准点的下发与上报（两条驱动路径共用）：稳压误差 ≤3mmHg 才在带压下发 0x66，有 ACK 判 PASS；超限或无 ACK 判 FAIL 并跳过指令</summary>
        private async Task CalibratePcalPointAsync(IProductController product, TestItemResultCollector collector,
            double value, double stable, CancellationToken token)
        {
            var itemName = SpecItems.PcalPoint(value);
            var hasInstrument = !double.IsNaN(stable);
            var err = hasInstrument ? Math.Abs(stable - value) : (double?)null;
            var ack = false;
            if (err.HasValue && err.Value <= PcalTolerance)
            {
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
