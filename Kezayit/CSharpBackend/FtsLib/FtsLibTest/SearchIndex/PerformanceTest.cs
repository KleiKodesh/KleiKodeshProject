using FtsLib.Core;
using FtsLib.Misc;
using FtsLib.Seforim;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Battery of performance tests covering every feature the user can exercise
    /// in the FtsLibDemo UI:
    ///
    ///   • Literal AND search (single word, multi-word, long phrase)
    ///   • Wildcard search  (prefix *, suffix *, infix *, multi-wildcard)
    ///   • Fuzzy search     (distance 1, 2, 3)
    ///   • OR groups        (a | b, mixed with AND, chained)
    ///   • Word-distance filter  (tight / loose / zero)
    ///   • Ordered search   (requireOrdered = true)
    ///   • SearchIds()      (ID-only path, no DB fetch)
    ///   • Snippet generation (GenerateSnippet per result)
    ///   • High-cardinality queries (many results)
    ///   • Zero-result queries (no match)
    ///   • Edge-case queries (single char, nikud, very long pattern)
    ///
    /// For each case the test records:
    ///   A  Expansion  ms  — fuzzy/wildcard LIKE scans
    ///   B  Index      ms  — posting-list intersection → IDs
    ///   C  Fetch      ms  — SQLite content fetch for all IDs
    ///   D  Snippet    ms  — snippet generation for all results
    ///   E  1st-batch  ms  — time to first 200 results with snippets (UX latency)
    ///   F  Total      ms  — full pipeline A+B+C+D
    ///
    /// Usage:
    ///   FtsLibTest.exe perf [tier]   tier = 500k | 1m | 3m | full  (default: full)
    /// </summary>
    internal static class PerformanceTest
    {
        private const int FirstBatchSize = 200;

        // ── Test cases ────────────────────────────────────────────────
        // Each entry: (label, query, maxWordDistance, requireOrdered)
        // maxWordDistance = int.MaxValue means "no filter" (UI default is 10,
        // but for perf tests we want to see raw throughput).

        private static readonly PerfCase[] Cases =
        {
            // ── Single-word literals ──────────────────────────────────
            new PerfCase("literal / single common word",
                "תורה"),

            new PerfCase("literal / single rare word",
                "שויתי"),

            new PerfCase("literal / single word (English)",
                "torah"),

            // ── Multi-word AND literals ───────────────────────────────
            new PerfCase("literal / 2-word AND",
                "כי ביצחק"),

            new PerfCase("literal / 2-word AND (common)",
                "תורה מצוה"),

            new PerfCase("literal / 3-word AND",
                "אברהם יצחק יעקב"),

            new PerfCase("literal / 4-word AND",
                "אבל בן אין לה"),

            new PerfCase("literal / 5-word AND (long phrase)",
                "וידבר משה כן אל בני"),

            new PerfCase("literal / 6-word AND (very long phrase)",
                "שויתי לנגדי תמיד כי מימיני בל"),

            // ── Zero-result queries ───────────────────────────────────
            new PerfCase("literal / zero results (nonexistent word)",
                "nonexistentword123xyz"),

            new PerfCase("literal / zero results (impossible AND)",
                "nonexistentword123 תורה"),

            // ── Wildcard searches ─────────────────────────────────────
            new PerfCase("wildcard / prefix (short anchor)",
                "תור*"),

            new PerfCase("wildcard / prefix (longer anchor)",
                "תורה*"),

            new PerfCase("wildcard / suffix",
                "*ישראל"),

            new PerfCase("wildcard / infix",
                "*אבר*"),

            new PerfCase("wildcard / prefix + AND literal",
                "משה* תורה"),

            new PerfCase("wildcard / suffix + AND literal",
                "*ישראל תורה"),

            new PerfCase("wildcard / high-cardinality prefix (very short anchor)",
                "בני*"),

            new PerfCase("wildcard / optional char (?)",
                "תור?ה"),

            new PerfCase("wildcard / multiple optional chars",
                "תו?ר?ה"),

            // ── Fuzzy searches ────────────────────────────────────────
            new PerfCase("fuzzy / distance 1 (default)",
                "יצחק~"),

            new PerfCase("fuzzy / distance 1 + AND literal",
                "כי יצחק~"),

            new PerfCase("fuzzy / distance 2",
                "יסראל~2"),

            new PerfCase("fuzzy / distance 2 + AND literal",
                "כי יסראל~2"),

            new PerfCase("fuzzy / distance 3",
                "ישראל~3"),

            new PerfCase("fuzzy / 3-letter word distance 1",
                "אנב~"),

            new PerfCase("fuzzy / common word distance 1",
                "תארה~"),

            new PerfCase("fuzzy / common word distance 1 + AND",
                "תארה~ מצוה"),

            // ── OR groups ─────────────────────────────────────────────
            new PerfCase("OR / two alternatives",
                "תורה | מצוה"),

            new PerfCase("OR / three alternatives",
                "אברהם | יצחק | יעקב"),

            new PerfCase("OR / OR group AND literal",
                "תורה | מצוה כי"),

            new PerfCase("OR / literal AND OR group",
                "כי תורה | מצוה"),

            new PerfCase("OR / wildcard in OR group",
                "תור* | מצוה"),

            new PerfCase("OR / fuzzy in OR group",
                "יצחק~ | יעקב"),

            new PerfCase("OR / mixed wildcard + fuzzy in OR group",
                "תור* | יצחק~"),

            new PerfCase("OR / chained: a | b | c AND d",
                "אברהם | יצחק | יעקב תורה"),

            // ── Word-distance filter ──────────────────────────────────
            new PerfCase("word-dist / tight window (maxDist=0, adjacent only)",
                "כי ביצחק",
                maxWordDistance: 0),

            new PerfCase("word-dist / narrow window (maxDist=2)",
                "כי ביצחק",
                maxWordDistance: 2),

            new PerfCase("word-dist / default window (maxDist=10)",
                "כי ביצחק",
                maxWordDistance: 10),

            new PerfCase("word-dist / wide window (maxDist=50)",
                "כי ביצחק",
                maxWordDistance: 50),

            new PerfCase("word-dist / no filter (maxDist=int.MaxValue)",
                "כי ביצחק",
                maxWordDistance: int.MaxValue),

            new PerfCase("word-dist / 3-word AND tight (maxDist=0)",
                "אברהם יצחק יעקב",
                maxWordDistance: 0),

            new PerfCase("word-dist / 3-word AND default (maxDist=10)",
                "אברהם יצחק יעקב",
                maxWordDistance: 10),

            // ── Ordered search ────────────────────────────────────────
            new PerfCase("ordered / 2-word ordered",
                "כי ביצחק",
                requireOrdered: true),

            new PerfCase("ordered / 3-word ordered",
                "אברהם יצחק יעקב",
                requireOrdered: true),

            new PerfCase("ordered / 5-word ordered",
                "וידבר משה כן אל בני",
                requireOrdered: true),

            new PerfCase("ordered / 2-word ordered + tight distance",
                "כי ביצחק",
                maxWordDistance: 2,
                requireOrdered: true),

            new PerfCase("ordered / fuzzy + ordered",
                "כי יצחק~",
                requireOrdered: true),

            new PerfCase("ordered / wildcard + ordered",
                "משה* תורה",
                requireOrdered: true),

            // ── SearchIds() — ID-only path ────────────────────────────
            new PerfCase("searchids / single word",
                "תורה",
                idsOnly: true),

            new PerfCase("searchids / 2-word AND",
                "כי ביצחק",
                idsOnly: true),

            new PerfCase("searchids / wildcard",
                "בני*",
                idsOnly: true),

            new PerfCase("searchids / fuzzy",
                "יצחק~",
                idsOnly: true),

            // ── High-cardinality (stress) ─────────────────────────────
            new PerfCase("stress / very common single word",
                "כי"),

            new PerfCase("stress / very common 2-word AND",
                "כי לא"),

            new PerfCase("stress / high-cardinality wildcard",
                "כ*"),

            // ── Edge cases ────────────────────────────────────────────
            new PerfCase("edge / nikud in query (stripped by parser)",
                "שָׁלוֹם"),

            new PerfCase("edge / leading pipe ignored",
                "| תורה"),

            new PerfCase("edge / trailing pipe ignored",
                "תורה |"),

            new PerfCase("edge / double pipe treated as one",
                "תורה || מצוה"),

            new PerfCase("edge / fuzzy + wildcard on same token (wildcard wins)",
                "תור*~"),

            new PerfCase("edge / single-char token (dropped by tokenizer)",
                "א"),

            new PerfCase("edge / query with only pipes",
                "| | |"),
        };

        // ── Entry point ───────────────────────────────────────────────

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string tierLabel = args.Length > 1 ? args[1] : "full";
            string label;
            try   { label = TestHelpers.ResolveTier(tierLabel).Label; }
            catch (ArgumentException ex) { Console.WriteLine(ex.Message); return; }

            string indexDir = TestHelpers.IndexDir(label);
            string dbPath   = BuildTest.ResolveDbPath();

            if (!Directory.Exists(indexDir) ||
                Directory.GetFiles(indexDir, "seg_*.dat").Length == 0)
            {
                Console.WriteLine($"No index at: {indexDir}");
                Console.WriteLine($"Run 'build {label}' first.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"╔══ PERFORMANCE TEST — {label.ToUpper()} ══");
            Console.WriteLine($"║  Index : {indexDir}");
            Console.WriteLine($"║  DB    : {dbPath}");
            Console.WriteLine($"║  Cases : {Cases.Length}");
            Console.WriteLine($"║  First-batch size: {FirstBatchSize}");
            Console.WriteLine();

            var index = new SeforimIndex(indexDir, dbPath);

            // ── Warm-up ───────────────────────────────────────────────
            Console.WriteLine("║  Warming up (תורה)…");
            int warmCount = 0;
            var swWarm = Stopwatch.StartNew();
            foreach (var _ in index.Search("תורה")) warmCount++;
            swWarm.Stop();
            Console.WriteLine($"║  Warm-up: {warmCount:N0} results  ({swWarm.ElapsedMilliseconds} ms)");
            Console.WriteLine();

            // ── Run all cases ─────────────────────────────────────────
            var rows = new List<PerfRow>();
            foreach (var c in Cases)
            {
                var row = RunCase(index, indexDir, dbPath, c);
                rows.Add(row);
                PrintRow(row);
            }

            // ── Summary table ─────────────────────────────────────────
            PrintSummary(rows, label);

            // ── HTML report ───────────────────────────────────────────
            string path   = BuildTest.TempReportPath("perf", label);
            var    report = BuildHtmlReport(rows, label, indexDir, dbPath);
            report.SaveAndOpen(path);
        }

        // ── Single case runner ────────────────────────────────────────

        private static PerfRow RunCase(
            SeforimIndex index,
            string       indexDir,
            string       dbPath,
            PerfCase     c)
        {
            // ── Phase A: expansion only ───────────────────────────────
            var parsed   = QueryParser.Parse(c.Query);
            var datFiles = Directory.GetFiles(indexDir, "seg_*.dat");
            System.Array.Sort(datFiles);
            var segments = new List<SegmentHandle>();
            foreach (var dat in datFiles)
            {
                string db2 = Path.ChangeExtension(dat, ".db");
                if (File.Exists(db2)) segments.Add(new SegmentHandle(dat, db2));
            }

            var swA = Stopwatch.StartNew();
            foreach (var group in parsed.Groups)
            {
                if (group.IsFuzzy)
                    FuzzyExpander.Expand(group.Pattern, group.FuzzyDistance, segments);
                else if (group.IsWildcard)
                    WildcardExpander.Expand(group.Pattern, segments);
            }
            swA.Stop();
            long expandMs = swA.ElapsedMilliseconds;
            foreach (var s in segments) s.Dispose();

            // ── Phase B: index search (IDs only) ──────────────────────
            var swB = Stopwatch.StartNew();
            var ids = new List<int>();
            foreach (var id in index.SearchIds(c.Query)) ids.Add(id);
            swB.Stop();
            long indexMs = swB.ElapsedMilliseconds;

            // IDs-only mode: skip C, D, E
            if (c.IdsOnly)
            {
                return new PerfRow(c.Label, c.Query, ids.Count,
                    expandMs, indexMs, -1, -1, -1, -1,
                    c.MaxWordDistance, c.RequireOrdered, idsOnly: true);
            }

            // ── Phase C: DB fetch (all content) ───────────────────────
            long fetchMs;
            using (var db = new ZayitDb(dbPath))
            {
                var swC = Stopwatch.StartNew();
                int n = 0;
                foreach (var _ in db.FetchSearchResults(ids)) n++;
                swC.Stop();
                fetchMs = swC.ElapsedMilliseconds;
            }

            // ── Phase D: full pipeline with snippets + word-dist filter
            // This mirrors exactly what SearchService.RunSearch does.
            var swD = Stopwatch.StartNew();
            int passed = 0, filtered = 0;
            foreach (var result in index.Search(c.Query))
            {
                var snippet = index.GenerateSnippet(result, c.RequireOrdered);
                if (!snippet.IsMatch || snippet.WordDistance > c.MaxWordDistance)
                { filtered++; continue; }
                passed++;
            }
            swD.Stop();
            long snippetAllMs = swD.ElapsedMilliseconds;

            // ── Phase E: time to first batch ──────────────────────────
            var swE = Stopwatch.StartNew();
            int firstCount = 0;
            long firstBatchMs = -1;
            foreach (var result in index.Search(c.Query))
            {
                var snippet = index.GenerateSnippet(result, c.RequireOrdered);
                if (!snippet.IsMatch || snippet.WordDistance > c.MaxWordDistance)
                    continue;
                firstCount++;
                if (firstCount == FirstBatchSize)
                {
                    swE.Stop();
                    firstBatchMs = swE.ElapsedMilliseconds;
                    break;
                }
            }
            if (firstBatchMs < 0)
            {
                swE.Stop();
                firstBatchMs = swE.ElapsedMilliseconds;
            }

            return new PerfRow(c.Label, c.Query, ids.Count,
                expandMs, indexMs, fetchMs, snippetAllMs, firstBatchMs,
                passed, c.MaxWordDistance, c.RequireOrdered,
                filtered: filtered);
        }

        // ── Console output ────────────────────────────────────────────

        private static void PrintRow(PerfRow r)
        {
            string idStr   = r.IdCount >= 0 ? $"{r.IdCount:N0}" : "—";
            string passStr = r.IdsOnly ? "(ids only)" : $"{r.PassedCount:N0}";
            string filtStr = r.IdsOnly ? "" : $"  filtered={r.Filtered:N0}";

            Console.WriteLine($"║  {TestHelpers.Truncate(r.Label, 52),-52}");
            Console.WriteLine($"║    query=\"{r.Query}\"" +
                (r.MaxWordDistance < int.MaxValue ? $"  maxDist={r.MaxWordDistance}" : "") +
                (r.RequireOrdered ? "  ordered" : ""));
            Console.WriteLine($"║    IDs={idStr}  passed={passStr}{filtStr}");

            if (r.IdsOnly)
            {
                Console.WriteLine($"║    A:expand={r.ExpandMs} ms  B:index={r.IndexMs} ms");
            }
            else
            {
                string fbStr = r.FirstBatchMs >= 0 ? $"{r.FirstBatchMs} ms" : "—";
                Console.WriteLine(
                    $"║    A:expand={r.ExpandMs} ms  B:index={r.IndexMs} ms" +
                    $"  C:fetch={r.FetchMs} ms  D:snip={r.SnippetAllMs} ms" +
                    $"  1st-batch={fbStr}");
            }
            Console.WriteLine("║");
        }

        private static void PrintSummary(List<PerfRow> rows, string label)
        {
            Console.WriteLine($"╠══ SUMMARY — {label.ToUpper()} ══");
            Console.WriteLine($"  {"Label",-52}  {"IDs",8}  {"Passed",8}  {"A:Exp",7}  {"B:Idx",7}  {"C:Ftch",7}  {"D:Snip",7}  {"1stBatch",9}");
            Console.WriteLine($"  {new string('─', 52)}  {new string('─', 8)}  {new string('─', 8)}  {new string('─', 7)}  {new string('─', 7)}  {new string('─', 7)}  {new string('─', 7)}  {new string('─', 9)}");

            foreach (var r in rows)
            {
                string idStr   = r.IdCount >= 0 ? $"{r.IdCount:N0}" : "—";
                string passStr = r.IdsOnly ? "ids-only" : $"{r.PassedCount:N0}";
                string expStr  = $"{r.ExpandMs} ms";
                string idxStr  = $"{r.IndexMs} ms";
                string ftchStr = r.FetchMs >= 0 ? $"{r.FetchMs} ms" : "—";
                string snipStr = r.SnippetAllMs >= 0 ? $"{r.SnippetAllMs} ms" : "—";
                string fbStr   = r.FirstBatchMs >= 0 ? $"{r.FirstBatchMs} ms" : "—";

                Console.WriteLine(
                    $"  {TestHelpers.Truncate(r.Label, 52),-52}  {idStr,8}  {passStr,8}" +
                    $"  {expStr,7}  {idxStr,7}  {ftchStr,7}  {snipStr,7}  {fbStr,9}");
            }

            Console.WriteLine();
            Console.WriteLine("╚══ PERFORMANCE TEST DONE ══");
        }

        // ── HTML report ───────────────────────────────────────────────

        private static HtmlReport BuildHtmlReport(
            List<PerfRow> rows,
            string        label,
            string        indexDir,
            string        dbPath)
        {
            var report = new HtmlReport($"Performance Report — {label.ToUpper()}");
            report.AddBanner($"FTS Performance Test  ·  Tier: {label.ToUpper()}");
            report.AddMeta("Generated",  DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AddMeta("Index dir",  indexDir);
            report.AddMeta("DB path",    dbPath);
            report.AddMeta("Cases",      rows.Count.ToString());
            report.AddMeta("1st-batch",  $"{FirstBatchSize} results");

            // ── Legend ────────────────────────────────────────────────
            report.AddSection("Legend");
            report.AddRawHtml(@"
<ul style='margin:8px 0 12px 20px;font-size:13px;line-height:1.8'>
  <li><b>IDs</b> — total matching line IDs from the posting-list intersection (no DB)</li>
  <li><b>Passed</b> — results that survived the word-distance / ordered filter</li>
  <li><b>Filtered</b> — results dropped by word-distance or ordered filter</li>
  <li><b>A:Expand</b> — fuzzy/wildcard LIKE scans against term_index SQLite</li>
  <li><b>B:Index</b> — posting-list intersection, returns IDs only (no DB)</li>
  <li><b>C:Fetch</b> — read all content rows from SQLite (worst case)</li>
  <li><b>D:Snip</b> — generate snippets for ALL results (worst case)</li>
  <li><b>1st-batch</b> — time until first 200 results with snippets (UX latency)</li>
  <li><b>—</b> — not measured for this case (ids-only mode skips C/D/E)</li>
</ul>");

            // ── Full table ────────────────────────────────────────────
            report.AddSection("All Cases");
            var headers = new[] { "Label", "Query", "Opts", "IDs", "Passed", "Filtered",
                                  "A:Expand", "B:Index", "C:Fetch", "D:Snip", "1st-batch" };
            var tableRows = new List<IReadOnlyList<string>>();

            foreach (var r in rows)
            {
                string opts = BuildOpts(r);
                tableRows.Add(new[]
                {
                    r.Label,
                    r.Query,
                    opts,
                    r.IdCount >= 0 ? $"{r.IdCount:N0}" : "—",
                    r.IdsOnly ? "ids-only" : $"{r.PassedCount:N0}",
                    r.IdsOnly ? "—" : $"{r.Filtered:N0}",
                    $"{r.ExpandMs} ms",
                    $"{r.IndexMs} ms",
                    r.FetchMs >= 0 ? $"{r.FetchMs} ms" : "—",
                    r.SnippetAllMs >= 0 ? $"{r.SnippetAllMs} ms" : "—",
                    r.FirstBatchMs >= 0 ? $"{r.FirstBatchMs} ms" : "—",
                });
            }

            report.AddTable(headers, tableRows);

            // ── Category breakdowns ───────────────────────────────────
            AddCategorySection(report, rows, "literal",   "Literal AND Searches");
            AddCategorySection(report, rows, "wildcard",  "Wildcard Searches");
            AddCategorySection(report, rows, "fuzzy",     "Fuzzy Searches");
            AddCategorySection(report, rows, "OR",        "OR Group Searches");
            AddCategorySection(report, rows, "word-dist", "Word-Distance Filter");
            AddCategorySection(report, rows, "ordered",   "Ordered Search");
            AddCategorySection(report, rows, "searchids", "SearchIds() — ID-only Path");
            AddCategorySection(report, rows, "stress",    "High-Cardinality / Stress");
            AddCategorySection(report, rows, "edge",      "Edge Cases");

            return report;
        }

        private static void AddCategorySection(
            HtmlReport    report,
            List<PerfRow> rows,
            string        prefix,
            string        heading)
        {
            var subset = rows.FindAll(r =>
                r.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (subset.Count == 0) return;

            report.AddSection(heading);
            var headers = new[] { "Label", "Query", "Opts", "IDs", "Passed",
                                  "A:Expand", "B:Index", "C:Fetch", "D:Snip", "1st-batch" };
            var tableRows = new List<IReadOnlyList<string>>();
            foreach (var r in subset)
            {
                tableRows.Add(new[]
                {
                    r.Label,
                    r.Query,
                    BuildOpts(r),
                    r.IdCount >= 0 ? $"{r.IdCount:N0}" : "—",
                    r.IdsOnly ? "ids-only" : $"{r.PassedCount:N0}",
                    $"{r.ExpandMs} ms",
                    $"{r.IndexMs} ms",
                    r.FetchMs >= 0 ? $"{r.FetchMs} ms" : "—",
                    r.SnippetAllMs >= 0 ? $"{r.SnippetAllMs} ms" : "—",
                    r.FirstBatchMs >= 0 ? $"{r.FirstBatchMs} ms" : "—",
                });
            }
            report.AddTable(headers, tableRows);
        }

        private static string BuildOpts(PerfRow r)
        {
            var parts = new List<string>();
            if (r.MaxWordDistance < int.MaxValue)
                parts.Add($"maxDist={r.MaxWordDistance}");
            if (r.RequireOrdered)
                parts.Add("ordered");
            if (r.IdsOnly)
                parts.Add("ids-only");
            return parts.Count > 0 ? string.Join(", ", parts) : "—";
        }

        // ── Value types ───────────────────────────────────────────────

        private sealed class PerfCase
        {
            public readonly string Label;
            public readonly string Query;
            public readonly int    MaxWordDistance;
            public readonly bool   RequireOrdered;
            public readonly bool   IdsOnly;

            public PerfCase(
                string label,
                string query,
                int    maxWordDistance = int.MaxValue,
                bool   requireOrdered  = false,
                bool   idsOnly         = false)
            {
                Label           = label;
                Query           = query;
                MaxWordDistance = maxWordDistance;
                RequireOrdered  = requireOrdered;
                IdsOnly         = idsOnly;
            }
        }

        private sealed class PerfRow
        {
            public readonly string Label;
            public readonly string Query;
            public readonly int    IdCount;
            public readonly long   ExpandMs;
            public readonly long   IndexMs;
            public readonly long   FetchMs;
            public readonly long   SnippetAllMs;
            public readonly long   FirstBatchMs;
            public readonly int    PassedCount;
            public readonly int    Filtered;
            public readonly int    MaxWordDistance;
            public readonly bool   RequireOrdered;
            public readonly bool   IdsOnly;

            public PerfRow(
                string label, string query, int idCount,
                long expandMs, long indexMs, long fetchMs,
                long snippetAllMs, long firstBatchMs,
                int passedCount,
                int maxWordDistance, bool requireOrdered,
                bool idsOnly = false, int filtered = 0)
            {
                Label           = label;
                Query           = query;
                IdCount         = idCount;
                ExpandMs        = expandMs;
                IndexMs         = indexMs;
                FetchMs         = fetchMs;
                SnippetAllMs    = snippetAllMs;
                FirstBatchMs    = firstBatchMs;
                PassedCount     = passedCount;
                MaxWordDistance = maxWordDistance;
                RequireOrdered  = requireOrdered;
                IdsOnly         = idsOnly;
                Filtered        = filtered;
            }
        }
    }
}
