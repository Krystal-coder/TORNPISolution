using System.Windows.Controls;

namespace TORNPISolution.Views
{
    /// <summary>
    /// ECG 波形采集页：界面侧只做布局，采样、坐标轴与框选读数都在 EcgCaptureViewModel 与两路 EcgTraceChannel 上。
    /// </summary>
    public partial class EcgCaptureView : UserControl
    {
        public EcgCaptureView()
        {
            InitializeComponent();
        }
    }
}
