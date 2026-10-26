using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TORNPISolution.ViewModels;

namespace TORNPISolution.Views
{
    /// <summary>
    /// 历史数据窗口：左侧轮次列表 + 右侧两个页签（轮次明细 / 区间统计）+ 底部导出/刷新/关闭。
    /// 交互全走绑定命令，代码隐藏只管窗口生命周期。
    /// </summary>
    public partial class HistoryWindow : Window
    {
        public HistoryWindow(HistoryViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();

            // LoadAsync 内部已兜异常，这里只管发起
            Loaded += async (_, _) => await viewModel.LoadAsync();
        }
    }

    /// <summary>通过率(0~1) → 比例条所在列的星宽（ConverterParameter=rest 给剩下那段）：
    /// 条宽按容器实际宽度分比例，写死像素在窗口拖窄时会溢出压到右侧计数</summary>
    public class RatioToBarColumnConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var ratio = value is double d && double.IsFinite(d) ? Math.Clamp(d, 0, 1) : 0;
            if (string.Equals(parameter as string, "rest", StringComparison.OrdinalIgnoreCase)) ratio = 1 - ratio;
            return new GridLength(ratio * 100, GridUnitType.Star);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
