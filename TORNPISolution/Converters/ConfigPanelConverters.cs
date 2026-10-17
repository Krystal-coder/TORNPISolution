using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TORNPISolution.Converters
{
    /// <summary>
    /// 「左侧导航选中项 → 面板可见性」转换器：绑定当前选中的导航 Key，ConverterParameter 填本面板的 Key，
    /// 相等即显示。配置页由横排 TabControl 改成左导航 + 右面板后，每个面板一行绑定即可，
    /// 不必各写一段 Style/DataTrigger。
    /// </summary>
    [ValueConversion(typeof(string), typeof(Visibility))]
    public class NavKeyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string key && parameter is string want && key == want
                ? Visibility.Visible
                : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 「值相等 → bool」双向转换器，给 RadioButton 组直接绑枚举/整型值用：
    /// Convert 判断当前值是否等于 ConverterParameter（IsChecked 由此算出）；
    /// ConvertBack 只在被选中时把 ConverterParameter 反解析回写，取消选中不写（同组必有一个选中，避免互相覆盖）。
    /// 用于波形类型、电极、显示灵敏度这类「一个整数/枚举值、界面上是一组单选」的配置项。
    /// </summary>
    public class ValueEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null && parameter != null && value.ToString() == parameter.ToString();

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not true) return DependencyProperty.UnsetValue;   // 取消选中不回写
            var text = parameter?.ToString() ?? string.Empty;
            if (targetType == typeof(int) || targetType == typeof(long) || targetType == typeof(double))
                return System.Convert.ChangeType(text, targetType, culture);
            return text;
        }
    }

    /// <summary>
    /// 「true → 隐藏」转换器：给“某些项不走这套界面”的分支用（如行详情内嵌波形只对血压类项显示）。
    /// </summary>
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
