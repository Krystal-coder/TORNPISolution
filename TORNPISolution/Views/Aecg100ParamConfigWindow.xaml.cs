using System.Windows;
using TORNPISolution.ViewModels;

namespace TORNPISolution.Views
{
    /// <summary>
    /// ECG 参数配置对话框：仅负责窗口关闭交互，
    /// 参数的加载与保存全部在 Aecg100ParamConfigViewModel 中完成。
    /// </summary>
    public partial class Aecg100ParamConfigWindow : Window
    {
        public Aecg100ParamConfigWindow()
        {
            InitializeComponent();

            if (DataContext is Aecg100ParamConfigViewModel vm)
            {
                vm.CloseRequested += () =>
                {
                    DialogResult = true;
                    Close();
                };
            }
        }
    }
}
