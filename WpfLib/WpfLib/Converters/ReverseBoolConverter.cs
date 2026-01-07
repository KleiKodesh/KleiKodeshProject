using System;
using System.Globalization;
using System.Windows.Data;

namespace WpfLib.Converters
{
    public class ReverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return !b;

            return Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return !b;

            return Binding.DoNothing;
        }
    }
}
