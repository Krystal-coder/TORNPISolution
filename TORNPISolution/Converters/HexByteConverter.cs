using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TORNPISolution.Converters
{
    /// <summary>byte ↔ "0x72" 双向转换器，用于 DataGrid 中命令码的十六进制编辑</summary>
    [ValueConversion(typeof(byte), typeof(string))]
    public class HexByteConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is byte b)
                return $"0x{b:X2}";
            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = (value as string)?.Trim();
            if (string.IsNullOrWhiteSpace(s))
                return DependencyProperty.UnsetValue;

            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                s = s[2..];

            if (byte.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                return b;

            return DependencyProperty.UnsetValue;
        }
    }
}
