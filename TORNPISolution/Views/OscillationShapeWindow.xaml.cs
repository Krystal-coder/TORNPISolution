using System.Windows;
using TORNPISolution.Share.Models;
using TORNPISolution.ViewModels;

namespace TORNPISolution.Views
{
    /// <summary>
    /// Oscillation Shape 包络曲线配置对话框：
    /// 曲线预览（彩色标记 + 图例）+ x 轴八点（含收缩压/舒张压）/ y 轴五点比率滑块调参
    /// </summary>
    public partial class OscillationShapeWindow : Window
    {
        private readonly OscillationShapeViewModel _vm;

        public OscillationShapeWindow(BpaDynamicBpParams model)
        {
            InitializeComponent();

            _vm = new OscillationShapeViewModel(model);
            DataContext = _vm;

            // 任一参数变化即刷新包络曲线预览
            _vm.ParamsChanged += ShapeChart.Refresh;
        }

        /// <summary>确定：参数已实时写回，直接关闭</summary>
        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        /// <summary>取消：还原到打开前的参数</summary>
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _vm.RestoreSnapshot();
            DialogResult = false;
            Close();
        }
    }
}
