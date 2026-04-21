using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;
using WpfControl = System.Windows.Controls.Control;

namespace RegexFindLib.UI
{
    /// <summary>
    /// Word-style color picker button. Pure Control — no .xaml file.
    /// Template lives in Themes/ColorPickerStyles.xaml (merged by RegexFindDictionary.xaml).
    /// </summary>
    [TemplatePart(Name = PartButton, Type = typeof(WpfButton))]
    [TemplatePart(Name = PartPopup,  Type = typeof(Popup))]
    public class ColorPickerButton : WpfControl
    {
        const string PartButton = "PART_Button";
        const string PartPopup  = "PART_Popup";

        public ColorPickerButton()
        {
            UpdateSwatch();  // Initialize swatch on construction
            SelectColorCommand = new RelayCommand<Color?>(ExecuteSelectColor);
            OpenNativePickerCommand = new RelayCommand(ExecuteOpenNativePicker);
        }

        // ── Color collections — exposed for XAML ItemsControl binding ─────────
        public IReadOnlyList<WordColor> ThemeColors    => WordColors.ThemeColors;
        public IReadOnlyList<WordColor> StandardColors => WordColors.StandardColors;

        // ── SelectedColor DP — WPF Color?, null = no color ───────────────────
        public static readonly DependencyProperty SelectedColorProperty =
            DependencyProperty.Register(nameof(SelectedColor), typeof(Color?),
                typeof(ColorPickerButton),
                new FrameworkPropertyMetadata(null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    (d, _) => ((ColorPickerButton)d).UpdateSwatch()));

        public Color? SelectedColor
        {
            get => (Color?)GetValue(SelectedColorProperty);
            set => SetValue(SelectedColorProperty, value);
        }

        // ── SwatchBrush — read-only, derived from SelectedColor ───────────────
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

        // ── Template wiring ───────────────────────────────────────────────────
        WpfButton _button;
        Popup     _popup;

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (_button != null) _button.Click -= OnButtonClick;

            _button = GetTemplateChild(PartButton) as WpfButton;
            _popup  = GetTemplateChild(PartPopup)  as Popup;

            if (_button != null) _button.Click += OnButtonClick;
        }

        void OnButtonClick(object sender, RoutedEventArgs e)
        {
            if (_popup != null) _popup.IsOpen = true;
        }

        // ── Commands for template binding ─────────────────────────────────────
        public ICommand SelectColorCommand { get; }
        public ICommand OpenNativePickerCommand { get; }

        void ExecuteSelectColor(Color? color)
        {
            SelectedColor = color;
            if (_popup != null) _popup.IsOpen = false;
        }

        void ExecuteOpenNativePicker()
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

        // ── Simple RelayCommand implementation ────────────────────────────────
        class RelayCommand : ICommand
        {
            readonly Action _execute;
            public RelayCommand(Action execute) => _execute = execute;
            public event EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object parameter) => true;
            public void Execute(object parameter) => _execute();
        }

        class RelayCommand<T> : ICommand
        {
            readonly Action<T> _execute;
            public RelayCommand(Action<T> execute) => _execute = execute;
            public event EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object parameter) => true;
            public void Execute(object parameter) => _execute((T)parameter);
        }
    }
}
