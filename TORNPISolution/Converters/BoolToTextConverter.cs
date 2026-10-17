using System.Globalization;
using System.Windows.Data;

namespace TORNPISolution.Converters
{
    /// <summary>
    /// bool → 文本转换器：parameter 为 "A|B" 时，true 取 A、false 取 B（供同一模板按设备页签切换文案）。
    /// </summary>
    [ValueConversion(typeof(bool), typeof(string))]
    public class BoolToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var parts = ((parameter as string) ?? string.Empty).Split('|');
            if (parts.Length != 2) return string.Empty;
            var flag = value is bool b && b;
            return flag ? parts[0] : parts[1];
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
