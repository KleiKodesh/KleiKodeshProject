using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WpfLib.Gallery
{
    /// <summary>
    /// The gallery window. Two jobs beyond simply rendering the controls.
    ///
    /// The theme switch flips only the window Background and Foreground, which
    /// is exactly what a VSTO task pane host does. Nothing else is touched, so
    /// if a control stops being legible after a switch, that control is holding
    /// a colour it should have inherited or overlaid.
    ///
    /// The swatch strip is built from the palette itself rather than from a
    /// hardcoded list, so a token added to Brushes.xaml shows up here without
    /// anyone remembering to update this file.
    /// </summary>
    public partial class MainWindow : Window
    {
        // The four Office themes, as (window background, window foreground).
        private static readonly (string Bg, string Fg)[] Themes =
        {
            ("#FFFFFFFF", "#FF262626"), // White
            ("#FFF3F3F3", "#FF262626"), // Light Gray
            ("#FF666666", "#FFE6E6E6"), // Dark Gray
            ("#FF262626", "#FFD4D4D4"), // Black
        };

        /// <summary>Section labels, in rail order. Shared with the snapshot renderer.</summary>
        public static readonly string[] SectionNames =
        {
            "Buttons", "Selection", "Text input", "Type",
            "Lists & trees", "Containers", "Indicators", "Menus", "Colour tokens",
        };

        /// <summary>Theme names, in picker order. Shared with the snapshot renderer.</summary>
        public static readonly string[] ThemeNames =
        {
            "Office White", "Office Light Gray", "Office Dark Gray", "Office Black",
        };

        public MainWindow()
        {
            InitializeComponent();
            BuildSwatches();
        }

        /// <summary>
        /// Show the section the rail selected and hide the rest. Each nav item
        /// carries the x:Name of its panel in Tag, so adding a section is one
        /// ListBoxItem plus one panel, with nothing to keep in sync here.
        /// </summary>
        private void OnSectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsInitialized) return;

            var wanted = (Nav.SelectedItem as ListBoxItem)?.Tag as string;
            if (wanted == null) return;

            foreach (UIElement child in Sections.Children)
            {
                var named = child as FrameworkElement;
                if (named == null) continue;
                child.Visibility = named.Name == wanted ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>Switch theme by name, without going through the picker.</summary>
        public void ApplyTheme(string themeName)
        {
            var index = System.Array.IndexOf(ThemeNames, themeName);
            if (index < 0) return;
            ThemePicker.SelectedIndex = index;
            var theme = Themes[index];
            Background = Brush(theme.Bg);
            Foreground = Brush(theme.Fg);
        }

        /// <summary>Show one section by its rail label, without going through the rail.</summary>
        public void ApplySection(string label)
        {
            var index = System.Array.IndexOf(SectionNames, label);
            if (index < 0) return;
            Nav.SelectedIndex = index;

            var wanted = (Nav.SelectedItem as ListBoxItem)?.Tag as string;
            foreach (UIElement child in Sections.Children)
            {
                if (child is FrameworkElement named)
                    child.Visibility = named.Name == wanted ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsInitialized) return;

            var index = ThemePicker.SelectedIndex;
            if (index < 0 || index >= Themes.Length) return;

            var theme = Themes[index];
            Background = Brush(theme.Bg);
            Foreground = Brush(theme.Fg);
        }

        /// <summary>
        /// Read the colour tokens out of the merged palette rather than from a
        /// list kept here.
        ///
        /// There WAS a list here, and it silently went out of date the moment
        /// Brushes.xaml grew: eight new tokens existed and none of them showed
        /// up, while the comment above claimed they would. Enumerating the
        /// dictionary means the gallery cannot drift from the palette.
        /// </summary>
        private void BuildSwatches()
        {
            foreach (var name in BrushTokenNames())
            {
                if (!(TryFindResource(name) is Brush brush)) continue;

                SwatchPanel.Children.Add(new StackPanel
                {
                    Margin = new Thickness(0, 0, 14, 10),
                    Children =
                    {
                        new Border
                        {
                            Width = 96,
                            Height = 34,
                            CornerRadius = new CornerRadius(4),
                            Background = brush,
                            BorderThickness = new Thickness(1),
                            BorderBrush = TryFindResource("BorderBrush") as Brush,
                        },
                        new TextBlock
                        {
                            Text = name,
                            FontSize = 11,
                            Opacity = 0.7,
                            Margin = new Thickness(0, 3, 0, 0),
                        },
                    },
                });
            }
        }

        /// <summary>
        /// Every SolidColorBrush key the palette defines, sorted by name.
        ///
        /// Sorted rather than in declaration order, because a ResourceDictionary
        /// enumerates in hash order and the strip came out shuffled. Sorted is
        /// at least stable between runs, which the baselines need.
        /// </summary>
        private static List<string> BrushTokenNames()
        {
            var brushes = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/WpfLib;component/themes/brushes.xaml"),
            };

            var names = new List<string>();
            foreach (System.Collections.DictionaryEntry entry in brushes)
                if (entry.Value is SolidColorBrush && entry.Key is string key)
                    names.Add(key);

            names.Sort(StringComparer.Ordinal);
            return names;
        }

        private static SolidColorBrush Brush(string hex) =>
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }
}
