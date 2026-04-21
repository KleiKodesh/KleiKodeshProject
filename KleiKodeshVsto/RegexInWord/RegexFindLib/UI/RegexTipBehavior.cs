using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RegexFindLib.UI
{
    /// <summary>
    /// Attached property that wires a Border's MouseLeftButtonUp to the
    /// nearest RegexPalettePanel.InsertAction with the tip's Symbol.
    /// </summary>
    public static class RegexTipBehavior
    {
        public static readonly DependencyProperty EnableProperty =
            DependencyProperty.RegisterAttached(
                "Enable", typeof(bool), typeof(RegexTipBehavior),
                new PropertyMetadata(false, OnEnableChanged));

        public static bool GetEnable(DependencyObject d) => (bool)d.GetValue(EnableProperty);
        public static void SetEnable(DependencyObject d, bool v) => d.SetValue(EnableProperty, v);

        static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is Button btn)) return;
            if ((bool)e.NewValue)
                btn.Click += OnClick;
            else
                btn.Click -= OnClick;
        }

        static void OnClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe)) return;
            if (!(fe.DataContext is RegexTip tip)) return;
            var panel = FindAncestor<RegexPalettePanel>(fe);
            panel?.InsertAction?.Invoke(tip.Symbol);
        }

        static T FindAncestor<T>(DependencyObject d) where T : DependencyObject
        {
            var p = VisualTreeHelper.GetParent(d);
            while (p != null)
            {
                if (p is T t) return t;
                p = VisualTreeHelper.GetParent(p);
            }
            return null;
        }
    }
}
