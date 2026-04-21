using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WpfLib.Converters
{
    public class MultiplyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double number && double.TryParse(parameter?.ToString(), out var factor))
                return number * factor;
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }

    public class HeightToCornerRadiusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double height && double.TryParse(parameter?.ToString(), out var factor))
            {
                double radius = height * factor;
                // Return a CornerRadius with radius for all corners or only top-left if needed
                return new CornerRadius(radius);
            }
            return new CornerRadius(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

}
