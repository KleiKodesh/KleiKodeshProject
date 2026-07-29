using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Builds an HTML report section.
    ///
    /// Two usage modes:
    ///
    ///   1. Standalone page  — call SaveAndOpen(path) to write + open a full HTML page.
    ///   2. Combined page    — call ToFragment() to get the inner HTML, then pass all
    ///      fragments to HtmlReport.Combine(title, fragments) which returns a full page
    ///      string you can write and open yourself.
    ///
    /// Use HtmlReport.OpenHtml(path) to open any saved file in the default browser.
    /// </summary>
    internal sealed class HtmlReport
    {
        private readonly string        _title;
        private readonly StringBuilder _body = new StringBuilder();

        public HtmlReport(string title) { _title = title; }

        // ── Content builders ──────────────────────────────────────────

        public void AddBanner(string text)
            => _body.AppendLine($"<div class='banner'>{Esc(text)}</div>");

        public void AddMeta(string label, string value)
            => _body.AppendLine(
                $"<div class='meta'><span class='meta-label'>{Esc(label)}</span>" +
                $"<span class='meta-value'>{Esc(value)}</span></div>");

        public void AddSection(string heading)
            => _body.AppendLine($"<h2>{Esc(heading)}</h2>");

        public void AddAlert(string text, bool isError = false)
        {
            string cls = isError ? "alert alert-error" : "alert alert-info";
            _body.AppendLine($"<div class='{cls}'>{Esc(text)}</div>");
        }

        public void AddRawHtml(string html)
            => _body.AppendLine(html);

        /// <summary>
        /// Adds a table. All cell content is HTML-escaped automatically.
        /// <paramref name="cellClass"/> receives (rowIndex, colIndex) and returns
        /// an optional CSS class name for that cell, or null for none.
        /// </summary>
        public void AddTable(
            IReadOnlyList<string>                  headers,
            IReadOnlyList<IReadOnlyList<string>>   rows,
            Func<int, int, string>                 cellClass = null)
        {
            _body.AppendLine("<div class='table-wrap'><table>");
            _body.AppendLine("<thead><tr>");
            foreach (var h in headers)
                _body.AppendLine($"<th>{Esc(h)}</th>");
            _body.AppendLine("</tr></thead><tbody>");

            for (int r = 0; r < rows.Count; r++)
            {
                _body.AppendLine("<tr>");
                for (int c = 0; c < rows[r].Count; c++)
                {
                    string cls  = cellClass?.Invoke(r, c);
                    string attr = cls != null ? $" class='{cls}'" : string.Empty;
                    _body.AppendLine($"<td{attr}>{Esc(rows[r][c])}</td>");
                }
                _body.AppendLine("</tr>");
            }
            _body.AppendLine("</tbody></table></div>");
        }

        public void AddProgressTable(IReadOnlyList<ProgressRow> rows)
        {
            var tableRows = new List<IReadOnlyList<string>>();
            foreach (var r in rows)
                tableRows.Add(new[] { $"{r.Lines:N0}", r.Elapsed, r.Rate, $"{r.Delta:N0}", r.DeltaTime });
            AddTable(
                new[] { "Lines indexed", "Elapsed", "Rate", "Δ lines", "Δ time" },
                tableRows);
        }

        /// <summary>
        /// Adds a result card. <paramref name="snippetHtml"/> is trusted HTML
        /// already produced by SnippetBuilder — not escaped.
        /// </summary>
        public void AddResultCard(int lineId, string bookTitle, string snippetHtml,
            int score, bool isMatch)
        {
            string scoreStr  = score == int.MaxValue ? "n/a" : score.ToString();
            string matchCls  = isMatch ? "match-yes" : "match-no";
            string matchText = isMatch ? "✓ match" : "✗ no match";

            _body.AppendLine("<div class='result-card'>");
            _body.AppendLine(
                $"<div class='result-header'>" +
                $"<span class='line-id'>#{lineId}</span>" +
                $"<span class='book-title'>{Esc(bookTitle)}</span>" +
                $"<span class='score'>score: {scoreStr}</span>" +
                $"<span class='{matchCls}'>{matchText}</span>" +
                $"</div>");
            if (isMatch && !string.IsNullOrEmpty(snippetHtml))
                _body.AppendLine($"<div class='snippet' dir='rtl'>{snippetHtml}</div>");
            _body.AppendLine("</div>");
        }

        // ── Output ────────────────────────────────────────────────────

        /// <summary>
        /// Returns the inner HTML body fragment (no html/head/body wrapper).
        /// Use this when combining multiple reports into one page.
        /// </summary>
        public string ToFragment()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<div class='report-block'>");
            sb.AppendLine($"<h1 class='report-title'>{Esc(_title)}</h1>");
            sb.AppendLine(_body.ToString());
            sb.AppendLine("</div>");
            return sb.ToString();
        }

        /// <summary>
        /// Wraps this report's fragment in a full HTML page, saves it, and opens it.
        /// </summary>
        public void SaveAndOpen(string path)
        {
            string html = WrapPage(_title, ToFragment());
            File.WriteAllText(path, html, Encoding.UTF8);
            Console.WriteLine($"Report → {path}");
            OpenHtml(path);
        }

        // ── Static helpers ────────────────────────────────────────────

        /// <summary>
        /// Combines multiple HTML fragments into one full page, saves it, and opens it.
        /// </summary>
        public static void CombineAndOpen(string title, IEnumerable<string> fragments, string path)
        {
            var combined = new StringBuilder();
            foreach (var f in fragments)
                combined.AppendLine(f);

            string html = WrapPage(title, combined.ToString());
            File.WriteAllText(path, html, Encoding.UTF8);
            Console.WriteLine($"Combined report → {path}");
            OpenHtml(path);
        }

        public static void OpenHtml(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch { /* best-effort */ }
        }

        public static string EscapeStatic(string s) => Esc(s);

        // ── Page template ─────────────────────────────────────────────

        private static string WrapPage(string title, string bodyContent)
        {
            return $@"<!DOCTYPE html>
<html lang='he'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1'>
<title>{Esc(title)}</title>
<style>
  *, *::before, *::after {{ box-sizing: border-box; margin: 0; padding: 0; }}
  body {{
    font-family: 'Segoe UI', Arial, sans-serif;
    font-size: 14px;
    background: #f4f6f9;
    color: #1a1a2e;
    padding: 24px;
    direction: ltr;
  }}
  h1 {{ font-size: 1.6rem; margin-bottom: 6px; color: #0d1b2a; }}
  h1.report-title {{
    font-size: 1.4rem;
    margin: 0 0 14px;
    padding-bottom: 8px;
    border-bottom: 2px solid #1a3a5c;
    color: #1a3a5c;
  }}
  h2 {{
    font-size: 1rem;
    margin: 22px 0 8px;
    padding: 5px 12px;
    background: #1a3a5c;
    color: #fff;
    border-radius: 4px;
  }}
  .report-block {{
    background: #fff;
    border: 1px solid #dde3ec;
    border-radius: 8px;
    padding: 20px 24px;
    margin-bottom: 28px;
    box-shadow: 0 2px 8px rgba(0,0,0,.07);
  }}
  .banner {{
    background: #0d1b2a;
    color: #e0e7ff;
    font-size: 1.2rem;
    font-weight: 700;
    padding: 12px 18px;
    border-radius: 5px;
    margin-bottom: 14px;
    letter-spacing: .4px;
  }}
  .meta {{
    display: flex;
    gap: 10px;
    padding: 3px 0;
    font-size: 13px;
    color: #444;
  }}
  .meta-label {{ font-weight: 600; min-width: 160px; color: #1a3a5c; }}
  .meta-value {{ color: #222; }}
  .table-wrap {{ overflow-x: auto; margin: 10px 0 16px; }}
  table {{
    border-collapse: collapse;
    width: 100%;
    background: #fff;
    border-radius: 5px;
    overflow: hidden;
    box-shadow: 0 1px 3px rgba(0,0,0,.07);
  }}
  th {{
    background: #1a3a5c;
    color: #fff;
    padding: 7px 11px;
    text-align: left;
    font-size: 12px;
    text-transform: uppercase;
    letter-spacing: .3px;
  }}
  td {{
    padding: 6px 11px;
    border-bottom: 1px solid #eef0f4;
    font-size: 13px;
  }}
  tr:last-child td {{ border-bottom: none; }}
  tr:nth-child(even) td {{ background: #f8f9fc; }}
  .ok     {{ color: #1a7a3c; font-weight: 600; }}
  .bogus  {{ color: #c0392b; font-weight: 700; }}
  .empty  {{ color: #888; }}
  .result-card {{
    background: #fafbfd;
    border: 1px solid #dde3ec;
    border-radius: 5px;
    margin: 6px 0;
    overflow: hidden;
  }}
  .result-header {{
    display: flex;
    gap: 12px;
    align-items: center;
    padding: 7px 12px;
    background: #eef2f8;
    border-bottom: 1px solid #dde3ec;
    flex-wrap: wrap;
  }}
  .line-id    {{ font-weight: 700; color: #1a3a5c; font-size: 12px; }}
  .book-title {{ font-weight: 600; color: #0d1b2a; flex: 1; }}
  .score      {{ font-size: 12px; color: #555; }}
  .match-yes  {{ font-size: 12px; color: #1a7a3c; font-weight: 600; }}
  .match-no   {{ font-size: 12px; color: #c0392b; font-weight: 600; }}
  .snippet {{
    padding: 9px 14px;
    font-size: 15px;
    line-height: 1.75;
    color: #222;
    direction: rtl;
  }}
  .snippet mark {{
    background: #fff176;
    color: #000;
    border-radius: 2px;
    padding: 0 2px;
  }}
  .alert {{
    padding: 9px 14px;
    border-radius: 4px;
    margin: 8px 0;
    font-size: 13px;
  }}
  .alert-info  {{ background: #e8f4fd; border-left: 4px solid #2980b9; color: #1a4a6e; }}
  .alert-error {{ background: #fdecea; border-left: 4px solid #c0392b; color: #7b1a1a; }}
  .overall-ok    {{ color: #1a7a3c; font-size: 1rem; font-weight: 700; margin: 14px 0 4px; }}
  .overall-bogus {{ color: #c0392b; font-size: 1rem; font-weight: 700; margin: 14px 0 4px; }}
  .page-title {{
    font-size: 1.7rem;
    font-weight: 800;
    color: #0d1b2a;
    margin-bottom: 20px;
    padding-bottom: 10px;
    border-bottom: 3px solid #0d1b2a;
  }}
  .generated {{
    margin-top: 28px;
    font-size: 11px;
    color: #aaa;
  }}
</style>
</head>
<body>
<p class='page-title'>{Esc(title)}</p>
{bodyContent}
<p class='generated'>Generated {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>
</body>
</html>";
        }

        // ── Internal escape ───────────────────────────────────────────

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;")
                    .Replace("'", "&#39;");
        }

        // ── Value types ───────────────────────────────────────────────

        public sealed class ProgressRow
        {
            public long   Lines;
            public string Elapsed;
            public string Rate;
            public long   Delta;
            public string DeltaTime;
        }
    }
}
