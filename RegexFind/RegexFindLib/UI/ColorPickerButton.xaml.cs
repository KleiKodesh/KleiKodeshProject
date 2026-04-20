using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Media;
using WpfHAlign = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace RegexFindLib.UI
{
    public partial class ColorPickerButton : WpfUserControl
    {
        public ColorPickerButton()
        {
            InitializeComponent();
        }

        // ── SelectedColor DP ──────────────────────────────────────────────────
        public static readonly DependencyProperty SelectedColorProperty =
            DependencyProperty.Register(nameof(SelectedColor), typeof(Color?),
                typeof(ColorPickerButton),
                new FrameworkPropertyMetadata(null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnSelectedColorChanged));

        public Color? SelectedColor
        {
            get => (Color?)GetValue(SelectedColorProperty);
            set => SetValue(SelectedColorProperty, value);
        }

        static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ColorPickerButton btn)
                btn.UpdateSwatch();
        }

        // ── SwatchBrush (read-only, derived from SelectedColor) ───────────────
        static readonly DependencyPropertyKey SwatchBrushKey =
            DependencyProperty.RegisterReadOnly(nameof(SwatchBrush), typeof(Brush),
                typeof(ColorPickerButton), new PropertyMetadata(Brushes.Transparent));

        public static readonly DependencyProperty SwatchBrushProperty = SwatchBrushKey.DependencyProperty;

        public Brush SwatchBrush
        {
            get => (Brush)GetValue(SwatchBrushProperty);
            private set => SetValue(SwatchBrushKey, value);
        }

        void UpdateSwatch()
        {
            SwatchBrush = SelectedColor.HasValue
                ? new SolidColorBrush(SelectedColor.Value)
                : Brushes.Transparent;
        }

        // ── Popup ─────────────────────────────────────────────────────────────
        Popup _popup;

        void SwatchButton_Click(object sender, RoutedEventArgs e)
        {
            if (_popup == null)
                _popup = BuildPopup();
            _popup.IsOpen = true;
        }

        void SelectColor(Color? color)
        {
            SelectedColor = color;
            if (_popup != null) _popup.IsOpen = false;
        }

        Popup BuildPopup()
        {
            // 10 swatches × 27px + margins = ~290px needed for single row
            var root = new StackPanel { Width = 220, Margin = new Thickness(0) };

            // ── Auto / No color row ───────────────────────────────────────────
            var autoNoGrid = new Grid();
            autoNoGrid.ColumnDefinitions.Add(new ColumnDefinition());
            autoNoGrid.ColumnDefinitions.Add(new ColumnDefinition());

            var autoBtn = MakeTextButton("אוטומטי", Colors.Black, isAuto: true);
            autoBtn.Click += (_, __) => SelectColor(Colors.Black);
            Grid.SetColumn(autoBtn, 0);

            var noColorBtn = MakeTextButton("ללא צבע", null, isAuto: false);
            noColorBtn.Click += (_, __) => SelectColor(null);
            Grid.SetColumn(noColorBtn, 1);

            autoNoGrid.Children.Add(autoBtn);
            autoNoGrid.Children.Add(noColorBtn);
            root.Children.Add(autoNoGrid);

            root.Children.Add(MakeSeparator());

            // ── Theme colors ──────────────────────────────────────────────────
            root.Children.Add(MakeSectionLabel("צבעי ערכת נושא"));
            root.Children.Add(MakeColorRow(WordColors.ThemeColors));

            root.Children.Add(MakeSeparator());

            // ── Standard colors ───────────────────────────────────────────────
            root.Children.Add(MakeSectionLabel("צבעים רגילים"));
            root.Children.Add(MakeColorRow(WordColors.StandardColors));

            root.Children.Add(MakeSeparator());

            // ── More colors ───────────────────────────────────────────────────
            var moreBtn = new System.Windows.Controls.Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Height = 29,
                HorizontalContentAlignment = WpfHAlign.Right,
                Cursor = System.Windows.Input.Cursors.Hand,
                Padding = new Thickness(5, 0, 5, 0)
            };
            var moreBtnContent = new StackPanel { Orientation = WpfOrientation.Horizontal };
            moreBtnContent.Children.Add(new TextBlock
            {
                Text = "צבעים נוספים...",
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            });
            moreBtn.Content = moreBtnContent;
            moreBtn.Click += (_, __) => OpenNativeColorPicker();
            root.Children.Add(moreBtn);

            var border = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD1, 0xD1, 0xD1)),
                BorderThickness = new Thickness(1),
                Child = root,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 8, ShadowDepth = 2, Opacity = 0.15
                }
            };

            return new Popup
            {
                StaysOpen = false,
                PlacementTarget = SwatchButton,
                Placement = PlacementMode.Bottom,
                Child = border
            };
        }

        System.Windows.Controls.Button MakeTextButton(string text, Color? previewColor, bool isAuto)
        {
            var btn = new System.Windows.Controls.Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Height = 29,
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalContentAlignment = WpfHAlign.Center
            };
            var sp = new StackPanel { Orientation = WpfOrientation.Horizontal };
            var preview = new Border
            {
                Width = 15, Height = 15, Margin = new Thickness(5),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                BorderThickness = new Thickness(1)
            };
            if (previewColor.HasValue)
                preview.Background = new SolidColorBrush(previewColor.Value);
            sp.Children.Add(preview);
            sp.Children.Add(new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5)
            });
            btn.Content = sp;
            return btn;
        }

        WrapPanel MakeColorRow(System.Collections.Generic.IReadOnlyList<WordColor> colors)
        {
            // 10 swatches × (22px + 2px margin each side) = 10 × 26 = 260px — fits in 220px popup with padding
            var wrap = new WrapPanel
            {
                HorizontalAlignment = WpfHAlign.Right,
                Margin = new Thickness(6, 2, 6, 2),
                Width = 200   // force single row: 10 × 20px = 200px
            };
            foreach (var wc in colors)
            {
                var c = wc.WpfColor;
                var swatch = new Border
                {
                    Width = 18, Height = 18,
                    Margin = new Thickness(1),
                    Background = new SolidColorBrush(c),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                    BorderThickness = new Thickness(0.5),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = wc.Name
                };
                swatch.MouseLeftButtonUp += (_, __) => SelectColor(c);
                wrap.Children.Add(swatch);
            }
            return wrap;
        }

        TextBlock MakeSectionLabel(string text) => new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Margin = new Thickness(3, 0, 0, 0)
        };

        Separator MakeSeparator() => new Separator
        {
            Height = 0.5,
            Margin = new Thickness(0, 3, 0, 3)
        };

        void OpenNativeColorPicker()
        {
            if (_popup != null) _popup.IsOpen = false;

            var dlg = new ColorDialog();
            if (SelectedColor.HasValue)
            {
                var c = SelectedColor.Value;
                dlg.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
            }

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                var dc = dlg.Color;
                SelectedColor = Color.FromRgb(dc.R, dc.G, dc.B);
            }
        }
    }
}
