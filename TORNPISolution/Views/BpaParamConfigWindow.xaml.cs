using System.Windows;
using TORNPISolution.ViewModels;

namespace TORNPISolution.Views
{
    /// <summary>
    /// 血压仪参数配置对话框：仅负责窗口关闭交互，
    /// 参数的加载与保存全部在 BpaParamConfigViewModel 中完成。
    /// </summary>
    public partial class BpaParamConfigWindow : Window
    {
        public BpaParamConfigWindow(BpaParamConfigViewModel viewModel)
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

        /// <summary>动态血压方法 = Customized 时，点包络曲线图标打开 Oscillation Shape 配置页</summary>
        private void OpenOscillationShape_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is BpaParamConfigViewModel vm)
            {
                var dialog = new OscillationShapeWindow(vm.Params.DynamicBloodPressure) { Owner = this };
                dialog.ShowDialog();
            }
        }
    }
}
