using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WpfLib.Controls
{
    public class UpDownTextBox : TextBox
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(int), typeof(UpDownTextBox),
                new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

        public int Value
        {
            get => (int)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (UpDownTextBox)d;

            // While the user is typing we let their raw text stand (so multi-digit entry
            // like "15" isn't clobbered by clamping the leading "1"); the display is
            // normalized to the clamped value on commit (LostFocus).
            if (control._suppressTextUpdate)
                return;

            if (int.TryParse(control.Text, out var current) && current == (int)e.NewValue)
                return;

            control.Text = ((int)e.NewValue).ToString();
        }

        public static readonly DependencyProperty StepProperty =
            DependencyProperty.Register(nameof(Step), typeof(int), typeof(UpDownTextBox), new PropertyMetadata(1));

        public int Step
        {
            get => (int)GetValue(StepProperty);
            set => SetValue(StepProperty, value);
        }

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(nameof(Minimum), typeof(int), typeof(UpDownTextBox), new PropertyMetadata(int.MinValue));

        public int Minimum
        {
            get => (int)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(int), typeof(UpDownTextBox), new PropertyMetadata(int.MaxValue));

        public int Maximum
        {
            get => (int)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        private bool _suppressTextUpdate;

        private int Clamp(int value) => Math.Max(Minimum, Math.Min(Maximum, value));

        public UpDownTextBox()
        {
            PreviewTextInput += OnPreviewTextInput;
            PreviewKeyDown += OnPreviewKeyDown;
            TextChanged += OnTextChanged;
            LostFocus += OnLostFocus;
            DataObject.AddPastingHandler(this, OnPaste);
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(Text, out var val))
            {
                // Push the clamped value to the binding, but keep the user's raw text
                // on screen until they commit (see _suppressTextUpdate in OnValueChanged).
                _suppressTextUpdate = true;
                Value = Clamp(val);
                _suppressTextUpdate = false;
            }
        }

        private void OnLostFocus(object sender, RoutedEventArgs e)
        {
            // Normalize the display to the committed, clamped value (also handles an
            // empty/partial entry like "" or "-" by snapping back to the current Value).
            int val = int.TryParse(Text, out var parsed) ? parsed : Value;
            int clamped = Clamp(val);
            Value = clamped;
            Text = clamped.ToString();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up)
            {
                Value = Clamp(Value + Step);
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                Value = Clamp(Value - Step);
                e.Handled = true;
            }
        }

        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsTextNumeric(e.Text);
        }

        private void OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.Text)) return;

            string pasted = e.SourceDataObject.GetData(DataFormats.Text) as string;
            if (!IsTextNumeric(pasted))
            {
                e.CancelCommand();
            }
        }

        private static bool IsTextNumeric(string text)
        {
            foreach (char c in text)
            {
                if (!char.IsDigit(c) && c != '-') return false;
            }
            return true;
        }
    }

}
