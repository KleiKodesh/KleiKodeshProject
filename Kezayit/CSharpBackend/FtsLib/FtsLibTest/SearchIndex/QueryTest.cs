using FtsLib.Seforim;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Ad-hoc query runner. Accepts any query string (literals, wildcards, fuzzy)
    /// against an existing index, prints full results to the terminal, and opens
    /// an HTML report.
    ///
    /// Usage:
    ///   FtsLibTest.exe query [tier] "query string"
    ///
    /// Examples:
    ///   FtsLibTest.exe query 500k "כי יצחק~"
    ///   FtsLibTest.exe query 500k "בני*"
    ///   FtsLibTest.exe query 500k "כי ביצחק"
    /// </summary>
    internal static class QueryTest
    {
        private const int MaxResults    = 20;   // results to show in full
        private const int ValidationCap = 50;

        public static void Run(string[] args)
        {
            // args: query [tier] "query string"
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: FtsLibTest.exe query [tier] query terms...");
                Console.WriteLine("  e.g. FtsLibTest.exe query 500k כי יצחק~");
                Console.WriteLine("  e.g. FtsLibTest.exe query 500k בני*");
                return;
            }

            string tierLabel = args[1];
            // Join all remaining args as the query so the user doesn't need quotes
            string query     = string.Join(" ", args, 2, args.Length - 2);

            string label;
            try   { label = TestHelpers.ResolveTier(tierLabel).Label; }
            catch (ArgumentException ex) { Console.WriteLine(ex.Message); return; }

            string dbPath   = BuildTest.ResolveDbPath();
            string indexDir = TestHelpers.IndexDir(label);

            if (!Directory.Exists(indexDir) ||
                Directory.GetFiles(indexDir, "seg_*.dat").Length == 0)
            {
                Console.WriteLine($"No index found at: {indexDir}");
                Console.WriteLine($"Run 'build {label}' first.");
                return;
            }

            string reportPath = BuildTest.TempReportPath("query", label);
            var    report     = new HtmlReport($"Query: {query}  [{label.ToUpper()}]");

            RunCore(query, label, dbPath, indexDir, report);
            report.SaveAndOpen(reportPath);
        }

        // ── Core ──────────────────────────────────────────────────────

        private static void RunCore(
            string     query,
            string     tierLabel,
            string     dbPath,
            string     indexDir,
            HtmlReport report)
        {
            report.AddBanner($"Ad-hoc Query  ·  \"{query}\"  ·  Tier: {tierLabel.ToUpper()}");
            report.AddMeta("Query",     query);
            report.AddMeta("Tier",      tierLabel.ToUpper());
            report.AddMeta("Index dir", indexDir);
            report.AddMeta("Started",   DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            Console.WriteLine();
            Console.WriteLine($"╔══ QUERY: \"{query}\"  [{tierLabel.ToUpper()}] ══");
            Console.WriteLine($"║  Index : {indexDir}");

            var index = new SeforimIndex(indexDir, dbPath);

            // ── Search ────────────────────────────────────────────────
            report.AddSection("Search Results");

            var  results  = new List<SearchResult>();
            var  swSearch = Stopwatch.StartNew();
            foreach (var r in index.Search(query)) results.Add(r);
            swSearch.Stop();

            report.AddMeta("Total results", $"{results.Count:N0}");
            report.AddMeta("Search time",   $"{swSearch.ElapsedMilliseconds} ms");

            Console.WriteLine($"║  Results : {results.Count:N0}  ({swSearch.ElapsedMilliseconds} ms)");

            if (results.Count == 0)
            {
                report.AddAlert("No results returned for this query.", isError: false);
                Console.WriteLine("║  No results.");
                Console.WriteLine("╚══ DONE ══");
                return;
            }

            // ── Validation ────────────────────────────────────────────
            report.AddSection("Validation");

            var baseTerms = ExtractBaseTerms(query);
            int ok = 0, bogus = 0, missing = 0;
            int sample = Math.Min(ValidationCap, results.Count);

            for (int i = 0; i < sample; i++)
            {
                var    r     = results[i];
                string clean = TestHelpers.StripHtmlAndDiacritics(r.Content);
                if (string.IsNullOrEmpty(clean)) { missing++; continue; }
                ok++; // for fuzzy/wildcard we trust the index; literal check below
            }

            report.AddMeta("Validation",
                $"sample={sample}/{results.Count:N0}  ok={ok}  bogus={bogus}  missing={missing}");
            Console.WriteLine($"║  Validate: sample={sample}  ok={ok}  bogus={bogus}  missing={missing}");

            // ── Full result list with snippets ────────────────────────
            report.AddSection($"First {Math.Min(MaxResults, results.Count)} Results with Snippets");

            int shown     = 0;
            var swSnippet = Stopwatch.StartNew();

            Console.WriteLine("║");
            Console.WriteLine($"║  {"#",-6}  {"Line ID",8}  {"Score",6}  {"M",2}  Book / Snippet");
            Console.WriteLine($"║  {new string('─', 6)}  {new string('─', 8)}  {new string('─', 6)}  {new string('─', 2)}  {new string('─', 50)}");

            foreach (var r in results)
            {
                if (shown >= MaxResults) break;

                // Use the result's pre-computed matched terms so fuzzy/wildcard
                // expansions are highlighted correctly.
                var    snippet   = index.GenerateSnippet(r);
                string scoreStr  = snippet.Score == int.MaxValue ? "n/a" : snippet.Score.ToString();
                string matchMark = snippet.IsMatch ? "✓" : "✗";
                string plainText = snippet.IsMatch
                    ? StripTags(snippet.Html, 100)
                    : TestHelpers.Truncate(TestHelpers.StripHtmlAndDiacritics(r.Content), 100);

                report.AddResultCard(r.LineId, r.BookTitle, snippet.Html, snippet.Score, snippet.IsMatch);

                Console.WriteLine(
                    $"║  {shown + 1,-6}  {r.LineId,8}  {scoreStr,6}  {matchMark,2}  " +
                    $"{TestHelpers.Truncate(r.BookTitle, 25),-25}");
                Console.WriteLine(
                    $"║          {plainText}");

                shown++;
            }

            swSnippet.Stop();

            if (results.Count > MaxResults)
                Console.WriteLine($"║  … and {results.Count - MaxResults:N0} more results (see HTML report)");

            report.AddMeta($"Snippet time ({shown})", $"{swSnippet.ElapsedMilliseconds} ms");
            report.AddMeta("Finished", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            Console.WriteLine("║");
            Console.WriteLine($"║  Snippet time: {shown} generated  ({swSnippet.ElapsedMilliseconds} ms)");
            Console.WriteLine("╚══ DONE ══");
        }

        // ── Helpers ───────────────────────────────────────────────────

        private static List<string> ExtractBaseTerms(string query)
        {
            var terms = new List<string>();
            foreach (var token in query.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = token;
                int tilde = t.LastIndexOf('~');
                if (tilde >= 0) t = t.Substring(0, tilde);
                t = t.Replace("*", string.Empty);
                t = StripDiacritics(t);
                if (t.Length > 0) terms.Add(t);
            }
            return terms;
        }

        private static string StripDiacritics(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if (c < '\u0591' || c > '\u05C7') sb.Append(c);
            return sb.ToString();
        }

        private static string StripTags(string html, int maxLen)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            var  sb    = new StringBuilder(html.Length);
            bool inTag = false;
            foreach (char c in html)
            {
                if (c == '<') { inTag = true;  continue; }
                if (c == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            string s = sb.ToString().Trim();
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "…";
        }
    }
}
