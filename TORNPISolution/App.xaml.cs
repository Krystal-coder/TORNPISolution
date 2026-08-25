using System.Windows;
using TORNPISolution.Interface;
using TORNPISolution.Service;
using TORNPISolution.Service.Devices;
using TORNPISolution.Service.Hardware;

namespace TORNPISolution
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly ILogService _log = new LogService(nameof(App));

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _log.Info("========== 应用启动 ==========");

            // 未捕获异常统一落盘，便于现场问题追溯
            DispatcherUnhandledException += (_, args) =>
            {
                _log.Error("UI 线程未捕获异常", args.Exception);
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 先安全关闭硬件（固高IO模块等），再落盘日志
            HardwareManager.Instance.Dispose();

            // 兜底释放 BPA700：程序关闭即断开对仪器的控制，仪器回到可手动操作状态
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
