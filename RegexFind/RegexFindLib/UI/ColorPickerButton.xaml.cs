using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
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
                    (d, e) => ((ColorPickerButton)d).UpdateSwatch()));

        public Color? SelectedColor
        {
            get => (Color?)GetValue(SelectedColorProperty);
            set => SetValue(SelectedColorProperty, value);
        }

        // ── SwatchBrush (read-only, derived from SelectedColor) ───────────────
        static readonly DependencyPropertyKey SwatchBrushKey =
            DependencyProperty.RegisterReadOnly(nameof(SwatchBrush), typeof(Brush),
                typeof(ColorPickerButton), new PropertyMetadata(Brushes.Transparent));

        public static readonly DependencyProperty SwatchBrushProperty =
            SwatchBrushKey.DependencyProperty;

        public Brush SwatchBrush
        {
            get => (Brush)GetValue(SwatchBrushProperty);
            private set => SetValue(SwatchBrushKey, value);
        }

        void UpdateSwatch() =>
            SwatchBrush = SelectedColor.HasValue
                ? new SolidColorBrush(SelectedColor.Value)
                : Brushes.Transparent;

        // ── Popup — built lazily via ColorPickerPopupBuilder ──────────────────
        Popup _popup;

        void SwatchButton_Click(object sender, RoutedEventArgs e)
        {
            if (_popup == null)
                _popup = ColorPickerPopupBuilder.Build(SwatchButton, SelectColor);
            _popup.IsOpen = true;
        }

        void SelectColor(Color? color)
        {
            SelectedColor = color;
            if (_popup != null) _popup.IsOpen = false;
        }
    }
}
