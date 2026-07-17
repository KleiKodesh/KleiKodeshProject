using FtsLib.Indexing;
using FtsLib.Search;
using FtsLib.SeforimDb;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Sweeps the wildcard/fuzzy expansion cap (MaxExpandedTerms) across a range
    /// of values and reports, for each query and cap:
    ///   - how many concrete terms the query expands to (post-cap)
    ///   - how many line IDs the intersection returns
    ///   - percentage of results kept relative to the uncapped run
    ///   - total SearchIds wall time
    ///
    /// The sweep runs uncapped FIRST (cold — this also warms the OS file cache),
    /// then each cap descending, then uncapped AGAIN so the baseline time can be
    /// read warm. Result counts are what matter for the "results kept" column;
    /// times should be compared between warm runs only.
    ///
    /// Usage:
    ///   FtsLibTest.exe capsweep [tier] [query ...]
    ///   FtsLibTest.exe capsweep [tier] --defaults [query ...]
    ///
    /// --defaults skips the sweep and runs each query once with the SHIPPED
    /// default caps untouched — use it to verify what production will do.
    ///
    /// Default tier: full.  Default queries: *כי* *יצח* יצחק~1 יצחק~2 אמר~2
    /// (each argument is ONE query; use quotes if a query contains spaces).
    /// </summary>
    internal static class CapSweepTest
    {
        private static readonly int[] Caps = { 5000, 3000, 2000, 1000, 500, 250, 100, 50 };

        private static readonly string[] DefaultQueries =
        {
            "*כי*",
            "*יצח*",
            "יצחק~1",
            "יצחק~2",
            "אמר~2",
        };

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string tierLabel = args.Length > 1 ? args[1] : "full";
            string label;
            try   { label = TestHelpers.ResolveTier(tierLabel).Label; }
            catch (ArgumentException ex) { Console.WriteLine(ex.Message); return; }

            bool defaultsOnly = false;
            int  queryStart   = 2;
            if (args.Length > 2 && args[2] == "--defaults")
            {
                defaultsOnly = true;
                queryStart   = 3;
            }

            string[] queries;
            if (args.Length > queryStart)
            {
                queries = new string[args.Length - queryStart];
                Array.Copy(args, queryStart, queries, 0, queries.Length);
            }
            else
            {
                queries = DefaultQueries;
            }

            string indexDir = TestHelpers.IndexDir(label);
            if (!Directory.Exists(indexDir) ||
                Directory.GetFiles(indexDir, "seg_*.dat").Length == 0)
            {
                Console.WriteLine($"No index found at: {indexDir}");
                Console.WriteLine($"Run 'build {label}' first.");
                return;
            }

            string dbPath = BuildTest.ResolveDbPath();
            var index = new SeforimIndex(indexDir, dbPath);

            if (defaultsOnly)
            {
                Console.WriteLine();
                Console.WriteLine($"╔══ DEFAULT-CAP CHECK ══ tier={label.ToUpper()} ══");
                Console.WriteLine($"║  Wildcard MaxExpandedTerms = {HebrewWildcardExpander.MaxExpandedTerms}");
                Console.WriteLine($"║  Fuzzy    MaxExpandedTerms = {FuzzyExpander.MaxExpandedTerms}");
                Console.WriteLine($"╚═══════════════════════");

                foreach (var query in queries)
                {
                    var sw = Stopwatch.StartNew();
                    long ids = 0;
                    foreach (var _ in index.SearchIds(query)) ids++;
                    sw.Stop();

                    var swSnip = Stopwatch.StartNew();
                    foreach (var r in index.Search(query, cap: 2000))
                        index.GenerateSnippet(r);
                    swSnip.Stop();

                    Console.WriteLine($"   \"{query}\" → {ids:N0} ids  ({sw.ElapsedMilliseconds:N0} ms, snip2k {swSnip.ElapsedMilliseconds:N0} ms)");
                }
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"╔══ EXPANSION-CAP SWEEP ══ tier={label.ToUpper()} ══");
            Console.WriteLine($"║  Caps: uncapped(cold), {string.Join(", ", Caps)}, uncapped(warm)");
            Console.WriteLine($"╚═════════════════════════");

            int savedWild  = HebrewWildcardExpander.MaxExpandedTerms;
            int savedFuzzy = FuzzyExpander.MaxExpandedTerms;
            try
            {
                foreach (var query in queries)
                    SweepQuery(index, indexDir, query);
            }
            finally
            {
                HebrewWildcardExpander.MaxExpandedTerms = savedWild;
                FuzzyExpander.MaxExpandedTerms          = savedFuzzy;
            }
        }

        // ── Per-query sweep ───────────────────────────────────────────

        private static void SweepQuery(SeforimIndex index, string indexDir, string query)
        {
            Console.WriteLine();
            Console.WriteLine($"══ QUERY: \"{query}\" ══");

            // Uncapped expansion size, measured directly on the expander so the
            // table can show "terms used" per cap (min(cap, uncapped)).
            int uncappedTerms = MeasureUncappedTermCount(indexDir, query, out long expandMs);
            Console.WriteLine($"   uncapped expansion: {uncappedTerms:N0} term(s)  (expand-only: {expandMs:N0} ms)");

            // Sweep order: 0 (cold) → caps descending → 0 (warm baseline).
            var caps = new List<int> { 0 };
            foreach (var c in Caps)
                if (c < uncappedTerms) caps.Add(c);   // skip caps that change nothing
            caps.Add(0);

            long baselineCount = -1;

            Console.WriteLine();
            Console.WriteLine("   cap      terms     ids         kept     ids time   snip2k time");
            Console.WriteLine("   ───────  ────────  ──────────  ───────  ─────────  ───────────");

            for (int i = 0; i < caps.Count; i++)
            {
                int cap = caps[i];
                HebrewWildcardExpander.MaxExpandedTerms = cap;
                FuzzyExpander.MaxExpandedTerms          = cap;

                var sw = Stopwatch.StartNew();
                long ids = 0;
                foreach (var _ in index.SearchIds(query)) ids++;
                sw.Stop();

                // End-to-end proxy: fetch the first 2,000 results and build a
                // snippet for each. Snippet cost scales with the number of
                // expanded terms (term→group map rebuilt per line), so this is
                // where the cap actually pays off.
                var swSnip = Stopwatch.StartNew();
                int snipped = 0;
                foreach (var r in index.Search(query, cap: 2000))
                {
                    index.GenerateSnippet(r);
                    snipped++;
                }
                swSnip.Stop();

                if (baselineCount < 0) baselineCount = ids;

                int    termsUsed = cap == 0 ? uncappedTerms : Math.Min(cap, uncappedTerms);
                string kept      = baselineCount == 0 ? "  n/a"
                                 : $"{100.0 * ids / baselineCount,6:F1}%";
                string capLabel  = cap == 0
                                 ? (i == 0 ? "0 cold" : "0 warm")
                                 : cap.ToString();

                Console.WriteLine($"   {capLabel,-7}  {termsUsed,8:N0}  {ids,10:N0}  {kept,7}  {sw.ElapsedMilliseconds,6:N0} ms  {swSnip.ElapsedMilliseconds,8:N0} ms");
            }

            HebrewWildcardExpander.MaxExpandedTerms = 0;
            FuzzyExpander.MaxExpandedTerms          = 0;
        }

        /// <summary>
        /// Expands every group of <paramref name="query"/> uncapped and returns the
        /// total number of concrete terms (summed across groups — the sweep queries
        /// are single-token, so this is just that token's expansion size).
        /// </summary>
        private static int MeasureUncappedTermCount(string indexDir, string query, out long elapsedMs)
        {
            HebrewWildcardExpander.MaxExpandedTerms = 0;
            FuzzyExpander.MaxExpandedTerms          = 0;

            var parsed = QueryParser.Parse(query);
            if (parsed.IsEmpty) { elapsedMs = 0; return 0; }

            var datFiles = Directory.GetFiles(indexDir, "seg_*.dat");
            Array.Sort(datFiles);
            var segments = new List<SegmentHandle>();
            foreach (var dat in datFiles)
            {
                string db = Path.ChangeExtension(dat, ".db");
                if (File.Exists(db)) segments.Add(new SegmentHandle(dat, db));
            }

            try
            {
                var sw = Stopwatch.StartNew();
                int total = 0;
                foreach (var group in parsed.Groups)
                {
                    foreach (var alt in group.Alternatives)
                    {
                        if (alt.IsFuzzy)
                            total += FuzzyExpander.Expand(alt.Pattern, alt.FuzzyDistance, segments).Count;
                        else if (alt.IsWildcard)
                            total += HebrewWildcardExpander.Expand(alt.Pattern, segments).Count;
                        else
                            total += 1;
                    }
                }
                sw.Stop();
                elapsedMs = sw.ElapsedMilliseconds;
                return total;
            }
            finally
            {
                foreach (var s in segments) s.Dispose();
            }
        }
    }
}
