using TORNPISolution.Interface;
using TORNPISolution.Service.Hardware;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// 测试项目类：每个模块方法 = 该模块完整测试流程（自包含），一次执行返回全部测试项实测值。
    /// 结果字典由入口按 Spec 项名初始化，未测到默认 0 = FAIL；新增测试项只改 Spec，需现算规格的项用 WithDynamicSpec/WithDualValues。
    /// </summary>
    public partial class TestItem : ITestItemService
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
        /// 产品控制器工厂：每次测量新建，由组合根注入，绝不能单例化
        /// </summary>
        private readonly Func<IProductController> _productFactory;

        /// <summary>
        /// 血压模拟仪方案级共享会话（由组合根注入）：RunPlan 建立后各血压模块复用同一连接，逐模块开关串口会使康伟达在退出/重入 PC 控制模式的恢复窗口内失联
        /// </summary>
        private readonly Devices.BloodPressureSimulatorSession _simSession;

        /// <summary>
        /// 设备连接失败通知（模块名 + 失败原因），UI 层订阅后弹出提示。
        /// </summary>
        public event Action<string, string>? DeviceConnectionFailed;

        /// <summary>
        /// 人工操作提示（模块名 + 提示文本）：UI 层订阅后弹模态框，本类在返回的 Task 上等测试员点确定（见 PromptOperatorAsync）。
        /// </summary>
        public event Func<string, string, Task>? OperatorPromptRequested;

        /// <summary>
        /// 压力采样点实时上抛（测试项名 + 带相对开始秒数的采样点，供 UI 在对应行下画波形，后台线程触发）
        /// </summary>
        public event Action<string, PressureSamplePoint>? PressureSampled;

        //公用构造函数（依赖由 DI 容器注入，注册见 App.xaml.cs 组合根）
        public TestItem(HardwareManager hardware, IInstrumentConfigService configService, Func<IProductController> productFactory, Devices.BloodPressureSimulatorSession simSession)
        {
            _hw = hardware;
            _configService = configService;
            _productFactory = productFactory;
            _simSession = simSession;
        }

        /// <summary>
        /// 方案级产品通讯状态：A16 产品通讯初始化握手后的记录（true=可用/false=不可用）；null=本轮方案未包含初始化模块，方案开始时由 BeginPlanRun 复位
        /// </summary>
        private bool? _productCommOk;

        /// <summary>复位方案级状态（MainViewModel.RunPlan 开始时调用）：产品通讯状态回到“初始化模块未执行”，并清掉槽位范围以免上一轮单板通道范围渗入整机方案</summary>
        public void BeginPlanRun(IReadOnlyList<int>? slotScope = null)
        {
            _productCommOk = null;
            _slotScope = slotScope is { Count: > 0 } ? slotScope.ToHashSet() : null;
        }

        /// <summary>
        /// 单板“只跑这几路”的作用范围：null 表示整批（所有已启用的槽位）。
        /// </summary>
        private HashSet<int>? _slotScope;

        /// <summary>这个槽位在本轮范围内吗（没限范围就全算）</summary>
        private bool InSlotScope(int slot) => _slotScope == null || _slotScope.Contains(slot);

        /// <summary>把算出来的槽位清单按本轮范围筛一遍：单板右键“只跑通道 3”时，其余七路不驱动也不上报</summary>
        private List<T> WithinSlotScope<T>(IEnumerable<T> rows, Func<T, int> slotOf)
            => _slotScope == null ? rows.ToList() : rows.Where(r => InSlotScope(slotOf(r))).ToList();

        /// <summary>
        /// 按方案序号执行测试模块：按 module.Id 分派到对应模块方法，一次执行完整流程，返回该模块全部测试项结果（测试项名称 → 测试值/动态规格）。
        /// </summary>
        public async Task<Dictionary<string, TestItemResult>> TestItemxx(int index, TestModuleModel module, Func<string, TestItemResult, Task>? onItemCompleted, CancellationToken token)
        {
            _log.Info($"{DateTime.Now} Start calling the TestItem.TestItemxx() method . {Environment.NewLine}");
            _log.Info($"TestItem{index}：{module.Name} 开始执行");
            try
            {
                // 结果字典按 Spec 测试项名称统一初始化（默认测试值 0 = 未测到即 FAIL），模块方法无需再手写项名清单
                var results = module.Items.ToDictionary(i => i.Name, _ => TestItemResult.Of(0));
                // 收集器：每次赋值自动触发 onItemCompleted 回调，UI 逐项回填
                var collector = new TestItemResultCollector(results, onItemCompleted);

                var (moduleName, body, onError) = ResolveModule(module.Id, collector, token);
                await RunModuleAsync(moduleName, body, onError);

                // 自检：目录项清单（Spec）与模块实际上报名单双向对一遍，两边不同源时界面上看不出问题，只能靠这一条日志
                //   ① 模块报了目录里没有的名 → UI 按目录项名取结果，这条上报被静默丢掉（项名拼错、或动态项名生成口径与目录不一致）
                //   ② 目录里有、模块一个字没报 → UI 收尾按读回缺失强制判 FAIL，这里点出漏报的是哪几项
                var unknown = results.Keys.Where(k => module.Items.All(i => i.Name != k)).ToList();
                if (unknown.Count > 0)
                    _log.Error($"TestItem{index}：上报了 Spec 里不存在的结果项 {unknown.Count} 个（不会出现在明细表）：{string.Join("、", unknown)}");

                var unreported = module.Items.Where(i => !collector.WrittenKeys.Contains(i.Name)).Select(i => i.Name).ToList();
                if (unreported.Count > 0)
                    _log.Warn($"TestItem{index}：未上报结果的项目 {unreported.Count} 个（界面按读回缺失判 FAIL）：{string.Join("、", unreported)}");

                _log.Info($"TestItem{index}：{module.Name} 执行完成，共 {results.Count} 项结果");
                return results;
            }
            catch (OperationCanceledException)
            {
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
        /// 模块分派表：模块 Id →（日志用模块名、模块方法体）。
        /// </summary>
        private (string Name, Func<Task> Body, Func<Exception, Task>? OnError) ResolveModule(
            string moduleId, TestItemResultCollector collector, CancellationToken token) => moduleId switch
        {
            TestModuleIds.InitInstruments => (nameof(InitInstrumentsModule), () => InitInstrumentsModule(collector, token), null),
            TestModuleIds.BoardInitInstruments => (nameof(BoardInitInstrumentsModule), () => BoardInitInstrumentsModule(collector, token), null),
            TestModuleIds.BoardBarcodeRead => (nameof(BoardBarcodeModule), () => BoardBarcodeModule(collector, token), null),
            TestModuleIds.BoardBluetoothTest => (nameof(BoardBluetoothModule), () => BoardBluetoothModule(collector, token), null),
            TestModuleIds.BoardPowerOn => (nameof(BoardPowerOnModule), () => BoardPowerOnModule(collector, token), null),
            TestModuleIds.BpDeviceInit => (nameof(BpDeviceInitModule), () => BpDeviceInitModule(collector, token), _ => BpDeviceInitFailedAsync(collector)),
            TestModuleIds.NfcFunctionTest => (nameof(NfcFunctionModule), () => NfcFunctionModule(collector, token), null),
            TestModuleIds.BpAccuracyTest => (nameof(BpAccuracyTestModule), () => BpAccuracyTestModule(collector, token), null),
            TestModuleIds.PressureControlTest => (nameof(PressureControlModule), () => PressureControlModule(collector, token), null),
            TestModuleIds.BpStaticAccuracyTest => (nameof(BpStaticAccuracyModule), () => BpStaticAccuracyModule(collector, token), null),
            TestModuleIds.ProductStaticPressureCalibration => (nameof(ProductStaticPressureCalibrationModule), () => ProductStaticPressureCalibrationModule(collector, token), null),
            TestModuleIds.Jjg692DeflationRateTest => (nameof(Jjg692DeflationRateModule), () => Jjg692DeflationRateModule(collector, token), null),
            TestModuleIds.Jjg692AirtightnessTest => (nameof(Jjg692AirtightnessModule), () => Jjg692AirtightnessModule(collector, token), null),
            TestModuleIds.BpDynamicAccuracyTest => (nameof(BpDynamicAccuracyModule), () => BpDynamicAccuracyModule(collector, token), null),
            TestModuleIds.BpHeartRateAccuracyTest => (nameof(BpHeartRateAccuracyModule), () => BpHeartRateAccuracyModule(collector, token), null),
            TestModuleIds.Aecg100Init => (nameof(Aecg100InitModule), () => Aecg100InitModule(collector, token), null),
            TestModuleIds.EcgAcquisitionTest => (nameof(EcgAcquisitionTestModule), () => EcgAcquisitionTestModule(collector, token), null),
            TestModuleIds.EcgHeartRateTest => (nameof(EcgHeartRateTestModule), () => EcgHeartRateTestModule(collector, token), null),
            TestModuleIds.EcgSignalQualityTest => (nameof(EcgSignalQualityTestModule), () => EcgSignalQualityTestModule(collector, token), null),
            TestModuleIds.PpgHeartRateTest => (nameof(PpgHeartRateTestModule), () => PpgHeartRateTestModule(collector, token), null),
            TestModuleIds.Spo2MeasurementTest => (nameof(Spo2MeasurementModule), () => Spo2MeasurementModule(collector, token), null),
            TestModuleIds.Spo2AccuracyTest => (nameof(Spo2AccuracyModule), () => Spo2AccuracyModule(collector, token), null),
            TestModuleIds.Spo2PerfusionTest => (nameof(Spo2PerfusionModule), () => Spo2PerfusionModule(collector, token), null),
            TestModuleIds.ChargingTest => (nameof(ChargingTestModule), () => ChargingTestModule(collector, token), null),
            TestModuleIds.ProductCommInit => (nameof(ProductCommInitModule), () => ProductCommInitModule(collector, token), null),
            _ => throw new NotSupportedException($"未实现的测试模块：{moduleId}")
        };

        /// <summary>
        /// 模块 Id 在分派表中是否有实现：只走一遍 switch 组 lambda、不执行测试动作，传入空收集器即可判定。
        /// </summary>
        public bool ImplementsModule(string moduleId)
        {
            try { ResolveModule(moduleId, new TestItemResultCollector(new(), null), CancellationToken.None); return true; }
            catch (NotSupportedException) { return false; }
        }

        /// <summary>A01 血压仪初始化异常钩子：写 0 并通知 UI 设备连接失败（异常本身已由 RunModuleAsync 记 Error）</summary>
        private async Task BpDeviceInitFailedAsync(TestItemResultCollector collector)
        {
            await collector.SetAsync(SpecItems.BpSimulatorInit, TestItemResult.Of(0));
            DeviceConnectionFailed?.Invoke("血压仪初始化连接", "设备未连接，请检查串口配置");
        }

        /// <summary>
        /// 模块方法统一执行包装：Start/Ended 日志与异常隔离集中在此处，模块方法只写测试流程本身。
        /// </summary>
        private async Task RunModuleAsync(string moduleName, Func<Task> body, Func<Exception, Task>? onError = null)
        {
            // 模块级追踪上下文：本模块内每一条日志（含产品串口 TX/RX）自动带 [模块名] 前缀，点位/轮次由流程内 TraceScope.Push 追加（各模块分部文件按需调用）
            using (TraceScope.Begin(moduleName))
            {
                _log.Info($"{DateTime.Now} Start calling the TestItem.{moduleName}() method . {Environment.NewLine}");
                try
                {
                    await body();
                }
                catch (OperationCanceledException)
                {
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
        }
    }
}
