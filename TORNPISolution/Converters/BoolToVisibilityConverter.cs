using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TORNPISolution.Converters
{
    /// <summary>
    /// bool → Visibility 转换器（可反向）。
    /// true → Visible（parameter 为 "invert" 时 → Collapsed），false 反之。
    /// 用于按设备页签隐藏无效配置项
    /// </summary>
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var flag = value is bool b && b;
            if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
                flag = !flag;
            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
