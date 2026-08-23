using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WpfLib.AttachedProperties
{
    /// <summary>
    /// Turns an editable ComboBox into a type-to-filter picker.
    ///
    /// Set ComboBoxFilter.IsEnabled="True" on an editable ComboBox and typing
    /// narrows the list to the items that contain what was typed, with the
    /// drop-down opening as soon as the box takes focus. That is the behaviour
    /// people expect from a combo they can type into, and the behaviour the
    /// web front end already has; a plain WPF ComboBox instead jumps the
    /// selection to the first prefix match and leaves the list alone.
    ///
    /// Filtering only, deliberately. It does not touch SelectedItem, so the
    /// binding a caller already has keeps working, and it restores the
    /// unfiltered list whenever the drop-down closes so the next open starts
    /// clean.
    ///
    /// This is a behaviour rather than a style because a style cannot reach
    /// ItemsSource. It lives beside the styles so a consumer that merges the
    /// palette also has this available.
    /// </summary>
    public static class ComboBoxFilter
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(ComboBoxFilter),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject element, bool value) =>
            element.SetValue(IsEnabledProperty, value);

        public static bool GetIsEnabled(DependencyObject element) =>
            (bool)element.GetValue(IsEnabledProperty);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is ComboBox combo)) return;

            combo.Loaded             -= OnLoaded;
            combo.PreviewKeyDown     -= OnPreviewKeyDown;
            combo.GotKeyboardFocus   -= OnGotFocus;
            combo.DropDownClosed     -= OnDropDownClosed;
            Unhook(combo);

            if (!(e.NewValue is bool enabled) || !enabled)
            {
                ClearFilter(combo);
                return;
            }

            combo.Loaded             += OnLoaded;
            combo.PreviewKeyDown     += OnPreviewKeyDown;
            combo.GotKeyboardFocus   += OnGotFocus;
            combo.DropDownClosed     += OnDropDownClosed;
            Hook(combo);
        }

        private static void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!(sender is ComboBox combo)) return;

            // Typing into a combo that cannot be typed into does nothing.
            if (!combo.IsEditable) combo.IsEditable = true;

            Hook(combo);
        }

        /// <summary>
        /// Listen on the editable TextBox rather than on the ComboBox's KeyUp.
        ///
        /// KeyUp was the first attempt and it is subtly wrong: it misses text
        /// that arrives any other way - a paste, an IME commit, an automated
        /// caller setting the value - so the list silently stopped filtering
        /// for anything but literal keystrokes. TextChanged is what "the text
        /// changed" actually means.
        /// </summary>
        private static void Hook(ComboBox combo)
        {
            var box = combo.Template?.FindName("PART_EditableTextBox", combo) as TextBox;
            if (box == null) return;
            box.TextChanged -= OnTextChanged;
            box.TextChanged += OnTextChanged;
        }

        private static void Unhook(ComboBox combo)
        {
            if (combo.Template?.FindName("PART_EditableTextBox", combo) is TextBox box)
                box.TextChanged -= OnTextChanged;
        }

        private static void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!(sender is TextBox box)) return;
            var combo = ItemsControl.ItemsControlFromItemContainer(box) as ComboBox
                        ?? FindCombo(box);
            if (combo == null || !GetIsEnabled(combo)) return;

            ApplyFilter(combo, box.Text ?? string.Empty);

            if (!combo.IsDropDownOpen && box.IsKeyboardFocusWithin)
                combo.IsDropDownOpen = true;
        }

        private static ComboBox FindCombo(DependencyObject from)
        {
            while (from != null && !(from is ComboBox))
                from = System.Windows.Media.VisualTreeHelper.GetParent(from);
            return from as ComboBox;
        }

        /// <summary>Open on focus, so the list is there to filter.</summary>
        private static void OnGotFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is ComboBox combo && !combo.IsDropDownOpen)
                combo.IsDropDownOpen = true;
        }

        /// <summary>Escape abandons the filter; the rest belongs to the list.</summary>
        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!(sender is ComboBox combo) || e.Key != Key.Escape) return;
            ClearFilter(combo);
            combo.IsDropDownOpen = false;
        }

        private static void OnDropDownClosed(object sender, EventArgs e)
        {
            // Leave the list whole for whoever opens it next.
            if (sender is ComboBox combo) ClearFilter(combo);
        }

        private static void ApplyFilter(ComboBox combo, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                ClearFilter(combo);
                return;
            }

            combo.Items.Filter = item => Matches(combo, item, text);
        }

        private static void ClearFilter(ComboBox combo)
        {
            if (combo.Items.Filter != null) combo.Items.Filter = null;
        }

        private static bool Matches(ComboBox combo, object item, string text)
        {
            var candidate = TextOf(combo, item);
            return candidate != null
                   && candidate.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        /// <summary>
        /// What the row actually reads as. DisplayMemberPath when the caller set
        /// one, the ComboBoxItem's content when the items are declared inline,
        /// otherwise ToString.
        /// </summary>
        private static string TextOf(ComboBox combo, object item)
        {
            if (item is ComboBoxItem cbi) return cbi.Content?.ToString();

            if (!string.IsNullOrEmpty(combo.DisplayMemberPath))
            {
                var property = item?.GetType().GetProperty(combo.DisplayMemberPath);
                if (property != null) return property.GetValue(item, null)?.ToString();
            }

            return item?.ToString();
        }
    }
}
