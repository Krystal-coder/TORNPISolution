using System.Windows;
using TORNPISolution.ViewModels;

namespace TORNPISolution.Views
{
    public partial class ProtocolCommandConfigWindow : Window
    {
        public ProtocolCommandConfigWindow()
        {
            InitializeComponent();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ProtocolCommandConfigViewModel vm) return;

            var dlg = new PasswordInputDialog { Owner = this };
            if (dlg.ShowDialog() != true) return;

            if (!vm.ValidateAndSave(dlg.Username, dlg.Password))
            {
                MessageBox.Show("用户名或密码错误", "验证失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
