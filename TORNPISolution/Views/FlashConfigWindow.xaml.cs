using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TORNPISolution.ViewModels;

namespace TORNPISolution.Views
{
    /// <summary>
    /// 烧录配置对话框（菜单：配置 → 烧录配置）：只管两颗芯片的全局参数与串口探测；DataContext 直接用烧录页那份活配置对象，不另建副本。
    /// 内存里改了不等于生效：编排每一轮自己读 flashing.json，必须点保存落盘；保存失败时窗口不关，错误原因显示在窗口下面那一行。
    /// 未保存就关窗（✕ / 关闭 都一样）按盘上那份重建——开烧前会把内存里这份自动落盘，不退回则一次没点保存的误改会被下一轮固化成正式配置。
    /// </summary>
    public partial class FlashConfigWindow : Window
    {
        /// <summary>只有保存成功才算数，其余关窗方式一律当取消</summary>
        private bool _saved;

        public FlashConfigWindow(FlashingViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
        }

        private FlashingViewModel? ViewModel => DataContext as FlashingViewModel;

        /// <summary>保存：落盘成功后关窗；失败留着，错误原因就写在窗口下面那一行</summary>
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SaveConfigFromDialog() != true) return;

            _saved = true;
            Close();
        }

        /// <summary>重新载入：丢掉没保存的改动，按盘上那份重来</summary>
        private void Reload_Click(object sender, RoutedEventArgs e) => ViewModel?.ReloadConfigFromDisk();

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            if (!_saved) ViewModel?.ReloadConfigFromDisk();
            base.OnClosed(e);
        }

        // ==================== 路径浏览 ====================

        private void BrowseWizProApp_Click(object sender, RoutedEventArgs e)
            => Browse((TextBox)WizProAppBox, "上位机程序", "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*");

        private void BrowseWizProProject_Click(object sender, RoutedEventArgs e)
            => Browse((TextBox)WizProProjectBox, "工程文件", "所有文件 (*.*)|*.*");

        private void BrowseFreqchipTool_Click(object sender, RoutedEventArgs e)
            => Browse((TextBox)FreqchipToolBox, "烧录工具", "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*");

        private void BrowseFreqchipFirmware_Click(object sender, RoutedEventArgs e)
            => Browse((TextBox)FreqchipFirmwareBox, "固件文件", "所有文件 (*.*)|*.*");

        /// <summary>开文件框并把选中的路径写进目标文本框；取消时原值不动，避免一次误点清空路径框里现成的路径</summary>
        private void Browse(TextBox target, string title, string filter)
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = filter,
                CheckFileExists = true,
            };

            // 起点定在当前路径所在目录；目录已经不在了就不给（InitialDirectory 填个坏目录会让文件框直接抛异常）
            var directory = SafeDirectory(target.Text);
            if (directory.Length > 0 && System.IO.Directory.Exists(directory)) dialog.InitialDirectory = directory;

            if (dialog.ShowDialog(this) != true) return;

            // 写 Text 就靠 TwoWay 绑定进配置对象，这里不碰配置本身
            target.Text = dialog.FileName;
        }

        private static string SafeDirectory(string? path)
        {
            try
            {
                return string.IsNullOrWhiteSpace(path) ? string.Empty : System.IO.Path.GetDirectoryName(path) ?? string.Empty;
            }
            catch
            {
                // 路径框里是手打的半截内容时别抛：退回默认目录就行
                return string.Empty;
            }
        }
    }
}
