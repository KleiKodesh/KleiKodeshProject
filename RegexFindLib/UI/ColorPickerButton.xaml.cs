using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RegexFindLib.UI
{
    public partial class ColorPickerButton : UserControl
    {
        public ColorPickerButton()
        {
            InitializeComponent();
        }

        // ── SelectedColor DP ──────────────────────────────────────────────
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

        // ── SwatchBrush (read-only, derived from SelectedColor) ───────────
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

        // ── Popup ─────────────────────────────────────────────────────────
        Popup _popup;

        void SwatchButton_Click(object sender, RoutedEventArgs e)
        {
            if (_popup == null)
                _popup = BuildPopup();
            _popup.IsOpen = true;
        }

        Popup BuildPopup()
        {
            var panel = new StackPanel { Margin = new Thickness(8) };

            var label = new TextBlock
            {
                Text = "צבע טקסט",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5A)),
                Margin = new Thickness(0, 0, 0, 6)
            };
            panel.Children.Add(label);

            // Standard Office-style palette
            var colors = new[]
            {
                Colors.Black,                          Colors.White,
                Color.FromRgb(0xFF,0x00,0x00),         Color.FromRgb(0xFF,0x99,0x00),
                Color.FromRgb(0xFF,0xFF,0x00),         Color.FromRgb(0x00,0xFF,0x00),
                Color.FromRgb(0x00,0xFF,0xFF),         Color.FromRgb(0x00,0x00,0xFF),
                Color.FromRgb(0x99,0x00,0xFF),         Color.FromRgb(0xFF,0x00,0xFF),
                Color.FromRgb(0xC0,0x00,0x00),         Color.FromRgb(0xFF,0x66,0x00),
                Color.FromRgb(0xFF,0xCC,0x00),         Color.FromRgb(0x00,0x80,0x00),
                Color.FromRgb(0x00,0x80,0x80),         Color.FromRgb(0x00,0x00,0x80),
                Color.FromRgb(0x66,0x00,0x99),         Color.FromRgb(0x80,0x00,0x80),
                Color.FromRgb(0x40,0x40,0x40),         Color.FromRgb(0x80,0x80,0x80),
            };

            var wrap = new WrapPanel { Width = 164 };
            foreach (var color in colors)
            {
                var c = color;
                var swatch = new Border
                {
                    Width = 22, Height = 22,
                    Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(c),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                    BorderThickness = new Thickness(1),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = c.ToString()
                };
                swatch.MouseLeftButtonUp += (_, __) =>
                {
                    SelectedColor = c;
                    if (_popup != null) _popup.IsOpen = false;
                };
                wrap.Children.Add(swatch);
            }
            panel.Children.Add(wrap);

            // Clear button
            var clearBtn = new Button
            {
                Content = "ללא צבע",
                Margin = new Thickness(0, 8, 0, 0),
                Height = 26,
                FontSize = 11,
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            clearBtn.Click += (_, __) =>
            {
                SelectedColor = null;
                if (_popup != null) _popup.IsOpen = false;
            };
            panel.Children.Add(clearBtn);

            var border = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE5, 0xE5)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = panel,
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
    }
}
