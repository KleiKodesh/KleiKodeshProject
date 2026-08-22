namespace WpfLib.Converters
{
    using System;
    using System.Globalization;
    using System.Windows;
    using System.Windows.Data;

    /// <summary>bool -> Visibility (true = Visible, false = Collapsed).</summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public static readonly BoolToVisibilityConverter Instance = new BoolToVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is Visibility v && v == Visibility.Visible;
    }

    /// <summary>bool -> Visibility (true = Collapsed, false = Visible).</summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public static readonly InverseBoolToVisibilityConverter Instance = new InverseBoolToVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is Visibility v && v == Visibility.Collapsed;
    }

    /// <summary>int -> Visibility (0 = Collapsed, greater than 0 = Visible).</summary>
    public class IntToVisibilityConverter : IValueConverter
    {
        public static readonly IntToVisibilityConverter Instance = new IntToVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is int i && i > 0 ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
