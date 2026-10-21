using TORNPISolution.Interface;
using TORNPISolution.Service.Devices;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// TestItem 分部：血压校准——A17 产品静态压力传感器逐点校准。
    /// </summary>
    public partial class TestItem
    {
        /// <summary>
        /// 产品静态压力校准（A17）：产品压力传感器逐点校准，校准点固定 60/120/180/240mmHg，时序与手动校准一致（无需先进静态压力模式）。
        /// ① 启动压力传感器（0x65）前先泄气归零（零点在常压取），有响应即通过；
        /// ② 逐点：先泄压（终止压力源调压再放气）-> 模拟仪稳压 3s -> |稳压值-校准点| ≤3mmHg 才发校准（0x66 + uint16 LE）
        ///    -> 有 ACK 判 PASS（测试值=稳压误差，仪器列=稳压值）-> 延时 2s -> 点后泄气 -> 下一点；稳压超限或无 ACK 判 FAIL 并跳过该点指令；
        /// ③ 四个点完成后关闭压力传感器（0x67）：finally + None 令牌，停止/异常路径也发送；产品不可用（未连接/握手失败）时全项判 FAIL、不驱动模拟仪。
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

                // 回读依赖采样的仪器（BPA700）必须先开采样，否则稳压值恒为 NaN、四个校准点全判 FAIL；
                // 康伟达常态回传 AA 帧，此处不发任何指令，指令流保持原样
                if (simulator.RequiresSamplingForReadback)
                {
                    try
                    {
                        await simulator.StartSamplingAsync(token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
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
                        foreach (var value in SpecItems.PcalPoints)
                        {
                            token.ThrowIfCancellationRequested();
                            var itemName = SpecItems.PcalPoint(value);

                            // 切换前泄压（手动实测：先泄到 0 再切下一点更准）；未归零（有回读确认）补泄一次，康伟达保压顶阀时驱动坚持开阀等设备放弃；仍带残压也不硬断，后续稳压误差 >3mmHg 或稳压值无回读会拦住校准指令
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
                                _log.Warn(hasInstrument
                                    ? $"A17 {itemName}：稳压误差超限（稳压={stable:0.0}），跳过校准指令，判 FAIL"
                                    : $"A17 {itemName}：模拟仪无稳压回读，跳过校准指令，判 FAIL");
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

                            // 每点校准完成后立即泄放回零：接大容积容器时憋压会让泵补载持续发热。
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

                    // 排在泄气之后：BPA700 要靠采样回读判断是否已归零（与 BP-003 收尾同序）
                    if (simulator.RequiresSamplingForReadback)
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

    }
}
