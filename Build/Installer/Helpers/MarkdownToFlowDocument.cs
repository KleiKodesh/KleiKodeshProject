using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace KleiKodeshVstoInstallerWpf
{
    /// <summary>
    /// Minimal Markdown → FlowDocument for the LLM-written release notes.
    /// Supports exactly: "### Heading" lines (any 1-6 '#'), "- " / "* " bullets,
    /// **bold** and `code` inline, plain paragraphs (consecutive lines join), blank lines.
    /// Anything else is shown as plain text.
    /// </summary>
    public static class MarkdownToFlowDocument
    {
        static readonly Regex HeadingRx = new Regex(@"^\s{0,3}(#{1,6})\s+(.*?)\s*#*\s*$");
        static readonly Regex BulletRx  = new Regex(@"^\s*[-*]\s+(.*)$");
        static readonly Regex InlineRx  = new Regex(@"\*\*(.+?)\*\*|`([^`]+)`");

        static readonly Brush TextBrush  = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
        static readonly Brush CodeBack   = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));

        public static FlowDocument Convert(string markdown, FlowDirection direction)
        {
            var doc = new FlowDocument
            {
                FlowDirection = direction,
                FontFamily    = new FontFamily("Segoe UI"),
                FontSize      = 13,
                Foreground    = TextBrush,
                PagePadding   = new Thickness(0),
                TextAlignment = TextAlignment.Left, // Left = start edge; mirrored under RTL
            };

            List list = null;
            Paragraph para = null;

            foreach (string raw in (markdown ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.TrimEnd();

                if (line.Trim().Length == 0)
                {
                    list = null; para = null;
                    continue;
                }

                var h = HeadingRx.Match(line);
                if (h.Success)
                {
                    list = null; para = null;
                    var hp = new Paragraph { FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 4) };
                    AddInlines(hp.Inlines, h.Groups[2].Value);
                    doc.Blocks.Add(hp);
                    continue;
                }

                var b = BulletRx.Match(line);
                if (b.Success)
                {
                    para = null;
                    if (list == null)
                    {
                        list = new List { MarkerStyle = TextMarkerStyle.Disc, Margin = new Thickness(0, 2, 0, 6), Padding = new Thickness(18, 0, 0, 0) };
                        doc.Blocks.Add(list);
                    }
                    var ip = new Paragraph { Margin = new Thickness(0, 0, 0, 3) };
                    AddInlines(ip.Inlines, b.Groups[1].Value);
                    list.ListItems.Add(new ListItem(ip));
                    continue;
                }

                list = null;
                if (para == null)
                {
                    para = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
                    doc.Blocks.Add(para);
                }
                else
                {
                    para.Inlines.Add(new Run(" "));
                }
                AddInlines(para.Inlines, line.Trim());
            }

            return doc;
        }

        static void AddInlines(InlineCollection inlines, string text)
        {
            int pos = 0;
            foreach (Match m in InlineRx.Matches(text))
            {
                if (m.Index > pos) inlines.Add(new Run(text.Substring(pos, m.Index - pos)));

                if (m.Groups[1].Success)
                    inlines.Add(new Bold(new Run(m.Groups[1].Value)));
                else
                    inlines.Add(new Run(m.Groups[2].Value) { FontFamily = new FontFamily("Consolas"), Background = CodeBack });

                pos = m.Index + m.Length;
            }
            if (pos < text.Length) inlines.Add(new Run(text.Substring(pos)));
        }
    }
}
