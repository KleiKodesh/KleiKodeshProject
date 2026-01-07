using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WpfLib.Controls
{
    public class UpDownFloatTextBox : TextBox
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(float), typeof(UpDownFloatTextBox),
                new FrameworkPropertyMetadata(0f, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

        public float Value
        {
            get => (float)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (UpDownFloatTextBox)d;
            if (float.TryParse(control.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var current) &&
                Math.Abs(current - (float)e.NewValue) < 0.0001)
                return;

            control.Text = ((float)e.NewValue).ToString("0.###", CultureInfo.InvariantCulture);
        }

        public static readonly DependencyProperty StepProperty =
            DependencyProperty.Register(nameof(Step), typeof(float), typeof(UpDownFloatTextBox), new PropertyMetadata(1f));

        public float Step
        {
            get => (float)GetValue(StepProperty);
            set => SetValue(StepProperty, value);
        }

        public UpDownFloatTextBox()
        {
            PreviewTextInput += OnPreviewTextInput;
            PreviewKeyDown += OnPreviewKeyDown;
            TextChanged += OnTextChanged;
            DataObject.AddPastingHandler(this, OnPaste);
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (float.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            {
                Value = val;
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up)
            {
                Value += Step;
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                Value -= Step;
                e.Handled = true;
            }
        }

        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsTextFloatCompatible(e.Text);
        }

        private void OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.Text)) return;

            string pasted = e.SourceDataObject.GetData(DataFormats.Text) as string;
            if (!IsTextFloatCompatible(pasted))
            {
                e.CancelCommand();
            }
        }

        private bool IsTextFloatCompatible(string newText)
        {
            // Simulate what the text will be after insertion
            var proposedText = Text.Remove(SelectionStart, SelectionLength)
                                  .Insert(SelectionStart, newText);

            // Allow empty input or a partial number like "2." or "-2."
            if (string.IsNullOrWhiteSpace(proposedText) ||
                proposedText == "-" ||
                proposedText == "." ||
                proposedText.EndsWith(".") ||
                proposedText == "-.")
            {
                return true;
            }

            return float.TryParse(proposedText, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }

    }
}
