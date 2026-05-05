using FtsLibDemo.ViewModels;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace FtsLibDemo.Services
{
    /// <summary>
    /// Builds an RTL-aware HTML page from search results (kept for reference; app uses native WPF rendering).
    /// </summary>
    public sealed class ResultsHtmlService : IResultsHtmlService
    {
        public string Render(IReadOnlyList<SearchResultItem> items, string query)
        {
            var sb = new StringBuilder(items.Count * 200);
            AppendHead(sb, items.Count);
            foreach (var item in items)
            {
                sb.AppendLine("<div class='result'>");
                sb.Append("  <div class='title'>").Append(Encode(item.BookTitle)).AppendLine("</div>");
                sb.Append("  <div class='snippet'>").Append(Encode(item.Snippet)).AppendLine("</div>");
                sb.AppendLine("</div>");
            }
            sb.AppendLine("</div></body></html>");
            return sb.ToString();
        }

        public string RenderEmpty(string message)
        {
            var sb = new StringBuilder();
            AppendHead(sb, 0);
            sb.Append("<p class='empty'>").Append(Encode(message)).AppendLine("</p>");
            sb.AppendLine("</div></body></html>");
            return sb.ToString();
        }

        // ── HTML head + result count header ─────────────────────────

        private static void AppendHead(StringBuilder sb, int count)
        {
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html dir='rtl' lang='he'>");
            sb.AppendLine("<head><meta charset='utf-8'><style>");

            // Reset
            sb.AppendLine("*, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }");

            // Page
            sb.AppendLine("body {");
            sb.AppendLine("  font-family: Arial, 'Segoe UI', sans-serif;");
            sb.AppendLine("  font-size: 14px;");
            sb.AppendLine("  background: #fff;");
            sb.AppendLine("  color: #202124;");
            sb.AppendLine("  direction: rtl;");
            sb.AppendLine("}");

            // Results container
            sb.AppendLine("#results {");
            sb.AppendLine("  max-width: 652px;");
            sb.AppendLine("  margin: 0 auto;");
            sb.AppendLine("  padding: 0 16px 40px;");
            sb.AppendLine("}");

            // Result count bar
            sb.AppendLine(".count {");
            sb.AppendLine("  font-size: 13px;");
            sb.AppendLine("  color: #70757a;");
            sb.AppendLine("  padding: 12px 0 4px;");
            sb.AppendLine("  border-bottom: 1px solid #ebebeb;");
            sb.AppendLine("  margin-bottom: 8px;");
            sb.AppendLine("}");

            // Individual result card
            sb.AppendLine(".result {");
            sb.AppendLine("  padding: 14px 0 4px;");
            sb.AppendLine("  border-bottom: 1px solid #ebebeb;");
            sb.AppendLine("}");
            sb.AppendLine(".result:last-child { border-bottom: none; }");

            // Breadcrumb (green path line)
            sb.AppendLine(".breadcrumb {");
            sb.AppendLine("  font-size: 12px;");
            sb.AppendLine("  color: #202124;");
            sb.AppendLine("  line-height: 1.3;");
            sb.AppendLine("  margin-bottom: 3px;");
            sb.AppendLine("  white-space: nowrap;");
            sb.AppendLine("  overflow: hidden;");
            sb.AppendLine("  text-overflow: ellipsis;");
            sb.AppendLine("}");

            // Title (blue link style — not actually a link)
            sb.AppendLine(".title {");
            sb.AppendLine("  font-size: 18px;");
            sb.AppendLine("  color: #1a0dab;");
            sb.AppendLine("  line-height: 1.3;");
            sb.AppendLine("  margin-bottom: 4px;");
            sb.AppendLine("  cursor: default;");
            sb.AppendLine("}");
            sb.AppendLine(".title:hover { text-decoration: underline; }");

            // Snippet
            sb.AppendLine(".snippet {");
            sb.AppendLine("  font-size: 13px;");
            sb.AppendLine("  color: #4d5156;");
            sb.AppendLine("  line-height: 1.58;");
            sb.AppendLine("  word-break: break-word;");
            sb.AppendLine("}");

            // Highlighted terms
            sb.AppendLine("mark {");
            sb.AppendLine("  background: transparent;");
            sb.AppendLine("  color: #202124;");
            sb.AppendLine("  font-weight: bold;");
            sb.AppendLine("}");

            // Empty state
            sb.AppendLine(".empty {");
            sb.AppendLine("  padding: 48px 0;");
            sb.AppendLine("  color: #70757a;");
            sb.AppendLine("  font-size: 16px;");
            sb.AppendLine("  text-align: center;");
            sb.AppendLine("}");

            sb.AppendLine("</style></head>");
            sb.AppendLine("<body><div id='results'>");

            if (count > 0)
                sb.Append("<div class='count'>").Append($"{count:N0} תוצאות").AppendLine("</div>");
        }

        private static string Encode(string s) => WebUtility.HtmlEncode(s ?? string.Empty);
    }
}
