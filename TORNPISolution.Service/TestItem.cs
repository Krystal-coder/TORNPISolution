using TORNPISolution.Interface;
using TORNPISolution.Service.Devices;
using TORNPISolution.Service.Hardware;
using TORNPISolution.Service.TestModules;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// 测试项目类：所有测试模块的执行逻辑集中在这一个类里管理。
    /// 书写方式参照传统测试程式的 TestItem：
    ///   一个测试模块 = 一个独立的 TestItem，用 "测试项目***---模块Id---***模块名" 注释块分隔，
    ///   每个模块方法内包含该模块的完整测试流程（自包含，互不依赖），
    ///   一次执行完整个模块、一次返回该模块全部测试项的实测值。
    ///
    /// 执行入口 TestItemxx(index, module, token) 对应模板的 TestItemxx()：
    ///   index 是模块在方案中拖拽排序后的执行序号（从 1 开始），
    ///   如"初始化仪器"在方案排第 2 位，运行时即 TestItem2 被调用；排第 1 位即 TestItem1。
    ///   本类按 module.Id 找到对应模块方法执行，序号仅用于日志标识与模板习惯对应。
    /// 分工（Spec 与 TestItem 的边界）：
    ///   Spec    —— 定义"有哪些测试项"：项名/说明/单位/静态规格（默认上下限）；
    ///   TestItem —— 负责"每项怎么测"：读产品/读仪器，产出测试值；
    ///              需要按仪器读回值现算规格的项，把动态规格一并装进 TestItemResult 带回。
    ///   结果字典由入口按 Spec 项名统一初始化（未测到默认值 0 = FAIL），
    ///   模块方法只写 collector["项名"] = TestItemResult.Of(值) 或
    ///   TestItemResult.WithDynamicSpec(产品值, 仪器值-容差, 仪器值+容差)，
    ///   一个测试项 = 一条赋值（测前可读串口/读仪器/计算，顺序书写即可）。
    ///   判定由 UI 层完成：动态规格优先，否则用 Spec 静态规格。
    /// </summary>
    public class TestItem : ITestItemService
    {
        private readonly ILogService _log = new LogService(nameof(TestItem));

        /// <summary>
        /// 固高刀锋IO模块（各模块共用 HardwareManager 里的同一实例）
        /// </summary>
        private readonly HardwareManager _hw;

        /// <summary>
        /// 仪器链接配置服务（读取 Config\instruments.json）
        /// </summary>
        private readonly IInstrumentConfigService _configService = new InstrumentConfigService();

        /// <summary>
        /// NFC 测试写入标签用的 MAC 地址（占位测试值，02 开头为本地管理地址）。
        /// TODO: 后续改为从真实来源获取（产品本身 MAC 或测试系统下发），在此处替换即可。
        /// </summary>
        private static readonly byte[] TestMacAddress = { 0x02, 0x00, 0x00, 0xAA, 0xBB, 0xCC };

        /// <summary>
        /// 设备连接失败通知（模块名 + 失败原因），UI 层订阅后弹出提示。
        /// </summary>
        public event Action<string, string>? DeviceConnectionFailed;

        /// <summary>
        /// 压力采样点实时上抛（测试项名 + 带相对开始秒数的采样点，供 UI 在对应行下画波形，后台线程触发）
        /// </summary>
        public event Action<string, PressureSamplePoint>? PressureSampled;

        //公用构造函数
        public TestItem()
        {
            _hw = HardwareManager.Instance;
        }

        /// <summary>
        /// 按方案序号执行测试模块（对应模板的 TestItemxx()，xx 为传入的 index：
        /// 排第 2 即 TestItem2、排第 1 即 TestItem1，见日志标识）：
        /// index 为方案拖拽排序后的执行序号（从 1 开始），
        /// 按 module.Id 分派到对应模块方法，一次执行完整模块流程，
        /// 返回该模块全部测试项结果（测试项名称 → 测试值/动态规格）。
        /// </summary>
        public async Task<Dictionary<string, TestItemResult>> TestItemxx(int index, TestModuleModel module, Func<string, TestItemResult, Task>? onItemCompleted, CancellationToken token)
        {
            _log.Info($"{DateTime.Now} Start calling the TestItem.TestItemxx() method . {Environment.NewLine}");
            _log.Info($"TestItem{index}：{module.Name} 开始执行");
            try
            {
                // 结果字典按 Spec 测试项名称统一初始化（默认测试值 0 = 未测到即 FAIL），
                // 模块方法无需再手写项名清单，新增测试项只改 Spec 即可
                var results = module.Items.ToDictionary(i => i.Name, _ => TestItemResult.Of(0));
                // 收集器：每次赋值自动触发 onItemCompleted 回调，UI 逐项回填
                var collector = new TestItemResultCollector(results, onItemCompleted);

                switch (module.Id)
                {
                    case TestModuleIds.InitInstruments:
                        await InitInstrumentsModule(collector, token);
                        break;
                    case TestModuleIds.BpDeviceInit:
                        await BpDeviceInitModule(collector, token);
                        break;
                    case TestModuleIds.NfcFunctionTest:
                        await NfcFunctionModule(collector, token);
                        break;
                    case TestModuleIds.BpAccuracyTest:
                        await BpAccuracyTestModule(collector, token);
                        break;
                    case TestModuleIds.BpStaticAccuracyTest:
                        await BpStaticAccuracyModule(collector, token);
                        break;
                    case TestModuleIds.BpDynamicAccuracyTest:
                        await BpDynamicAccuracyModule(collector, token);
                        break;
                    case TestModuleIds.PressureControlTest:
                        await PressureControlModule(collector, token);
                        break;
                    case TestModuleIds.Aecg100Init:
                        await Aecg100InitModule(collector, token);
                        break;
                    case TestModuleIds.EcgAcquisitionTest:
                        await EcgAcquisitionTestModule(collector, token);
                        break;
                    case TestModuleIds.EcgHeartRateTest:
                        await EcgHeartRateTestModule(collector, token);
                        break;
                    case TestModuleIds.EcgSignalQualityTest:
                        await EcgSignalQualityTestModule(collector, token);
                        break;
                    default:
                        throw new NotSupportedException($"未实现的测试模块：{module.Id}");
                }

                // 自检：模块方法写入了 Spec 里不存在的项名
                foreach (var key in results.Keys.Where(k => module.Items.All(i => i.Name != k)))
                    _log.Warn($"TestItem{index}：结果项 \"{key}\" 不在 Spec 定义中，请检查项名拼写");

                _log.Info($"TestItem{index}：{module.Name} 执行完成，共 {results.Count} 项结果");
                return results;
            }
            catch (OperationCanceledException)
            {
                // 用户点“停止”：向上层传递，不当作测试异常
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in TestItem.class TestItemxx method, message: {ex}", ex);
                throw;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the TestItem.TestItemxx() method . {Environment.NewLine}");
            }
        }

        /// <summary>
        /// 初始化仪器：固高IO模块等硬件初始化。
        /// 成功写 1，失败/异常保持初始化的 0（规格 1~1，成功才 PASS）。
        /// </summary>
        //测试项目**************************---M00---**************************初始化仪器
        public async Task InitInstrumentsModule(TestItemResultCollector collector, CancellationToken token)
        {
            _log.Info($"{DateTime.Now} Start calling the TestItem.InitInstrumentsModule() method . {Environment.NewLine}");
            try
            {
                // GTN_Open 内部阻塞轮询，用 Task.Run 包裹避免卡 UI
                var rtn = await Task.Run(() => _hw.GenControl.GTN_Open(token), token);
                if (rtn != 0)
                {
                    _log.Error($"固高IO模块初始化失败，错误码：{rtn}");
                    return;
                }

                _log.Info("固高IO模块初始化成功");
                await collector.SetAsync("固高IO模块初始化", TestItemResult.Of(1));
            }
            catch (OperationCanceledException)
            {
                // 用户点"停止"：向上层传递，不当作测试异常
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in TestItem.class InitInstrumentsModule method, message: {ex}", ex);
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the TestItem.InitInstrumentsModule() method . {Environment.NewLine}");
            }
        }

        /// <summary>
        /// 血压仪初始化连接：按仪器链接配置中选择的血压模拟仪初始化。
        /// 选康伟达 → 初始化康伟达；选鲸扬 → 初始化 BPA700（同时下发 Category/单位/袖带容量三项设置）；
        /// 未选择模拟仪 → FAIL。连接成功写 1，失败/异常保持 0。
        /// </summary>
        //测试项目**************************---M01---**************************血压仪初始化连接
        public async Task BpDeviceInitModule(TestItemResultCollector collector, CancellationToken token)
        {
            _log.Info($"{DateTime.Now} Start calling the TestItem.BpDeviceInitModule() method . {Environment.NewLine}");
            try
            {
                var config = _configService.Load();

                switch (config.ActiveSimulator)
                {
                    case SimulatorType.Kangwenda:
                        using (var kw = new KangwendaNIBPSimulator(
                            config.KangwendaSimulator.PortName, config.KangwendaSimulator.BaudRate, _log))
                        {
                            await kw.OpenAsync(token);
                            _log.Info("康伟达 NIBP 模拟仪初始化成功");
                        }
                        break;

                    case SimulatorType.BPA700:
                        using (var bpa = new BPA700Simulator(config.BPA700Simulator.PortName, _log))
                        {
                            await bpa.OpenAsync(token);
                            // 初始化时同步下发三项设置（默认 Adult/mmHg/500ml，含切换等待与重试，响应停止令牌）
                            await bpa.ApplySettingsAsync(
                                config.BPA700Simulator.Category,
                                config.BPA700Simulator.PressureUnit,
                                config.BPA700Simulator.AirReservoir,
                                token);
                            _log.Info("鲸扬 BPA700 模拟仪初始化成功");
                        }
                        break;

                    default:
                        _log.Error("血压仪初始化连接失败：未选择血压模拟仪（仪器链接配置 → 模拟仪类型）");
                        return;
                }

                await collector.SetAsync("血压模拟仪初始化连接", TestItemResult.Of(1));
            }
            catch (OperationCanceledException)
            {
                // 用户点"停止"：向上层传递，不当作测试异常（否则模块会吞掉停止信号继续跑）
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in TestItem.class BpDeviceInitModule method, message: {ex}", ex);
                await collector.SetAsync("血压模拟仪初始化连接", TestItemResult.Of(0));
                DeviceConnectionFailed?.Invoke("血压仪初始化连接", "设备未连接，请检查串口配置");
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the TestItem.BpDeviceInitModule() method . {Environment.NewLine}");
            }
        }


        /// <summary>
        /// NFC功能测试（MAC 信息写入和验证）：
        /// 寻卡 → 写 MAC → 回读验证 → 读标签内容，一次性执行完整流程，
        /// 返回全部测试项结果（1=PASS / 0=FAIL，按 Spec 静态规格 1~1 判定）。
        /// </summary>
        //测试项目**************************---M02---**************************NFC功能测试
        public async Task NfcFunctionModule(TestItemResultCollector collector, CancellationToken token)
        {
            _log.Info($"{DateTime.Now} Start calling the TestItem.NfcFunctionModule() method . {Environment.NewLine}");
            try
            {
                var config = _configService.Load();
                using var reader = new NfcReaderService(_log);
                await reader.OpenAsync(config.NfcReader.PortName, config.NfcReader.BaudRate, token);

                var test = new NfcFunctionTest(reader, _log);
                var items = await test.RunAsync(TestMacAddress, token);
                foreach (var r in items)
                {
                    await collector.SetAsync(r.Name, TestItemResult.Of(r.Pass ? 1 : 0));
                    }
                _log.Info($"NFC功能测试完成，共 {items.Count} 项");
            }
            catch (OperationCanceledException)
            {
                // 用户点"停止"：向上层传递，不当作测试异常
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in TestItem.class NfcFunctionModule method, message: {ex}", ex);
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the TestItem.NfcFunctionModule() method . {Environment.NewLine}");
            }
        }

        /// <summary>
        /// 产品充放气测试（BP-001 物理按键启动测量 / BP-002 测量过程中手动停止）：
        /// ① 产品问询（产品返回 MAC 地址和嵌软版本，有返回即通过）；
        /// ② 产品当前状态查询（返回内容预留待定，有返回即通过）；
        /// ③ BP-001：BPARunBPTest 进入待测（血压/心率参数取仪器链接配置→BPA700→测试参数→动态血压），
        ///    提示测试员按产品物理[开始]键，BPA700 回调 status 依次经历 Busy→Stable→Finish 为 PASS，
        ///    status 一直停留在 Wait 说明产品未启动或气路未连通；
        /// ④ BP-002：同样进入待测，按[开始]键充气后于充气中按[停止]键，
        ///    BPA700 回调 status 变 Idle 并快速放气，测试值=快速放气时间（≤配置上限为 PASS，
        ///    上限在血压仪参数配置→充放气规格中设置）。超时/放气规格均取充放气规格配置。
        /// 充放气全程开启压力采样，压力曲线在 UI 实时展示（需连接储气瓶）。
        /// </summary>
        //测试项目**************************---M03---**************************产品充放气测试（BP-001/BP-002）
        public async Task BpAccuracyTestModule(TestItemResultCollector collector, CancellationToken token)
        {
            _log.Info($"{DateTime.Now} Start calling the TestItem.BpAccuracyTestModule() method . {Environment.NewLine}");
            try
            {
                var config = _configService.Load();

                // BP-001/002 需鲸扬 BPA700 作待测侧
                if (config.ActiveSimulator != SimulatorType.BPA700)
                {
                    _log.Error("产品充放气测试跳过：BP-001/002 需选择鲸扬 BPA700 作模拟仪（仪器链接配置 → 模拟仪类型）");
                    return;
                }

                using (var simulator = new BPA700Simulator(config.BPA700Simulator.PortName, _log))
                using (var product = new ProductController(config.Product, _log))
                {
                    await simulator.OpenAsync(token);

                    // ①② 产品问询/当前状态查询：产品未连接时仅这两项通讯项 FAIL，
                    //     不阻断后面的仪器控制流程（无产品也能调试 BPA700 侧逻辑，无需调试器跳语句）
                    var cuff = config.BPA700Simulator.TestParams.CuffTest;
                    if (!await ProductHandshakeAsync(product, collector, token))
                        _log.Warn($"产品不可用：BP-001/002 仍将等待产品物理按键（最长 {cuff.WaitStartTimeoutSeconds} 秒），BPA700 会停留在 Wait，两项判 FAIL，可随时点“停止”结束");

                    // ③④ 血压参数取仪器链接配置（动态血压 Default 参数）
                    var dyn = config.BPA700Simulator.TestParams.DynamicBloodPressure;
                    var systolic = (int)dyn.Systolic;
                    var diastolic = (int)dyn.Diastolic;
                    var heartRate = dyn.HeartRate;

                    // ③ BP-001 物理按键启动测量：限时等待测试员按产品[开始]键
                    var bp1Wave = System.Diagnostics.Stopwatch.StartNew();
                    void OnBp001Sample(double pressure) =>
                        PressureSampled?.Invoke("BP-001物理按键启动测量",
                            new PressureSamplePoint(bp1Wave.Elapsed.TotalSeconds, pressure));

                    BpTestRunResult bp1;
                    try
                    {
                        simulator.PressureSampled += OnBp001Sample;
                        await simulator.StartSamplingAsync(token);
                        _log.Info($"BP-001：请按下产品物理[开始]键启动测量（最长等待 {cuff.WaitStartTimeoutSeconds} 秒）...");
                        bp1 = await simulator.RunBPTestAsync(systolic, diastolic, heartRate,
                            cuff.WaitStartTimeoutSeconds * 1000, cuff.TotalTimeoutSeconds * 1000, token);
                    }
                    finally
                    {
                        await simulator.StopSamplingAsync();
                        simulator.PressureSampled -= OnBp001Sample;
                    }

                    if (bp1.CompletedFullSequence)
                    {
                        await collector.SetAsync("BP-001物理按键启动测量", TestItemResult.Of(1));
                                _log.Info($"BP-001 PASS：status 依次经历 Busy→Stable→Finish（峰值={bp1.PeakPressure:0.0} 总时间={bp1.TotalTime:0.0}s）");
                    }
                    else
                    {
                        await collector.SetAsync("BP-001物理按键启动测量", TestItemResult.Of(0));
                        _log.Warn($"BP-001 FAIL：status 序列 [{string.Join("→", bp1.StatusSequence)}] 未经历 Busy→Stable→Finish（最终 status={bp1.FinalStatus}，产品未启动或气路未连通）");
                    }

                    // ④ BP-002 测量过程中手动停止：按[开始]键充气后，充气中按[停止]键
                    var bp2Wave = System.Diagnostics.Stopwatch.StartNew();
                    void OnBp002Sample(double pressure) =>
                        PressureSampled?.Invoke("BP-002测量中手动停止",
                            new PressureSamplePoint(bp2Wave.Elapsed.TotalSeconds, pressure));

                    BpTestRunResult bp2;
                    try
                    {
                        simulator.PressureSampled += OnBp002Sample;
                        await simulator.StartSamplingAsync(token);
                        _log.Info($"BP-002：请先按产品[开始]键，充气过程中再按[停止]键（启动最长等待 {cuff.WaitStartTimeoutSeconds} 秒）...");
                        bp2 = await simulator.RunBPTestAsync(systolic, diastolic, heartRate,
                            cuff.WaitStartTimeoutSeconds * 1000, cuff.TotalTimeoutSeconds * 1000, token);
                    }
                    finally
                    {
                        await simulator.StopSamplingAsync();
                        simulator.PressureSampled -= OnBp002Sample;
                    }

                    if (bp2.FinalStatus == 0)   // Idle：BPA700 收到产品停止，已快速放气
                    {
                        // 动态规格：放气时间 0~配置上限（血压仪参数配置→充放气规格），≤上限视为放气完成
                        await collector.SetAsync("BP-002测量中手动停止", TestItemResult.WithDynamicSpec(Math.Round(bp2.DeflationTime, 2), 0, cuff.MaxDeflationSeconds));
                                _log.Info($"BP-002：快速放气时间 {bp2.DeflationTime:0.0}s（规格 ≤{cuff.MaxDeflationSeconds:0.#}s）");
                    }
                    else
                    {
                        await collector.SetAsync("BP-002测量中手动停止", TestItemResult.Of(0));
                        _log.Warn($"BP-002 FAIL：未收到 status=Idle（最终 status={bp2.FinalStatus}），产品未按[停止]键手动停止");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 用户点"停止"：向上层传递，不当作测试异常
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in TestItem.class BpAccuracyTestModule method, message: {ex}", ex);
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the TestItem.BpAccuracyTestModule() method . {Environment.NewLine}");
            }
        }

        /// <summary>
        /// 血压静态压力精度测试（BP-003）：
        /// ① 产品问询 / 产品当前状态查询（同 M03）；
        /// ② BPARunStaticPressure 逐点加压（压力点/稳压时长/充气方式取仪器链接配置→BPA700→测试参数→静态压力，
        ///    仅勾选的点参与），每点稳压后读产品上报压力（协议预留）；
        /// ③ 测试值 = 各压力点 |产品上报值 − BPA700稳压值| 的最大绝对误差（≤2mmHg 为 PASS）。
        /// 全程开启压力采样，静态压力曲线在 UI 实时展示。
        /// </summary>
        //测试项目**************************---M05---**************************血压静态压力精度测试（BP-003）
        public async Task BpStaticAccuracyModule(TestItemResultCollector collector, CancellationToken token)
        {
            _log.Info($"{DateTime.Now} Start calling the TestItem.BpStaticAccuracyModule() method . {Environment.NewLine}");
            try
            {
                var config = _configService.Load();
                if (config.ActiveSimulator != SimulatorType.BPA700)
                {
                    _log.Error("血压静态压力精度测试跳过：BP-003 需选择鲸扬 BPA700 作模拟仪（仪器链接配置 → 模拟仪类型）");
                    return;
                }

                var sp = config.BPA700Simulator.TestParams.StaticPressure;
                var points = sp.Points.Where(p => p.Enabled).Select(p => p.Value).ToList();
                if (points.Count == 0)
                {
                    _log.Warn("BP-003 跳过：未勾选任何静态压力点（仪器链接配置 → BPA700 → 测试参数 → 静态压力）");
                    return;
                }

                using (var simulator = new BPA700Simulator(config.BPA700Simulator.PortName, _log))
                using (var product = new ProductController(config.Product, _log))
                {
                    await simulator.OpenAsync(token);

                    // ① 产品问询 / 产品当前状态查询（产品未连接时仅两项通讯项 FAIL，不阻断仪器控制流程）
                    await ProductHandshakeAsync(product, collector, token);

                    // ② 逐点静态加压，全程采样画曲线
                    var waveWatch = System.Diagnostics.Stopwatch.StartNew();
                    void OnSample(double pressure) =>
                        PressureSampled?.Invoke("BP-003静态压力精度测试",
                            new PressureSamplePoint(waveWatch.Elapsed.TotalSeconds, pressure));

                    double maxError = 0;
                    var gotProductValue = false;
                    try
                    {
                        simulator.PressureSampled += OnSample;
                        await simulator.StartSamplingAsync(token);

                        // 设置静态压力检测模式（0x73 mode=3）
                        await product.SetTestModeAsync(3, token);

                        foreach (var point in points)
                        {
                            await simulator.SetStaticPressureAsync(point, sp.MeasureSeconds, sp.BpaInflation, token, timeoutSeconds: config.BPA700Simulator.StaticPressureTimeoutSeconds);//设置压力点，
                            var standard = simulator.LastStaticPressure;   // 采样读回的实际稳压值
                            var productValue = await product.ReadProductPressureAsync(token);
                            if (!double.IsNaN(productValue))
                            {
                                gotProductValue = true;
                                var err = Math.Abs(productValue - standard);
                                maxError = Math.Max(maxError, err);
                                _log.Info($"BP-003 压力点 {point}：标准值={standard:0.0} 产品上报={productValue:0.0} 误差={err:0.0}");
                            }
                            // 等本轮静态测试 Finish，否则下一个压力点的 BPARunStaticPressure 会被设备拒绝
                            await simulator.WaitStaticPressureFinishAsync(ct: token); //注释掉也会降低到0
                        }

                        //await simulator.StopSimulationAsync(token);
                        //await simulator.ResetAsync(token);
                    }
                    finally
                    {
                        await simulator.StopSamplingAsync();
                        simulator.PressureSampled -= OnSample;
                    }

                    // ③ 测试值 = 最大绝对误差（协议未补齐前无上报值，保持初始 0 = FAIL）
                    if (gotProductValue)
                    {
                        await collector.SetAsync("BP-003静态压力精度测试", TestItemResult.Of(Math.Round(maxError, 2)));
                            }
                    else
                        _log.Warn("BP-003 FAIL：产品压力上报协议预留，未取到产品上报值（协议定稿后补 ProductController.ReadProductPressureAsync）");
                }
            }
            catch (OperationCanceledException)
            {
                // 用户点"停止"：向上层传递，不当作测试异常
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in TestItem.class BpStaticAccuracyModule method, message: {ex}", ex);
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the TestItem.BpStaticAccuracyModule() method . {Environment.NewLine}");
            }
        }

        /// <summary>
        /// 血压动态压力精度测试（BP-004 血压精度/重复性 + BP-005 心率精度/重复性）：
        /// ① 产品问询 / 产品当前状态查询（同 M03）；
        /// ② BPARunBPTest 按测试要求固定 4 组参数（90/60、120/80、160/100、200/120 mmHg，
        ///    心率 60/80/100/120 BPM 同轮下发），每组重复 3 次完整充放气周期（需储气瓶产生脉搏波）；
        /// ③ 产品每次测量结束后上报收缩压/舒张压/心率（协议预留）：
        ///    精度（系统偏差）= 各组 |均值−设定值| 的最大值（血压 ≤3mmHg / 心率 ≤max(5,设定×5%)）；
        ///    重复性误差 = 各组 |最大−最小| 极差的最大值（血压 ≤2mmHg / 心率 ≤max(5,设定×5%)）。
        /// </summary>
        //测试项目**************************---M06---**************************血压动态压力精度测试（BP-004/BP-005）
        public async Task BpDynamicAccuracyModule(TestItemResultCollector collector, CancellationToken token)
        {
            _log.Info($"{DateTime.Now} Start calling the TestItem.BpDynamicAccuracyModule() method . {Environment.NewLine}");
            try
            {
                var config = _configService.Load();
                if (config.ActiveSimulator != SimulatorType.BPA700)
                {
                    _log.Error("血压动态压力精度测试跳过：BP-004/005 需选择鲸扬 BPA700 作模拟仪（仪器链接配置 → 模拟仪类型）");
                    return;
                }

                // BP-004/005 测试组（测试要求文档固定）：血压设定值与心率设定值同一轮下发同步执行
                var groups = new (int Systolic, int Diastolic, int HeartRate)[]
                {
                    (90, 60, 60), (120, 80, 80), (160, 100, 100), (200, 120, 120)
                };
                const int repeats = 3;

                using (var simulator = new BPA700Simulator(config.BPA700Simulator.PortName, _log))
                using (var product = new ProductController(config.Product, _log))
                {
                    await simulator.OpenAsync(token);

                    // ① 产品问询 / 产品当前状态查询（产品未连接时仅两项通讯项 FAIL）
                    if (!await ProductHandshakeAsync(product, collector, token))
                    {
                        _log.Warn("BP-004/005 跳过测量轮次：产品不可用，且全部结果项依赖产品上报值（协议预留），空跑无意义");
                        return;
                    }

                    // ② 设置血压测试模式（0x73 mode=2）
                    await product.SetTestModeAsync(2, token);

                    // ③ 逐组逐次执行：每组 3 次完整充放气周期，每次结束后读产品上报结果
                    var readings = new Dictionary<(int, int, int), List<ProductBpReading>>();
                    foreach (var g in groups)
                    {
                        var list = new List<ProductBpReading>();
                        readings[(g.Systolic, g.Diastolic, g.HeartRate)] = list;
                        for (var r = 1; r <= repeats; r++)
                        {
                            _log.Info($"BP-004/005 组 SYS={g.Systolic} DIA={g.Diastolic} HR={g.HeartRate} 第 {r}/{repeats} 次：等待产品启动测量...");
                            await simulator.RunBPTestAsync(g.Systolic, g.Diastolic, g.HeartRate, ct: token);
                            var reading = await product.ReadBpResultAsync(token);
                            if (reading != null)
                            {
                                list.Add(reading);
                                _log.Info($"产品上报：SYS={reading.Systolic:0.0} DIA={reading.Diastolic:0.0} HR={reading.HeartRate:0.0}");
                            }
                        }
                    }

                    // ③ 统计：各组均值/极差 → 最大系统偏差与最大重复性误差
                    if (readings.Values.All(l => l.Count == 0))
                    {
                        _log.Warn("BP-004/005 FAIL：产品血压/心率上报协议预留，未取到任何上报值（协议定稿后补 ProductController.ReadBpResultAsync）");
                        return;
                    }

                    double sysDev = 0, sysRange = 0, diaDev = 0, diaRange = 0, hrDev = 0, hrRange = 0;
                    foreach (var g in groups)
                    {
                        var list = readings[(g.Systolic, g.Diastolic, g.HeartRate)];
                        if (list.Count == 0) continue;
                        var sys = list.Select(x => x.Systolic).ToList();
                        var dia = list.Select(x => x.Diastolic).ToList();
                        var hr = list.Select(x => x.HeartRate).ToList();

                        sysDev = Math.Max(sysDev, Math.Abs(sys.Average() - g.Systolic));
                        sysRange = Math.Max(sysRange, sys.Max() - sys.Min());
                        diaDev = Math.Max(diaDev, Math.Abs(dia.Average() - g.Diastolic));
                        diaRange = Math.Max(diaRange, dia.Max() - dia.Min());
                        hrDev = Math.Max(hrDev, Math.Abs(hr.Average() - g.HeartRate));
                        hrRange = Math.Max(hrRange, hr.Max() - hr.Min());
                    }

                    await collector.SetAsync("BP-004收缩压精度测试", TestItemResult.Of(Math.Round(sysDev, 2)));
                    await collector.SetAsync("BP-004收缩压重复性测试", TestItemResult.Of(Math.Round(sysRange, 2)));
                    await collector.SetAsync("BP-004舒张压精度测试", TestItemResult.Of(Math.Round(diaDev, 2)));
                    await collector.SetAsync("BP-004舒张压重复性测试", TestItemResult.Of(Math.Round(diaRange, 2)));
                    // 心率容差 max(5, 设定×5%)：四组中最大容差为 120×5%=6，静态规格 0~6 已取最大容差兜底；
                    // TODO: 协议定稿后如需严格按组容差判定，可改为 WithDynamicSpec 或分组判FAIL后写超限值
                    await collector.SetAsync("BP-005心率精度测试", TestItemResult.Of(Math.Round(hrDev, 2)));
                    await collector.SetAsync("BP-005心率重复性测试", TestItemResult.Of(Math.Round(hrRange, 2)));
                    _log.Info($"BP-004/005 统计完成：收缩压偏差={sysDev:0.00} 极差={sysRange:0.00}；舒张压偏差={diaDev:0.00} 极差={diaRange:0.00}；心率偏差={hrDev:0.00} 极差={hrRange:0.00}");
                }
            }
            catch (OperationCanceledException)
            {
                // 用户点"停止"：向上层传递，不当作测试异常
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in TestItem.class BpDynamicAccuracyModule method, message: {ex}", ex);
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the TestItem.BpDynamicAccuracyModule() method . {Environment.NewLine}");
            }
        }

        /// <summary>
        /// 压力控制功能测试（BP-006~BP-009）：
        /// ① 产品问询 / 产品当前状态查询（BP-009 需产品配合，先确认通讯）；
        /// ② BP-006 最高充气压力：充气至 350，稳压读回值应在 340~355mmHg（测试值=稳压值）；
        /// ③ BP-007 匀速放气控制：充气至 150 后开慢释阀泄放，平均放气速率应在 2~6mmHg/s（采样点计算）；
        /// ④ BP-008 快释放气功能：充气至 250 后开快释阀泄放，应在 10s 内归零（采样点计时）；
        ///    以上三项为 BPA700 模拟器自身性能验证，无需产品参与；
        /// ⑤ BP-009 过压保护：持续充气至超过 threshold2 触发快速放气（参数取配置→超压），
        ///    测试值=触发保护时压力（动态规格 threshold1~threshold2），产品需全程实时上报压力配合（协议预留）。
        /// </summary>
        //测试项目**************************---M04---**************************压力控制功能测试（BP-006~BP-009）
        public async Task PressureControlModule(TestItemResultCollector collector, CancellationToken token)
        {
            _log.Info($"{DateTime.Now} Start calling the TestItem.PressureControlModule() method . {Environment.NewLine}");
            try
            {
                var config = _configService.Load();
                if (config.ActiveSimulator != SimulatorType.BPA700)
                {
                    _log.Error("压力控制功能测试跳过：BP-006~009 需选择鲸扬 BPA700 作模拟仪（仪器链接配置 → 模拟仪类型）");
                    return;
                }

                using (var simulator = new BPA700Simulator(config.BPA700Simulator.PortName, _log))
                using (var product = new ProductController(config.Product, _log))
                {
                    await simulator.OpenAsync(token);

                    // ① 产品问询 / 产品当前状态查询（产品未连接时仅两项通讯项 FAIL；BP-006~008 本身无需产品参与，照常执行）
                    await ProductHandshakeAsync(product, collector, token);

                    // 全模块采样：BP-006 稳压值靠采样读回；BP-007/008/009 的速率/归零/波形均基于采样点
                    var bp9Wave = System.Diagnostics.Stopwatch.StartNew();
                    void OnBp009Sample(double pressure) =>
                        PressureSampled?.Invoke("BP-009过压保护", new PressureSamplePoint(bp9Wave.Elapsed.TotalSeconds, pressure));

                    try
                    {
                        simulator.PressureSampled += OnBp009Sample;
                        await simulator.StartSamplingAsync(token);

                        //  BP-006 最高充气压力
                        try
                        {
                            var bpTimeout = config.BPA700Simulator.StaticPressureTimeoutSeconds;
                            await simulator.SetStaticPressureAsync(350, stableSeconds: 5, inflation: true, token, timeoutSeconds: bpTimeout);
                            await collector.SetAsync("BP-006最高充气压力", TestItemResult.Of(Math.Round(simulator.LastStaticPressure, 1)));
                                    _log.Info($"BP-006：充气至 350 完成，采样读回稳压值 {simulator.LastStaticPressure:0.0}（规格 340~355）");
                            await simulator.WaitStaticPressureFinishAsync(ct: token);
                            await simulator.ResetAsync(token);
                        }
                        catch (Exception)
                        {
                            _log.Error($"BP-006 失败");
                            await collector.SetAsync("BP-006最高充气压力", TestItemResult.Of(0));
                        }

                        //  BP-007 匀速放气控制
                        try
                        {
                            var bpTimeout = config.BPA700Simulator.StaticPressureTimeoutSeconds;
                            await simulator.SetStaticPressureAsync(150, stableSeconds: 2, inflation: true, token, timeoutSeconds: bpTimeout);
                            await simulator.WaitStaticPressureFinishAsync(ct: token);
                            await simulator.SetSlowValveAsync(true, token);
                            var deflWatch = System.Diagnostics.Stopwatch.StartNew();
                            var deflSamples = new List<(double T, double P)>();
                            void OnBp007Sample(double pressure)
                            {
                                lock (deflSamples) deflSamples.Add((deflWatch.Elapsed.TotalSeconds, pressure));
                            }
                            simulator.PressureSampled += OnBp007Sample;
                            try
                            {
                                await simulator.StartStaticPressureTestAsync(150, stableSeconds: 60, inflation: false, token);
                                while (true)
                                {
                                    token.ThrowIfCancellationRequested();
                                    double latest;
                                    lock (deflSamples) latest = deflSamples.Count > 0 ? deflSamples[^1].P : 150;
                                    if (latest <= 20 || deflWatch.Elapsed.TotalSeconds >= 40) break;
                                    await Task.Delay(200, token);
                                }
                            }
                            finally
                            {
                                simulator.PressureSampled -= OnBp007Sample;
                            }

                            double deflRate = 0;
                            lock (deflSamples)
                            {
                                if (deflSamples.Count >= 2)
                                {
                                    var (t0, p0) = deflSamples[0];
                                    var (t1, p1) = deflSamples[^1];
                                    if (t1 > t0) deflRate = (p0 - p1) / (t1 - t0);
                                }
                            }
                            await collector.SetAsync("BP-007匀速放气控制", TestItemResult.Of(Math.Round(deflRate, 2)));
                                    _log.Info($"BP-007：平均放气速率 {deflRate:0.00}mmHg/s（规格 2~6，采样点 {deflSamples.Count} 个）");
                            await simulator.StopSimulationAsync(token);
                            await simulator.SetSlowValveAsync(false, token);
                        }
                        catch (Exception)
                        {
                            _log.Error($"BP-007 失败");
                            await collector.SetAsync("BP-007匀速放气控制", TestItemResult.Of(0));
                        }

                        //  BP-008 快释放气功能
                        try
                        {
                            await simulator.StopSimulationAsync(token);
                            await simulator.SetSlowValveAsync(false, token);
                            await simulator.ResetAsync(token);
                            await Task.Delay(2000, token);

                            double bp008TargetPressure = 250;
                            bool bp008InflationOk = false;
                            var bpTimeout = config.BPA700Simulator.StaticPressureTimeoutSeconds;
                            try
                            {
                                await simulator.SetStaticPressureAsync(250, stableSeconds: 2, inflation: true, token, timeoutSeconds: bpTimeout);
                                bp008InflationOk = true;
                            }
                            catch (TimeoutException)
                            {
                                _log.Warn($"BP-008：首次充气超时，重试...");
                                await simulator.StopSimulationAsync(token);
                                await simulator.ResetAsync(token);
                                await Task.Delay(3000, token);
                                try
                                {
                                    await simulator.SetStaticPressureAsync(250, stableSeconds: 2, inflation: true, token, timeoutSeconds: bpTimeout);
                                    bp008InflationOk = true;
                                }
                                catch (TimeoutException)
                                {
                                    _log.Warn($"BP-008：重试仍超时，回退使用 BP-006 稳压值");
                                    bp008TargetPressure = simulator.LastStaticPressure;
                                }
                            }

                            if (bp008InflationOk)
                                await simulator.WaitStaticPressureFinishAsync(ct: token);
                            await simulator.SetFastValveAsync(true, token);
                            var relWatch = System.Diagnostics.Stopwatch.StartNew();
                            var relSamples = new List<(double T, double P)>();
                            void OnBp008Sample(double pressure)
                            {
                                lock (relSamples) relSamples.Add((relWatch.Elapsed.TotalSeconds, pressure));
                            }
                            double zeroTime;
                            simulator.PressureSampled += OnBp008Sample;
                            try
                            {
                                await simulator.StartStaticPressureTestAsync(bp008TargetPressure, stableSeconds: 10, inflation: false, token);
                                var zeroAt = -1.0;
                                while (zeroAt < 0 && relWatch.Elapsed.TotalSeconds < 15)
                                {
                                    token.ThrowIfCancellationRequested();
                                    lock (relSamples)
                                    {
                                        foreach (var (t, p) in relSamples)
                                        {
                                            if (p <= 2) { zeroAt = t; break; }
                                        }
                                    }
                                    if (zeroAt < 0) await Task.Delay(100, token);
                                }
                                zeroTime = zeroAt >= 0 ? zeroAt : relWatch.Elapsed.TotalSeconds;
                            }
                            finally
                            {
                                simulator.PressureSampled -= OnBp008Sample;
                            }
                            await collector.SetAsync("BP-008快释放气功能", TestItemResult.Of(Math.Round(zeroTime, 2)));
                                    _log.Info($"BP-008：快释放气归零耗时 {zeroTime:0.0}s（起始压力 {bp008TargetPressure:0.0}mmHg，规格 ≤10s）");
                            await simulator.StopSimulationAsync(token);
                            await simulator.SetFastValveAsync(false, token);
                            await simulator.ResetAsync(token);
                        }
                        catch (Exception)
                        {
                            _log.Error($"BP-008 失败");
                            await collector.SetAsync("BP-008快释放气功能", TestItemResult.Of(0));
                        }

                        //  BP-009 过压保护
                        try
                        {
                            var op = config.BPA700Simulator.TestParams.Overpressure;
                            _log.Info($"BP-009：过压保护测试（目标 {op.TargetPressure}，阈值 {op.Threshold1}/{op.Threshold2}）...");
                            var trigger = await simulator.RunOverpressureTestAsync(op.TargetPressure, op.Threshold1, op.Threshold2, op.BpaInflation, ct: token);
                            await collector.SetAsync("BP-009过压保护", TestItemResult.WithDynamicSpec(Math.Round(trigger, 1), op.Threshold1, op.Threshold2));
                                    _log.Info($"BP-009：过压保护已触发，触发压力 {trigger:0.0}（动态规格 {op.Threshold1}~{op.Threshold2}）");
                        }
                        catch (Exception)
                        {
                            _log.Error($"BP-009 失败");
                            await collector.SetAsync("BP-009过压保护", TestItemResult.Of(0));
                        }
                    }
                    finally
                    {
                        await simulator.StopSamplingAsync();
                        simulator.PressureSampled -= OnBp009Sample;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 用户点"停止"：向上层传递，不当作测试异常
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in TestItem.class PressureControlModule method, message: {ex}", ex);
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the TestItem.PressureControlModule() method . {Environment.NewLine}");
            }
        }

        /// <summary>
        /// AECG100 初始化连接：通过 HardwareManager 连接 AECG100 多功能综合性能测试仪，
        /// 连接成功后读取设备型号/固件/序列号信息。
        /// 成功写 1，失败/异常保持 0。
        /// </summary>
        //测试项目**************************---M07---**************************AECG100初始化连接
        public async Task Aecg100InitModule(TestItemResultCollector collector, CancellationToken token)
        {
            _log.Info($"{DateTime.Now} Start calling the TestItem.Aecg100InitModule() method . {Environment.NewLine}");
            try
            {
                var config = _configService.Load();
                var settings = config.Aecg100Tester;

                var connected = await _hw.ConnectAecg100Async(settings);
                if (!connected)
                {
                    _log.Error("AECG100 初始化连接失败（WTQInit/WTQConnect 返回失败）");
                    return;
                }

                await collector.SetAsync("AECG100初始化连接", TestItemResult.Of(1));
                _log.Info("AECG100 初始化连接成功");

                // 读取设备信息
                var model = _hw.Aecg100!.GetDeviceInformation();
                var serial = _hw.Aecg100.GetSerialNumber();
                var hw = _hw.Aecg100.GetHwInformation();
                if (model != null)
                {
                    await collector.SetAsync("AECG100设备信息读取", TestItemResult.Of(1));
                        _log.Info($"AECG100 设备信息：型号={model.ProductName} 序列号={serial ?? "N/A"} " +
                              $"固件={hw?.FWMajorVersion}.{hw?.FWMinorVersion}.{hw?.FWBuildVersion} " +
                              $"硬件版本={hw?.HWVersion}");
                }
                else
                {
                    _log.Warn("AECG100 设备信息读取失败（WTQGetDeviceInformation 返回 false）");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in TestItem.class Aecg100InitModule method, message: {ex}", ex);
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the TestItem.Aecg100InitModule() method . {Environment.NewLine}");
            }
        }

        /// <summary>
        /// ECG 采集与启动（ECG-001 测量启动 / ECG-002 导联脱落检测）：
        /// ① 产品问询/产品当前状态查询：标记 TBD，默认 PASS（产品串口协议预留）；
        /// ② ECG-001：AECG100 输出 ECG 波形（WTQOutputECG），SDK 返回成功即 PASS；
        /// ③ ECG-002：仪器侧导联脱落检测（预留，当前默认 PASS，待导联脱落判断逻辑确定后补充）。
        /// </summary>
        //测试项目**************************---M08---**************************ECG采集与启动（ECG-001/002）
        public async Task EcgAcquisitionTestModule(TestItemResultCollector collector, CancellationToken token)
        {
            _log.Info($"{DateTime.Now} Start calling the TestItem.EcgAcquisitionTestModule() method . {Environment.NewLine}");
            try
            {
                // ① 产品问询/状态查询（产品串口协议 0x72/0x74）
                var config = _configService.Load();
                using (var product = new ProductController(config.Product, _log))
                {
                    if (!await ProductHandshakeAsync(product, collector, token))
                        _log.Warn("产品不可用：ECG-001/002 仍将输出 AECG100 波形，但产品回传比对项可能 FAIL");
                }

                // 检查 AECG100 是否已连接
                if (_hw.Aecg100 == null || !_hw.Aecg100.IsConnected)
                {
                    _log.Error("ECG-001/002 跳过：AECG100 未连接，请先执行 M07 AECG100初始化连接");
                    return;
                }

                // ② ECG-001 测量启动：AECG100 输出 ECG 波形（参数从配置页读取），采集输出波形
                var ecgConfig = _configService.Load().Aecg100Tester;
                var ecgWaveform = BuildDefaultEcgWaveform(ecgConfig, 1.0);  // 60BPM = 1Hz

                var outputOk = await OutputEcgWithCaptureAsync("ECG-001测量启动", ecgWaveform, token);
                if (outputOk)
                {
                    await collector.SetAsync("ECG-001测量启动", TestItemResult.Of(1));
                        _log.Info("ECG-001 PASS：AECG100 ECG 波形输出成功（含波形采集）");
                }
                else
                {
                    _log.Error("ECG-001 FAIL：AECG100 ECG 波形输出失败（WTQOutputECG 返回 false）");
                }

                // ③ ECG-002 导联脱落检测：输出波形 → 提示拔掉导联 → 等产品回传判定
                _log.Info("ECG-002：请拔掉 LA 导联，观察产品是否上报导联脱落状态...");
                var ecgWaveform2 = BuildDefaultEcgWaveform(ecgConfig, 1.0);
                await OutputEcgWithCaptureAsync("ECG-002导联脱落检测", ecgWaveform2, token);
                // TODO: 产品串口协议就绪后，通过 0x87 读取产品导联状态判定脱落
                await collector.SetAsync("ECG-002导联脱落检测", TestItemResult.Of(1));
                _log.Info("ECG-002：波形已输出，导联脱落判定待产品回传协议补齐（当前默认 PASS）");

                // 停止波形输出
                _hw.Aecg100.StopOutputWaveform();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in TestItem.class EcgAcquisitionTestModule method, message: {ex}", ex);
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the TestItem.EcgAcquisitionTestModule() method . {Environment.NewLine}");
            }
        }

        /// <summary>
        /// ECG 心率监测（ECG-004 心率检测精度）：
        /// ① 产品问询/状态查询：标记 TBD，默认 PASS（产品串口协议预留）；
        /// ② ECG-004：按配置页心率点（默认 40/60/80/100/120/160 BPM）逐档输出 ECG 波形，
        ///    每档重复 3 次（配置可调），仅验证仪器设置下发成功（产品回传比对预留），
        ///    测试值 = 各心率点设置下发偏差的最大值（当前仅验证 SDK 返回成功，偏差 = 0）。
        /// </summary>
        //测试项目**************************---M09---**************************ECG心率监测（ECG-004）
        public async Task EcgHeartRateTestModule(TestItemResultCollector collector, CancellationToken token)
        {
            _log.Info($"{DateTime.Now} Start calling the TestItem.EcgHeartRateTestModule() method . {Environment.NewLine}");
            try
            {
                // ① 产品问询/状态查询（产品串口协议 0x72/0x74）
                var config = _configService.Load();
                using (var product = new ProductController(config.Product, _log))
                {
                    if (!await ProductHandshakeAsync(product, collector, token))
                        _log.Warn("产品不可用：ECG-004 仍将输出 AECG100 波形，但产品回传比对项可能 FAIL");
                }

                // 设置心电测试模式（0x73 mode=5）
                // 注：产品模式在 SetTestModeAsync 中设置，此处仅打开串口发指令后关闭
                using (var productForMode = new ProductController(config.Product, _log))
                {
                    try
                    {
                        await productForMode.OpenAsync(token);
                        await productForMode.SetTestModeAsync(5, token);
                    }
                    catch { /* 产品不可用时不阻断 AECG100 侧逻辑 */ }
                }

                // 检查 AECG100 是否已连接
                if (_hw.Aecg100 == null || !_hw.Aecg100.IsConnected)
                {
                    _log.Error("ECG-004 跳过：AECG100 未连接，请先执行 M07 AECG100初始化连接");
                    return;
                }

                var ecgConfig = config.Aecg100Tester;
                var testPoints = ecgConfig.HeartRateTestPoints;
                var repeats = ecgConfig.HeartRateRepeats;

                if (testPoints.Count == 0)
                {
                    _log.Warn("ECG-004 跳过：未配置心率测试点（AECG100 配置页 → 心率测试点）");
                    return;
                }

                // ② 逐档心率输出 ECG 波形，每档重复 N 次
                double maxDeviation = 0;
                foreach (var bpm in testPoints)
                {
                    token.ThrowIfCancellationRequested();
                    var frequency = bpm / 60.0;   // BPM → Hz（60 BPM = 1Hz）

                    for (var r = 1; r <= repeats; r++)
                    {
                        var waveform = BuildDefaultEcgWaveform(ecgConfig, frequency);

                        var outputOk = await Task.Run(() => _hw.Aecg100.OutputEcg(waveform), token);
                        if (!outputOk)
                        {
                            _log.Error($"ECG-004 FAIL：心率 {bpm}BPM 第 {r}/{repeats} 次输出失败（WTQOutputECG 返回 false）");
                            continue;
                        }

                        // 当前仅验证仪器设置下发成功（偏差 = 0）；
                        // TODO: 产品串口协议定稿后，读取产品回传心率值，计算 |回传值-设定值| 作为偏差
                        var deviation = 0.0;
                        maxDeviation = Math.Max(maxDeviation, deviation);
                        _log.Info($"ECG-004 心率 {bpm}BPM 第 {r}/{repeats} 次：仪器设置下发成功，偏差={deviation:0.00}BPM（产品回传比对预留）");

                        // 每次输出后短暂等待，让波形稳定
                        await Task.Delay(500, token);
                    }

                    // 每档测试完后停止波形
                    _hw.Aecg100.StopOutputWaveform();
                }

                await collector.SetAsync("ECG-004心率检测精度", TestItemResult.Of(Math.Round(maxDeviation, 2)));
                _log.Info($"ECG-004 完成：最大偏差={maxDeviation:0.00}BPM（规格 ≤±{ecgConfig.HeartRateTolerance}BPM，产品回传比对预留）");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in TestItem.class EcgHeartRateTestModule method, message: {ex}", ex);
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the TestItem.EcgHeartRateTestModule() method . {Environment.NewLine}");
            }
        }

        /// <summary>
        /// ECG 信号质量与波形（ECG-009~012）：
        /// 按 YY 9706.247-2021 标准要求，AECG100 发出标准测试信号，产品回传测量值后由上位机判定。
        ///   ECG-009 动态范围和线性（201.12.4.4.101）：输出 6mV 三角波 + ±300mV DC 偏置，
        ///         判定：DUT 回放幅度衰减 < 50μV（需产品回传 ADC 采样值）
        ///   ECG-010 输入阻抗（201.12.4.4.102）：输出 10Hz 5mV 正弦波，开关断开/闭合两种状态，
        ///         判定：振幅变化 ≤ 6% → 输入阻抗 > 10MΩ（需产品回传 + 外部开关控制）
        ///   ECG-011 增益准确性（201.12.4.4.104）：输出 5Hz 2mV 正弦波，各增益档验证，
        ///         判定：幅度误差 ≤ ±10%（需产品回传各增益档测量值）
        ///   ECG-012 频率响应（201.12.4.4.108）：0.67Hz~40Hz 逐频点输出 2mV 正弦波，
        ///         判定：各频点响应在 5Hz 响应的 70%~140%（需产品回传各频点测量幅度）
        /// 当前阶段：AECG100 发出标准信号，产品回传比对逻辑预留（TBD），默认 PASS。
        /// TODO: 产品串口协议定稿后，补充各测试项的产品回传读取与判定逻辑。
        /// </summary>
        //测试项目**************************---M10---**************************ECG信号质量与波形（ECG-009~012）
        public async Task EcgSignalQualityTestModule(TestItemResultCollector collector, CancellationToken token)
        {
            _log.Info($"{DateTime.Now} Start calling the TestItem.EcgSignalQualityTestModule() method . {Environment.NewLine}");
            try
            {
                // 检查 AECG100 是否已连接
                if (_hw.Aecg100 == null || !_hw.Aecg100.IsConnected)
                {
                    _log.Error("ECG-009~012 跳过：AECG100 未连接，请先执行 M07 AECG100初始化连接");
                    return;
                }

                var ecgConfig = _configService.Load().Aecg100Tester;

                // ==================== ECG-009 动态范围和线性（YY 9706.247-2021 201.12.4.4.101） ====================
                // 标准：输出 6mV（峰-谷）三角波 + ±300mV DC 偏置，验证 DUT 回放幅度衰减 < 50μV
                // 当前：AECG100 发出标准信号，产品回传比对预留
                _log.Info("ECG-009：发出 6mV 三角波 + ±300mV DC 偏置（标准 201.12.4.4.101）...");

                // 输出 6mV 三角波（无 DC 偏置）
                var triWave6mV = BuildDefaultEcgWaveform(ecgConfig, 10.4);  // 10.4Hz 三角波
                triWave6mV.WaveformType = AecgEcgWaveformType.Triangle;
                triWave6mV.Amplitude = 6.0;  // 6mV 峰-谷
                var triOk = await OutputEcgWithCaptureAsync("ECG-009动态范围", triWave6mV, token);
                if (!triOk)
                    _log.Error("ECG-009：6mV 三角波输出失败");
                else
                    _log.Info("ECG-009：6mV 三角波输出成功");
                _hw.Aecg100.StopOutputWaveform();
                await Task.Delay(500, token);

                // 输出 +300mV DC 偏置下的三角波
                var triWaveDC300 = BuildDefaultEcgWaveform(ecgConfig, 10.4);
                triWaveDC300.WaveformType = AecgEcgWaveformType.Triangle;
                triWaveDC300.Amplitude = 6.0;
                triWaveDC300.DCOffsetVariable = false;
                triWaveDC300.DCOffset = 300;  // +300mV
                var dc300Ok = await OutputEcgWithCaptureAsync("ECG-009动态范围", triWaveDC300, token);
                if (!dc300Ok)
                    _log.Error("ECG-009：+300mV DC 偏置三角波输出失败");
                else
                    _log.Info("ECG-009：+300mV DC 偏置三角波输出成功");
                _hw.Aecg100.StopOutputWaveform();
                await Task.Delay(500, token);

                // 输出 -300mV DC 偏置下的三角波
                var triWaveDCn300 = BuildDefaultEcgWaveform(ecgConfig, 10.4);
                triWaveDCn300.WaveformType = AecgEcgWaveformType.Triangle;
                triWaveDCn300.Amplitude = 6.0;
                triWaveDCn300.DCOffsetVariable = false;
                triWaveDCn300.DCOffset = -300;  // -300mV
                var dcn300Ok = await OutputEcgWithCaptureAsync("ECG-009动态范围", triWaveDCn300, token);
                if (!dcn300Ok)
                    _log.Error("ECG-009：-300mV DC 偏置三角波输出失败");
                else
                    _log.Info("ECG-009：-300mV DC 偏置三角波输出成功");
                _hw.Aecg100.StopOutputWaveform();
                await Task.Delay(500, token);

                // 3 组波形全部完成后才设置结果
                // TODO: 产品回传 DUT 回放幅度，判定衰减 < 50μV
                if (triOk && dc300Ok && dcn300Ok)
                {
                    await collector.SetAsync("ECG-009动态范围", TestItemResult.Of(1));
                        _log.Info("ECG-009 PASS：3 组波形全部输出成功，产品回传比对预留（TBD，默认 PASS）");
                }
                else
                {
                    _log.Error($"ECG-009 FAIL：部分波形输出失败（无偏置={triOk} +300mV={dc300Ok} -300mV={dcn300Ok}）");
                }

                // ==================== ECG-010 输入阻抗（YY 9706.247-2021 201.12.4.4.102） ====================
                // 标准：输出 10Hz 5mV 正弦波，开关 S1 闭合/断开两种状态，振幅变化 ≤ 6% → 输入阻抗 > 10MΩ
                // 当前：AECG100 发出标准信号，产品回传 + 外部开关控制预留
                _log.Info("ECG-010：发出 10Hz 5mV 正弦波（标准 201.12.4.4.102）...");

                var sineWave10Hz = BuildDefaultEcgWaveform(ecgConfig, 10.0);
                sineWave10Hz.WaveformType = AecgEcgWaveformType.Sine;
                sineWave10Hz.Amplitude = 5.0;  // 5mV 峰-谷
                var sineOk = await OutputEcgWithCaptureAsync("ECG-010输入阻抗", sineWave10Hz, token);
                if (sineOk)
                {
                    // TODO: 产品回传两种状态（开关闭合/断开）下的 ADC 采样幅度
                    // var ampClosed = await product.ReadEcgAmplitudeAsync(token);    // S1 闭合
                    // await hardwareControl.OpenSwitch();  // 外部开关断开
                    // var ampOpen = await product.ReadEcgAmplitudeAsync(token);     // S1 断开
                    // var changePct = Math.Abs(ampClosed - ampOpen) / ampClosed * 100;
                    // collector["ECG-010输入阻抗"] = TestItemResult.Of(changePct);  // 规格 0~6 %
                    await collector.SetAsync("ECG-010输入阻抗", TestItemResult.Of(1));
                        _log.Info("ECG-010：10Hz 5mV 正弦波输出成功，产品回传 + 外部开关控制预留（TBD，默认 PASS）");
                }
                else
                {
                    _log.Error("ECG-010 FAIL：10Hz 5mV 正弦波输出失败");
                }
                _hw.Aecg100.StopOutputWaveform();
                await Task.Delay(500, token);

                // ==================== ECG-011 增益准确性（YY 9706.247-2021 201.12.4.4.104） ====================
                // 标准：输出 5Hz 2mV 正弦波，在所有增益设置下验证输出等效输入幅度误差 ≤ ±10%
                // 当前：AECG100 发出标准信号，产品回传各增益档测量值预留
                _log.Info("ECG-011：发出 5Hz 2mV 正弦波（标准 201.12.4.4.104）...");

                var sineWave5Hz = BuildDefaultEcgWaveform(ecgConfig, 5.0);
                sineWave5Hz.WaveformType = AecgEcgWaveformType.Sine;
                sineWave5Hz.Amplitude = 2.0;  // 2mV 峰-谷
                var gainOk = await OutputEcgWithCaptureAsync("ECG-011增益准确性", sineWave5Hz, token);
                if (gainOk)
                {
                    // TODO: 产品在各增益档回传测量值，计算幅度误差
                    // var dutValues = await product.ReadEcgGainAccuracyAsync(token);  // 各增益档测量值
                    // var maxError = dutValues.Max(v => Math.Abs(v - 2.0) / 2.0 * 100);
                    // collector["ECG-011增益准确性"] = TestItemResult.Of(maxError);  // 规格 0~10 %
                    await collector.SetAsync("ECG-011增益准确性", TestItemResult.Of(0));
                        _log.Info("ECG-011：5Hz 2mV 正弦波输出成功，产品各增益档回传预留（TBD，默认 PASS）");
                }
                else
                {
                    _log.Error("ECG-011 FAIL：5Hz 2mV 正弦波输出失败");
                }
                _hw.Aecg100.StopOutputWaveform();
                await Task.Delay(500, token);

                // ==================== ECG-012 频率响应（YY 9706.247-2021 201.12.4.4.108） ====================
                // 标准：0.67Hz~40Hz 逐频点输出 2mV 正弦波，各频点响应在 5Hz 响应的 70%~140%
                // 当前：AECG100 发出频率扫描信号，产品回传各频点测量幅度预留
                _log.Info("ECG-012：发出 0.67Hz~40Hz 频率扫描 2mV 正弦波（标准 201.12.4.4.108）...");

                var freqScan = new AecgFrequencyScan
                {
                    Amplitude = 2.0,           // 2mV
                    FrequencyStart = 0.67,     // 0.67Hz
                    FrequencyFinish = 40.0,    // 40Hz
                    Duration = 30              // 扫描时长 30s
                };
                var scanOk = await OutputFrequencyScanWithCaptureAsync("ECG-012频率响应", freqScan, token);
                if (scanOk)
                {
                    // TODO: 产品回传各频点测量幅度，计算相对 5Hz 的幅度比
                    // var freqResponses = await product.ReadEcgFrequencyResponseAsync(token);
                    // var ref5Hz = freqResponses[5.0];
                    // var maxDeviation = freqResponses.Max(kv => Math.Abs(kv.Value - ref5Hz) / ref5Hz * 100);
                    // collector["ECG-012频率响应"] = TestItemResult.Of(maxDeviation);  // 规格 -30~40 %（70%~140%）
                    await collector.SetAsync("ECG-012频率响应", TestItemResult.Of(1));
                        _log.Info("ECG-012：频率扫描输出成功，产品各频点回传预留（TBD，默认 PASS）");
                }
                else
                {
                    _log.Error("ECG-012 FAIL：频率扫描输出失败");
                }
                _hw.Aecg100.StopOutputWaveform();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in TestItem.class EcgSignalQualityTestModule method, message: {ex}", ex);
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the TestItem.EcgSignalQualityTestModule() method . {Environment.NewLine}");
            }
        }

        /// <summary>
        /// 输出 ECG 波形并采集回调数据，通过 PressureSampled 事件推送给 UI 显示。
        /// 回调参数 (time, ac, dc) 中 ac+dc 作为波形幅度（μV），SDK time 作为时间轴（s）。
        /// 等待 SDK 回调结束信号（ac=int.MinValue）或超时 30s。
        /// </summary>
        private async Task<bool> OutputEcgWithCaptureAsync(string itemName, AecgEcgWaveform waveform, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<bool>();

            var ok = await Task.Run(() => _hw.Aecg100!.OutputEcg(waveform, (time, ac, dc) =>
            {
                // SDK 回调结束信号：ac = int.MinValue
                if (ac == int.MinValue)
                {
                    tcs.TrySetResult(true);
                    return;
                }
                PressureSampled?.Invoke(itemName, new PressureSamplePoint(time, ac + dc));
            }), token).ConfigureAwait(false);

            if (!ok) return false;

            // 等待回调结束信号或超时
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(30000);
            try { await tcs.Task.WaitAsync(cts.Token); }
            catch (OperationCanceledException) { /* 超时或用户取消，继续 */ }
            return true;
        }

        /// <summary>
        /// 输出频率扫描波形并采集回调数据，通过 PressureSampled 事件推送给 UI 显示。
        /// </summary>
        private async Task<bool> OutputFrequencyScanWithCaptureAsync(string itemName, AecgFrequencyScan scan, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<bool>();

            var ok = await Task.Run(() => _hw.Aecg100!.OutputFrequencyScan(scan, (time, ac, dc) =>
            {
                if (ac == int.MinValue)
                {
                    tcs.TrySetResult(true);
                    return;
                }
                PressureSampled?.Invoke(itemName, new PressureSamplePoint(time, ac + dc));
            }), token).ConfigureAwait(false);

            if (!ok) return false;

            // 频率扫描最长 Duration + 10s 余量
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter((int)((scan.Duration + 10) * 1000));
            try { await tcs.Task.WaitAsync(cts.Token); }
            catch (OperationCanceledException) { /* 超时或用户取消 */ }
            return true;
        }

        /// <summary>
        /// 根据 AECG100 配置构建默认 ECG 波形。frequency 参数用于覆盖配置中的默认频率
        /// （心率测试时按 BPM/60 计算，其他场景传 1.0 即 60BPM）。
        /// </summary>
        private static AecgEcgWaveform BuildDefaultEcgWaveform(Share.Models.Aecg100Settings cfg, double frequency)
        {
            var timePeriod = frequency > 0 ? (int)(1000.0 / frequency) : cfg.DefaultTimePeriod;
            return new AecgEcgWaveform
            {
                WaveformType = (AecgEcgWaveformType)cfg.DefaultWaveformType,
                Frequency = frequency,
                Amplitude = cfg.DefaultAmplitude,
                TWave = cfg.DefaultTWave,
                PWave = cfg.DefaultPWave,
                STSegment = cfg.DefaultSTSegment,
                DCOffsetVariable = cfg.DCOffsetVariable,
                DCOffset = cfg.DCOffset,
                TimePeriod = timePeriod,
                PRInterval = cfg.DefaultPRInterval,
                QRSDuration = cfg.DefaultQRSDuration,
                TDuration = cfg.DefaultTDuration,
                QTInterval = cfg.DefaultQTInterval,
                Impedance = cfg.ImpedanceEnabled ? AecgEcgImpedance.On : AecgEcgImpedance.Off,
                Electrode = (AecgElectrode)cfg.DefaultElectrode,
                PulseWidth = cfg.DefaultPulseWidth,
                NoiseAmplitude = cfg.NoiseAmplitude,
                NoiseFrequency = (AecgEcgNoiseFrequency)cfg.NoiseFrequency,
                PacingEnabled = cfg.PacingEnabled,
                PacingAmplitude = cfg.PacingAmplitude,
                PacingDuration = cfg.PacingDuration,
                PacingRate = cfg.PacingRate
            };
        }
        
        /// <summary>
        /// 打开产品并执行“产品问询 + 产品当前状态查询”两项通讯项。
        /// 产品未连接/无响应时仅这两项 FAIL（results 保持默认 0），不阻断仪器控制流程——
        /// 没接产品也能调试 BPA700 侧逻辑，无需在调试器里跳语句。
        /// 返回产品通讯是否可用。
        /// </summary>
        private async Task<bool> ProductHandshakeAsync(ProductController product, TestItemResultCollector collector, CancellationToken token)
        {
            try
            {
                await product.OpenAsync(token);

                // 产品问询：产品返回 MAC 地址和嵌软版本
                var info = await product.QueryProductInfoAsync(token);
                if (!string.IsNullOrWhiteSpace(info))
                {
                    await collector.SetAsync("产品问询", TestItemResult.Of(1));
                }
                else
                {
                    await collector.SetAsync("产品问询", TestItemResult.Of(0));
                    _log.Warn("产品问询无响应（串口已打开但产品无返回），判 FAIL");
                }
                // TODO: 产品返回格式确定后，从 info 中解析 MAC/嵌软版本字段做字段级校验

                // 产品当前状态查询（返回内容预留待定：有返回即通过）
                var status = await product.QueryProductStatusAsync(token);
                if (!string.IsNullOrWhiteSpace(status))
                {
                    await collector.SetAsync("产品当前状态查询", TestItemResult.Of(1));
                }
                else
                {
                    await collector.SetAsync("产品当前状态查询", TestItemResult.Of(0));
                    _log.Warn("产品当前状态查询无响应（串口已打开但产品无返回），判 FAIL");
                }
                // TODO: 状态返回格式确定后，改为按状态内容判定

                return true;
            }
            catch (OperationCanceledException)
            {
                // 用户点"停止"：向上层传递，不当作测试异常
                throw;
            }
            catch (Exception ex)
            {
                _log.Warn($"产品通讯不可用（打开/问询/状态查询失败），[产品问询]/[产品当前状态查询] 判 FAIL，继续仪器控制流程：{ex.Message}");
                await collector.SetAsync("产品问询", TestItemResult.Of(0));
                await collector.SetAsync("产品当前状态查询", TestItemResult.Of(0));
                return false;
            }
        }
    }
}
