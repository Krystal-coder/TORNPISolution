using System.Windows;
using TORNPISolution.ViewModels;

namespace TORNPISolution.Views
{
    /// <summary>
    /// 协议命令配置对话框：仅负责密码验证交互与窗口关闭，
    /// 命令码校验、脱敏日志与写文件均在 ProtocolCommandConfigViewModel 中完成。
    /// </summary>
    public partial class ProtocolCommandConfigWindow : Window
    {
        public ProtocolCommandConfigWindow()
        {
            InitializeComponent();

            // 与其他配置窗一致：订阅 CloseRequested，保存成功后给出明确提示并关闭窗口
            if (DataContext is ProtocolCommandConfigViewModel vm)
            {
                vm.CloseRequested += () =>
                {
                    MessageBox.Show(this,
                        "保存成功！\n\n协议命令配置已写入文件并热重载，后续产品串口收发将按新的功能码/命令码执行（无需重启、无需改代码）。",
                        "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = true;
                    Close();
                };
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ProtocolCommandConfigViewModel vm) return;

            var dlg = new PasswordInputDialog { Owner = this };
            if (dlg.ShowDialog() != true) return;

            if (!vm.ValidateAndSave(dlg.Username, dlg.Password))
            {
                MessageBox.Show("用户名或密码错误", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
