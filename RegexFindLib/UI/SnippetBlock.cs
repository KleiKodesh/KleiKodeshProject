using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace RegexFindLib.UI
{
    /// <summary>
    /// Renders a snippet string that contains &lt;b&gt;...&lt;/b&gt; tags
    /// (produced by RegexSearchFind.GenerateSnippet) as a WPF TextBlock
    /// with the matched portion highlighted in the accent color.
    /// </summary>
    public class SnippetBlock : TextBlock
    {
        static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x5F, 0xB8));

        public static readonly DependencyProperty SnippetProperty =
            DependencyProperty.Register(nameof(Snippet), typeof(string), typeof(SnippetBlock),
                new PropertyMetadata(null, OnSnippetChanged));

        public string Snippet
        {
            get => (string)GetValue(SnippetProperty);
            set => SetValue(SnippetProperty, value);
        }

        static void OnSnippetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SnippetBlock tb)
                tb.Rebuild(e.NewValue as string);
        }

        void Rebuild(string snippet)
        {
            Inlines.Clear();
            TextWrapping = TextWrapping.Wrap;
            FontSize = 12;

            if (string.IsNullOrEmpty(snippet))
                return;

            // Split on <b>...</b> — GenerateSnippet produces exactly one bold span
            var parts = Regex.Split(snippet, @"<b>(.*?)</b>");
            // parts: [before, match, after]  (length 3 when one <b> tag present)
            for (int i = 0; i < parts.Length; i++)
            {
                if (i % 2 == 0)
                {
                    // plain text
                    if (!string.IsNullOrEmpty(parts[i]))
                        Inlines.Add(new Run(parts[i]));
                }
                else
                {
                    // bold / highlighted match
                    Inlines.Add(new Run(parts[i])
                    {
                        FontWeight = FontWeights.Bold,
                        Foreground = AccentBrush
                    });
                }
            }
        }
    }
}
