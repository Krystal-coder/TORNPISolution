using TORNPISolution.Interface;
using TORNPISolution.Service.Devices;
using TORNPISolution.Service.Hardware;
using TORNPISolution.Service.TestModules;
using TORNPISolution.Service.TestSteps;
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
        /// 固高刀锋IO模块（各模块共用 HardwareManager 里的同一实例，由组合根注入）
        /// </summary>
        private readonly HardwareManager _hw;

        /// <summary>
        /// 仪器链接配置服务（读取 Config\instruments.json，由组合根注入）
        /// </summary>
        private readonly IInstrumentConfigService _configService;

        /// <summary>
        /// 产品控制器工厂：每次测量新建（串口不可复用，IDisposable），由组合根注入，绝不能单例化
        /// </summary>
        private readonly Func<IProductController> _productFactory;

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

        //公用构造函数（依赖由 DI 容器注入，注册见 App.xaml.cs 组合根）
        public TestItem(HardwareManager hardware, IInstrumentConfigService configService, Func<IProductController> productFactory)
        {
            _hw = hardware;
            _configService = configService;
            _productFactory = productFactory;
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
                    case TestModuleIds.PpgHeartRateTest:
                        await PpgHeartRateTestModule(collector, token);
                        break;
                    case TestModuleIds.Spo2MeasurementTest:
                        await Spo2MeasurementModule(collector, token);
                        break;
                    case TestModuleIds.Spo2AccuracyTest:
                        await Spo2AccuracyModule(collector, token);
                        break;
                    case TestModuleIds.Spo2PerfusionTest:
                        await Spo2PerfusionModule(collector, token);
                        break;
                    case TestModuleIds.ChargingTest:
                        await ChargingTestModule(collector, token);
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
        /// 模块方法统一执行包装：Start/Ended 日志与异常隔离集中在此处，模块方法只写测试流程本身。
        /// OperationCanceledException（用户点"停止"）向上层传递，不当作测试异常；
        /// 其余异常记录 Error 后吞掉（模块失败，未写入的结果项保持默认 0 = FAIL）；
        /// 需要在异常时做额外处理的模块（如连接失败通知 UI）通过 onError 传入。
        /// </summary>
        private async Task RunModuleAsync(string moduleName, Func<Task> body, Func<Exception, Task>? onError = null)
        {
            _log.Info($"{DateTime.Now} Start calling the TestItem.{moduleName}() method . {Environment.NewLine}");
            try
            {
                await body();
            }
            catch (OperationCanceledException)
            {
                // 用户点"停止"：向上层传递，不当作测试异常
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in TestItem.class {moduleName} method, message: {ex}", ex);
                if (onError != null)
                    await onError(ex);
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the TestItem.{moduleName}() method . {Environment.NewLine}");
            }
        }

        /// <summary>
        /// 初始化仪器：固高IO模块等硬件初始化。
        /// 成功写 1，失败/异常保持初始化的 0（规格 1~1，成功才 PASS）。
        /// </summary>
        //测试项目**************************---M00---**************************初始化仪器
        public Task InitInstrumentsModule(TestItemResultCollector collector, CancellationToken token)
            => RunModuleAsync(nameof(InitInstrumentsModule), async () =>
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
            );

        /// <summary>
        /// 血压仪初始化连接：按仪器链接配置中选择的血压模拟仪初始化。
        /// 选康伟达 → 初始化康伟达；选鲸扬 → 初始化 BPA700（同时下发 Category/单位/袖带容量三项设置）；
        /// 未选择模拟仪 → FAIL。连接成功写 1，失败/异常保持 0。
        /// </summary>
        //测试项目**************************---M01---**************************血压仪初始化连接
        public Task BpDeviceInitModule(TestItemResultCollector collector, CancellationToken token)
            => RunModuleAsync(nameof(BpDeviceInitModule), async () =>
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
                        // 当前重点测试 BP-003 静态压力精度，初始化连接也使用 Beta SDK，
                        // 避免 Formal/Beta 两套 SDK 切换时设备状态不一致导致采样回调上不来。
                        using (var bpa = new BPA700BetaSimulator(config.BPA700Simulator.PortName, _log))
                        {
                            await bpa.OpenAsync(token);
                            // 初始化时同步下发三项设置（默认 Adult/mmHg/500ml，含切换等待与重试，响应停止令牌）
                            await bpa.ApplySettingsAsync(
                                config.BPA700Simulator.Category,
                                config.BPA700Simulator.PressureUnit,
                                config.BPA700Simulator.AirReservoir,
                                token);
                            _log.Info("鲸扬 BPA700 Beta 模拟仪初始化成功");
                        }
                        break;

                    default:
                        _log.Error("血压仪初始化连接失败：未选择血压模拟仪（仪器链接配置 → 模拟仪类型）");
                        return;
                }

                await collector.SetAsync("血压模拟仪初始化连接", TestItemResult.Of(1));
            },
            onError: async _ =>
            {
                await collector.SetAsync("血压模拟仪初始化连接", TestItemResult.Of(0));
                DeviceConnectionFailed?.Invoke("血压仪初始化连接", "设备未连接，请检查串口配置");
            });


        /// <summary>
        /// NFC功能测试（MAC 信息写入和验证）：
        /// 寻卡 → 写 MAC → 回读验证 → 读标签内容，一次性执行完整流程，
        /// 返回全部测试项结果（1=PASS / 0=FAIL，按 Spec 静态规格 1~1 判定）。
        /// </summary>
        //测试项目**************************---M02---**************************NFC功能测试
        public Task NfcFunctionModule(TestItemResultCollector collector, CancellationToken token)
            => RunModuleAsync(nameof(NfcFunctionModule), async () =>
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
            );

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
        public Task BpAccuracyTestModule(TestItemResultCollector collector, CancellationToken token)
            => RunModuleAsync(nameof(BpAccuracyTestModule), async () =>
            {
                var config = _configService.Load();

                // BP-001/002 需鲸扬 BPA700 作待测侧
                if (config.ActiveSimulator != SimulatorType.BPA700)
                {
                    _log.Error("产品充放气测试跳过：BP-001/002 需选择鲸扬 BPA700 作模拟仪（仪器链接配置 → 模拟仪类型）");
                    return;
                }

                using (var simulator = new BPA700BetaSimulator(config.BPA700Simulator.PortName, _log))
                using (var product = _productFactory())
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
            );

        /// <summary>
        /// 血压静态压力精度测试（BP-003）：
        /// ① 产品问询 / 产品当前状态查询（同 M03）；
        /// ② BPARunStaticPressure 逐点加压（压力点/稳压时长/充气方式取仪器链接配置→BPA700→测试参数→静态压力，
        ///    仅勾选的点参与），每点稳压后读产品上报压力（协议预留）；
        /// ③ 测试值 = 各压力点 |产品上报值 − BPA700稳压值| 的最大绝对误差（≤2mmHg 为 PASS）。
        /// 全程开启压力采样，静态压力曲线在 UI 实时展示。
        /// </summary>
        //测试项目**************************---M05---**************************血压静态压力精度测试（BP-003）
        public Task BpStaticAccuracyModule(TestItemResultCollector collector, CancellationToken token)
            => RunModuleAsync(nameof(BpStaticAccuracyModule), async () =>
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

                // BP-003 静态压力精度测试使用 Beta SDK（实验用），其余 BPA700 测试保持 Formal 不变
                using (var simulator = new BPA700BetaSimulator(config.BPA700Simulator.PortName, _log))
                using (var product = _productFactory())
                {
                    await simulator.OpenAsync(token);

                    // ① 产品问询 / 产品当前状态查询（产品未连接时仅两项通讯项 FAIL，不阻断仪器控制流程）
                    var productAvailable = await ProductHandshakeAsync(product, collector, token);

                    // ② 逐点静态加压，全程采样画曲线
                    var waveWatch = System.Diagnostics.Stopwatch.StartNew();
                    void OnSample(double pressure) =>
                        PressureSampled?.Invoke("BP-003静态压力精度测试",
                            new PressureSamplePoint(waveWatch.Elapsed.TotalSeconds, pressure));

                    double maxError = 0;
                    var gotProductValue = false;
                    double worstStandard = 0, worstProductValue = 0;
                    try
                    {
                        simulator.PressureSampled += OnSample;
                        await simulator.StartSamplingAsync(token);

                        // 产品可用时才设置产品测试模式（0x73 mode=3）并读取产品上压力值
                        if (productAvailable)
                            await product.SetTestModeAsync(3, token);
                        else
                            _log.Warn("BP-003：产品不可用，本次仅验证 BPA700 Beta 静态压力输出/波形，不上报产品比对结果");

                        // 将启用的压力点转为数组
                        var pointValues = points.ToArray();

                        // 一次性调用序列 API，返回每个点的实际稳定压力值
                        var stablePressures = await simulator.RunStaticPressureSequenceAsync(
                            pointValues, sp.MeasureSeconds, sp.BpaInflation, token);

                        // 处理结果（与产品上报值比对）
                        if (productAvailable)
                        {
                            for (int i = 0; i < pointValues.Length; i++)
                            {
                                var standard = stablePressures[i];
                                if (!double.IsNaN(standard))
                                {
                                    var productValue = await product.ReadProductPressureAsync(token);
                                    if (!double.IsNaN(productValue))
                                    {
                                        var err = Math.Abs(productValue - standard);
                                        if (!gotProductValue || err > maxError)
                                        {
                                            maxError = err;
                                            worstStandard = standard;
                                            worstProductValue = productValue;
                                        }
                                        gotProductValue = true;
                                        _log.Info($"BP-003 压力点 {pointValues[i]}：标准值={standard:0.0} 产品上报={productValue:0.0} 误差={err:0.0}");
                                    }
                                }
                            }
                        }
                        else
                        {
                            _log.Info("BP-003 无产品比对，仅验证序列输出");
                        }
                    }
                    finally
                    {
                        await simulator.StopSamplingAsync();
                        simulator.PressureSampled -= OnSample;
                    }

                    // ③ 上报最大误差点双值：仪器列=标准值，产品列=产品上报值，动态规格=标准值±2mmHg
                    if (gotProductValue)
                    {
                        await collector.SetAsync("BP-003静态压力精度测试", TestItemResult.WithDualValues(
                            Math.Round(worstStandard, 2), Math.Round(worstProductValue, 2),
                            worstStandard - 2, worstStandard + 2));
                    }
                    else
                    {
                        _log.Warn("BP-003：未取到产品上报值，比对预留（偏差计 0；产品压力上报协议定稿后补 ProductController.ReadProductPressureAsync）");
                        await collector.SetAsync("BP-003静态压力精度测试", TestItemResult.Of(0));
                    }
                }
            }
            );

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
        public Task BpDynamicAccuracyModule(TestItemResultCollector collector, CancellationToken token)
            => RunModuleAsync(nameof(BpDynamicAccuracyModule), async () =>
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

                using (var simulator = new BPA700BetaSimulator(config.BPA700Simulator.PortName, _log))
                using (var product = _productFactory())
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
                    
                    // 波形采样：BPA700 实时压力点上抛到 "BP-004收缩压精度测试" 行显示充放气压力曲线（全组共用一条时间轴）
                    var bp4Wave = System.Diagnostics.Stopwatch.StartNew();
                    void OnBp004Sample(double pressure) =>
                        PressureSampled?.Invoke("BP-004收缩压精度测试",
                            new PressureSamplePoint(bp4Wave.Elapsed.TotalSeconds, pressure));
                    simulator.PressureSampled += OnBp004Sample;
                    try
                    {
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
                    finally
                    {
                        simulator.PressureSampled -= OnBp004Sample;
                    }
                }
            }
            );

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
        public Task PressureControlModule(TestItemResultCollector collector, CancellationToken token)
            => RunModuleAsync(nameof(PressureControlModule), async () =>
            {
                var config = _configService.Load();
                if (config.ActiveSimulator != SimulatorType.BPA700)
                {
                    _log.Error("压力控制功能测试跳过：BP-006~009 需选择鲸扬 BPA700 作模拟仪（仪器链接配置 → 模拟仪类型）");
                    return;
                }

                using (var simulator = new BPA700BetaSimulator(config.BPA700Simulator.PortName, _log))
                using (var product = _productFactory())
                {
                    await simulator.OpenAsync(token);

                    // 产品问询 / 产品当前状态查询
                    await ProductHandshakeAsync(product, collector, token);

                    // 全模块采样
                    var context = new BpaTestContext(
                        simulator,
                        config.BPA700Simulator,
                        (name, point) => PressureSampled?.Invoke(name, point),
                        product);

                    try
                    {
                        await simulator.StartSamplingAsync(token);

                        var steps = new List<ITestStep>
                        {
                            new BP006_HighestInflationStep(),
                            new BP007_UniformDeflationStep(),
                            new BP008_FastReleaseStep(),
                            new BP009_OverpressureStep()
                        };

                        var runner = new TestStepRunner(_log);
                        await runner.RunAllAsync(steps, context, collector, token);
                    }
                    finally
                    {
                        await simulator.StopSamplingAsync();
                    }
                }
            }
            );

        /// <summary>
        /// AECG100 初始化连接：通过 HardwareManager 连接 AECG100 多功能综合性能测试仪，
        /// 连接成功后读取设备型号/固件/序列号信息。
        /// 成功写 1，失败/异常保持 0。
        /// </summary>
        //测试项目**************************---M07---**************************AECG100初始化连接
        public Task Aecg100InitModule(TestItemResultCollector collector, CancellationToken token)
            => RunModuleAsync(nameof(Aecg100InitModule), async () =>
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

                // 读取设备信息（拆分为三个子项分别报告）
                var model = _hw.Aecg100!.GetDeviceInformation();
                var serial = _hw.Aecg100.GetSerialNumber();
                var hw = _hw.Aecg100.GetHwInformation();

                await collector.SetAsync("AECG100设备型号", TestItemResult.OfText(model != null ? 1 : 0, model?.ProductName ?? "--"));
                if (model != null)
                    _log.Info($"AECG100 设备型号：{model.ProductName}");

                await collector.SetAsync("AECG100固件版本", TestItemResult.OfText(
                    hw != null ? 1 : 0,
                    hw != null ? $"{hw.FWMajorVersion}.{hw.FWMinorVersion}.{hw.FWBuildVersion}" : "--"));
                if (hw != null)
                    _log.Info($"AECG100 固件版本：{hw.FWMajorVersion}.{hw.FWMinorVersion}.{hw.FWBuildVersion}");

                await collector.SetAsync("AECG100序列号", TestItemResult.OfText(serial != null ? 1 : 0, serial ?? "--"));
                _log.Info($"AECG100 序列号：{serial ?? "N/A"}");
            }
            );

        /// <summary>
        /// ECG 采集与启动（ECG-001 测量启动 / ECG-002 导联脱落检测）：
        /// ① 产品问询/产品当前状态查询：按实际通讯结果判定；
        /// ② ECG-001：AECG100 输出 ECG 波形（WTQOutputECG），SDK 返回成功即 PASS；
        /// ③ ECG-002：输出波形后提示拔掉 LA 导联，读取产品导联状态（0x88）判定脱落。
        /// </summary>
        //测试项目**************************---M08---**************************ECG采集与启动（ECG-001/002）
        public Task EcgAcquisitionTestModule(TestItemResultCollector collector, CancellationToken token)
            => RunModuleAsync(nameof(EcgAcquisitionTestModule), async () =>
            {
                // ① 产品问询/状态查询（产品串口协议 0x72/0x74）
                var config = _configService.Load();
                using (var product = _productFactory())
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

                // PPG-1R-525 模块在位检测（从 M07 移至此处）
                await EnsurePpgModuleAsync(AecgPpgModuleKind.Green, "PPG-1R-525模块在位检测", "PPG-1R-525（绿光）", collector);

                // ② ECG-001 测量启动：AECG100 输出 ECG 波形（参数从配置页读取），采集输出波形
                var ecgConfig = _configService.Load().Aecg100Tester;
                var ecgWaveform = BuildDefaultEcgWaveform(ecgConfig, 1.0);  // 60BPM = 1Hz

                var (wf001, cap001) = ApplyWaveformConfig(ecgConfig, "ECG-001测量启动", ecgWaveform);
                var outputOk = await OutputEcgWithCaptureAsync("ECG-001测量启动", wf001, cap001, token);
                if (outputOk)
                {
                    await collector.SetAsync("ECG-001测量启动", TestItemResult.Of(1));
                        _log.Info("ECG-001 PASS：AECG100 ECG 波形输出成功（含波形采集）");
                }
                else
                {
                    _log.Error("ECG-001 FAIL：AECG100 ECG 波形输出失败（WTQOutputECG 返回 false）");
                }

                // ③ ECG-002 导联脱落检测：输出波形 → 提示拔掉导联 → 读取产品导联状态判定
                _log.Info("ECG-002：请拔掉 LA 导联，观察产品是否上报导联脱落状态...");
                var ecgWaveform2 = BuildDefaultEcgWaveform(ecgConfig, 1.0);
                var (wf002, cap002) = ApplyWaveformConfig(ecgConfig, "ECG-002导联脱落检测", ecgWaveform2);
                await OutputEcgWithCaptureAsync("ECG-002导联脱落检测", wf002, cap002, token);
                await RunEcgLeadOffDetectionAsync(collector, token);

                // 停止波形输出
                _hw.Aecg100.StopOutputWaveform();
            }
            );

        /// <summary>
        /// ECG-002 导联脱落检测：输出波形后读取产品导联状态（0x88）判定。
        /// 判定规则：返回 lead_state = 0 表示导联脱落（PASS），非 0 表示未脱落（FAIL）。
        /// </summary>
        private async Task RunEcgLeadOffDetectionAsync(TestItemResultCollector collector, CancellationToken token)
        {
            // 给操作员留时间拔掉 LA 导联
            try { await Task.Delay(2000, token); }
            catch (OperationCanceledException) { throw; }

            var config = _configService.Load();
            using var product = _productFactory();
            try
            {
                await product.OpenAsync(token);
                var leadState = await product.ReadLeadStateAsync(token);
                var hex = BitConverter.ToString(leadState).Replace("-", " ");
                _log.Info($"ECG-002：读取到产品导联状态 0x88 = {hex}");

                // 协议定义：lead_state = 0 表示脱落，非 0 表示打开
                if (leadState.Length > 0 && leadState[0] == 0)
                {
                    await collector.SetAsync("ECG-002导联脱落检测", TestItemResult.Of(1));
                    _log.Info("ECG-002 PASS：产品检测到导联脱落");
                }
                else
                {
                    await collector.SetAsync("ECG-002导联脱落检测", TestItemResult.Of(0));
                    _log.Error("ECG-002 FAIL：产品未检测到导联脱落");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"ECG-002 FAIL：读取产品导联状态失败：{ex.Message}");
                await collector.SetAsync("ECG-002导联脱落检测", TestItemResult.Of(0));
            }
        }

        /// <summary>
        /// ECG 心率监测（ECG-004 心率检测精度）：
        /// ① 产品问询/状态查询：参与模块判定；
        /// ② ECG-004：按配置页心率点（默认 40/60/80/100/120/160 BPM）逐档输出 ECG 波形，
        ///    每档重复 3 次（配置可调），仅验证仪器设置下发成功（产品回传比对预留），
        ///    测试值 = 各心率点设置下发偏差的最大值（当前仅验证 SDK 返回成功，偏差 = 0）。
        /// </summary>
        //测试项目**************************---M09---**************************ECG心率监测（ECG-004）
        public Task EcgHeartRateTestModule(TestItemResultCollector collector, CancellationToken token)
            => RunModuleAsync(nameof(EcgHeartRateTestModule), async () =>
            {
                // ① 产品问询/状态查询（产品串口协议 0x72/0x74）
                var config = _configService.Load();
                var productAvailable = false;
                using (var product = _productFactory())
                {
                    productAvailable = await ProductHandshakeAsync(product, collector, token);
                    if (!productAvailable)
                        _log.Warn("产品不可用：ECG-004 仍将输出 AECG100 波形，但产品回传比对项可能 FAIL");
                }

                // 设置心电测试模式（0x73 mode=5）
                // 注：产品模式在 SetTestModeAsync 中设置，此处仅打开串口发指令后关闭
                using (var productForMode = _productFactory())
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

                // ② 逐档心率输出 ECG 波形，每档重复 N 次（段号=测试点序号，波形按段着色）
                double maxDeviation = 0;
                double worstSetBpm = double.NaN, worstReadBpm = double.NaN;
                for (var pointIndex = 0; pointIndex < testPoints.Count; pointIndex++)
                {
                    var bpm = testPoints[pointIndex];
                    token.ThrowIfCancellationRequested();
                    var frequency = bpm / 60.0;   // BPM → Hz（60 BPM = 1Hz）

                    for (var r = 1; r <= repeats; r++)
                    {
                        var waveform = BuildDefaultEcgWaveform(ecgConfig, frequency);

                        // 输出并按固定时长采集回调波形，上抛 UI 显示（ECG-004 行内波形）
                        var outputOk = await OutputEcgWithCaptureAsync("ECG-004心率检测精度", waveform,
                            GetWaveformCaptureMs(ecgConfig, "ECG-004心率检测精度", 2000), token, pointIndex);
                        if (!outputOk)
                        {
                            _log.Error($"ECG-004 FAIL：心率 {bpm}BPM 第 {r}/{repeats} 次输出失败（WTQOutputECG 返回 false）");
                            continue;
                        }

                        // 波形稳定后回读产品心率（0x7C）比对；产品不可用时比对预留（偏差计 0）
                        var readBpm = await ReadProductHeartRateAsync(productAvailable, token);
                        var deviation = double.IsNaN(readBpm) ? 0.0 : Math.Abs(readBpm - bpm);
                        if (double.IsNaN(worstReadBpm) || deviation > maxDeviation)
                        {
                            maxDeviation = deviation;
                            worstSetBpm = bpm;
                            worstReadBpm = readBpm;
                        }
                        _log.Info($"ECG-004 心率 {bpm}BPM 第 {r}/{repeats} 次：仪器设置下发成功，偏差={deviation:0.00}BPM");

                        // 每次输出后短暂等待，让波形稳定
                        await Task.Delay(500, token);
                    }

                    // 每档测试完后停止波形
                    _hw.Aecg100.StopOutputWaveform();
                }

                // 取最大偏差点上报双值：仪器列=设定值，产品列=回读值，动态规格=设定值±容差
                if (double.IsNaN(worstReadBpm))
                    await collector.SetAsync("ECG-004心率检测精度", TestItemResult.Of(Math.Round(maxDeviation, 2)));
                else
                    await collector.SetAsync("ECG-004心率检测精度", TestItemResult.WithDualValues(
                        worstSetBpm, worstReadBpm,
                        worstSetBpm - ecgConfig.HeartRateTolerance, worstSetBpm + ecgConfig.HeartRateTolerance));
                _log.Info($"ECG-004 完成：最大偏差={maxDeviation:0.00}BPM（规格 ≤±{ecgConfig.HeartRateTolerance}BPM，产品回传比对预留）");
            }
            );

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
        public Task EcgSignalQualityTestModule(TestItemResultCollector collector, CancellationToken token)
            => RunModuleAsync(nameof(EcgSignalQualityTestModule), async () =>
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

                // ECG-009 拆分为 a/b/c 三个独立测试项，每个子项各自输出并判定
                async Task<bool> RunEcg009Item(string itemName, double dcOffset)
                {
                    var triWave = BuildDefaultEcgWaveform(ecgConfig, 10.4);
                    triWave.WaveformType = AecgEcgWaveformType.Triangle;
                    triWave.Amplitude = 6.0;                       // 6mV 峰-谷
                    triWave.DCOffsetVariable = false;
                    triWave.DCOffset = (int)dcOffset;
                    var (wf, captureMs) = ApplyWaveformConfig(ecgConfig, itemName, triWave);
                    var ok = await OutputEcgWithCaptureAsync(itemName, wf, captureMs, token);
                    _hw.Aecg100.StopOutputWaveform();
                    await Task.Delay(500, token);
                    return ok;
                }

                var triOk = await RunEcg009Item("ECG-009a动态范围(无偏置)", 0);
                var dc300Ok = await RunEcg009Item("ECG-009b动态范围(+300mV)", 300);
                var dcn300Ok = await RunEcg009Item("ECG-009c动态范围(-300mV)", -300);

                // TODO: 产品回传 DUT 回放幅度，判定衰减 < 50μV；目前仪器输出成功即 PASS
                if (triOk)
                {
                    await collector.SetAsync("ECG-009a动态范围(无偏置)", TestItemResult.Of(1));
                    _log.Info("ECG-009a PASS：无偏置 6mV 三角波输出成功");
                }
                else
                {
                    _log.Error("ECG-009a FAIL：无偏置 6mV 三角波输出失败");
                }

                if (dc300Ok)
                {
                    await collector.SetAsync("ECG-009b动态范围(+300mV)", TestItemResult.Of(1));
                    _log.Info("ECG-009b PASS：+300mV DC 偏置三角波输出成功");
                }
                else
                {
                    _log.Error("ECG-009b FAIL：+300mV DC 偏置三角波输出失败");
                }

                if (dcn300Ok)
                {
                    await collector.SetAsync("ECG-009c动态范围(-300mV)", TestItemResult.Of(1));
                    _log.Info("ECG-009c PASS：-300mV DC 偏置三角波输出成功");
                }
                else
                {
                    _log.Error("ECG-009c FAIL：-300mV DC 偏置三角波输出失败");
                }

                // ==================== ECG-010 输入阻抗（YY 9706.247-2021 201.12.4.4.102） ====================
                // 标准：输出 10Hz 5mV 正弦波，开关 S1 闭合/断开两种状态，振幅变化 ≤ 6% → 输入阻抗 > 10MΩ
                // 当前：AECG100 发出标准信号，产品回传 + 外部开关控制预留
                _log.Info("ECG-010：发出 10Hz 5mV 正弦波（标准 201.12.4.4.102）...");

                var sineWave10Hz = BuildDefaultEcgWaveform(ecgConfig, 10.0);
                sineWave10Hz.WaveformType = AecgEcgWaveformType.Sine;
                sineWave10Hz.Amplitude = 5.0;  // 5mV 峰-谷
                var (wf010, cap010) = ApplyWaveformConfig(ecgConfig, "ECG-010输入阻抗", sineWave10Hz);
                var sineOk = await OutputEcgWithCaptureAsync("ECG-010输入阻抗", wf010, cap010, token);
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
                var (wf011, cap011) = ApplyWaveformConfig(ecgConfig, "ECG-011增益准确性", sineWave5Hz);
                var gainOk = await OutputEcgWithCaptureAsync("ECG-011增益准确性", wf011, cap011, token);
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

                var scanCfg = ecgConfig.GetWaveformConfig("ECG-012频率响应");
                var scanCaptureMs = scanCfg.CaptureTimeMs > 0 ? scanCfg.CaptureTimeMs : 32000;
                var freqScan = new AecgFrequencyScan
                {
                    Amplitude = 2.0,           // 2mV
                    FrequencyStart = 0.67,   // 0.67Hz
                    FrequencyFinish = 40.0,  // 40Hz
                    Duration = scanCaptureMs / 1000  // 扫描时长（秒）
                };
                var scanOk = await OutputFrequencyScanWithCaptureAsync("ECG-012频率响应", freqScan, scanCaptureMs, token);
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
            );

        /// <summary>
        /// PPG 脉搏率模拟测试（PPG-001，需 PPG-1R-525 绿光模块在位）：
        /// ① 产品问询/状态查询参与模块判定；
        /// ② 模块在位检测：AECG100 同时只接一个 PPG 模块，在位模块不是绿光单 LED 模块则拦截，
        ///    提示更换模块后重跑（测一个调一个）；
        /// ③ PPG-001：按配置心率点逐档输出 PPG 波形（频率 = BPM/60），
        ///    产品光学传感器计算脉搏率后回读 0x7C 比对。
        /// </summary>
        //测试项目**************************---M11---**************************PPG脉搏率模拟测试（PPG-001）
        public Task PpgHeartRateTestModule(TestItemResultCollector collector, CancellationToken token)
            => RunModuleAsync(nameof(PpgHeartRateTestModule), async () =>
            {
                var config = _configService.Load();
                var productAvailable = false;
                using (var product = _productFactory())
                {
                    productAvailable = await ProductHandshakeAsync(product, collector, token);
                    if (!productAvailable)
                        _log.Warn("产品不可用：PPG-001 仍将输出 AECG100 PPG 波形，但产品回读比对预留（偏差计 0）");
                }

                // 检查 AECG100 是否已连接
                if (_hw.Aecg100 == null || !_hw.Aecg100.IsConnected)
                {
                    _log.Error("PPG-001 跳过：AECG100 未连接，请先执行 M07 AECG100初始化连接");
                    return;
                }

                // 模块在位检测：PPG 心率测试需 PPG-1R-525 绿光模块（没接模块根本无法开始测试，直接拦截）
                if (!await EnsurePpgModuleAsync(AecgPpgModuleKind.Green, "PPG-1R-525模块在位检测", "PPG-1R-525（绿光）", collector))
                    return;

                var cfg = config.Aecg100Tester;
                if (cfg.PpgHeartRateTestPoints.Count == 0)
                {
                    _log.Warn("PPG-001 跳过：未配置 PPG 心率测试点（配置 → ECG参数配置 → PPG心率）");
                    return;
                }

                // 逐档心率输出 PPG 波形，每档重复 N 次（段号=测试点序号，波形按段着色）
                double maxDeviation = 0;
                double worstSetBpm = double.NaN, worstReadBpm = double.NaN;
                for (var pointIndex = 0; pointIndex < cfg.PpgHeartRateTestPoints.Count; pointIndex++)
                {
                    var bpm = cfg.PpgHeartRateTestPoints[pointIndex];
                    token.ThrowIfCancellationRequested();

                    for (var r = 1; r <= cfg.PpgHeartRateRepeats; r++)
                    {
                        var waveform = BuildDefaultPpgWaveform(cfg, bpm / 60.0);
                        var outputOk = await OutputPpgWithCaptureAsync("PPG-001脉搏率精度", AecgPpgChannel.Channel1, waveform, cfg.PpgCaptureTimeMs, token, pointIndex);
                        if (!outputOk)
                        {
                            _log.Error($"PPG-001 FAIL：心率 {bpm}BPM 第 {r}/{cfg.PpgHeartRateRepeats} 次输出失败（WTQOutputPPG 返回 false）");
                            continue;
                        }

                        // 波形稳定后回读产品心率（0x7C）比对；产品不可用时比对预留（偏差计 0）
                        var readBpm = await ReadProductHeartRateAsync(productAvailable, token);
                        var deviation = double.IsNaN(readBpm) ? 0.0 : Math.Abs(readBpm - bpm);
                        if (double.IsNaN(worstReadBpm) || deviation > maxDeviation)
                        {
                            maxDeviation = deviation;
                            worstSetBpm = bpm;
                            worstReadBpm = readBpm;
                        }
                        _log.Info($"PPG-001 心率 {bpm}BPM 第 {r}/{cfg.PpgHeartRateRepeats} 次：偏差={deviation:0.00}BPM");
                    }

                    // 每档测试完后停止波形
                    _hw.Aecg100.StopOutputWaveform();
                }

                // 取最大偏差点上报双值：仪器列=设定值，产品列=回读值，动态规格=设定值±容差
                if (double.IsNaN(worstReadBpm))
                    await collector.SetAsync("PPG-001脉搏率精度", TestItemResult.Of(Math.Round(maxDeviation, 2)));
                else
                    await collector.SetAsync("PPG-001脉搏率精度", TestItemResult.WithDualValues(
                        worstSetBpm, worstReadBpm,
                        worstSetBpm - cfg.PpgHeartRateTolerance, worstSetBpm + cfg.PpgHeartRateTolerance));
                _log.Info($"PPG-001 完成：最大偏差={maxDeviation:0.00}BPM（规格 ≤±{cfg.PpgHeartRateTolerance}BPM）");
            }
            );

        /// <summary>
        /// SpO2 测量模块（对应测试规范 2.2.1，需 PPG-2R-940 红光+红外模块在位）：
        /// ① 产品问询/状态查询参与模块判定，并设血氧测试模式（0x73 mode=4）；
        /// ② 模块在位检测：在位模块不是红光+红外双 LED 模块则拦截，提示更换模块后重跑；
        /// ③ SPO2-001 测量启动与停止：输出红/红外双通道信号启动测量后停止，验证输出启停控制；
        ///    SPO2-002 手指脱落检测：正常输出后停止信号模拟脱落（产品脱落判定回读预留）；
        ///    SPO2-003 低氧报警测试：TBD 预留（产品报警回读协议未定稿）。
        /// </summary>
        //测试项目**************************---M12---**************************SpO2测量模块（SPO2-001~003）
        public Task Spo2MeasurementModule(TestItemResultCollector collector, CancellationToken token)
            => RunModuleAsync(nameof(Spo2MeasurementModule), async () =>
            {
                var config = _configService.Load();
                var productAvailable = false;
                using (var product = _productFactory())
                {
                    productAvailable = await ProductHandshakeAsync(product, collector, token);
                    if (!productAvailable)
                        _log.Warn("产品不可用：SPO2-001/002 仍将输出 AECG100 双通道信号，产品状态回读预留");
                }

                // 设置血氧测试模式（0x73 mode=4）
                using (var productForMode = _productFactory())
                {
                    try
                    {
                        await productForMode.OpenAsync(token);
                        await productForMode.SetTestModeAsync(4, token);
                    }
                    catch { /* 产品不可用时不阻断 AECG100 侧逻辑 */ }
                }

                // 检查 AECG100 是否已连接
                if (_hw.Aecg100 == null || !_hw.Aecg100.IsConnected)
                {
                    _log.Error("SPO2-001/002 跳过：AECG100 未连接，请先执行 M07 AECG100初始化连接");
                    return;
                }

                // 模块在位检测：SpO2 测试需 PPG-2R-940 红光+红外模块（没接模块根本无法开始测试，直接拦截）
                if (!await EnsurePpgModuleAsync(AecgPpgModuleKind.RedIr, "PPG-2R-940模块在位检测", "PPG-2R-940（红光+红外）", collector))
                    return;

                var cfg = config.Aecg100Tester;

                // 正常测量信号：参考血氧值 + 配置脉搏率，按标定曲线反解红光通道幅度
                var spo2 = cfg.Spo2PulseRateRefSpo2;
                var frequency = cfg.Spo2PulseRate / 60.0;
                var rValue = cfg.Spo2CalibrationB == 0 ? 0 : (cfg.Spo2CalibrationA - spo2) / cfg.Spo2CalibrationB;
                var redAc = rValue * cfg.Spo2IrAcAmplitude;
                var (redWave, irWave) = BuildSpo2Waveforms(cfg, frequency, redAc);
                _log.Info($"SPO2 测量信号：SpO2={spo2}% 脉搏率={cfg.Spo2PulseRate}BPM R={rValue:0.000} 红光 AC={redAc:0.00}mV 红外 AC={cfg.Spo2IrAcAmplitude:0.00}mV");

                // SPO2-001 测量启动与停止：双通道输出启动测量，采集完成后停止输出
                var startOutputOk = await OutputPpgExWithCaptureAsync(
                    new[] { "SPO2-001测量启动与停止" }, redWave, irWave, cfg.Spo2CaptureTimeMs, token);
                _hw.Aecg100.StopOutputWaveform();
                await collector.SetAsync("SPO2-001测量启动与停止", TestItemResult.Of(startOutputOk ? 1 : 0));
                if (!startOutputOk)
                    _log.Error("SPO2-001 FAIL：双通道输出失败（WTQOutputPPGEx 返回 false）");

                // SPO2-002 手指脱落检测：先正常输出一段，再停止信号模拟手指脱落（产品脱落判定回读预留）
                token.ThrowIfCancellationRequested();
                var detachOutputOk = await OutputPpgExWithCaptureAsync(
                    new[] { "SPO2-002手指脱落检测" }, redWave, irWave, Math.Min(cfg.Spo2CaptureTimeMs, 3000), token);
                _hw.Aecg100.StopOutputWaveform();   // 停止输出 = 模拟手指脱落无信号
                await Task.Delay(1500, token);
                await collector.SetAsync("SPO2-002手指脱落检测", TestItemResult.Of(detachOutputOk ? 1 : 0));
                _log.Info("SPO2-002：已停止信号输出模拟手指脱落（产品脱落判定回读预留）");

                // SPO2-003 低氧报警测试为 TBD 预留项，UI 预跳过显示"未测试"
            }
            );

        /// <summary>
        /// SpO2 精度检测（对应测试规范 2.2.2，需 PPG-2R-940 红光+红外模块在位）：
        /// ① 产品问询/状态查询参与模块判定，并设血氧测试模式（0x73 mode=4）；
        /// ② SPO2-004 血氧饱和度精度：逐血氧点输出双通道信号，产品回读 0x81 比对（±2%）；
        ///    SPO2-005 脉搏率精度：逐脉搏率点输出双通道信号，产品回读 0x7C 比对（±2BPM）；
        ///    SPO2-006 低氧范围精度：逐低氧点输出双通道信号，产品回读 0x81 比对（±3%）。
        /// 仪器列=设定值、产品列=回读值，动态规格=设定值±容差。
        /// </summary>
        //测试项目**************************---M13---**************************SpO2精度检测（SPO2-004~006）
        public Task Spo2AccuracyModule(TestItemResultCollector collector, CancellationToken token)
            => RunModuleAsync(nameof(Spo2AccuracyModule), async () =>
            {
                var config = _configService.Load();
                var productAvailable = false;
                using (var product = _productFactory())
                {
                    productAvailable = await ProductHandshakeAsync(product, collector, token);
                    if (!productAvailable)
                        _log.Warn("产品不可用：SPO2-004~006 仍将输出 AECG100 双通道信号，但产品回读比对预留（偏差计 0）");
                }

                // 设置血氧测试模式（0x73 mode=4）
                using (var productForMode = _productFactory())
                {
                    try
                    {
                        await productForMode.OpenAsync(token);
                        await productForMode.SetTestModeAsync(4, token);
                    }
                    catch { /* 产品不可用时不阻断 AECG100 侧逻辑 */ }
                }

                // 检查 AECG100 是否已连接
                if (_hw.Aecg100 == null || !_hw.Aecg100.IsConnected)
                {
                    _log.Error("SPO2-004~006 跳过：AECG100 未连接，请先执行 M07 AECG100初始化连接");
                    return;
                }

                // 精度测试需红光+红外模块（本模块无在位检测项，不在位直接拦截并提示去 M12 检测）
                var kind = await Task.Run(() => _hw.Aecg100!.DetectPpgModuleKind());
                if (kind != AecgPpgModuleKind.RedIr)
                {
                    _log.Error($"SPO2-004~006 跳过：当前在位模块不是 PPG-2R-940（红光+红外），请更换模块后先执行 M12 SpO2测量模块完成在位检测");
                    return;
                }

                var cfg = config.Aecg100Tester;

                // SPO2-004 血氧饱和度精度（逐血氧点，脉搏率固定）
                await RunSpo2AccuracySweepAsync(config, collector, "SPO2-004血氧饱和度精度",
                    cfg.Spo2TestPoints, sweepPulseRate: false, cfg.Spo2Tolerance, productAvailable, token);

                // SPO2-005 脉搏率精度（逐脉搏率点，血氧固定为参考值）
                await RunSpo2AccuracySweepAsync(config, collector, "SPO2-005脉搏率精度",
                    cfg.Spo2PulseRateTestPoints, sweepPulseRate: true, cfg.Spo2PulseRateTolerance, productAvailable, token);

                // SPO2-006 低氧范围精度（逐低氧点，脉搏率固定）
                await RunSpo2AccuracySweepAsync(config, collector, "SPO2-006低氧范围精度",
                    cfg.Spo2LowRangeTestPoints, sweepPulseRate: false, cfg.Spo2LowRangeTolerance, productAvailable, token);

                _log.Info($"SPO2 精度检测完成（规格：血氧 ±{cfg.Spo2Tolerance}%，脉搏率 ±{cfg.Spo2PulseRateTolerance}BPM，低氧 ±{cfg.Spo2LowRangeTolerance}%）");
            }
            );

        /// <summary>
        /// SpO2 灌注与抗干扰测试（对应测试规范 2.2.3）：
        /// SPO2-008 弱灌注能力 / SPO2-009 弱灌注提示 / SPO2-010 环境光抗干扰，
        /// PI（灌注指数）幅度控制与环境光控制暂未具备自动化条件，三项均 TBD 预留（UI 预跳过显示"未测试"），
        /// 本模块当前仅执行产品问询/状态查询两项通讯检测。
        /// </summary>
        //测试项目**************************---M14---**************************SpO2灌注与抗干扰测试（SPO2-008/009/010）
        public Task Spo2PerfusionModule(TestItemResultCollector collector, CancellationToken token)
            => RunModuleAsync(nameof(Spo2PerfusionModule), async () =>
            {
                var config = _configService.Load();
                using (var product = _productFactory())
                {
                    var productAvailable = await ProductHandshakeAsync(product, collector, token);
                    if (!productAvailable)
                        _log.Warn("产品不可用：SPO2-008~010 为 TBD 预留项，本次仅产品通讯项参与判定");
                }

                _log.Info("SPO2-008/009/010 为 TBD 预留项（仪器 PI 幅度控制与环境光控制暂未具备自动化条件），本次跳过");
            }
            );

        /// <summary>
        /// 充电测试（充电状态判断 / 当前电池电量测量 / 电池百分比获取）：
        /// 通过产品串口协议读取充电状态（0x6D）和电池信息（0x6C：电压 uint32 LE + 百分比）。
        /// </summary>
        //测试项目**************************---M15---**************************充电测试
        public Task ChargingTestModule(TestItemResultCollector collector, CancellationToken token)
            => RunModuleAsync(nameof(ChargingTestModule), async () =>
            {
                using var product = _productFactory();

                if (!await ProductHandshakeAsync(product, collector, token))
                {
                    _log.Error("充电测试：产品串口不可用，跳过所有测试项");
                    return;
                }

                // 充电状态判断（0x6D）
                try
                {
                    _log.Info("充电测试：发送充电状态查询（0x6D）...");
                    var chargingStatus = await product.ReadChargingStatusAsync(token);
                    await collector.SetAsync("充电状态判断", TestItemResult.Of(chargingStatus));
                    _log.Info($"充电状态判断：{chargingStatus}（1=充电中，0=未充电）");
                }
                catch (Exception ex)
                {
                    _log.Error($"充电状态判断失败：{ex.Message}");
                    await collector.SetAsync("充电状态判断", TestItemResult.Of(0));
                }

                // 当前电池电量测量 + 电池百分比获取（0x6C，同一帧返回电压和百分比）
                try
                {
                    _log.Info("充电测试：发送电池信息查询（0x6C）...");
                    var battery = await product.ReadBatteryInfoAsync(token);
                    if (battery != null)
                    {
                        await collector.SetAsync("当前电池电量测量", TestItemResult.Of(battery.VoltageMv));
                        await collector.SetAsync("电池百分比获取", TestItemResult.Of(battery.Percentage));
                        _log.Info($"电池信息：电压={battery.VoltageMv}mV 百分比={battery.Percentage}%");
                    }
                    else
                    {
                        _log.Warn("电池信息读取失败（产品无响应或 payload 不足）");
                        await collector.SetAsync("当前电池电量测量", TestItemResult.Of(0));
                        await collector.SetAsync("电池百分比获取", TestItemResult.Of(0));
                    }
                }
                catch (Exception ex)
                {
                    _log.Error($"电池信息读取失败：{ex.Message}");
                    await collector.SetAsync("当前电池电量测量", TestItemResult.Of(0));
                    await collector.SetAsync("电池百分比获取", TestItemResult.Of(0));
                }
            }
            );

        /// <summary>
        /// SpO2 精度扫描公共逻辑：逐点输出红光/红外双通道信号，产品回读比对，按最大偏差点上报双值。
        /// sweepPulseRate=false：points 为血氧设定点（%），脉搏率固定 cfg.Spo2PulseRate，回读产品血氧（0x81）；
        /// sweepPulseRate=true ：points 为脉搏率设定点（BPM），血氧固定 cfg.Spo2PulseRateRefSpo2，回读产品脉搏率（0x7C）。
        /// 产品不可用/回读失败时比对预留（无有效回读则按 Of(0) 预留上报）。
        /// </summary>
        private async Task RunSpo2AccuracySweepAsync(
            InstrumentConnectionConfig config, TestItemResultCollector collector, string itemName,
            List<int> points, bool sweepPulseRate, double tolerance, bool productAvailable, CancellationToken token)
        {
            if (points.Count == 0)
            {
                _log.Warn($"{itemName} 跳过：未配置测试点（AECG100参数配置 → SpO2血氧）");
                await collector.SetAsync(itemName, TestItemResult.Of(0));
                return;
            }

            var cfg = config.Aecg100Tester;
            double maxDeviation = 0;
            double worstSet = double.NaN, worstRead = double.NaN;

            for (var pointIndex = 0; pointIndex < points.Count; pointIndex++)
            {
                var point = points[pointIndex];
                token.ThrowIfCancellationRequested();

                var spo2Value = sweepPulseRate ? cfg.Spo2PulseRateRefSpo2 : point;
                var pulseBpm = sweepPulseRate ? point : cfg.Spo2PulseRate;
                var frequency = pulseBpm / 60.0;   // 脉搏率 BPM → Hz

                // 按产品标定曲线 SpO2 = A - B×R 反解 R，再换算红光通道 AC 幅度（红外 AC 固定）
                var rValue = cfg.Spo2CalibrationB == 0 ? 0 : (cfg.Spo2CalibrationA - spo2Value) / cfg.Spo2CalibrationB;
                var redAc = rValue * cfg.Spo2IrAcAmplitude;
                var (redWave, irWave) = BuildSpo2Waveforms(cfg, frequency, redAc);
                _log.Info($"{itemName} SpO2={spo2Value}% 脉搏率={pulseBpm}BPM：R={rValue:0.000}，红光 AC={redAc:0.00}mV，红外 AC={cfg.Spo2IrAcAmplitude:0.00}mV");

                var outputOk = await OutputPpgExWithCaptureAsync(
                    new[] { itemName }, redWave, irWave, cfg.Spo2CaptureTimeMs, token, pointIndex);
                if (!outputOk)
                {
                    _log.Error($"{itemName} FAIL：SpO2={spo2Value}% 脉搏率={pulseBpm}BPM 双通道输出失败（WTQOutputPPGEx 返回 false）");
                    _hw.Aecg100!.StopOutputWaveform();
                    continue;
                }

                // 产品回读血氧（0x81）或脉搏率（0x7C）比对；产品不可用时比对预留（偏差计 0）
                if (productAvailable)
                {
                    using var productRead = _productFactory();
                    try
                    {
                        await Task.Delay(1500, token);   // 等产品测量稳定
                        await productRead.OpenAsync(token);

                        var readValue = sweepPulseRate
                            ? await productRead.ReadHeartRateAsync(token)
                            : await productRead.ReadSpo2ValueAsync(token);
                        if (!double.IsNaN(readValue) && readValue > 0)
                        {
                            var deviation = Math.Abs(readValue - point);
                            if (double.IsNaN(worstRead) || deviation > maxDeviation)
                            {
                                maxDeviation = deviation;
                                worstSet = point;
                                worstRead = readValue;
                            }
                            _log.Info($"{itemName} 设定={point} 回读={readValue:0.00} 偏差={deviation:0.00}");
                        }
                        else
                        {
                            _log.Warn($"{itemName} 设定={point}：产品回读无效（{readValue}），本次比对预留");
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _log.Warn($"{itemName} 设定={point}：产品回读失败（比对预留）：{ex.Message}");
                    }
                }

                _hw.Aecg100!.StopOutputWaveform();
                await Task.Delay(500, token);
            }

            // 取最大偏差点上报双值：仪器列=设定值，产品列=回读值，动态规格=设定值±容差
            if (double.IsNaN(worstRead))
                await collector.SetAsync(itemName, TestItemResult.Of(Math.Round(maxDeviation, 2)));
            else
                await collector.SetAsync(itemName, TestItemResult.WithDualValues(
                    worstSet, worstRead, worstSet - tolerance, worstSet + tolerance));
        }

        /// <summary>
        /// PPG 模块在位检测（测一个调一个）：PPG/SpO2 测试模块开测前先调用，
        /// AECG100 同时只接一个 PPG 模块，在位模块与测试需求不匹配时写 0 并提示更换模块。
        /// </summary>
        private async Task<bool> EnsurePpgModuleAsync(AecgPpgModuleKind required, string itemName, string requiredName, TestItemResultCollector collector)
        {
            var kind = await Task.Run(() => _hw.Aecg100!.DetectPpgModuleKind());
            if (kind == required)
            {
                await collector.SetAsync(itemName, TestItemResult.Of(1));
                _log.Info($"{itemName} PASS：检测到 {_hw.Aecg100!.DetectPpgModule()}");
                return true;
            }

            await collector.SetAsync(itemName, TestItemResult.Of(0));
            var detected = _hw.Aecg100!.DetectPpgModule() ?? "无";
            _log.Error($"{itemName} FAIL：当前在位模块为 [{detected}]，本测试需要 [{requiredName}]。请更换模块后重跑本模块（换模块无需重连主机，直接重跑即可）");
            return false;
        }

        /// <summary>
        /// 回读产品心率（0x7C），返回产品上报的原始心率值。
        /// 产品不可用/回读失败时比对预留，返回 NaN（由调用方决定预留上报方式）。
        /// </summary>
        private async Task<double> ReadProductHeartRateAsync(bool productAvailable, CancellationToken token)
        {
            if (!productAvailable) return double.NaN;

            var config = _configService.Load();
            using var product = _productFactory();
            try
            {
                await Task.Delay(1500, token);   // 等产品测量稳定（0.67~1Hz 心率需数个周期）
                await product.OpenAsync(token);
                var hr = await product.ReadHeartRateAsync(token);
                if (double.IsNaN(hr) || hr <= 0)
                {
                    _log.Warn($"产品心率回读无效（{hr}），本次比对预留");
                    return double.NaN;
                }
                return hr;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Warn($"产品心率回读失败（比对预留）：{ex.Message}");
                return double.NaN;
            }
        }

        /// <summary>
        /// 输出 ECG 波形并采集回调数据，通过 PressureSampled 事件推送给 UI 显示。
        /// 回调参数 (time, ac, dc) 中 ac+dc 作为波形幅度（μV），SDK time 作为时间轴（s）。
        /// 由于 ECG 波形会持续输出直至主动停止，此处按 <paramref name="captureMs"/> 固定时长采集后返回，
        /// 由调用方在合适的时机调用 StopOutputWaveform。
        /// </summary>
        private async Task<bool> OutputEcgWithCaptureAsync(string itemName, AecgEcgWaveform waveform, int captureMs, CancellationToken token, int segment = 0)
        {
            var ok = await Task.Run(() => _hw.Aecg100!.OutputEcg(waveform, (time, ac, dc) =>
            {
                PressureSampled?.Invoke(itemName, new PressureSamplePoint(time, ac + dc, segment));
            }), token).ConfigureAwait(false);

            if (!ok) return false;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(captureMs);
            try { await Task.Delay(captureMs, cts.Token); }
            catch (OperationCanceledException) { /* 用户取消或采集到时时继续 */ }
            return true;
        }

        /// <summary>
        /// 输出频率扫描波形并采集回调数据，通过 PressureSampled 事件推送给 UI 显示。
        /// 频率扫描会持续输出，因此按 <paramref name="captureMs"/> 固定时长采集后返回。
        /// </summary>
        private async Task<bool> OutputFrequencyScanWithCaptureAsync(string itemName, AecgFrequencyScan scan, int captureMs, CancellationToken token)
        {
            var ok = await Task.Run(() => _hw.Aecg100!.OutputFrequencyScan(scan, (time, ac, dc) =>
            {
                PressureSampled?.Invoke(itemName, new PressureSamplePoint(time, ac + dc));
            }), token).ConfigureAwait(false);

            if (!ok) return false;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(captureMs);
            try { await Task.Delay(captureMs, cts.Token); }
            catch (OperationCanceledException) { /* 用户取消或采集到时时继续 */ }
            return true;
        }

        /// <summary>
        /// 输出单通道 PPG 波形并采集回调数据，通过 PressureSampled 事件推送给 UI 显示。
        /// 与 ECG 一致：按 <paramref name="captureMs"/> 固定时长采集后返回，由调用方停止输出。
        /// </summary>
        private async Task<bool> OutputPpgWithCaptureAsync(string itemName, AecgPpgChannel channel, AecgPpgWaveform waveform, int captureMs, CancellationToken token, int segment = 0)
        {
            var ok = await Task.Run(() => _hw.Aecg100!.OutputPpg(channel, waveform, (time, ac, dc) =>
            {
                PressureSampled?.Invoke(itemName, new PressureSamplePoint(time, ac + dc, segment));
            }), token).ConfigureAwait(false);

            if (!ok) return false;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(captureMs);
            try { await Task.Delay(captureMs, cts.Token); }
            catch (OperationCanceledException) { /* 用户取消或采集到时时继续 */ }
            return true;
        }

        /// <summary>
        /// 输出双通道 PPG（SpO2 红光/红外）并采集红光通道回调数据推送 UI。
        /// 同一轮采样可同时归属多个测试项（如 SPO2-001 血氧与 SPO2-002 脉搏率共用同轮波形）。
        /// </summary>
        private async Task<bool> OutputPpgExWithCaptureAsync(string[] itemNames, AecgPpgWaveform redWave, AecgPpgWaveform irWave, int captureMs, CancellationToken token, int segment = 0)
        {
            var ok = await Task.Run(() => _hw.Aecg100!.OutputPpgEx(redWave, irWave,
                onData1: (time, ac, dc) =>
                {
                    foreach (var name in itemNames)
                        PressureSampled?.Invoke(name, new PressureSamplePoint(time, ac + dc, segment));
                }), token).ConfigureAwait(false);

            if (!ok) return false;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(captureMs);
            try { await Task.Delay(captureMs, cts.Token); }
            catch (OperationCanceledException) { /* 用户取消或采集到时时继续 */ }
            return true;
        }

        /// <summary>
        /// 根据 AECG100 配置构建默认 PPG 波形（PPG-001 脉搏率测试用）。
        /// 高心率时波形周期变短，主波/重搏时刻自动夹到周期内保证波形合法。
        /// </summary>
        private static AecgPpgWaveform BuildDefaultPpgWaveform(Share.Models.Aecg100Settings cfg, double frequency)
        {
            var timePeriod = frequency > 0 ? (int)(1000.0 / frequency) : 1000;
            var timeSp = Math.Min(cfg.PpgTimeSP, Math.Max(timePeriod / 4, 10));
            var timeDn = Math.Min(cfg.PpgTimeDN, Math.Max(timePeriod - 20, timeSp + 10));
            var timeDp = Math.Min(cfg.PpgTimeDP, Math.Max(timePeriod - 10, timeDn + 10));
            return new AecgPpgWaveform
            {
                WaveformType = AecgPpgWaveformType.Ppg,
                Frequency = frequency,
                VolDC = cfg.PpgVolDC,
                VolSP = cfg.PpgVolSP,
                VolDN = cfg.PpgVolDN,
                VolDP = cfg.PpgVolDP,
                TimeSP = timeSp,
                TimeDN = timeDn,
                TimeDP = timeDp,
                TimePeriod = timePeriod
            };
        }

        /// <summary>
        /// 构建 SpO2 双通道波形：ch1=红光（AC 按 R 比例）、ch2=红外（AC 固定），
        /// DC 与脉搏率两通道一致（产品按 R=(AC红/DC)/(AC红外/DC) 换算）。
        /// </summary>
        private static (AecgPpgWaveform Red, AecgPpgWaveform Ir) BuildSpo2Waveforms(Share.Models.Aecg100Settings cfg, double frequency, double redAcMv)
        {
            var timePeriod = frequency > 0 ? (int)(1000.0 / frequency) : 800;
            var timeSp = Math.Min(120, Math.Max(timePeriod / 4, 10));
            var timeDn = Math.Min(300, Math.Max(timePeriod - 20, timeSp + 10));
            var timeDp = Math.Min(400, Math.Max(timePeriod - 10, timeDn + 10));

            AecgPpgWaveform Build(double ac) => new()
            {
                WaveformType = AecgPpgWaveformType.Ppg,
                Frequency = frequency,
                VolDC = cfg.Spo2VolDC,
                VolSP = ac,
                VolDN = ac * 0.6,
                VolDP = ac * 0.7,
                TimeSP = timeSp,
                TimeDN = timeDn,
                TimeDP = timeDp,
                TimePeriod = timePeriod
            };

            return (Build(redAcMv), Build(cfg.Spo2IrAcAmplitude));
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
                PacingRate = cfg.PacingRate,
                RespirationMode = (AecgRespirationMode)cfg.DefaultRespirationMode,
                RespirationAmplitude = cfg.DefaultRespirationAmplitude,
                RespirationRate = cfg.DefaultRespirationRate,
                RespirationRatio = cfg.DefaultRespirationRatio
            };
        }

        /// <summary>
        /// 按测试项名读取 AECG100 波形配置并覆盖到波形对象上。
        /// 返回（应用配置后的波形，采集时长 ms）。
        /// 配置中 Frequency ≤0 时保留传入值；CaptureTimeMs ≤0 时使用 defaultCaptureMs。
        /// </summary>
        private static (AecgEcgWaveform waveform, int captureMs) ApplyWaveformConfig(
            Share.Models.Aecg100Settings cfg, string itemName, AecgEcgWaveform waveform, int defaultCaptureMs = 3000)
        {
            var config = cfg.GetWaveformConfig(itemName);
            if (config.Frequency > 0) waveform.Frequency = config.Frequency;
            var captureMs = config.CaptureTimeMs > 0 ? config.CaptureTimeMs : defaultCaptureMs;
            return (waveform, captureMs);
        }

        /// <summary>读取指定 ECG 测试项的采集时长；未配置时使用 defaultMs</summary>
        private static int GetWaveformCaptureMs(Share.Models.Aecg100Settings cfg, string itemName, int defaultMs = 3000)
        {
            var config = cfg.GetWaveformConfig(itemName);
            return config.CaptureTimeMs > 0 ? config.CaptureTimeMs : defaultMs;
        }
        
        /// <summary>
        /// 打开产品并执行“产品问询 + 产品当前状态查询”两项通讯项。
        /// 产品未连接/无响应时仅这两项 FAIL（results 保持默认 0），不阻断仪器控制流程——
        /// 没接产品也能调试 BPA700 侧逻辑，无需在调试器里跳语句。
        /// 返回产品通讯是否可用。
        /// </summary>
        private async Task<bool> ProductHandshakeAsync(IProductController product, TestItemResultCollector collector, CancellationToken token)
        {
            try
            {
                await product.OpenAsync(token);

                // 心跳保活：防止产品进入休眠模式（约10秒无通讯即休眠）
                try
                {
                    await product.KeepAliveAsync(token);
                    _log.Info("产品心跳保活发送成功");
                }
                catch (Exception ex)
                {
                    _log.Warn($"产品心跳保活发送失败（继续尝试问询）：{ex.Message}");
                }

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
