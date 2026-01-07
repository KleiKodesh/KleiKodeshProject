using System;
using System.Collections.Generic;
using System.Windows.Data;

namespace WpfLib.Converters
{
    public class ListToStringConverter : IValueConverter
    {
        public string Separator { get; set; } = " \\ ";

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var list = value as List<string>;
            return list != null ? string.Join(Separator, list) + " " : string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var str = value as string;
            return str != null ? str.Split(new[] { Separator }, StringSplitOptions.None) : new string[0];
        }
    }

}
