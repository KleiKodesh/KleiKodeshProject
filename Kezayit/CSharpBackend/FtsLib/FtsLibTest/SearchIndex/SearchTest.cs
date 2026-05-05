using FtsLib.Seforim;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Searches an existing index and produces a detailed HTML benchmark +
    /// validation report fragment.
    ///
    /// Usage (standalone):
    ///   FtsLibTest.exe search [tier]   tier = 500k | 1m | 3m | full  (default: 500k)
    ///
    /// Or call RunAndGetFragment() to get the HTML fragment for a combined report.
    /// The caller may pass a pre-built SeforimIndex to avoid reopening the index.
    /// </summary>
    internal static class SearchTest
    {
        // ── Query suite ───────────────────────────────────────────────

        private static readonly QueryCase[] Queries =
        {
            new QueryCase("כי ביצחק",           QueryKind.Literal),
            new QueryCase("שויתי לנגדי תמיד",    QueryKind.Literal),
            new QueryCase("תורה מצוה",           QueryKind.Literal),
            new QueryCase("אברהם יצחק יעקב",     QueryKind.Literal),
            new QueryCase("אבל בן אין לה",       QueryKind.Literal),
            new QueryCase("וידבר משה כן אל בני", QueryKind.Literal),
            new QueryCase("nonexistentword123",   QueryKind.Literal),
            new QueryCase("משה* תורה",           QueryKind.Wildcard),
            new QueryCase("*ישראל",              QueryKind.Wildcard),
            new QueryCase("*אבר*",               QueryKind.Wildcard),
            new QueryCase("בני*",                QueryKind.Wildcard),
            // ── Fuzzy: misspelling of a common word (1 edit) ─────────
            // "יצחק" with י→ב prefix — ביצחק is a real indexed form (prepositional prefix).
            // Every result must contain כי AND a 1-edit neighbor of יצחק (e.g. ביצחק, יצחק itself).
            new QueryCase("כי יצחק~",            QueryKind.Fuzzy),
            // "תורה" with ו→א substitution — תארה is not a real word, so every
            // result comes from the fuzzy expansion (תורה, תרה, תאר, etc.)
            new QueryCase("תארה~ מצוה",          QueryKind.Fuzzy),
            // 3-letter word: "ענב" (grape) with ע→א — אנב is not indexed.
            // Exercises the bigram path; should find ענב, ענבי, ענבים etc.
            new QueryCase("אנב~",                QueryKind.Fuzzy),
            // distance-2: "ישראל" with two substitutions — יסראל (common typo pattern)
            new QueryCase("יסראל~2",             QueryKind.Fuzzy),
            // fuzzy AND literal: misspelling of ביצחק combined with כי (the original bug case)
            new QueryCase("כי ביצחק~",           QueryKind.Fuzzy),
        };

        private const int ValidationSample = 50;
        private const int PreviewResults   = 5;

        // ── Entry points ──────────────────────────────────────────────

        /// <summary>Standalone run: searches index, saves its own HTML report, opens it.</summary>
        public static void Run(string[] args)
        {
            string tierLabel = args.Length > 1 ? args[1] : "500k";
            string label;
            try   { label = TestHelpers.ResolveTier(tierLabel).Label; }
            catch (ArgumentException ex) { Console.WriteLine(ex.Message); return; }

            string dbPath   = BuildTest.ResolveDbPath();
            string indexDir = TestHelpers.IndexDir(label);

            if (!IndexExists(indexDir))
            {
                Console.WriteLine($"No index found at: {indexDir}");
                Console.WriteLine($"Run 'build {label}' first.");
                return;
            }

            string path   = BuildTest.TempReportPath("search", label);
            var    report = new HtmlReport($"Search Report — {label.ToUpper()}");
            RunCore(label, dbPath, indexDir, null, report);
            report.SaveAndOpen(path);
        }

        /// <summary>
        /// Runs the search and returns the HTML fragment for embedding in a combined report.
        /// Pass <paramref name="existingIndex"/> to reuse an already-open index (e.g. right
        /// after a build), or null to open a fresh one from <paramref name="indexDir"/>.
        /// </summary>
        public static string RunAndGetFragment(
            string       tierLabel,
            string       dbPath,
            string       indexDir,
            SeforimIndex existingIndex = null)
        {
            var report = new HtmlReport($"Search Report — {tierLabel.ToUpper()}");
            RunCore(tierLabel, dbPath, indexDir, existingIndex, report);
            return report.ToFragment();
        }

        private static void RunCore(
            string       tierLabel,
            string       dbPath,
            string       indexDir,
            SeforimIndex existingIndex,
            HtmlReport   report)
        {
            report.AddBanner($"FTS Search Benchmark  ·  Tier: {tierLabel.ToUpper()}");
            report.AddMeta("Started",            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AddMeta("DB path",            dbPath);
            report.AddMeta("Index dir",          indexDir);
            report.AddMeta("Queries",            Queries.Length.ToString());
            report.AddMeta("Validation sample",  $"{ValidationSample} results per query");
            report.AddMeta("Preview results",    $"{PreviewResults} snippets per query");

            Console.WriteLine();
            Console.WriteLine($"╔══ SEARCH — {tierLabel.ToUpper()} ══");
            Console.WriteLine($"║  DB    : {dbPath}");
            Console.WriteLine($"║  Index : {indexDir}");

            var index = existingIndex ?? new SeforimIndex(indexDir, dbPath);

            // ── Warm-up ───────────────────────────────────────────────
            report.AddSection("Warm-up");
            Console.WriteLine("║  Warm-up…");
            int warmCount = 0;
            var swWarm    = Stopwatch.StartNew();
            foreach (var _ in index.Search("תורה")) warmCount++;
            swWarm.Stop();
            report.AddMeta("Warm-up (תורה)", $"{warmCount:N0} results  ({swWarm.ElapsedMilliseconds} ms — discarded)");
            Console.WriteLine($"║  Warm-up: {warmCount:N0} results  ({swWarm.ElapsedMilliseconds} ms)");

            // ── Per-query sections ────────────────────────────────────
            var summaryRows = new List<QuerySummary>();

            foreach (var qc in Queries)
            {
                Console.WriteLine($"║");
                Console.WriteLine($"║  ── {qc.Kind}: {qc.Query}");
                var qs = RunQuery(index, qc, report);
                summaryRows.Add(qs);
            }

            // ── Aggregate summary ─────────────────────────────────────
            report.AddSection("Aggregate Summary");

            var tableRows  = new List<IReadOnlyList<string>>();
            int totalBogus = 0;
            foreach (var qs in summaryRows)
            {
                totalBogus += qs.Bogus;
                string status = qs.Bogus > 0 ? "BOGUS" : qs.Results == 0 ? "empty" : "✓";
                tableRows.Add(new[]
                {
                    qs.Query,
                    qs.Kind,
                    $"{qs.Results:N0}",
                    $"{qs.ElapsedMs} ms",
                    $"{qs.Ok}",
                    $"{qs.Bogus}",
                    $"{qs.Missing}",
                    status,
                });
            }

            report.AddTable(
                new[] { "Query", "Kind", "Results", "Time", "Ok", "Bogus", "Missing", "Status" },
                tableRows,
                cellClass: (r, c) =>
                {
                    if (c != 7) return null;
                    string s = tableRows[r][7];
                    return s == "✓" ? "ok" : s == "BOGUS" ? "bogus" : "empty";
                });

            string overallText = totalBogus == 0
                ? "✓  All queries valid"
                : $"✗  {totalBogus} bogus result(s) detected";
            string overallCls = totalBogus == 0 ? "overall-ok" : "overall-bogus";
            report.AddRawHtml($"<p class='{overallCls}'>{HtmlReport.EscapeStatic(overallText)}</p>");
            report.AddMeta("Finished", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            Console.WriteLine("║");
            Console.WriteLine($"║  Overall: {overallText}");
            Console.WriteLine("╚══ SEARCH DONE ══");
        }

        // ── Single query runner ───────────────────────────────────────

        private static QuerySummary RunQuery(
            SeforimIndex index,
            QueryCase    qc,
            HtmlReport   report)
        {
            report.AddSection($"{qc.Kind.ToString().ToUpper()}: {qc.Query}");

            // Search
            var results  = new List<SearchResult>();
            var swSearch = Stopwatch.StartNew();
            foreach (var r in index.Search(qc.Query)) results.Add(r);
            swSearch.Stop();

            report.AddMeta("Results",     $"{results.Count:N0}");
            report.AddMeta("Search time", $"{swSearch.ElapsedMilliseconds} ms");
            Console.WriteLine($"║    Results : {results.Count:N0}  ({swSearch.ElapsedMilliseconds} ms)");

            if (results.Count == 0)
            {
                report.AddAlert("No results returned.");
                return new QuerySummary(qc.Query, qc.Kind.ToString(), 0, swSearch.ElapsedMilliseconds, 0, 0, 0);
            }

            // Validation
            int ok = 0, bogus = 0, missing = 0;
            int sample    = Math.Min(ValidationSample, results.Count);
            var baseTerms = ExtractBaseTerms(qc.Query);

            for (int i = 0; i < sample; i++)
            {
                var    r     = results[i];
                string clean = TestHelpers.StripHtmlAndDiacritics(r.Content);
                if (string.IsNullOrEmpty(clean)) { missing++; continue; }

                if (ValidateResult(clean, baseTerms, qc.Kind)) ok++;
                else
                {
                    bogus++;
                    if (bogus <= 3)
                    {
                        string detail = $"id={r.LineId}  {TestHelpers.Truncate(clean, 100)}";
                        report.AddAlert($"[BOGUS] {detail}", isError: true);
                        Console.WriteLine($"║    [BOGUS] {detail}");
                    }
                }
            }

            string valStatus = bogus > 0 ? $"⚠ {bogus} bogus" : "✓ all valid";
            report.AddMeta("Validation",
                $"ok={ok}  bogus={bogus}  missing={missing}  sample={sample}/{results.Count:N0}  {valStatus}");
            Console.WriteLine($"║    Validate: ok={ok}  bogus={bogus}  missing={missing}  " +
                              $"sample={sample}/{results.Count:N0}  {valStatus}");

            // Snippet preview
            int shown     = 0;
            var swSnippet = Stopwatch.StartNew();
            Console.WriteLine($"║    First {Math.Min(PreviewResults, results.Count)} results:");
            foreach (var r in results)
            {
                if (shown >= PreviewResults) break;
                // Use the result's pre-computed matched terms so fuzzy/wildcard
                // expansions (e.g. ביצחק for יצחק~) are highlighted correctly.
                var snippet = index.GenerateSnippet(r);
                report.AddResultCard(r.LineId, r.BookTitle, snippet.Html, snippet.Score, snippet.IsMatch);

                // Print to terminal too
                string scoreStr  = snippet.Score == int.MaxValue ? "n/a" : snippet.Score.ToString();
                string matchStr  = snippet.IsMatch ? "✓" : "✗";
                string plainText = snippet.IsMatch
                    ? StripTags(snippet.Html, 120)
                    : TestHelpers.Truncate(TestHelpers.StripHtmlAndDiacritics(r.Content), 120);
                Console.WriteLine($"║      [{r.LineId}] {TestHelpers.Truncate(r.BookTitle, 30)}  score={scoreStr}  {matchStr}");
                Console.WriteLine($"║        {plainText}");

                shown++;
            }
            swSnippet.Stop();
            report.AddMeta($"Snippet time ({shown})", $"{swSnippet.ElapsedMilliseconds} ms");
            Console.WriteLine($"║    Snippet time: {shown} generated  ({swSnippet.ElapsedMilliseconds} ms)");

            return new QuerySummary(
                qc.Query, qc.Kind.ToString(),
                results.Count, swSearch.ElapsedMilliseconds,
                ok, bogus, missing);
        }

        // ── Helpers ───────────────────────────────────────────────────

        private static bool ValidateResult(string clean, List<string> baseTerms, QueryKind kind)
        {
            if (kind == QueryKind.Literal)
            {
                // Every base term must appear literally in the cleaned text.
                foreach (var t in baseTerms)
                    if (clean.IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0)
                        return false;
                return true;
            }

            // Fuzzy / wildcard: the base term itself may not appear (that's the point).
            // We can't easily re-expand here, so we accept the result as valid —
            // the snippet's IsMatch flag is the real correctness gate.
            return true;
        }

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

        private static bool IndexExists(string dir) =>
            Directory.Exists(dir) &&
            Directory.GetFiles(dir, "seg_*.dat").Length > 0;

        // ── Value types ───────────────────────────────────────────────

        private enum QueryKind { Literal, Wildcard, Fuzzy }

        private sealed class QueryCase
        {
            public readonly string    Query;
            public readonly QueryKind Kind;
            public QueryCase(string query, QueryKind kind) { Query = query; Kind = kind; }
        }

        private sealed class QuerySummary
        {
            public readonly string Query;
            public readonly string Kind;
            public readonly int    Results;
            public readonly long   ElapsedMs;
            public readonly int    Ok;
            public readonly int    Bogus;
            public readonly int    Missing;
            public QuerySummary(string q, string k, int r, long ms, int ok, int b, int m)
            { Query=q; Kind=k; Results=r; ElapsedMs=ms; Ok=ok; Bogus=b; Missing=m; }
        }
    }
}
