using System;
using System.Windows.Data;

namespace WpfLib.Converters
{
    public class ArrayToStringConverter : IValueConverter
    {
        public string Separator { get; set; } = " \\ ";

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var array = value as string[];
            return array != null ? string.Join(Separator, array) : string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var str = value as string;
            return str != null ? str.Split(new[] { Separator }, StringSplitOptions.None) : new string[0];
        }
    }

}
