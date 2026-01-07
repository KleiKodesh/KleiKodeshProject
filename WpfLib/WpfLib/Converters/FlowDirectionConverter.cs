using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WpfLib.Converters
{
    public class FlowDirectionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return boolValue ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

            return FlowDirection.LeftToRight;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
