using System;
using System.Windows.Data;

namespace WpfLib.Converters
{
    public class StringToBoolConverter : IValueConverter
    {
        public string Separator { get; set; } = " \\ ";

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return !string.IsNullOrWhiteSpace((string)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

}
