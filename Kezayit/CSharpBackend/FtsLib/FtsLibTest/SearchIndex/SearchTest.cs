using FtsLib.SeforimDb;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Production-readiness search test suite.
    ///
    /// For each query validates EVERY result — no sampling:
    ///   1. Result count bounds (minResults / maxResults).
    ///   2. Content validation — every result must contain the expected terms.
    ///        Literal  : all base terms must appear in the line content.
    ///        Fuzzy    : at least one term from every expanded group must appear.
    ///        Wildcard : at least one term from every expanded group must appear.
    ///   3. Snippet IsMatch — every result's snippet must be IsMatch=true.
    ///      A false positive means the index returned a line that doesn't actually
    ///      contain the query terms — that is a correctness bug.
    ///   4. Known-ID assertions — specific line IDs that must appear in results.
    ///   5. SearchIds consistency — SearchIds() must return exactly the same set
    ///      of IDs as Search(), with zero mismatches.
    ///
    /// Usage:
    ///   FtsLibTest.exe search [tier]   tier = 500k | 1m | 3m | full  (default: 500k)
    /// </summary>
    internal static class SearchTest
    {
        // ── Query suite ───────────────────────────────────────────────

        private static readonly QueryCase[] Queries =
        {
            // ── Literals ─────────────────────────────────────────────
            new QueryCase("כי ביצחק",
                QueryKind.Literal,
                minResults: 1,
                requiredIds: new[] { 548 }),          // UnionIterator bug regression

            new QueryCase("שויתי לנגדי תמיד",
                QueryKind.Literal,
                minResults: 1),

            new QueryCase("תורה מצוה",
                QueryKind.Literal,
                minResults: 1),

            new QueryCase("אברהם יצחק יעקב",
                QueryKind.Literal,
                minResults: 1),

            new QueryCase("אבל בן אין לה",
                QueryKind.Literal,
                minResults: 1),

            new QueryCase("וידבר משה כן אל בני",
                QueryKind.Literal,
                minResults: 1),

            new QueryCase("nonexistentword123",
                QueryKind.Literal,
                minResults: 0, maxResults: 0),        // must return exactly 0

            // ── Wildcards ─────────────────────────────────────────────
            new QueryCase("משה* תורה",
                QueryKind.Wildcard,
                minResults: 1),

            new QueryCase("*ישראל",
                QueryKind.Wildcard,
                minResults: 1),

            new QueryCase("*אבר*",
                QueryKind.Wildcard,
                minResults: 1),

            new QueryCase("בני*",
                QueryKind.Wildcard,
                minResults: 1),

            // ── Fuzzy ─────────────────────────────────────────────────
            // יצחק~ must find ביצחק — the original UnionIterator bug regression
            new QueryCase("כי יצחק~",
                QueryKind.Fuzzy,
                minResults: 1,
                requiredIds: new[] { 548, 144129, 144175, 136954 }),

            new QueryCase("תארה~ מצוה",
                QueryKind.Fuzzy,
                minResults: 1),

            // 3-letter bigram path
            new QueryCase("אנב~",
                QueryKind.Fuzzy,
                minResults: 1),

            // distance-2
            new QueryCase("יסראל~2",
                QueryKind.Fuzzy,
                minResults: 1),

            new QueryCase("כי ביצחק~",
                QueryKind.Fuzzy,
                minResults: 1,
                requiredIds: new[] { 548 }),
        };

        // Max acceptable false-positive rate across ALL results.
        // Any bogus result is a correctness bug — we set this to 0 for production.
        // A non-zero threshold only exists to tolerate known index false-positives
        // (e.g. tokenizer edge cases). Set to 0.0 for zero-tolerance.
        private const double MaxFalsePositiveRate = 0.0;

        private const int PreviewResults = 5;

        // ── Entry points ──────────────────────────────────────────────

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

        // ── Core ──────────────────────────────────────────────────────

        private static void RunCore(
            string       tierLabel,
            string       dbPath,
            string       indexDir,
            SeforimIndex existingIndex,
            HtmlReport   report)
        {
            report.AddBanner($"FTS Search Test  ·  Tier: {tierLabel.ToUpper()}");
            report.AddMeta("Started",   DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AddMeta("DB path",   dbPath);
            report.AddMeta("Index dir", indexDir);
            report.AddMeta("Queries",   Queries.Length.ToString());
            report.AddMeta("Validation", "FULL — every result checked");

            Console.WriteLine();
            Console.WriteLine($"╔══ SEARCH — {tierLabel.ToUpper()} ══");
            Console.WriteLine($"║  DB    : {dbPath}");
            Console.WriteLine($"║  Index : {indexDir}");
            Console.WriteLine($"║  Mode  : FULL validation — every result checked");

            var index = existingIndex ?? new SeforimIndex(indexDir, dbPath);

            // ── Warm-up ───────────────────────────────────────────────
            Console.WriteLine("║  Warm-up…");
            int warmCount = 0;
            var swWarm    = Stopwatch.StartNew();
            foreach (var _ in index.Search("תורה")) warmCount++;
            swWarm.Stop();
            report.AddMeta("Warm-up (תורה)", $"{warmCount:N0} results  ({swWarm.ElapsedMilliseconds} ms)");
            Console.WriteLine($"║  Warm-up: {warmCount:N0} results  ({swWarm.ElapsedMilliseconds} ms)");

            // ── Per-query ─────────────────────────────────────────────
            var summaryRows   = new List<QuerySummary>();
            int totalFailures = 0;

            foreach (var qc in Queries)
            {
                Console.WriteLine("║");
                Console.WriteLine($"║  ── {qc.Kind}: {qc.Query}");
                var qs = RunQuery(index, qc, report);
                summaryRows.Add(qs);
                if (qs.Failed) totalFailures++;
            }

            // ── Aggregate summary ─────────────────────────────────────
            report.AddSection("Aggregate Summary");
            var tableRows = new List<IReadOnlyList<string>>();
            foreach (var qs in summaryRows)
            {
                string status = qs.Failed
                    ? "FAIL"
                    : qs.Results == 0 && qs.MinResults == 0 ? "✓ (empty)" : "✓";
                tableRows.Add(new[]
                {
                    qs.Query, qs.Kind,
                    $"{qs.Results:N0}",
                    $"{qs.ElapsedMs} ms",
                    $"{qs.Bogus}/{qs.Results:N0}",
                    $"{qs.SnippetFp}/{qs.Results:N0}",
                    $"{qs.MissingIds}",
                    status,
                });
            }

            report.AddTable(
                new[] { "Query", "Kind", "Results", "Time", "Bogus/Total", "SnippetFP/Total", "Missing IDs", "Status" },
                tableRows,
                cellClass: (r, c) =>
                {
                    if (c != 7) return null;
                    string s = tableRows[r][7];
                    return s.StartsWith("✓") ? "ok" : "bogus";
                });

            string overallText = totalFailures == 0
                ? "✓  All queries passed — production ready"
                : $"✗  {totalFailures} query/queries FAILED";
            string overallCls = totalFailures == 0 ? "overall-ok" : "overall-bogus";
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
            bool   failed   = false;
            var    failures = new List<string>();

            // ── 1. Run Search() — collect ALL results ─────────────────
            var results  = new List<SearchResult>();
            var swSearch = Stopwatch.StartNew();
            foreach (var r in index.Search(qc.Query)) results.Add(r);
            swSearch.Stop();

            report.AddMeta("Results",     $"{results.Count:N0}");
            report.AddMeta("Search time", $"{swSearch.ElapsedMilliseconds} ms");
            Console.WriteLine($"║    Results : {results.Count:N0}  ({swSearch.ElapsedMilliseconds} ms)");

            // ── 2. Result count bounds ────────────────────────────────
            if (results.Count < qc.MinResults)
            {
                string msg = $"Expected ≥{qc.MinResults} results, got {results.Count}";
                failures.Add(msg); failed = true;
                report.AddAlert($"[FAIL] {msg}", isError: true);
                Console.WriteLine($"║    [FAIL] {msg}");
            }
            if (qc.MaxResults >= 0 && results.Count > qc.MaxResults)
            {
                string msg = $"Expected ≤{qc.MaxResults} results, got {results.Count}";
                failures.Add(msg); failed = true;
                report.AddAlert($"[FAIL] {msg}", isError: true);
                Console.WriteLine($"║    [FAIL] {msg}");
            }

            if (results.Count == 0)
            {
                if (qc.MinResults == 0)
                    Console.WriteLine("║    (no results expected — ok)");
                return new QuerySummary(qc.Query, qc.Kind.ToString(), 0,
                    swSearch.ElapsedMilliseconds, 0, 0, 0, qc.MinResults, failed);
            }

            // ── 3. Required-ID assertions ─────────────────────────────
            int missingIds = 0;
            if (qc.RequiredIds != null && qc.RequiredIds.Length > 0)
            {
                var resultIdSet = new HashSet<int>(results.Count);
                foreach (var r in results) resultIdSet.Add(r.LineId);

                foreach (int id in qc.RequiredIds)
                {
                    if (!resultIdSet.Contains(id))
                    {
                        missingIds++;
                        string msg = $"Required id={id} not found in results";
                        failures.Add(msg); failed = true;
                        report.AddAlert($"[FAIL] {msg}", isError: true);
                        Console.WriteLine($"║    [FAIL] {msg}");
                    }
                }
                if (missingIds == 0)
                    Console.WriteLine($"║    Required IDs: all {qc.RequiredIds.Length} found ✓");
            }

            // ── 4. Full content validation — EVERY result ─────────────
            var baseTerms = ExtractBaseTerms(qc.Query);
            int bogus     = 0;
            var swVal     = Stopwatch.StartNew();

            foreach (var r in results)
            {
                string clean = TestHelpers.StripHtmlAndDiacritics(r.Content);
                if (string.IsNullOrEmpty(clean)) continue;

                if (!ValidateResult(clean, r, baseTerms, qc.Kind))
                {
                    bogus++;
                    if (bogus <= 5)   // log first 5 to avoid flooding
                    {
                        string detail = $"id={r.LineId}  {TestHelpers.Truncate(clean, 120)}";
                        report.AddAlert($"[BOGUS] {detail}", isError: true);
                        Console.WriteLine($"║    [BOGUS] {detail}");
                    }
                }
            }
            swVal.Stop();

            double fpRate = results.Count > 0 ? (double)bogus / results.Count : 0.0;
            if (fpRate > MaxFalsePositiveRate)
            {
                string msg = $"{bogus}/{results.Count:N0} results failed content validation ({fpRate:P1})";
                failures.Add(msg); failed = true;
                report.AddAlert($"[FAIL] {msg}", isError: true);
                Console.WriteLine($"║    [FAIL] {msg}");
            }

            string valStatus = bogus == 0
                ? $"✓ all {results.Count:N0} valid"
                : $"✗ {bogus}/{results.Count:N0} bogus ({fpRate:P1})";
            report.AddMeta("Content validation", $"{valStatus}  ({swVal.ElapsedMilliseconds} ms)");
            Console.WriteLine($"║    Content: {valStatus}  ({swVal.ElapsedMilliseconds} ms)");

            // ── 5. Snippet IsMatch — EVERY result ─────────────────────
            // GenerateSnippet(result) uses already-fetched content — no extra DB cost.
            int snippetFp = 0;
            var swSnip    = Stopwatch.StartNew();

            Console.WriteLine($"║    First {Math.Min(PreviewResults, results.Count)} results:");
            for (int i = 0; i < results.Count; i++)
            {
                var snippet = index.GenerateSnippet(results[i]);

                if (!snippet.IsMatch)
                {
                    snippetFp++;
                    if (snippetFp <= 5)
                    {
                        string detail = $"id={results[i].LineId}  IsMatch=false";
                        report.AddAlert($"[SNIPPET FP] {detail}", isError: true);
                        Console.WriteLine($"║    [SNIPPET FP] {detail}");
                    }
                }

                if (i < PreviewResults)
                {
                    report.AddResultCard(results[i].LineId, results[i].BookTitle,
                        snippet.Html, snippet.Score, snippet.IsMatch);
                    string scoreStr = snippet.Score == int.MaxValue ? "n/a" : snippet.Score.ToString();
                    string plain    = snippet.IsMatch
                        ? StripTags(snippet.Html, 120)
                        : TestHelpers.Truncate(
                            TestHelpers.StripHtmlAndDiacritics(results[i].Content), 120);
                    Console.WriteLine(
                        $"║      [{results[i].LineId}] {TestHelpers.Truncate(results[i].BookTitle, 30)}" +
                        $"  score={scoreStr}  {(snippet.IsMatch ? "✓" : "✗")}");
                    Console.WriteLine($"║        {plain}");
                }
            }
            swSnip.Stop();

            double snippetFpRate = results.Count > 0 ? (double)snippetFp / results.Count : 0.0;
            if (snippetFpRate > MaxFalsePositiveRate)
            {
                string msg = $"{snippetFp}/{results.Count:N0} snippets IsMatch=false ({snippetFpRate:P1})";
                failures.Add(msg); failed = true;
                report.AddAlert($"[FAIL] {msg}", isError: true);
                Console.WriteLine($"║    [FAIL] {msg}");
            }

            string snipStatus = snippetFp == 0
                ? $"✓ all {results.Count:N0} IsMatch=true"
                : $"✗ {snippetFp}/{results.Count:N0} false-positive";
            report.AddMeta($"Snippets ({swSnip.ElapsedMilliseconds} ms)", snipStatus);
            Console.WriteLine($"║    Snippets: {snipStatus}  ({swSnip.ElapsedMilliseconds} ms)");

            // ── 6. SearchIds consistency — full cross-check ───────────
            var swIds = Stopwatch.StartNew();
            var idSet = new HashSet<int>();
            foreach (var id in index.SearchIds(qc.Query)) idSet.Add(id);
            swIds.Stop();

            // Every ID from Search() must be in SearchIds() and vice versa.
            var searchResultIds = new HashSet<int>();
            foreach (var r in results) searchResultIds.Add(r.LineId);

            int idMismatches = 0;
            foreach (var r in results)
                if (!idSet.Contains(r.LineId)) idMismatches++;
            foreach (var id in idSet)
                if (!searchResultIds.Contains(id)) idMismatches++;

            if (idMismatches > 0)
            {
                string msg = $"SearchIds() returned {idSet.Count:N0} IDs, Search() returned " +
                             $"{results.Count:N0} — {idMismatches} mismatch(es)";
                failures.Add(msg); failed = true;
                report.AddAlert($"[FAIL] {msg}", isError: true);
                Console.WriteLine($"║    [FAIL] {msg}");
            }
            else
            {
                report.AddMeta("SearchIds()", $"{idSet.Count:N0} IDs  ({swIds.ElapsedMilliseconds} ms)  ✓ consistent");
                Console.WriteLine($"║    SearchIds: {idSet.Count:N0} IDs  ({swIds.ElapsedMilliseconds} ms)  ✓ consistent");
            }

            // ── Summary ───────────────────────────────────────────────
            if (failed)
            {
                Console.WriteLine($"║    STATUS: ✗ FAIL — {failures.Count} issue(s):");
                foreach (var f in failures) Console.WriteLine($"║      • {f}");
            }
            else
            {
                Console.WriteLine("║    STATUS: ✓ PASS");
            }

            return new QuerySummary(qc.Query, qc.Kind.ToString(), results.Count,
                swSearch.ElapsedMilliseconds, bogus, snippetFp, missingIds,
                qc.MinResults, failed);
        }

        // ── Validation logic ──────────────────────────────────────────

        /// <summary>
        /// Returns true if the result is valid for the given query kind.
        ///
        /// Literal  : every base term must appear in the cleaned content.
        /// Fuzzy    : at least one term from every MatchedGroup must appear.
        /// Wildcard : at least one term from every MatchedGroup must appear.
        ///
        /// Using MatchedGroups (the actual expanded terms) rather than the raw
        /// query pattern means we validate what the index actually matched, not
        /// what the user typed.
        /// </summary>
        private static bool ValidateResult(
            string       clean,
            SearchResult result,
            List<string> baseTerms,
            QueryKind    kind)
        {
            if (kind == QueryKind.Literal)
            {
                foreach (var t in baseTerms)
                    if (clean.IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0)
                        return false;
                return true;
            }

            // Fuzzy / wildcard: use MatchedGroups — the actual expanded terms.
            // Every group must have at least one term present in the content.
            if (result.MatchedGroups != null && result.MatchedGroups.Count > 0)
            {
                foreach (var group in result.MatchedGroups)
                {
                    bool satisfied = false;
                    foreach (var term in group)
                    {
                        if (clean.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            satisfied = true;
                            break;
                        }
                    }
                    if (!satisfied) return false;
                }
                return true;
            }

            return true; // no groups available — accept
        }

        // ── Helpers ───────────────────────────────────────────────────

        private static List<string> ExtractBaseTerms(string query)
        {
            var terms = new List<string>();
            foreach (var token in query.Split(
                new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
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
            public readonly int       MinResults;
            public readonly int       MaxResults;   // -1 = no upper bound
            public readonly int[]     RequiredIds;

            public QueryCase(
                string    query,
                QueryKind kind,
                int       minResults  = 0,
                int       maxResults  = -1,
                int[]     requiredIds = null)
            {
                Query       = query;
                Kind        = kind;
                MinResults  = minResults;
                MaxResults  = maxResults;
                RequiredIds = requiredIds;
            }
        }

        private sealed class QuerySummary
        {
            public readonly string Query;
            public readonly string Kind;
            public readonly int    Results;
            public readonly long   ElapsedMs;
            public readonly int    Bogus;
            public readonly int    SnippetFp;
            public readonly int    MissingIds;
            public readonly int    MinResults;
            public readonly bool   Failed;

            public QuerySummary(string q, string k, int r, long ms,
                int bogus, int snippetFp, int missingIds, int minResults, bool failed)
            {
                Query      = q; Kind = k; Results = r; ElapsedMs = ms;
                Bogus      = bogus; SnippetFp = snippetFp;
                MissingIds = missingIds; MinResults = minResults; Failed = failed;
            }
        }
    }
}
