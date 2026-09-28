using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using TORNPISolution.Interface;
using TORNPISolution.Service;
using TORNPISolution.Service.Devices;
using TORNPISolution.Service.Hardware;

namespace TORNPISolution
{
    /// <summary>
    /// Interaction logic for App.xaml。
    /// 同时作为 DI 组合根（Composition Root）：全应用唯一的容器构建点，
    /// XAML 直接实例化的 ViewModel（无参构造）从 <see cref="Services"/> 解析依赖。
    /// </summary>
    public partial class App : Application
    {
        private readonly ILogService _log = new LogService(nameof(App));

        /// <summary>
        /// 全局服务容器（OnStartup 中构建）：服务注册集中在 ConfigureServices，
        /// 其余代码不得自行 new 服务实现（LogService 等叶子工具除外）。
        /// </summary>
        public static IServiceProvider Services { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _log.Info("========== 应用启动 ==========");

            Services = ConfigureServices();

            // 未捕获异常统一落盘，便于现场问题追溯
            DispatcherUnhandledException += (_, args) =>
            {
                _log.Error("UI 线程未捕获异常", args.Exception);
            };
        }

        /// <summary>
        /// 服务注册（生命周期约定）：
        /// — IInstrumentConfigService 单例：Load() 仍每次读盘，配置页修改即时生效，不得加内存缓存；
        /// — HardwareManager 单例：与静态 Instance 同一实例（固高/AECG100 全局共享连接），退出时在 OnExit 显式 Dispose；
        /// — TestItem 单例：PressureSampled/DeviceConnectionFailed 事件要求主 ViewModel 与执行方订阅同一实例；
        /// — ProductController 工厂委托：每次测量新开串口（IDisposable），绝不能注册为单例。
        /// </summary>
        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            services.AddSingleton<IInstrumentConfigService, InstrumentConfigService>();
            services.AddSingleton(_ => HardwareManager.Instance);
            services.AddSingleton<ITestModuleService, Spec>();
            // 血压模拟仪方案级共享会话：RunPlan 开始建立/结束关闭，TestItem 各模块借用同一连接
            services.AddSingleton<BloodPressureSimulatorSession>();
            services.AddSingleton<ITestItemService, TestItem>();
            services.AddSingleton<ITestPlanService, TestPlanService>();
            services.AddSingleton<Func<IProductController>>(sp =>
            {
                var configService = sp.GetRequiredService<IInstrumentConfigService>();
                return () => new ProductController(configService.Load().Product);
            });

            return services.BuildServiceProvider();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 先安全关闭硬件（固高IO模块等），再落盘日志
            HardwareManager.Instance.Dispose();

            // 兜底释放 BPA700 Beta：程序关闭即断开对仪器的控制，仪器回到可手动操作状态
            try
            {
                BPA700BetaSimulator.ReleaseDeviceControl();
            }
            catch (Exception ex)
            {
                _log.Error("退出时释放 BPA700 Beta 失败", ex);
            }

            _log.Info("========== 应用退出 ==========");
            LogService.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
