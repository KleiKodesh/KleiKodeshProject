using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WpfLib.Controls;

namespace RegexFindLib.UI
{
    /// <summary>
    /// Office-styled numeric spinner — TextBox with ▲▼ buttons.
    /// Supports both int (IntValue/Step) and float (FloatValue/FloatStep) modes.
    /// Set IsFloat=True to switch to float mode.
    /// Pure Control — no .xaml file.
    /// Template lives in Themes/SpinnerTextBoxStyles.xaml (merged by RegexFindDictionary.xaml).
    /// </summary>
    [TemplatePart(Name = PartIntBox, Type = typeof(UpDownTextBox))]
    [TemplatePart(Name = PartFloatBox, Type = typeof(UpDownFloatTextBox))]
    [TemplatePart(Name = PartUpBtn, Type = typeof(RepeatButton))]
    [TemplatePart(Name = PartDownBtn, Type = typeof(RepeatButton))]
    public class SpinnerTextBox : Control
    {
        const string PartIntBox = "PART_IntBox";
        const string PartFloatBox = "PART_FloatBox";
        const string PartUpBtn = "PART_UpBtn";
        const string PartDownBtn = "PART_DownBtn";

        UpDownTextBox _intBox;
        UpDownFloatTextBox _floatBox;
        RepeatButton _upBtn;
        RepeatButton _downBtn;

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            // Unwire old
            if (_upBtn != null) _upBtn.Click -= UpBtn_Click;
            if (_downBtn != null) _downBtn.Click -= DownBtn_Click;

            // Get new
            _intBox = GetTemplateChild(PartIntBox) as UpDownTextBox;
            _floatBox = GetTemplateChild(PartFloatBox) as UpDownFloatTextBox;
            _upBtn = GetTemplateChild(PartUpBtn) as RepeatButton;
            _downBtn = GetTemplateChild(PartDownBtn) as RepeatButton;

            // Wire new
            if (_upBtn != null) _upBtn.Click += UpBtn_Click;
            if (_downBtn != null) _downBtn.Click += DownBtn_Click;

            // Sync visibility
            UpdateVisibility();
        }

        // ── IsFloat — switches between int and float mode ─────────────────────
        public static readonly DependencyProperty IsFloatProperty =
            DependencyProperty.Register(nameof(IsFloat), typeof(bool),
                typeof(SpinnerTextBox), new PropertyMetadata(false, OnIsFloatChanged));

        public bool IsFloat
        {
            get => (bool)GetValue(IsFloatProperty);
            set => SetValue(IsFloatProperty, value);
        }

        static void OnIsFloatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SpinnerTextBox s)
                s.UpdateVisibility();
        }

        void UpdateVisibility()
        {
            if (_intBox == null || _floatBox == null) return;
            _intBox.Visibility = IsFloat ? Visibility.Collapsed : Visibility.Visible;
            _floatBox.Visibility = IsFloat ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── IntValue DP ───────────────────────────────────────────────────────
        public static readonly DependencyProperty IntValueProperty =
            DependencyProperty.Register(nameof(IntValue), typeof(int),
                typeof(SpinnerTextBox),
                new FrameworkPropertyMetadata(0,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public int IntValue
        {
            get => (int)GetValue(IntValueProperty);
            set => SetValue(IntValueProperty, value);
        }

        // ── Step (int) ────────────────────────────────────────────────────────
        public static readonly DependencyProperty StepProperty =
            DependencyProperty.Register(nameof(Step), typeof(int),
                typeof(SpinnerTextBox), new PropertyMetadata(1));

        public int Step
        {
            get => (int)GetValue(StepProperty);
            set => SetValue(StepProperty, value);
        }

        // ── FloatValue DP ─────────────────────────────────────────────────────
        public static readonly DependencyProperty FloatValueProperty =
            DependencyProperty.Register(nameof(FloatValue), typeof(float),
                typeof(SpinnerTextBox),
                new FrameworkPropertyMetadata(0f,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public float FloatValue
        {
            get => (float)GetValue(FloatValueProperty);
            set => SetValue(FloatValueProperty, value);
        }

        // ── FloatStep ─────────────────────────────────────────────────────────
        public static readonly DependencyProperty FloatStepProperty =
            DependencyProperty.Register(nameof(FloatStep), typeof(float),
                typeof(SpinnerTextBox), new PropertyMetadata(0.5f));

        public float FloatStep
        {
            get => (float)GetValue(FloatStepProperty);
            set => SetValue(FloatStepProperty, value);
        }

        // ── Min / Max ─────────────────────────────────────────────────────────
        public static readonly DependencyProperty MinValueProperty =
            DependencyProperty.Register(nameof(MinValue), typeof(int),
                typeof(SpinnerTextBox), new PropertyMetadata(int.MinValue));

        public int MinValue
        {
            get => (int)GetValue(MinValueProperty);
            set => SetValue(MinValueProperty, value);
        }

        public static readonly DependencyProperty MaxValueProperty =
            DependencyProperty.Register(nameof(MaxValue), typeof(int),
                typeof(SpinnerTextBox), new PropertyMetadata(int.MaxValue));

        public int MaxValue
        {
            get => (int)GetValue(MaxValueProperty);
            set => SetValue(MaxValueProperty, value);
        }

        // ── Button handlers ───────────────────────────────────────────────────

        void UpBtn_Click(object sender, RoutedEventArgs e)
        {
            if (IsFloat)
                FloatValue += FloatStep;
            else
                IntValue = Clamp(IntValue + Step, MinValue, MaxValue);
        }

        void DownBtn_Click(object sender, RoutedEventArgs e)
        {
            if (IsFloat)
                FloatValue -= FloatStep;
            else
                IntValue = Clamp(IntValue - Step, MinValue, MaxValue);
        }

        static int Clamp(int v, int min, int max) =>
            v < min ? min : v > max ? max : v;
    }
}
