using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace FtsLibDemo
{
    /// <summary>Inverts a boolean value.</summary>
    public sealed class InvertBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;
    }

    /// <summary>Converts bool to Visibility, inverted (true → Collapsed, false → Visible).</summary>
    public sealed class BoolToVisibilityInvertedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility v && v == Visibility.Visible ? (object)false : true;
    }

    /// <summary>
    /// Attached property that renders a snippet containing &lt;mark&gt;...&lt;/mark&gt; tags
    /// as bold highlighted Runs inside a TextBlock.
    ///
    /// The snippet produced by SnippetBuilder already has the correct highlight positions —
    /// this just converts those tags to WPF Inlines. No second search pass needed.
    ///
    /// Usage: local:MarkupBehavior.Text="{Binding Snippet, Mode=OneTime}"
    /// </summary>
    public static class MarkupBehavior
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.RegisterAttached(
                "Text",
                typeof(string),
                typeof(MarkupBehavior),
                new PropertyMetadata(null, OnTextChanged));

        public static string GetText(DependencyObject obj)               => (string)obj.GetValue(TextProperty);
        public static void   SetText(DependencyObject obj, string value) => obj.SetValue(TextProperty, value);

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is TextBlock tb)) return;

            string raw = (e.NewValue as string) ?? string.Empty;
            tb.Inlines.Clear();

            // Split on <mark> / </mark> tags (case-insensitive, no attributes).
            // Odd-indexed segments are inside <mark>…</mark>, even-indexed are plain text.
            const string open  = "<mark>";
            const string close = "</mark>";

            int pos       = 0;
            int len       = raw.Length;
            bool inMark   = false;

            while (pos < len)
            {
                string tag    = inMark ? close : open;
                int    tagIdx = raw.IndexOf(tag, pos, StringComparison.OrdinalIgnoreCase);

                string segment = tagIdx < 0
                    ? raw.Substring(pos)
                    : raw.Substring(pos, tagIdx - pos);

                // Decode the two entities the renderer emits (&amp; → & and &gt; → >).
                string text = segment
                    .Replace("&amp;", "&")
                    .Replace("&gt;",  ">");

                if (text.Length > 0)
                {
                    if (inMark)
                        tb.Inlines.Add(new Run(text) { FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
                    else
                        tb.Inlines.Add(new Run(text));
                }

                if (tagIdx < 0) break;

                pos    = tagIdx + tag.Length;
                inMark = !inMark;
            }
        }
    }
}
