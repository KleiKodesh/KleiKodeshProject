using BloomSearchEngineLib;
using System;
using System.IO;
using System.Text;

namespace BloomSearchEngineTest
{
    internal static class SearchIndexHelper
    {
        internal static string GenerateHtmlReport(string query, System.Collections.Generic.List<SearchResultItem> results, TimeSpan totalTime, double firstResultTime)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "search_results.html");

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang=\"he\">");
            html.AppendLine("<head>");
            html.AppendLine("    <meta charset=\"UTF-8\">");
            html.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            html.AppendLine($"    <title>Search Results - {EscapeHtml(query)}</title>");
            html.AppendLine("    <style>");
            html.AppendLine("        * { margin: 0; padding: 0; box-sizing: border-box; }");
            html.AppendLine("        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, sans-serif; background: #f5f5f5; padding: 20px; }");
            html.AppendLine("        .container { max-width: 1200px; margin: 0 auto; background: white; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.1); }");
            html.AppendLine("        .header { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 30px; border-radius: 8px 8px 0 0; }");
            html.AppendLine("        .header h1 { font-size: 28px; margin-bottom: 10px; }");
            html.AppendLine("        .query { font-size: 18px; opacity: 0.9; font-weight: 500; }");
            html.AppendLine("        .stats { padding: 20px 30px; background: #f8f9fa; border-bottom: 1px solid #e9ecef; display: flex; gap: 30px; flex-wrap: wrap; }");
            html.AppendLine("        .stat { display: flex; flex-direction: column; }");
            html.AppendLine("        .stat-label { font-size: 12px; color: #6c757d; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 4px; }");
            html.AppendLine("        .stat-value { font-size: 20px; font-weight: 600; color: #212529; }");
            html.AppendLine("        .results { padding: 20px 30px; }");
            html.AppendLine("        .result-item { padding: 20px; margin-bottom: 15px; border: 1px solid #e9ecef; border-radius: 6px; transition: all 0.2s; }");
            html.AppendLine("        .result-item:hover { border-color: #667eea; box-shadow: 0 2px 8px rgba(102, 126, 234, 0.1); }");
            html.AppendLine("        .result-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }");
            html.AppendLine("        .result-id { font-size: 14px; color: #6c757d; font-weight: 500; }");
            html.AppendLine("        .result-scores { display: flex; gap: 15px; }");
            html.AppendLine("        .score-badge { padding: 4px 10px; border-radius: 12px; font-size: 12px; font-weight: 600; }");
            html.AppendLine("        .score-match { background: #d4edda; color: #155724; }");
            html.AppendLine("        .score-proximity { background: #d1ecf1; color: #0c5460; }");
            html.AppendLine("        .result-snippet { font-size: 14px; color: #6c757d; font-style: italic; padding: 12px; background: #f8f9fa; border-left: 3px solid #667eea; border-radius: 4px; }");
            html.AppendLine("        .no-results { text-align: center; padding: 60px 30px; color: #6c757d; }");
            html.AppendLine("        mark { background: #fff3cd; padding: 2px 4px; border-radius: 2px; font-weight: 500; }");
            html.AppendLine("    </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body dir=\"rtl\">");
            html.AppendLine("    <div class=\"container\">");
            html.AppendLine("        <div class=\"header\">");
            html.AppendLine("            <h1>🔍 Search Results</h1>");
            html.AppendLine($"            <div class=\"query\">Query: \"{EscapeHtml(query)}\"</div>");
            html.AppendLine("        </div>");
            html.AppendLine("        <div class=\"stats\">");
            html.AppendLine("            <div class=\"stat\">");
            html.AppendLine("                <div class=\"stat-label\">Total Results</div>");
            html.AppendLine($"                <div class=\"stat-value\">{results.Count}</div>");
            html.AppendLine("            </div>");
            html.AppendLine("            <div class=\"stat\">");
            html.AppendLine("                <div class=\"stat-label\">Total Time</div>");
            html.AppendLine($"                <div class=\"stat-value\">{totalTime.TotalSeconds:0.00} s</div>");
            html.AppendLine("            </div>");

            if (firstResultTime >= 0)
            {
                html.AppendLine("            <div class=\"stat\">");
                html.AppendLine("                <div class=\"stat-label\">First Result</div>");
                html.AppendLine($"                <div class=\"stat-value\">{firstResultTime:0.000} s</div>");
                html.AppendLine("            </div>");
            }

            html.AppendLine("            <div class=\"stat\">");
            html.AppendLine("                <div class=\"stat-label\">Avg per Result</div>");
            html.AppendLine($"                <div class=\"stat-value\">{totalTime.TotalSeconds / results.Count:0.00} s</div>");
            html.AppendLine("            </div>");
            html.AppendLine("        </div>");
            html.AppendLine("        <div class=\"results\">");

            if (results.Count == 0)
            {
                html.AppendLine("            <div class=\"no-results\">");
                html.AppendLine("                <h2>No results found</h2>");
                html.AppendLine("                <p>Try a different search query.</p>");
                html.AppendLine("            </div>");
            }
            else
            {
                foreach (var result in results)
                {
                    html.AppendLine("            <div class=\"result-item\">");
                    html.AppendLine("                <div class=\"result-header\">");
                    html.AppendLine($"                    <div class=\"result-id\">Line ID: {result.LineId}</div>");
                    html.AppendLine("                    <div class=\"result-scores\">");
                    html.AppendLine($"                        <span class=\"score-badge score-match\">Score: {result.Score}</span>");
                    html.AppendLine($"                        <span class=\"score-badge score-proximity\">Proximity: {result.ProximityScore:0.00}</span>");
                    html.AppendLine("                    </div>");
                    html.AppendLine("                </div>");

                    if (!string.IsNullOrEmpty(result.Snippet))
                    {
                        html.AppendLine($"                <div class=\"result-snippet\">{HighlightQuery(EscapeHtml(result.Snippet), query)}</div>");
                    }

                    html.AppendLine("            </div>");
                }
            }

            html.AppendLine("        </div>");
            html.AppendLine("    </div>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");

            File.WriteAllText(tempPath, html.ToString(), Encoding.UTF8);
            return tempPath;
        }

        private static string EscapeHtml(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }

        private static string HighlightQuery(string escapedText, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return escapedText;

            // normalize query same way snippets are normalized
            var normalizedQuery = query.NormalizeText();

            var terms = normalizedQuery
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            var result = escapedText;

            foreach (var term in terms)
            {
                var index = result.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                while (index >= 0)
                {
                    var actual = result.Substring(index, term.Length);
                    result = result.Substring(0, index) +
                             $"<mark>{actual}</mark>" +
                             result.Substring(index + term.Length);

                    index = result.IndexOf(
                        term,
                        index + term.Length + 13,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
            return result;
        }
    }
}
