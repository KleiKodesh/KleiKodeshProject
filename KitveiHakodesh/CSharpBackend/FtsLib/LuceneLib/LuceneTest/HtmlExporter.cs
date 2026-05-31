using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LuceneLib.Search;
using LuceneLib.SeforimDb;

namespace LuceneTest
{
    /// <summary>
    /// Exports search results to an HTML file for viewing in a browser.
    /// </summary>
    internal static class HtmlExporter
    {
        public static void ExportResults(string queryText, string outputPath = null, string dbPath = null)
        {
            if (outputPath == null)
                outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "search_results.html");

            Console.WriteLine($"=== EXPORT RESULTS TO HTML ===");
            Console.WriteLine($"Query: {queryText}");
            Console.WriteLine($"Output: {outputPath}");

            var indexDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lucene_index");
            if (!Directory.Exists(indexDir))
            {
                Console.WriteLine("Index not found — run build first.");
                return;
            }

            using (var searcher = new LuceneLib.Search.LuceneSearcher(indexDir))
            using (var db = new ZayitDb(dbPath))
            {
                if (!db.IsOpen)
                {
                    Console.WriteLine("Database not found.");
                    return;
                }

                var results = new List<(int RowId, string Snippet)>();
                var sw = System.Diagnostics.Stopwatch.StartNew();

                foreach (var (rowId, snippet) in searcher.SearchWithSnippets(
                    queryText,
                    id => db.GetLineById(id),
                    preTag: "<mark>",
                    postTag: "</mark>",
                    batchSize: 100,
                    maxDistance: int.MaxValue))
                {
                    results.Add((rowId, snippet.Html ?? ""));
                }

                sw.Stop();

                // Generate HTML
                string html = GenerateHtml(queryText, results, sw.Elapsed);
                File.WriteAllText(outputPath, html, Encoding.UTF8);

                Console.WriteLine($"Exported {results.Count} results in {sw.Elapsed.TotalSeconds:F1}s");
                Console.WriteLine($"HTML file saved to: {outputPath}");
            }
        }

        private static string GenerateHtml(string query, List<(int RowId, string Snippet)> results, TimeSpan elapsed)
        {
            var sb = new StringBuilder();

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang='he' dir='rtl'>");
            sb.AppendLine("<head>");
            sb.AppendLine("  <meta charset='utf-8'>");
            sb.AppendLine("  <meta name='viewport' content='width=device-width, initial-scale=1'>");
            sb.AppendLine("  <title>Lucene Search Results</title>");
            sb.AppendLine("  <style>");
            sb.AppendLine("    * { margin: 0; padding: 0; box-sizing: border-box; }");
            sb.AppendLine("    body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background: #f5f5f5; color: #333; }");
            sb.AppendLine("    .container { max-width: 1000px; margin: 0 auto; padding: 20px; }");
            sb.AppendLine("    header { background: white; padding: 20px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); margin-bottom: 20px; }");
            sb.AppendLine("    h1 { color: #007bff; margin-bottom: 10px; }");
            sb.AppendLine("    .query { font-size: 18px; color: #666; margin: 10px 0; }");
            sb.AppendLine("    .stats { font-size: 14px; color: #999; margin-top: 10px; }");
            sb.AppendLine("    .results { margin-top: 20px; }");
            sb.AppendLine("    .result-item { background: white; padding: 20px; margin-bottom: 15px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); border-right: 4px solid #007bff; }");
            sb.AppendLine("    .result-id { font-weight: bold; color: #007bff; font-size: 14px; margin-bottom: 10px; }");
            sb.AppendLine("    .result-snippet { line-height: 1.8; font-size: 15px; color: #555; }");
            sb.AppendLine("    mark { background: #ffeb3b; padding: 2px 6px; border-radius: 3px; font-weight: 500; }");
            sb.AppendLine("    .no-results { text-align: center; padding: 40px; color: #999; }");
            sb.AppendLine("  </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("  <div class='container'>");
            sb.AppendLine("    <header>");
            sb.AppendLine($"      <h1>🔍 Lucene Search Results</h1>");
            sb.AppendLine($"      <div class='query'>Query: <strong>{EscapeHtml(query)}</strong></div>");
            sb.AppendLine($"      <div class='stats'>{results.Count} results found in {elapsed.TotalSeconds:F2}s</div>");
            sb.AppendLine("    </header>");
            sb.AppendLine("    <div class='results'>");

            if (results.Count == 0)
            {
                sb.AppendLine("      <div class='no-results'>No results found.</div>");
            }
            else
            {
                for (int i = 0; i < results.Count; i++)
                {
                    var (rowId, snippet) = results[i];
                    sb.AppendLine("      <div class='result-item'>");
                    sb.AppendLine($"        <div class='result-id'>Row #{rowId}</div>");
                    sb.AppendLine($"        <div class='result-snippet'>{snippet}</div>");
                    sb.AppendLine("      </div>");
                }
            }

            sb.AppendLine("    </div>");
            sb.AppendLine("  </div>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }

        private static string EscapeHtml(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }
    }
}
