using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nakdan
{
    // ═══════════════════════════════════════════════════════════
    //  BOOL → VISIBILITY
    //  Instantiated as a resource in XAML:
    //    <local:BoolToVisibilityConverter x:Key="BoolToVis"/>
    //  Then used as:
    //    Visibility="{Binding IsBusy, Converter={StaticResource BoolToVis}}"
    // ═══════════════════════════════════════════════════════════
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType,
                              object parameter, CultureInfo culture)
            => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType,
                                  object parameter, CultureInfo culture)
            => value is Visibility v && v == Visibility.Visible;
    }

    // ═══════════════════════════════════════════════════════════
    //  DICTA GENRE → DESCRIPTION STRING
    //  Returns the Hebrew description for the currently selected
    //  DictaGenre. Used in XAML:
    //    <local:GenreDescriptionConverter x:Key="GenreDesc"/>
    //    Text="{Binding SelectedGenre.Genre, Converter={StaticResource GenreDesc}}"
    // ═══════════════════════════════════════════════════════════
    public class GenreDescriptionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType,
                              object parameter, CultureInfo culture)
        {
            if (value is DictaGenre genre)
            {
                switch (genre)
                {
                    case DictaGenre.Modern:
                        return "עברית מודרנית: מתאים לרוב הטקסטים";
                    case DictaGenre.Poetry:
                        return "שירה: מיוחד לשירה ולטקסטים פיוטיים";
                    case DictaGenre.Bible:
                        return "מקראי: לטקסטים ממקור מקראי";
                    case DictaGenre.Rabbinic:
                        return "רבני: לספרות הלכתית ורבנית";
                    default:
                        return string.Empty;
                }
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType,
                                  object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    // ═══════════════════════════════════════════════════════════
    //  COLOR → COLOR WITH OPACITY
    //  Applies 55% opacity to a color for section labels.
    //  Used in XAML:
    //    <local:OpacityConverter x:Key="OpacityConverter"/>
    //    Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=UserControl}, Converter={StaticResource OpacityConverter}}"
    // ═══════════════════════════════════════════════════════════
    public class OpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType,
                              object parameter, CultureInfo culture)
        {
            if (value is System.Windows.Media.SolidColorBrush brush)
            {
                var color = brush.Color;
                color.A = (byte)(color.A * 0.55);
                return new System.Windows.Media.SolidColorBrush(color);
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType,
                                  object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
