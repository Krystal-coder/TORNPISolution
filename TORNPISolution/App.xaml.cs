using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using TORNPISolution.Interface;
using TORNPISolution.Service;
using TORNPISolution.Service.Devices;
using TORNPISolution.Service.Flashing;
using TORNPISolution.Service.Hardware;
using TORNPISolution.ViewModels;
using TORNPISolution.Views;

namespace TORNPISolution
{
    /// <summary>
    /// App.xaml 交互逻辑，同时是 DI 组合根（Composition Root）：全应用唯一的容器构建点。
    /// ViewModel 只保留构造函数注入，由容器解析后交给窗口，XAML 里不自行实例化。
    /// </summary>
    public partial class App : Application
    {
        private readonly ILogService _log = new LogService(nameof(App));

        /// <summary>
        /// 全局服务容器：服务注册集中在 ConfigureServices
        /// </summary>
        public static IServiceProvider Services { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _log.Info("========== 应用启动 ==========");

            Services = ConfigureServices();

            DispatcherUnhandledException += (_, args) =>
            {
                _log.Error("UI 线程未捕获异常", args.Exception);
                // 不置 Handled 时 WPF 会终止进程：正在执行的测试跟着中断，串口句柄与仪器控制权来不及释放。
                // 故记账并提示后置 Handled 让进程存活，操作人员还能保存日志、重跑这一步。
                args.Handled = true;
                MessageBox.Show($"界面操作出现异常，本次操作已中止，程序继续运行：\n{args.Exception.Message}",
                    "异常", MessageBoxButton.OK, MessageBoxImage.Warning);
            };

            // 后台线程（串口打开/回收、设备轮询等）的异常处理
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                _log.Error("后台线程未捕获异常（进程即将终止）", args.ExceptionObject as Exception);
            };

            // ViewModel 从容器解析构造函数注入
            MainWindow = new MainWindow(Services.GetRequiredService<MainViewModel>());
            MainWindow.Show();
        }

        /// <summary>
        /// 服务注册：设备、配置与执行链都是单例，ViewModel 一律 Transient；有生命周期讲究的是下面几条。
        /// — InstrumentConfigService：Save 后同步刷新缓存，执行链路读到的就是最新值
        /// — HardwareManager：注册为与静态 Instance 同一实例（固高/AECG100 全局共享连接）
        /// — TestItem：PressureSampled/DeviceConnectionFailed 事件要求主 ViewModel 与执行方订阅同一实例
        /// — ProductController 走工厂委托而不是单例：产品串口按模块用完即关，多个模块各自开关同一个口
        /// — FlashingViewModel / BoardChannelViewModel 归 MainViewModel 持有：切页、返入口页、关窗收尾都要当闸门使
        /// </summary>
        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            services.AddSingleton<IInstrumentConfigService, InstrumentConfigService>();
            // 烧录工位配置：与测试工位的仪器清单各一份文件，互不影响
            services.AddSingleton<IFlashingConfigService, FlashingConfigService>();
            // 单板工位运行配置（勾选项 + 通道参与）：单独一份 board-test.json
            services.AddSingleton<IBoardTestConfigService, BoardTestConfigService>();
            // 烧录编排与记录：单例只持有配置服务与日志，每轮的状态在方法内自建，不跨轮残留
            services.AddSingleton<IFlashingService, FlashingService>();
            services.AddSingleton<IFlashRecordService, FlashRecordService>();
            services.AddSingleton<IProtocolCommandConfigService, ProtocolCommandConfigService>();
            services.AddSingleton(_ => HardwareManager.Instance);
            services.AddSingleton<ITestModuleService, Spec>();
            // 血压模拟仪方案级共享会话：RunPlan 开始建立/结束关闭，TestItem 各模块借用同一连接
            services.AddSingleton<BloodPressureSimulatorSession>();
            services.AddSingleton<ITestItemService, TestItem>();
            services.AddSingleton<ITestPlanService, TestPlanService>();
            services.AddSingleton<IAppStateService, AppStateService>();
            services.AddSingleton<Func<IProductController>>(sp =>
            {
                var configService = sp.GetRequiredService<IInstrumentConfigService>();
                return () => new ProductController(configService.Load().Product);
            });

            services.AddTransient<FlashingViewModel>();
            services.AddTransient<BoardChannelViewModel>();
            services.AddTransient<MainViewModel>();
            services.AddTransient<InstrumentConfigViewModel>();
            services.AddTransient<BpaParamConfigViewModel>();
            services.AddTransient<Aecg100ParamConfigViewModel>();
            services.AddTransient<ProtocolCommandConfigViewModel>();

            return services.BuildServiceProvider();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 先安全关闭硬件
            try
            {
                HardwareManager.Instance.Dispose();
            }
            catch (Exception ex)
            {
                _log.Error("退出时关闭硬件失败", ex);
            }

            // 释放 BPA700：程序关闭即断开对仪器的控制，仪器回到可手动操作状态
            try
            {
                BPA700Simulator.ReleaseDeviceControl();
            }
            catch (Exception ex)
            {
                _log.Error("退出时释放 BPA700 失败", ex);
            }

            _log.Info("========== 应用退出 ==========");
            LogService.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
