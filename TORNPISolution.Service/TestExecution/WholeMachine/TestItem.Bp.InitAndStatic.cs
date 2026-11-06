using System.Diagnostics;

using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// TestItem 分部：血压 A01 初始化连接 / A03 测试启动与停止（BP-001~002）/ A05 静态压力精度（BP-003），含产品压力回读、逐项上报等辅助。
    /// </summary>
    public partial class TestItem
    {
        /// <summary>
        /// 血压仪初始化连接：按仪器链接配置中选择的血压模拟仪初始化（经共享会话）；未选择模拟仪 → FAIL；连接成功写 1，失败/异常保持 0。
        /// </summary>
        //测试项目**************************---A01---**************************血压仪初始化连接
        public async Task BpDeviceInitModule(TestItemResultCollector collector, CancellationToken token)
        {
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
        /// 两项测试值均为 1/0（规格 1~1），实际回读字节显示在产品列。
        /// </summary>
        //测试项目**************************---A03---**************************测试启动与停止功能（BP-001/BP-002）
        public async Task BpAccuracyTestModule(TestItemResultCollector collector, CancellationToken token)
        {
            const string ModuleName = "测试启动与停止功能";
            const int settleSeconds = 5;                              // 按键后等设备端状态落定再查询
            var measuring = (Mode: (byte)0x03, State: (byte)0x01);    // 启动键期望：测量中
            var standby = (Mode: (byte)0x03, State: (byte)0x00);      // 停止键期望：待机

            // 两项均以产品设备端状态判定，产品不可用时不弹按键提示。
            if (!GetProductAvailable())
            {
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
                catch (Exception ex) when (ex is not OperationCanceledException)
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
                    catch (Exception ex) when (ex is not OperationCanceledException)
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
        ///    仪器列=稳压值、产品列=上报值；产品无上报的点判 FAIL，模块结论=各项全过；单轮路径每点测完立即上报（序列路径同样逐点上报），不等全部点跑完；
        ///    全程开启压力采样，每个点的项只挂本点那一段曲线（时间轴逐点重开，不与前一点相连）。
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
                var waveWatch = Stopwatch.StartNew();
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
                                productValues[i] = await ReadStableProductPressureAsync(product, pointValues[i], token);
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
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
                        catch (Exception ex) when (ex is not OperationCanceledException)
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
                            waveWatch = Stopwatch.StartNew();   // 本点独立时间轴
                            stablePressures[i] = (await simulator.RunStaticPressureSequenceAsync(
                                new[] { pointValues[i] }, sp.MeasureSeconds, sp.BpaInflation, token))[0];

                            Task[] pending;
                            lock (readTasksLock) { pending = readTasks.ToArray(); readTasks.Clear(); }
                            await Task.WhenAll(pending);

                            // 逐点上报：本点仪器稳压值与产品回读都齐了就立即交出一项，UI 边测边出，不等全部点跑完
                            await ReportStaticAccuracyPointAsync(collector, pointValues[i], stablePressures[i], productValues[i]);

                            // 点间泄压（配置选「点间泄压」时）：本点测完泄回零再升下一点，末点留给模块结束统一泄
                            if (sp.VentBetweenPoints && i < pointValues.Length - 1)
                            {
                                try { await simulator.VentToZeroAsync(ct: token); }
                                catch (Exception ex) when (ex is not OperationCanceledException) { _log.Warn($"BP-003 点间泄压失败（下一点清场会补偿）：{ex.Message}"); }
                            }
                        }
                        currentPoint = -1;   // 收尾段（泄气/退模式）不再画进任何点的曲线
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

        /// <summary>BP-003 单点判定与上报（两条驱动路径共用）：产品/仪器任一缺值时误差算不出，测试值落到规格外保证 FAIL。</summary>
        private async Task ReportStaticAccuracyPointAsync(TestItemResultCollector collector,
            double target, double standard, double? productValue)
        {
            const double Tolerance = 0.5;   // mmHg，测试要求文档固定
            const double TargetTolerance = 2.0;   // mmHg：仪器稳压值必须贴住本点目标（设备稳压判据 ±1.5，留余量）

            var hasInstrument = !double.IsNaN(standard);
            var hasProduct = productValue.HasValue && !double.IsNaN(productValue.Value);
            // 先卡"本点到底测的是不是这个目标"：设 100 却停在 250 时，产品回读和仪器一起偏，
            // 误差照样算得出 0——不先拦住就会判 PASS（牛头不对马嘴）。仪器没到点一律不报告误差。
            var instrumentAtTarget = hasInstrument && Math.Abs(standard - target) <= TargetTolerance;
            // 带符号上报：取绝对值的话规格下界 -0.5 永远用不上，也看不出产品偏高还是偏低
            var err = instrumentAtTarget && hasProduct ? productValue!.Value - standard : (double?)null;
            var pass = err.HasValue && Math.Abs(err.Value) <= Tolerance;
            var remark = pass
                ? string.Empty
                : !hasProduct ? "无产品上报值（产品未连接或无响应）"
                : !hasInstrument ? "仪器未稳压到位"
                : !instrumentAtTarget ? $"仪器未稳到本点目标（仪器 {standard:0.0} / 目标 {target:0.0}，偏差超过 ±{TargetTolerance:0.##}mmHg）"
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
            const int PointArriveSeconds = 60;

            try
            {
                await RunStaticPressureSequenceByPointsAsync(simulator, pointValues, holdSeconds, inflation,
                    PointArriveSeconds,
                    i =>
                    {
                        // 本点独立时间轴；起压阈值取目标 10%（100→10、200→20）：
                        // BPA700 每点前有一段设备恢复/泄压平台（现场约 8.8s），与目标无关，
                        // 画进曲线只占时间轴、把真正的升压段压扁
                        rig.BeginPhase(SpecItems.Bp003Point(pointValues[i]), pointValues[i] * 0.1);
                        return Task.CompletedTask;
                    },
                    async i =>
                    {
                        // 等压力源真正稳到 ±0.5mmHg 再取标准值：SDK 报 Stable 后曲线仍可能小幅爬升/回落，
                        // 0.8s 窗口均值会在目标附近跳 1~2mmHg（0.5 规格下这点抖动就足以判错）
                        var standard = await WaitPressureStableAsync(rig, pointValues[i], 0.5, 5, token);
                        double? productValue = null;
                        if (productAvailable)
                        {
                            var read = await ReadStableProductPressureAsync(product, pointValues[i], token);
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

        /// <summary>
        /// 等仪器采样均值真正贴住本点目标（|均值−目标| ≤ tolerance 连续三拍，约 0.6s）再取标准值。
        /// BPA700 的 Stable 只是 SDK 判定点，之后曲线仍会小幅爬升/回落，0.8s 窗口均值在目标附近能跳 1~2mmHg；
        /// ±0.5mmHg 的规格下这点抖动就足以把合格点判成不合格。超时用最后均值并记 Warn，不中断本轮。
        /// </summary>
        private async Task<double> WaitPressureStableAsync(AirPathRig rig, double target, double tolerance,
            int timeoutSeconds, CancellationToken token)
        {
            await Task.Delay(800, token);   // 原固定 settle：先让采样窗从爬坡段走出来

            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            var last = rig.WindowAverage();
            var hits = 0;
            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                last = rig.WindowAverage();
                if (!double.IsNaN(last) && Math.Abs(last - target) <= tolerance)
                {
                    if (++hits >= 3) return last;
                }
                else hits = 0;
                await Task.Delay(200, token);
            }

            _log.Warn($"BP-003 {target:0.#}mmHg：{timeoutSeconds}s 内仪器稳压没进 ±{tolerance:0.##}（当前 {last:0.0}），按最后均值判定");
            return last;
        }

        /// <summary>
        /// 读产品静压（0x76）并等它自己稳下来：连续两次读数差 ≤0.5mmHg 才采用，最多读 5 次（间隔 300ms）。
        /// 产品内部压力滤波/上报有滞后，Stable 瞬间读到的是还在爬的中间值；不稳定的单次读数会在 ±0.5 规格下造成 3~5mmHg 的假误差。
        /// </summary>
        private async Task<double> ReadStableProductPressureAsync(IProductController product, double target, CancellationToken token)
        {
            double prev = double.NaN, last = double.NaN;
            for (var i = 0; i < 5; i++)
            {
                last = await ReadProductPressureAsync(product, $"{target:0.#}mmHg 上报", token);
                if (!double.IsNaN(last) && !double.IsNaN(prev) && Math.Abs(last - prev) <= 0.5)
                    return last;
                prev = last;
                if (i < 4) await Task.Delay(300, token);
            }
            return last;
        }

    }
}
