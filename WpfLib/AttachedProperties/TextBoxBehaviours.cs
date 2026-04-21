using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace WpfLib.ComboBoxThemeBehavior
{
    public static class TextBoxBehaviours
    {
        public static readonly DependencyProperty TbEnableProperty =
            DependencyProperty.RegisterAttached(
                "TbEnable",
                typeof(bool),
                typeof(TextBoxBehaviours),
                new PropertyMetadata(false, OnTbEnableChanged));

        public static bool GetTbEnable(DependencyObject obj) => (bool)obj.GetValue(TbEnableProperty);
        public static void SetTbEnable(DependencyObject obj, bool value) => obj.SetValue(TbEnableProperty, value);

        static void OnTbEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(e.NewValue is bool tbEnable) || !tbEnable) return;

            if (d is TextBox tb)
            {
                tb.TextChanged += (s, _) =>
                {
                    if (!string.IsNullOrEmpty(tb.Text))
                        tb.FlowDirection = GetFlow(tb.Text, tb.FlowDirection);
                };
            }
            else if (d is ComboBox cb)
            {
                cb.Loaded += (s, x) =>
                {
                    if (cb.Template != null)
                    {
                        cb.ApplyTemplate(); // Ensure template is applied
                        var textBox = cb.Template.FindName("PART_EditableTextBox", cb) as TextBox;
                        textBox.Style = (Style)cb.FindResource("WaterMarkTextBox");
                        textBox.ToolTip = cb.Tag;
                        textBox.TextChanged += (y, _) =>
                        {
                            if (!string.IsNullOrEmpty(textBox.Text))
                                textBox.FlowDirection = GetFlow(textBox.Text, textBox.FlowDirection);
                        };
                    }
                };
            }
        }

        static FlowDirection GetFlow(string text, FlowDirection defaultFlow)
        {
            if (Regex.IsMatch(text, @"\w"))
                return Regex.IsMatch(text, @"\p{IsHebrew}|\p{IsArabic}")
                    ? FlowDirection.RightToLeft
                    : FlowDirection.LeftToRight;
            else return defaultFlow;
        }

    }
}
