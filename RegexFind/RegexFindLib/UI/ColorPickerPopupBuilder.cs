using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Media;
using WpfButton    = System.Windows.Controls.Button;
using WpfHAlign    = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace RegexFindLib.UI
{
    /// <summary>
    /// Builds the Word-style color picker popup for ColorPickerButton.
    /// Extracted to keep ColorPickerButton.xaml.cs under 200 lines.
    /// </summary>
    internal static class ColorPickerPopupBuilder
    {
        internal static Popup Build(
            UIElement placementTarget,
            Action<Color?> onColorSelected)
        {
            var root = new StackPanel { Width = 220 };

            // Auto / No color
            var autoNoGrid = new Grid();
            autoNoGrid.ColumnDefinitions.Add(new ColumnDefinition());
            autoNoGrid.ColumnDefinitions.Add(new ColumnDefinition());

            var autoBtn = MakeTextButton("אוטומטי", Colors.Black);
            autoBtn.Click += (_, __) => onColorSelected(Colors.Black);
            Grid.SetColumn(autoBtn, 0);

            var noColorBtn = MakeTextButton("ללא צבע", null);
            noColorBtn.Click += (_, __) => onColorSelected(null);
            Grid.SetColumn(noColorBtn, 1);

            autoNoGrid.Children.Add(autoBtn);
            autoNoGrid.Children.Add(noColorBtn);
            root.Children.Add(autoNoGrid);
            root.Children.Add(MakeSeparator());

            root.Children.Add(MakeSectionLabel("צבעי ערכת נושא"));
            root.Children.Add(MakeColorRow(WordColors.ThemeColors, onColorSelected));
            root.Children.Add(MakeSeparator());

            root.Children.Add(MakeSectionLabel("צבעים רגילים"));
            root.Children.Add(MakeColorRow(WordColors.StandardColors, onColorSelected));
            root.Children.Add(MakeSeparator());

            var moreBtn = new WpfButton
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Height = 29, HorizontalContentAlignment = WpfHAlign.Right,
                Cursor = System.Windows.Input.Cursors.Hand,
                Padding = new Thickness(5, 0, 5, 0)
            };
            var moreSp = new StackPanel { Orientation = WpfOrientation.Horizontal };
            moreSp.Children.Add(new TextBlock
            {
                Text = "צבעים נוספים...",
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)),
                VerticalAlignment = VerticalAlignment.Center
            });
            moreBtn.Content = moreSp;

            Popup popup = null;
            moreBtn.Click += (_, __) => OpenNativeColorPicker(ref popup, onColorSelected);
            root.Children.Add(moreBtn);

            popup = new Popup
            {
                StaysOpen = false,
                PlacementTarget = placementTarget,
                Placement = PlacementMode.Bottom,
                Child = new Border
                {
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xD1, 0xD1, 0xD1)),
                    BorderThickness = new Thickness(1),
                    Child = root,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                        { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.15 }
                }
            };
            return popup;
        }

        static WpfButton MakeTextButton(string text, Color? previewColor)
        {
            var btn = new WpfButton
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Height = 29, Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalContentAlignment = WpfHAlign.Center
            };
            var sp = new StackPanel { Orientation = WpfOrientation.Horizontal };
            var preview = new Border
            {
                Width = 15, Height = 15, Margin = new Thickness(5),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                BorderThickness = new Thickness(1),
                Background = previewColor.HasValue
                    ? (Brush)new SolidColorBrush(previewColor.Value)
                    : Brushes.Transparent
            };
            sp.Children.Add(preview);
            sp.Children.Add(new TextBlock
                { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5) });
            btn.Content = sp;
            return btn;
        }

        static WrapPanel MakeColorRow(
            System.Collections.Generic.IReadOnlyList<WordColor> colors,
            Action<Color?> onColorSelected)
        {
            var wrap = new WrapPanel
            {
                HorizontalAlignment = WpfHAlign.Right,
                Margin = new Thickness(6, 2, 6, 2),
                Width = 200
            };
            foreach (var wc in colors)
            {
                var c = wc.WpfColor;
                var swatch = new Border
                {
                    Width = 18, Height = 18, Margin = new Thickness(1),
                    Background = new SolidColorBrush(c),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                    BorderThickness = new Thickness(0.5),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = wc.Name
                };
                swatch.MouseLeftButtonUp += (_, __) => onColorSelected(c);
                wrap.Children.Add(swatch);
            }
            return wrap;
        }

        static TextBlock MakeSectionLabel(string text) => new TextBlock
            { Text = text, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(3, 0, 0, 0) };

        static Separator MakeSeparator() => new Separator
            { Height = 0.5, Margin = new Thickness(0, 3, 0, 3) };

        static void OpenNativeColorPicker(ref Popup popup, Action<Color?> onColorSelected)
        {
            if (popup != null) popup.IsOpen = false;
            var dlg = new ColorDialog();
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                var dc = dlg.Color;
                onColorSelected(Color.FromRgb(dc.R, dc.G, dc.B));
            }
        }
    }
}
