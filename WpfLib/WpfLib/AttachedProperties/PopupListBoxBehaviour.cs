using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace WpfLib.AttachedProperties
{
    public static class ListPopupBehaviour
    {
        public static readonly DependencyProperty ListPopupEnableProperty =
             DependencyProperty.RegisterAttached(
                 "ListPopupEnable",
                 typeof(bool),
                 typeof(ListPopupBehaviour),
                 new PropertyMetadata(false, OnTbEnableChanged));

        public static bool GetListPopupEnable(DependencyObject obj) => (bool)obj.GetValue(ListPopupEnableProperty);
        public static void SetListPopupEnable(DependencyObject obj, bool value) => obj.SetValue(ListPopupEnableProperty, value);

        static void OnTbEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(e.NewValue is bool tbEnable) || !tbEnable) return;
            if (d is ListBox lb)
                lb.SelectionChanged += Lb_SelectionChanged;
        }

        private static void Lb_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox lb && lb.SelectedIndex > -1)
            {
                if (lb.Parent is Popup popup)
                    popup.IsOpen = false;
                
                if (lb.SelectedItem is TabItem tabItem && tabItem.Parent is TabControl tabControl
                    && tabControl.Items.Count > 1)
                {
                    tabControl.Items?.Remove(tabItem);
                    tabControl.Items?.Insert(0, tabItem);
                    tabItem.IsSelected = true;
                }
            }
        }
    }
}

