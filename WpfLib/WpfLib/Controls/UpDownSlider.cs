using System;
using System.Windows;
using System.Windows.Controls;
using WpfLib.ViewModels;

namespace WpfLib.Controls
{
    public class UpDownSlider : Slider
    {
        public UpDownSlider()
        {
            IncreaseValueCommand = new RelayCommand(() => IncreaseValue());
            DecreaseValueCommand = new RelayCommand(() => DecreaseValue());
            UpdateStyle();
        }

        // Dependency Property: IncreaseValueCommand
        public RelayCommand IncreaseValueCommand
        {
            get => (RelayCommand)GetValue(IncreaseValueCommandProperty);
            set => SetValue(IncreaseValueCommandProperty, value);
        }

        public static readonly DependencyProperty IncreaseValueCommandProperty =
            DependencyProperty.Register(
                nameof(IncreaseValueCommand),
                typeof(RelayCommand),
                typeof(UpDownSlider),
                new PropertyMetadata(null));

        // Dependency Property: DecreaseValueCommand
        public RelayCommand DecreaseValueCommand
        {
            get => (RelayCommand)GetValue(DecreaseValueCommandProperty);
            set => SetValue(DecreaseValueCommandProperty, value);
        }

        public static readonly DependencyProperty DecreaseValueCommandProperty =
            DependencyProperty.Register(
                nameof(DecreaseValueCommand),
                typeof(RelayCommand),
                typeof(UpDownSlider),
                new PropertyMetadata(null));

        private void IncreaseValue()
        {
            Value = Math.Min(Value + SmallChange, Maximum);
        }

        private void DecreaseValue()
        {
            Value = Math.Max(Value - SmallChange, Minimum);
        }

        private bool CanIncrease() => Value < Maximum;
        private bool CanDecrease() => Value > Minimum;

       
        public static readonly DependencyProperty HorizontalLayoutProperty =
        DependencyProperty.Register(
        nameof(HorizontalLayout),
        typeof(bool),
        typeof(UpDownSlider),
        new PropertyMetadata(true, OnHorizontalLayoutChanged));

        public bool HorizontalLayout
        {
            get => (bool)GetValue(HorizontalLayoutProperty);
            set => SetValue(HorizontalLayoutProperty, value);
        }

        private static void OnHorizontalLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UpDownSlider control)
            {
                control.UpdateStyle();
            }
        }

        private void UpdateStyle()
        {
            var styleKey = HorizontalLayout ? "RightLeftStyle" : "UpDownStyle";

            // Load from pack URI
            var uri = new Uri("pack://application:,,,/WpfLib;component/Dictionaries/UpDownSliderDictionary.xaml", UriKind.Absolute);
            var resourceDictionary = new ResourceDictionary { Source = uri };

            if (resourceDictionary[styleKey] is Style resolvedStyle)
            {
                Style = resolvedStyle;
            }
        }


    }
}
