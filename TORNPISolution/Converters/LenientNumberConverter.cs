using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TORNPISolution.Converters
{
    /// <summary>
    /// 宽容数字转换器：数值 ↔ 文本双向绑定
    /// </summary>
    [ValueConversion(typeof(object), typeof(string))]
    public class LenientNumberConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value?.ToString() ?? string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = (value as string)?.Trim();
            if (string.IsNullOrWhiteSpace(s))
                return DependencyProperty.UnsetValue;

            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out var d) &&
                !double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                return DependencyProperty.UnsetValue;

            if (targetType == typeof(int)) return (int)Math.Round(d);
            if (targetType == typeof(long)) return (long)Math.Round(d);
            if (targetType == typeof(float)) return (float)d;
            return d;
        }
    }
}
