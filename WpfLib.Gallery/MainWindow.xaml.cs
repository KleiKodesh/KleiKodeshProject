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

        private static readonly string[] TokenNames =
        {
            "BgSecBrush", "BgTerBrush", "HoverBrush", "PressedBrush",
            "BorderBrush", "BorderStrong", "AccentBrush", "AccentHover",
            "AccentPressed", "SelectedBrush", "TextSecBrush",
        };

        public MainWindow()
        {
            InitializeComponent();
            BuildSwatches();
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

        private void BuildSwatches()
        {
            foreach (var name in TokenNames)
            {
                var brush = TryFindResource(name) as Brush;
                if (brush == null) continue;

                SwatchPanel.Children.Add(new StackPanel
                {
                    Margin = new Thickness(0, 0, 14, 10),
                    Children =
                    {
                        new Border
                        {
                            Width = 96,
                            Height = 34,
                            CornerRadius = new CornerRadius(3),
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

        private static SolidColorBrush Brush(string hex) =>
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }
}
