using TORNPISolution.Interface;
using TORNPISolution.Service.Hardware;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// 测试项目类 每个模块方法 = 该模块完整测试流程（自包含），一次执行返回全部测试项实测值。
    /// 需现算规格的项用 WithDynamicSpec/WithDualValues 
    /// 结果字典由入口按 Spec 项名初始化，未测到默认 0 = FAIL —— 新增测试项只需改 Spec。
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
        /// 血压模拟仪方案级共享会话（由组合根注入）：RunPlan 已建立时各血压模块复用同一连接，
        /// 避免逐模块开关串口导致康伟达退出/重入 PC 控制模式的恢复窗口失联
        /// </summary>
        private readonly Devices.BloodPressureSimulatorSession _simSession;

        /// <summary>
        /// 设备连接失败通知（模块名 + 失败原因），UI 层订阅后弹出提示。
        /// </summary>
        public event Action<string, string>? DeviceConnectionFailed;

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
        /// 方案级产品通讯状态：本轮 M16 产品通讯初始化握手后的记录（true=可用/false=不可用）；
        /// null = 本轮方案未包含初始化模块。方案开始时由 BeginPlanRun 复位。
        /// </summary>
        private bool? _productCommOk;

        /// <summary>复位方案级状态（MainViewModel.RunPlan 开始时调用）：产品通讯状态回到“初始化模块未执行”</summary>
        public void BeginPlanRun() => _productCommOk = null;

        /// <summary>
        /// 按方案序号执行测试模块
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

                var (moduleName, body, onError) = ResolveModule(module.Id, collector, token);
                await RunModuleAsync(moduleName, body, onError);

                // 自检：模块方法写入了本模块 Spec 里不存在的项名
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
        /// 模块分派表：模块 Id →（日志用模块名、模块方法体）。
        /// </summary>
        private (string Name, Func<Task> Body, Func<Exception, Task>? OnError) ResolveModule(
            string moduleId, TestItemResultCollector collector, CancellationToken token) => moduleId switch
        {
            TestModuleIds.InitInstruments => (nameof(InitInstrumentsModule), () => InitInstrumentsModule(collector, token), null),
            TestModuleIds.BpDeviceInit => (nameof(BpDeviceInitModule), () => BpDeviceInitModule(collector, token), _ => BpDeviceInitFailedAsync(collector)),
            TestModuleIds.NfcFunctionTest => (nameof(NfcFunctionModule), () => NfcFunctionModule(collector, token), null),
            TestModuleIds.BpAccuracyTest => (nameof(BpAccuracyTestModule), () => BpAccuracyTestModule(collector, token), null),
            TestModuleIds.BpStaticAccuracyTest => (nameof(BpStaticAccuracyModule), () => BpStaticAccuracyModule(collector, token), null),
            TestModuleIds.ProductStaticPressureCalibration => (nameof(ProductStaticPressureCalibrationModule), () => ProductStaticPressureCalibrationModule(collector, token), null),
            TestModuleIds.BpDynamicAccuracyTest => (nameof(BpDynamicAccuracyModule), () => BpDynamicAccuracyModule(collector, token), null),
            TestModuleIds.PressureControlTest => (nameof(PressureControlModule), () => PressureControlModule(collector, token), null),
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

        /// <summary>M01 血压仪初始化异常钩子：写 0 并通知 UI 设备连接失败（异常本身已由 RunModuleAsync 记 Error）</summary>
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
    }
}
