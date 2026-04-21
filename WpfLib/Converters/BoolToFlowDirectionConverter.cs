namespace WpfLib.Converters
{
    using System;
    using System.Globalization;
    using System.Windows;
    using System.Windows.Data;

    public class BoolToFlowDirectionConverter : IValueConverter
    {
        public bool Invert { get; set; } = false; // Optional: allows inverting logic

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool flag)
            {
                if (Invert)
                    flag = !flag;

                return flag ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            }

            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is FlowDirection direction)
            {
                bool result = direction == FlowDirection.RightToLeft;
                return Invert ? !result : result;
            }

            return DependencyProperty.UnsetValue;
        }
    }

}
