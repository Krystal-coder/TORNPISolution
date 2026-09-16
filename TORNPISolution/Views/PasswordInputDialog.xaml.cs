using System.Windows;

namespace TORNPISolution.Views
{
    public partial class PasswordInputDialog : Window
    {
        public string Username => TxtUsername.Text;
        public string Password => PwdBox.Password;

        public PasswordInputDialog()
        {
            InitializeComponent();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
