using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RegexFindLib.UI
{
    /// <summary>
    /// Attached property that adds a right-aligned placeholder to any TextBox or ComboBox.
    /// For ComboBox: overlays directly on PART_EditableTextBox after template is applied.
    /// </summary>
    public static class PlaceholderBehavior
    {
        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.RegisterAttached(
                "Placeholder", typeof(string), typeof(PlaceholderBehavior),
                new PropertyMetadata(null, OnPlaceholderChanged));

        public static string GetPlaceholder(DependencyObject obj) =>
            (string)obj.GetValue(PlaceholderProperty);

        public static void SetPlaceholder(DependencyObject obj, string value) =>
            obj.SetValue(PlaceholderProperty, value);

        static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var text = (string)e.NewValue;
            if (d is TextBox tb)
                AttachToTextBox(tb, text);
            else if (d is ComboBox cb)
                AttachToComboBox(cb, text);
        }

        // ── TextBox ───────────────────────────────────────────────────────────

        static void AttachToTextBox(TextBox tb, string placeholder)
        {
            tb.Loaded += (_, __) =>
            {
                EnsureOverlayOnTextBox(tb, placeholder);
                Update(tb, placeholder);
            };
            tb.TextChanged        += (_, __) => Update(tb, placeholder);
            tb.GotKeyboardFocus   += (_, __) => Update(tb, placeholder);
            tb.LostKeyboardFocus  += (_, __) => Update(tb, placeholder);
        }

        static void Update(TextBox tb, string placeholder)
        {
            var ov = GetOverlay(tb);
            if (ov == null) return;
            ov.Text = placeholder;
            ov.Visibility = string.IsNullOrEmpty(tb.Text) && !tb.IsKeyboardFocusWithin
                ? Visibility.Visible : Visibility.Collapsed;
        }

        static void EnsureOverlayOnTextBox(TextBox tb, string placeholder)
        {
            if (GetOverlay(tb) != null) return;
            var grid = GetOrWrapInGrid(tb);
            if (grid == null) return;
            var ov = MakeOverlay(tb.FontSize, tb.FontFamily, tb.Padding);
            grid.Children.Insert(0, ov);
            SetOverlay(tb, ov);
        }

        // ── ComboBox — attach to PART_EditableTextBox directly ────────────────

        static void AttachToComboBox(ComboBox cb, string placeholder)
        {
            cb.Loaded += (_, __) =>
            {
                cb.ApplyTemplate();
                var inner = cb.Template?.FindName("PART_EditableTextBox", cb) as TextBox;
                if (inner == null) return;

                EnsureOverlayOnInnerTextBox(inner, cb, placeholder);
                UpdateCombo(inner, cb, placeholder);

                inner.TextChanged       += (_a, _b) => UpdateCombo(inner, cb, placeholder);
                inner.GotKeyboardFocus  += (_a, _b) => UpdateCombo(inner, cb, placeholder);
                inner.LostKeyboardFocus += (_a, _b) => UpdateCombo(inner, cb, placeholder);
            };
            cb.SelectionChanged  += (_, __) => UpdateComboLazy(cb, placeholder);
            cb.GotKeyboardFocus  += (_, __) => UpdateComboLazy(cb, placeholder);
            cb.LostKeyboardFocus += (_, __) => UpdateComboLazy(cb, placeholder);
        }

        static void UpdateComboLazy(ComboBox cb, string placeholder)
        {
            var inner = cb.Template?.FindName("PART_EditableTextBox", cb) as TextBox;
            if (inner != null) UpdateCombo(inner, cb, placeholder);
        }

        static void UpdateCombo(TextBox inner, ComboBox cb, string placeholder)
        {
            var ov = GetOverlay(cb);
            if (ov == null) return;
            ov.Text = placeholder;
            bool empty = string.IsNullOrEmpty(inner.Text);
            ov.Visibility = empty && !cb.IsKeyboardFocusWithin
                ? Visibility.Visible : Visibility.Collapsed;
        }

        static void EnsureOverlayOnInnerTextBox(TextBox inner, ComboBox cb, string placeholder)
        {
            if (GetOverlay(cb) != null) return;

            // Insert overlay into the inner TextBox's own template Grid
            var grid = GetTemplateRootGrid(inner);
            if (grid == null) return;

            var ov = MakeOverlay(inner.FontSize, inner.FontFamily, inner.Padding);
            grid.Children.Insert(0, ov);
            SetOverlay(cb, ov);  // keyed on the ComboBox
        }

        // ── Overlay factory ───────────────────────────────────────────────────

        static TextBlock MakeOverlay(double fontSize, FontFamily fontFamily, Thickness padding)
        {
            return new TextBlock
            {
                Foreground          = new SolidColorBrush(Color.FromRgb(0x96, 0x94, 0x92)),
                IsHitTestVisible    = false,
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                TextAlignment       = System.Windows.TextAlignment.Right,
                Padding             = new Thickness(0, 0, padding.Right + 4, 0),
                FontSize            = fontSize,
                FontFamily          = fontFamily,
                Visibility          = Visibility.Collapsed
            };
        }

        // ── Visual tree helpers ───────────────────────────────────────────────

        /// <summary>
        /// For a TextBox that is a direct child of a Grid, return that Grid.
        /// If it's not in a Grid, wrap it (not needed for our templates).
        /// </summary>
        static Grid GetOrWrapInGrid(TextBox tb)
        {
            if (VisualTreeHelper.GetChildrenCount(tb) == 0) return null;
            // TextBox template root is a ScrollViewer — walk up to find parent Grid
            var parent = VisualTreeHelper.GetParent(tb);
            if (parent is Grid g) return g;
            return null;
        }

        /// <summary>Returns the root Grid of a control's ControlTemplate.</summary>
        static Grid GetTemplateRootGrid(Control control)
        {
            if (VisualTreeHelper.GetChildrenCount(control) == 0) return null;
            var root = VisualTreeHelper.GetChild(control, 0);
            if (root is Grid g) return g;
            if (VisualTreeHelper.GetChildrenCount(root) > 0)
            {
                var child = VisualTreeHelper.GetChild(root, 0);
                if (child is Grid g2) return g2;
            }
            return null;
        }

        // ── Overlay storage ───────────────────────────────────────────────────

        static readonly DependencyProperty OverlayProperty =
            DependencyProperty.RegisterAttached(
                "Overlay", typeof(TextBlock), typeof(PlaceholderBehavior));

        static TextBlock GetOverlay(DependencyObject d) =>
            (TextBlock)d.GetValue(OverlayProperty);

        static void SetOverlay(DependencyObject d, TextBlock ov) =>
            d.SetValue(OverlayProperty, ov);
    }
}
