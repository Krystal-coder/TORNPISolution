using System.Windows;
using TORNPISolution.ViewModels;

namespace TORNPISolution.Views
{
    /// <summary>
    /// 仪器链接配置对话框：仅负责窗口关闭交互，
    /// 配置的加载与保存全部在 InstrumentConfigViewModel 中完成。
    /// </summary>
    public partial class InstrumentConfigWindow : Window
    {
        public InstrumentConfigWindow(InstrumentConfigViewModel viewModel)
        {
            // DataContext 先于 InitializeComponent 
            DataContext = viewModel;
            InitializeComponent();

            viewModel.CloseRequested += () =>
            {
                DialogResult = true;
                Close();
            };
        }
    }
}
