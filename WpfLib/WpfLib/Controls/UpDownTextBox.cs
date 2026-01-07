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

        public UpDownTextBox()
        {
            PreviewTextInput += OnPreviewTextInput;
            PreviewKeyDown += OnPreviewKeyDown;
            TextChanged += OnTextChanged;
            DataObject.AddPastingHandler(this, OnPaste);
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(Text, out var val))
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
