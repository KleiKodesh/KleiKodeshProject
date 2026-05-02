using FtsLib;
using FtsLib.Index;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.VisualBasic;

namespace FtsLibTest
{
    internal static class LiveDbTest
    {
        private const int    IndexLimit  = 100_000;   // lines to index
        private const string SearchQuery = "כי ביצחק"; // terms to search

        // ----------------------------------------------------------------
        // Entry point
        // ----------------------------------------------------------------
        public static void Run()
        {
            Console.WriteLine();
            Console.WriteLine("=== LIVE DB TEST ===");

            // 1. Open DB
            string dbPath = ResolveDbPath();
            Console.WriteLine($"DB: {dbPath}");

            if (!File.Exists(dbPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("ERROR: database file not found.");
                Console.ResetColor();
                return;
            }

            using (var conn = OpenConnection(dbPath))
            {
                // 2. Build index from first N lines
                var sw = Stopwatch.StartNew();
                var (index, lineStore) = BuildIndex(conn);
                sw.Stop();
                Console.WriteLine($"Indexed {lineStore.Count:N0} lines in {sw.ElapsedMilliseconds} ms");

                // 3. Search
                var tokenizer = new Tokenizer();
                var queryTerms = new List<string>(tokenizer.Extract(SearchQuery));

                Console.WriteLine($"Query terms: [{string.Join(", ", queryTerms)}]");

                sw.Restart();
                var matchIds = new List<int>(index.Search(queryTerms));
                sw.Stop();
                Console.WriteLine($"Found {matchIds.Count} result(s) in {sw.ElapsedMilliseconds} ms");

                // 4. Fetch full rows for matched IDs
                var results = FetchRows(conn, matchIds, lineStore);

                // 5. Render HTML and open in browser
                string htmlPath = RenderHtml(SearchQuery, queryTerms, results, lineStore.Count);
                OpenInBrowser(htmlPath);
            }
        }

        // ----------------------------------------------------------------
        // Index builder
        // ----------------------------------------------------------------
        private static (IndexManager index, Dictionary<int, LineRow> store)
            BuildIndex(SQLiteConnection conn)
        {
            var index     = new IndexManager();
            var tokenizer = new Tokenizer();
            var store     = new Dictionary<int, LineRow>(IndexLimit);

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    @"SELECT l.id, l.bookId, l.lineIndex, l.heRef, l.content, b.title
                        FROM line l
                        JOIN book b ON b.id = l.bookId
                       LIMIT @limit";
                cmd.Parameters.AddWithValue("@limit", IndexLimit);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int    lineId    = reader.GetInt32(0);
                        int    bookId    = reader.GetInt32(1);
                        int    lineIndex = reader.GetInt32(2);
                        string heRef     = reader.IsDBNull(3) ? null : reader.GetString(3);
                        string content   = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
                        string bookTitle = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);

                        store[lineId] = new LineRow(lineId, bookId, lineIndex, heRef, content, bookTitle);

                        foreach (var term in tokenizer.Extract(content))
                            index.Add(term, lineId);
                    }
                }
            }

            return (index, store);
        }

        // ----------------------------------------------------------------
        // Fetch full rows for result IDs (already in store)
        // ----------------------------------------------------------------
        private static List<LineRow> FetchRows(
            SQLiteConnection conn,
            List<int> ids,
            Dictionary<int, LineRow> store)
        {
            var rows = new List<LineRow>(ids.Count);
            foreach (var id in ids)
                if (store.TryGetValue(id, out var row))
                    rows.Add(row);

            // Sort by bookId then lineIndex for readable output
            rows.Sort((a, b) =>
            {
                int c = a.BookId.CompareTo(b.BookId);
                return c != 0 ? c : a.LineIndex.CompareTo(b.LineIndex);
            });

            return rows;
        }

        // ----------------------------------------------------------------
        // HTML renderer
        // ----------------------------------------------------------------
        private static string RenderHtml(
            string query,
            List<string> terms,
            List<LineRow> rows,
            int totalIndexed)
        {
            var sb = new StringBuilder();

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html dir=\"rtl\" lang=\"he\">");
            sb.AppendLine("<head>");
            sb.AppendLine("  <meta charset=\"utf-8\">");
            sb.AppendLine($"  <title>חיפוש: {HtmlEncode(query)}</title>");
            sb.AppendLine("  <style>");
            sb.AppendLine("    body { font-family: 'David', 'Times New Roman', serif; font-size: 18px;");
            sb.AppendLine("           background: #fafaf7; color: #222; margin: 2em auto; max-width: 900px; padding: 0 1em; }");
            sb.AppendLine("    h1   { font-size: 1.6em; border-bottom: 2px solid #c8a96e; padding-bottom: .3em; }");
            sb.AppendLine("    .meta { color: #666; font-size: .85em; margin-bottom: 1.5em; }");
            sb.AppendLine("    table { width: 100%; border-collapse: collapse; }");
            sb.AppendLine("    th   { background: #3a2a0a; color: #f5e6c8; padding: .5em .8em; text-align: right; }");
            sb.AppendLine("    tr:nth-child(even) { background: #f0ece0; }");
            sb.AppendLine("    td   { padding: .45em .8em; border-bottom: 1px solid #ddd; vertical-align: top; }");
            sb.AppendLine("    .book { color: #5a3e1b; font-weight: bold; white-space: nowrap; }");
            sb.AppendLine("    .ref  { color: #888; font-size: .85em; white-space: nowrap; }");
            sb.AppendLine("    .content { line-height: 1.7; }");
            sb.AppendLine("    mark { background: #ffe066; border-radius: 2px; padding: 0 2px; }");
            sb.AppendLine("    .none { color: #999; font-style: italic; padding: 2em; text-align: center; }");
            sb.AppendLine("  </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine($"  <h1>תוצאות חיפוש: <em>{HtmlEncode(query)}</em></h1>");
            sb.AppendLine($"  <p class=\"meta\">נמצאו <strong>{rows.Count}</strong> תוצאות " +
                          $"מתוך {totalIndexed:N0} שורות מאונדקסות &nbsp;|&nbsp; " +
                          $"מונחים: {HtmlEncode(string.Join(", ", terms))}</p>");

            if (rows.Count == 0)
            {
                sb.AppendLine("  <p class=\"none\">לא נמצאו תוצאות.</p>");
            }
            else
            {
                sb.AppendLine("  <table>");
                sb.AppendLine("    <thead><tr>");
                sb.AppendLine("      <th>#</th><th>ספר</th><th>מיקום</th><th>תוכן</th>");
                sb.AppendLine("    </tr></thead>");
                sb.AppendLine("    <tbody>");

                int n = 0;
                foreach (var row in rows)
                {
                    n++;
                    string highlighted = HighlightTerms(row.Content, terms);
                    string refText     = row.HeRef ?? $"שורה {row.LineIndex}";

                    sb.AppendLine("      <tr>");
                    sb.AppendLine($"        <td>{n}</td>");
                    sb.AppendLine($"        <td class=\"book\">{HtmlEncode(row.BookTitle)}</td>");
                    sb.AppendLine($"        <td class=\"ref\">{HtmlEncode(refText)}</td>");
                    sb.AppendLine($"        <td class=\"content\">{highlighted}</td>");
                    sb.AppendLine("      </tr>");
                }

                sb.AppendLine("    </tbody>");
                sb.AppendLine("  </table>");
            }

            sb.AppendLine("</body></html>");

            string path = Path.Combine(Path.GetTempPath(), "fts_results.html");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Console.WriteLine($"HTML: {path}");
            return path;
        }

        // ----------------------------------------------------------------
        // Highlight matched terms in content (strip HTML first)
        // ----------------------------------------------------------------
        private static string HighlightTerms(string content, List<string> terms)
        {
            // Strip HTML tags for display
            var plain = new StringBuilder(content.Length);
            bool inTag = false;
            foreach (char c in content)
            {
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (!inTag) plain.Append(c);
            }

            string text = plain.ToString();

            // Wrap each matched term with <mark>
            foreach (var term in terms)
            {
                int i = 0;
                while (i < text.Length)
                {
                    int idx = text.IndexOf(term, i, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) break;
                    text = text.Substring(0, idx)
                         + "<mark>" + HtmlEncode(text.Substring(idx, term.Length)) + "</mark>"
                         + text.Substring(idx + term.Length);
                    i = idx + "<mark>".Length + term.Length + "</mark>".Length;
                }
            }

            return text;
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------
        private static void OpenInBrowser(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName        = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not open browser: {ex.Message}");
            }
        }

        private static string ResolveDbPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string defaultPath = Path.Combine(
                appData,
                "io.github.kdroidfilter.seforimapp",
                "databases",
                "seforim.db");

            return Interaction.GetSetting("ZayitApp", "Database", "Path", defaultPath);
        }

        private static SQLiteConnection OpenConnection(string dbPath)
        {
            var conn = new SQLiteConnection(
                $"Data Source={dbPath};Version=3;Page Size=4096;Read Only=True;");
            conn.Open();
            return conn;
        }

        private static string HtmlEncode(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;");
        }

        // ----------------------------------------------------------------
        // Data record
        // ----------------------------------------------------------------
        private struct LineRow
        {
            public int    Id;
            public int    BookId;
            public int    LineIndex;
            public string HeRef;
            public string Content;
            public string BookTitle;

            public LineRow(int id, int bookId, int lineIndex,
                           string heRef, string content, string bookTitle)
            {
                Id        = id;
                BookId    = bookId;
                LineIndex = lineIndex;
                HeRef     = heRef;
                Content   = content;
                BookTitle = bookTitle;
            }
        }
    }
}
