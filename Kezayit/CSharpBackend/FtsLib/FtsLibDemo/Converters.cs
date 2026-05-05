using System;
using System.Collections.Generic;
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
    /// Attached property that populates a TextBlock's Inlines with highlighted runs.
    /// Usage: local:HighlightBehavior.Text="{Binding Snippet}"
    ///        local:HighlightBehavior.Query="{Binding DataContext.CurrentQuery, RelativeSource={RelativeSource AncestorType=Window}}"
    /// </summary>
    public static class HighlightBehavior
    {
        // ── Text ─────────────────────────────────────────────────────────────

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.RegisterAttached(
                "Text",
                typeof(string),
                typeof(HighlightBehavior),
                new PropertyMetadata(null, OnChanged));

        public static string GetText(DependencyObject obj)  => (string)obj.GetValue(TextProperty);
        public static void   SetText(DependencyObject obj, string value) => obj.SetValue(TextProperty, value);

        // ── Query ─────────────────────────────────────────────────────────────

        public static readonly DependencyProperty QueryProperty =
            DependencyProperty.RegisterAttached(
                "Query",
                typeof(string),
                typeof(HighlightBehavior),
                new PropertyMetadata(null, OnChanged));

        public static string GetQuery(DependencyObject obj)  => (string)obj.GetValue(QueryProperty);
        public static void   SetQuery(DependencyObject obj, string value) => obj.SetValue(QueryProperty, value);

        // ── Change handler ────────────────────────────────────────────────────

        private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var tb = d as TextBlock;
            if (tb == null) return;

            string text  = GetText(tb)  ?? string.Empty;
            string query = GetQuery(tb) ?? string.Empty;

            tb.Inlines.Clear();

            var terms = ExtractTerms(query);
            if (terms.Count == 0 || string.IsNullOrEmpty(text))
            {
                tb.Inlines.Add(new Run(text));
                return;
            }

            string lower = text.ToLowerInvariant();
            int    pos   = 0;

            while (pos < text.Length)
            {
                int bestStart = -1, bestLen = 0;
                foreach (var term in terms)
                {
                    int idx = lower.IndexOf(term, pos, StringComparison.Ordinal);
                    if (idx < 0) continue;
                    if (bestStart < 0 || idx < bestStart || (idx == bestStart && term.Length > bestLen))
                    {
                        bestStart = idx;
                        bestLen   = term.Length;
                    }
                }

                if (bestStart < 0)
                {
                    tb.Inlines.Add(new Run(text.Substring(pos)));
                    break;
                }

                if (bestStart > pos)
                    tb.Inlines.Add(new Run(text.Substring(pos, bestStart - pos)));

                tb.Inlines.Add(new Run(text.Substring(bestStart, bestLen))
                {
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black,
                });

                pos = bestStart + bestLen;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static List<string> ExtractTerms(string query)
        {
            var terms = new List<string>();
            if (string.IsNullOrWhiteSpace(query)) return terms;

            foreach (var word in query.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = word.ToLowerInvariant().Trim();
                if (t.Length > 0 && !terms.Contains(t))
                    terms.Add(t);
            }
            return terms;
        }
    }
}
