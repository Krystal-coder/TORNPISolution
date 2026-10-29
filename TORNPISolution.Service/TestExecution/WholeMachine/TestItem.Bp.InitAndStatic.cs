using TORNPISolution.Interface;
using TORNPISolution.Service.Devices;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// TestItem 分部：血压 A01 初始化连接 / A03 测试启动与停止（BP-001~002）/ A05 静态压力精度（BP-003），含演示分支、产品压力回读、逐项上报等辅助。
    /// </summary>
    public partial class TestItem
    {
        /// <summary>
        /// 血压仪初始化连接：按仪器链接配置中选择的血压模拟仪初始化（经共享会话）；未选择模拟仪 → FAIL；连接成功写 1，失败/异常保持 0。
        /// </summary>
        //测试项目**************************---A01---**************************血压仪初始化连接
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
                await lease.Simulator.ApplySettingsAsync(
                    config.BPA700Simulator.Category,
                    config.BPA700Simulator.PressureUnit,
                    config.BPA700Simulator.AirReservoir,
                    token);
            }

            await collector.SetAsync(SpecItems.BpSimulatorInit, TestItemResult.Of(1));
        }


        /// <summary>
        /// 测试启动与停止功能（BP-001 短按启动键 / BP-002 短按停止键）：整机自带气源，按了键自己充放气，只看产品设备端状态（0x74）；产品通讯初始化仍集中在 A16（0x74 需产品处于产测模式）。
        /// ① BP-001：弹提示让测试员短按[测量启动]键 → 等 5s → 读设备端状态，回读 03 01（测量中）为 PASS；② BP-002：弹提示让测试员短按[测量停止]键 → 等 5s → 读设备端状态，回读 03 00（待机）为 PASS。
        /// 两项测试值均为 1/0（规格 1~1），实际回读字节显示在产品列；无产品演示模式下两项不判定（本模块没有仪器侧内容可演示）。
        /// </summary>
        //测试项目**************************---A03---**************************测试启动与停止功能（BP-001/BP-002）
        public async Task BpAccuracyTestModule(TestItemResultCollector collector, CancellationToken token)
        {
            const string ModuleName = "测试启动与停止功能";
            const int settleSeconds = 5;                              // 按键后等设备端状态落定再查询
            var measuring = (Mode: (byte)0x03, State: (byte)0x01);    // 启动键期望：测量中
            var standby = (Mode: (byte)0x03, State: (byte)0x00);      // 停止键期望：待机

            var config = _configService.Load();

            // 两项均以产品设备端状态判定，产品不可用时不弹按键提示。
            if (!GetProductAvailable(config.NoProductDemoMode))
            {
                if (config.NoProductDemoMode)
                {
                    _log.Info($"{ModuleName}：无产品演示模式，两项均以产品设备端状态判定，报不判定");
                    await collector.SetAsync(SpecItems.Bp001KeyStart, TestItemResult.Demo("产品未连接，不判定"));
                    await collector.SetAsync(SpecItems.Bp002KeyStop, TestItemResult.Demo("产品未连接，不判定"));
                    return;
                }
                _log.Error($"{ModuleName} 判 FAIL：产品不可用（串口未连接或 A16 握手失败），两项均以产品设备端状态（0x74）判定");
                await collector.SetAsync(SpecItems.Bp001KeyStart, TestItemResult.OfProductText(0, "产品未连接"));
                await collector.SetAsync(SpecItems.Bp002KeyStop, TestItemResult.OfProductText(0, "产品未连接"));
                return;
            }

            using (var product = _productFactory())
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
                    _log.Error($"{ModuleName} 判 FAIL：产品串口打开失败：{ex.Message}");
                    await collector.SetAsync(SpecItems.Bp001KeyStart, TestItemResult.OfProductText(0, "产品串口未连接"));
                    await collector.SetAsync(SpecItems.Bp002KeyStop, TestItemResult.OfProductText(0, "产品串口未连接"));
                    return;
                }

                // 一项一次往返：弹提示等按键 → 等状态落定 → 0x74 读回两字节与期望比对；读失败也报出结果，不让另一项跟着消失（未上报项会被收尾扫描再补一条 FAIL，但看不出原因）
                async Task CheckAsync(string itemName, string keyName, (byte Mode, byte State) expected, string expectedDesc)
                {
                    await PromptOperatorAsync(ModuleName, $"请短按产品[{keyName}]键，完成后点“确定”", token);
                    await Task.Delay(TimeSpan.FromSeconds(settleSeconds), token);

                    (byte Mode, byte State)? state;
                    try
                    {
                        state = await product.ReadDeviceStateAsync(token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _log.Warn($"{itemName} FAIL：读设备端状态（0x74）异常：{ex.Message}");
                        await collector.SetAsync(itemName, TestItemResult.OfProductText(0, "读取异常"));
                        return;
                    }

                    var read = state == null ? "无响应" : $"{state.Value.Mode:X2} {state.Value.State:X2}";
                    var pass = state != null && state.Value.Mode == expected.Mode && state.Value.State == expected.State;
                    if (pass)
                    {
                        _log.Info($"{itemName} PASS：设备端状态 {read}（{expectedDesc}）");
                    }
                    else
                    {
                        _log.Warn($"{itemName} FAIL：期望 {expected.Mode:X2} {expected.State:X2}（{expectedDesc}），实际 {read}"
                            + (state == null ? "；0x74 无响应，请确认方案已先执行 A16 产品通讯初始化（产品需处于产测模式）" : string.Empty));
                    }
                    await collector.SetAsync(itemName, TestItemResult.OfProductText(pass ? 1 : 0, read));
                }

                await CheckAsync(SpecItems.Bp001KeyStart, "测量启动", measuring, "测量中");
                await CheckAsync(SpecItems.Bp002KeyStop, "测量停止", standby, "待机");
            }
        }

        /// <summary>
        /// 血压静态压力精度测试（BP-003），产品通讯统一在 A16 初始化：
        /// ① 压力点/稳压时长/充气方式取血压模拟仪测试配置→当前启用设备→静态压力（仅勾选点参与），按仪器分两条驱动路径：
        ///    BPA700=静态压力序列一次下发全部点位（点之间直接切压不泄回零，省掉每轮约 10s 的设备恢复等待与从零重新充气）；
        ///    康伟达（或勾了「点间泄压」的 BPA700）=逐点单轮，每点经 RunStaticPressureSequenceAsync 执行（到位后本轮自带泄压收尾）。
        /// ② 每点到稳压后取窗口末端均值作仪器标准值，并在同一保持窗内回读产品压力（0x76），保证与仪器稳定值同时刻口径；
        /// ③ 逐点判定 产品−仪器 按静态规格 -0.5~0.5mmHg，一点一项（项名 SpecItems.Bp003Point，与 Spec 目录同源）：测试值=带符号偏差（正=产品偏高），
        ///    仪器列=稳压值、产品列=上报值；产品无上报的点判 FAIL，模块结论=各项全过；
        ///    全程开启压力采样，每个点的项只挂本点那一段曲线（时间轴逐点重开，不与前一点相连）；无仪器演示分支见 DemoStaticAccuracyAsync。
        /// </summary>
        //测试项目**************************---A05---**************************血压静态压力精度测试（BP-003）
        public async Task BpStaticAccuracyModule(TestItemResultCollector collector, CancellationToken token)
        {
            var config = _configService.Load();

            // 静态压力点按仪器链接配置选的模拟仪路由读取（康伟达读康伟达节点、BPA700 读 BPA700 节点），点位清单与目录同源
            var sp = config.ActiveBpTestParams.StaticPressure;
            var points = SpecItems.StaticPressurePoints(config);
            if (points.Count == 0)
            {
                _log.Warn($"BP-003 跳过：未勾选任何静态压力点（血压模拟仪测试配置 → {(config.ActiveSimulator == SimulatorType.Kangwenda ? "康伟达" : "BPA700")} → 静态压力）");
                return;
            }

            if (NoInstrumentDemo("血压静态压力精度测试"))
            {
                await DemoStaticAccuracyAsync(collector, points, token);
                return;
            }

            if (config.ActiveSimulator is not (SimulatorType.BPA700 or SimulatorType.Kangwenda))
            {
                _log.Error("血压静态压力精度测试跳过：未选择血压模拟仪（仪器链接配置 → 模拟仪类型）");
                return;
            }

            // 序列模式点之间直接切压、不泄回零，做不出「点间泄压」这项配置；勾选它时 BPA700 也走逐点单轮
            var useSequenceSdk = config.ActiveSimulator == SimulatorType.BPA700 && !sp.VentBetweenPoints;

            using (var product = _productFactory())
            {
                // 产品可用性以本轮 A16 产品通讯初始化记录为准（此处不再重复 0x72/0x74 问询）；本模块自建的 product 实例仍需打开产品串口，才能设置测试模式（0x73）并回读产品压力（0x76）
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
                        _log.Warn($"BP-003：产品串口打开失败，本模块按“无产品上报值”继续（仪器侧照常逐点稳压并上报）：{ex.Message}");
                    }
                }

                // 经共享会话取模拟仪（接口调用：只用接口成员）
                await using var lease = useSequenceSdk
                    ? await _simSession.RentSequenceSdkAsync(token)
                    : await _simSession.RentAsync(token);
                var simulator = lease.Simulator;

                var pointNames = points.Select(SpecItems.Bp003Point).ToArray();
                var pointValues = points.ToArray();
                var productValues = new double?[pointValues.Length];
                var currentPoint = -1;                                  // 正在测的点下标（-1 = 不在任何点的窗口内）
                var waveWatch = System.Diagnostics.Stopwatch.StartNew();
                var readTasks = new List<Task>();
                var readTasksLock = new object();

                // 序列路径用采样旁路（每点一条独立时间轴 + 窗口末端均值），单轮路径用下面这套逐点回调
                var rig = useSequenceSdk ? new AirPathRig(simulator, _log, PressureSampled) : null;

                void OnSample(double pressure)
                {
                    if (currentPoint < 0 || currentPoint >= pointNames.Length) return;
                    PressureSampled?.Invoke(pointNames[currentPoint],
                        new PressureSamplePoint(waveWatch.Elapsed.TotalSeconds, pressure));
                }

                // 稳压回调：在保持窗内并发回读产品上报值（不阻塞仪器回调线程），按当前点下标定位
                void OnPointStable(int index, double stable)
                {
                    var i = currentPoint;
                    if (!productAvailable || i < 0 || i >= productValues.Length) return;
                    var task = Task.Run(async () =>
                    {
                        // 点位级追踪上下文：本点的 0x76 TX/RX/解析值日志统一带 [A05…·BP-003@90mmHg] 前缀
                        using (TraceScope.Push($"BP-003@{pointValues[i]:0.##}mmHg"))
                        {
                            try
                            {
                                productValues[i] = await product.ReadProductPressureAsync(token);
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                _log.Warn($"BP-003 测试点{i + 1} 产品上报值读失败：{ex.Message}");
                            }
                        }
                    }, token);
                    lock (readTasksLock) readTasks.Add(task);
                }

                var staticModeEntered = false;
                try
                {
                    if (rig != null)
                    {
                        await rig.StartAsync(token);
                    }
                    else
                    {
                        simulator.PressureSampled += OnSample;
                        simulator.PressurePointStable += OnPointStable;
                        await simulator.StartSamplingAsync(token);
                    }

                    // 产品可用时才设置产品测试模式（0x73 mode=3）并读取产品上压力值；设置失败不中止仪器序列：各点产品上报值按缺失处理（产品列判 FAIL），仪器稳压值照常上报
                    if (productAvailable)
                    {
                        try
                        {
                            await product.SetTestModeAsync(3, ct: token);
                            staticModeEntered = true;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            productAvailable = false;
                            _log.Warn($"BP-003：产品测试模式设置失败，各点按“无产品上报值”判 FAIL：{ex.Message}");
                        }
                    }
                    else
                    {
                        _log.Warn("BP-003：产品未连接或握手失败，各压力点将按“无产品上报值”判 FAIL");
                    }

                    if (rig != null)
                    {
                        // 序列路径：全部点位一次下发，逐点 Stable 后在本点保持窗内取稳压值并回读产品
                        await BpStaticAccuracyBySequenceAsync(product, simulator, rig, collector, pointValues,
                            productAvailable, sp.BpaInflation, Math.Max(sp.MeasureSeconds, 2), token);
                    }
                    else
                    {
                        // 逐点单点调用（SDK 一次只支持一个压力点，多点由上位机循环）；每点保持窗内发起的产品回读在下一点开始前收干净
                        var stablePressures = new double[pointValues.Length];
                        for (var i = 0; i < pointValues.Length; i++)
                        {
                            currentPoint = i;
                            waveWatch = System.Diagnostics.Stopwatch.StartNew();   // 本点独立时间轴
                            stablePressures[i] = (await simulator.RunStaticPressureSequenceAsync(
                                new[] { pointValues[i] }, sp.MeasureSeconds, sp.BpaInflation, token))[0];

                            Task[] pending;
                            lock (readTasksLock) { pending = readTasks.ToArray(); readTasks.Clear(); }
                            await Task.WhenAll(pending);

                            // 点间泄压（配置选「点间泄压」时）：本点测完泄回零再升下一点，末点留给模块结束统一泄
                            if (sp.VentBetweenPoints && i < pointValues.Length - 1)
                            {
                                try { await simulator.VentToZeroAsync(ct: token); }
                                catch (OperationCanceledException) { throw; }
                                catch (Exception ex) { _log.Warn($"BP-003 点间泄压失败（下一点清场会补偿）：{ex.Message}"); }
                            }
                        }
                        currentPoint = -1;   // 收尾段（泄气/退模式）不再画进任何点的曲线

                        await ReportStaticAccuracyAsync(collector, pointValues, stablePressures, productValues);
                    }
                }
                finally
                {
                    // 序列路径的末点可能还停在保持窗里：先结束本轮稳压，否则仪器一边补压一边泄，回零判不到
                    if (rig != null)
                    {
                        try
                        {
                            await simulator.StopSimulationAsync(CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            _log.Warn($"BP-003：结束静态压力序列失败：{ex.Message}");
                        }
                    }

                    // 模块结束泄气：接大容积容器时避免残压憋到下一模块；停/异常路径也尽力泄。
                    // 排在停采样之前：BPA700 要靠采样回读判断是否已归零
                    try
                    {
                        await simulator.VentToZeroAsync(ct: CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _log.Warn($"BP-003：模块结束泄气失败：{ex.Message}");
                    }

                    if (rig != null)
                    {
                        await rig.StopAsync();
                    }
                    else
                    {
                        await simulator.StopSamplingAsync();
                        simulator.PressureSampled -= OnSample;
                        simulator.PressurePointStable -= OnPointStable;
                    }

                    // 测试完退出静态压力模式（0x73 mode=3 status=1）：用 None 令牌保证停止/异常路径也执行，避免产品遗留在静态压力模式影响后续模块
                    if (staticModeEntered)
                    {
                        try
                        {
                            await product.SetTestModeAsync(3, 1, CancellationToken.None);
                            _log.Info("BP-003：已退出静态压力模式（0x73 mode=3 status=1）");
                        }
                        catch (Exception ex)
                        {
                            _log.Warn($"BP-003：退出静态压力模式（0x73 mode=3 status=1）失败：{ex.Message}");
                        }
                    }
                }
            }
        }

        /// <summary>BP-003 无仪器演示分支：不驱动模拟仪（无仪器参考值），进静态压力模式（0x73 mode=3）后逐点回读产品上报压力（0x76），实测值填产品列</summary>
        private async Task DemoStaticAccuracyAsync(TestItemResultCollector collector, List<double> points, CancellationToken token)
        {
            const double Tolerance = 0.5;   // mmHg，与正式流程同规格

            using var product = _productFactory();
            var opened = false;
            var staticModeEntered = false;
            try
            {
                await product.OpenAsync(token);
                opened = true;
                await product.SetTestModeAsync(3, ct: token);   // 进静态压力模式，产品在该模式下才上报静态压力
                staticModeEntered = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Warn($"BP-003 演示：产品串口打开/设置模式失败，各点产品列显示 --：{ex.Message}");
            }

            try
            {
                // 探活：产品无响应就不逐点空等 3s 超时
                var probe = opened ? await ReadProductPressureAsync(product, "BP-003 演示探活", token) : double.NaN;
                if (double.IsNaN(probe))
                {
                    _log.Warn("BP-003 演示：产品无响应（0x76 读回缺失），各压力点按读回缺失判 FAIL");
                    foreach (var value in points)
                        await collector.SetAsync(SpecItems.Bp003Point(value), TestItemResult.Of(Tolerance + 1));
                    return;
                }

                foreach (var value in points)
                {
                    token.ThrowIfCancellationRequested();
                    var itemName = SpecItems.Bp003Point(value);
                    var read = await ReadProductPressureAsync(product, $"{itemName} 产品上报", token);
                    _log.Info($"BP-003 演示 {itemName}：产品上报={(double.IsNaN(read) ? "--" : read.ToString("0.0"))}（无仪器参考值，不判合格）");

                    // 无仪器参考值算不出误差：测试值落规格外保证 FAIL，产品列照常显示读回值
                    await collector.SetAsync(itemName, new TestItemResult
                    {
                        Value = Tolerance + 1,
                        ProductValue = double.IsNaN(read) ? null : read
                    });
                }
            }
            finally
            {
                // 测试完退出静态压力模式（0x73 mode=3 status=1），避免产品遗留在静态压力模式
                if (staticModeEntered)
                {
                    try
                    {
                        await product.SetTestModeAsync(3, 1, CancellationToken.None);
                        _log.Info("BP-003 演示：已退出静态压力模式（0x73 mode=3 status=1）");
                    }
                    catch (Exception ex)
                    {
                        _log.Warn($"BP-003 演示：退出静态压力模式失败：{ex.Message}");
                    }
                }
            }
        }

        /// <summary>BP-003 逐点判定与上报（一点一项）：每点 产品上报值 − 仪器稳压值 按静态规格 -0.5~0.5mmHg 判定；无产品上报/仪器未稳压的点误差落到规格外保证 FAIL，缺值列显示 --。</summary>
        private async Task ReportStaticAccuracyAsync(TestItemResultCollector collector,
            double[] pointValues, double[] stablePressures, double?[] productValues)
        {
            for (var i = 0; i < pointValues.Length; i++)
                await ReportStaticAccuracyPointAsync(collector, pointValues[i], stablePressures[i], productValues[i]);
        }

        /// <summary>BP-003 单点判定与上报（两条驱动路径共用）：产品/仪器任一缺值时误差算不出，测试值落到规格外保证 FAIL。</summary>
        private async Task ReportStaticAccuracyPointAsync(TestItemResultCollector collector,
            double target, double standard, double? productValue)
        {
            const double Tolerance = 0.5;   // mmHg，测试要求文档固定

            var hasInstrument = !double.IsNaN(standard);
            var hasProduct = productValue.HasValue && !double.IsNaN(productValue.Value);
            // 带符号上报：取绝对值的话规格下界 -0.5 永远用不上，也看不出产品偏高还是偏低
            var err = hasInstrument && hasProduct ? productValue!.Value - standard : (double?)null;
            var pass = err.HasValue && Math.Abs(err.Value) <= Tolerance;
            var remark = pass
                ? string.Empty
                : !hasProduct ? "无产品上报值（产品未连接或无响应）"
                : !hasInstrument ? "仪器未稳压到位"
                // 备注只写未通过原因，具体偏差数值看测试值列与下一行日志
                : $"误差超过 ±{Tolerance:0.##}mmHg（测试值为带符号偏差：正=产品偏高）";

            var itemName = SpecItems.Bp003Point(target);
            _log.Info($"BP-003 {itemName}：目标={target:0.#} 仪器稳压={(hasInstrument ? standard.ToString("0.0") : "--")} 产品上报={(hasProduct ? productValue!.Value.ToString("0.0") : "--")} 误差={(err?.ToString("0.0") ?? "--")} → {(pass ? "PASS" : "FAIL（" + remark + "）")}");

            await collector.SetAsync(itemName, new TestItemResult
            {
                Value = err ?? Tolerance + 1,
                InstrumentValue = hasInstrument ? standard : null,
                ProductValue = hasProduct ? productValue : null
            });
        }

        /// <summary>
        /// BP-003 序列路径（BPA700 句柄版 SDK）：全部压力点一次下发，点之间直接切压不泄回零，省掉每轮约 10s 的设备恢复等待与从零重新充气。
        /// 每点 Stable 后先等采样铺平再取窗口末端均值作仪器标准值，并在同一保持窗内回读产品 0x76；某点到压超时后续点测不到，剩余项由收尾判 FAIL。
        /// </summary>
        private async Task BpStaticAccuracyBySequenceAsync(IProductController product, IBloodPressureSimulator simulator,
            AirPathRig rig, TestItemResultCollector collector, double[] pointValues, bool productAvailable,
            bool inflation, int holdSeconds, CancellationToken token)
        {
            const int SettleMs = 800;
            const int PointArriveSeconds = 60;

            try
            {
                await RunStaticPressureSequenceByPointsAsync(simulator, pointValues, holdSeconds, inflation,
                    PointArriveSeconds,
                    i =>
                    {
                        rig.BeginPhase(SpecItems.Bp003Point(pointValues[i]));   // 本点独立时间轴
                        return Task.CompletedTask;
                    },
                    async i =>
                    {
                        await Task.Delay(SettleMs, token);
                        var standard = rig.WindowAverage();
                        double? productValue = null;
                        if (productAvailable)
                        {
                            var read = await ReadProductPressureAsync(product, $"{pointValues[i]:0.#}mmHg 上报", token);
                            if (!double.IsNaN(read)) productValue = read;
                        }
                        await ReportStaticAccuracyPointAsync(collector, pointValues[i], standard, productValue);
                    },
                    token);
            }
            catch (TimeoutException ex)
            {
                _log.Warn($"BP-003：{ex.Message}（未报告的点由收尾判 FAIL）");
            }
        }

    }
}
