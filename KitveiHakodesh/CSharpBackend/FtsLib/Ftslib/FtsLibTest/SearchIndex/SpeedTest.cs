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
    /// Speed breakdown test. Measures what the user actually experiences:
    ///
    ///   "Time to first batch" — how long until the first 200 results are ready.
    ///   "Total time"          — how long to process all results with snippets.
    ///
    /// Also breaks down each pipeline phase independently:
    ///   A  Expansion  — fuzzy/wildcard LIKE scans against term_index
    ///   B  Index      — posting-list intersection → list of IDs (no DB)
    ///   C  Fetch      — read content + bookTitle for all IDs from SQLite
    ///   D  Snippets   — tokenize + proximity window + render (no DB, uses content)
    ///
    /// Usage:
    ///   FtsLibTest.exe speed [tier]
    /// </summary>
    internal static class SpeedTest
    {
        private const int FirstBatchSize = 200;

        private static readonly string[] Queries =
        {
            "כי ביצחק",
            "תורה מצוה",
            "אברהם יצחק יעקב",
            "משה* תורה",
            "*ישראל",
            "בני*",
            "כי יצחק~",
            "יסראל~2",
        };

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string label = args.Length > 1 ? args[1] : "500k";
            try { label = TestHelpers.ResolveTier(label).Label; }
            catch (ArgumentException ex) { Console.WriteLine(ex.Message); return; }

            string indexDir = TestHelpers.IndexDir(label);
            string dbPath   = BuildTest.ResolveDbPath();

            if (!Directory.Exists(indexDir))
            { Console.WriteLine($"No index at: {indexDir}"); return; }

            Console.WriteLine();
            Console.WriteLine($"╔══ SPEED BREAKDOWN — {label.ToUpper()} ══");
            Console.WriteLine($"║  Index : {indexDir}");
            Console.WriteLine($"║  First-batch size: {FirstBatchSize}");
            Console.WriteLine();

            // Warm up the DB page cache
            Console.WriteLine("║  Warming up DB cache…");
            var index = new SeforimIndex(indexDir, dbPath);
            int warmCount = 0;
            var swWarm = Stopwatch.StartNew();
            foreach (var _ in index.Search("תורה")) warmCount++;
            swWarm.Stop();
            Console.WriteLine($"║  Warm-up: {warmCount:N0} results  ({swWarm.ElapsedMilliseconds} ms)");
            Console.WriteLine();

            // ── Phase breakdown header ────────────────────────────────
            Console.WriteLine($"  {"Query",-28}  {"IDs",7}  {"A:Expand",9}  {"B:Index",8}  {"C:Fetch",8}  {"D:Snip/all",11}  {"1st batch",10}  {"Total",8}");
            Console.WriteLine($"  {new string('─',28)}  {new string('─',7)}  {new string('─',9)}  {new string('─',8)}  {new string('─',8)}  {new string('─',11)}  {new string('─',10)}  {new string('─',8)}");

            foreach (var query in Queries)
            {
                var parsed = QueryParser.Parse(query);

                // ── Phase A: expansion only ───────────────────────────
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
                        HebrewWildcardExpander.Expand(group.Pattern, segments);
                }
                swA.Stop();
                long expandMs = swA.ElapsedMilliseconds;
                foreach (var s in segments) s.Dispose();

                // ── Phase B: index search (IDs only, no DB) ───────────
                var swB = Stopwatch.StartNew();
                var ids = new List<int>();
                foreach (var id in index.SearchIds(query)) ids.Add(id);
                swB.Stop();
                long indexMs = swB.ElapsedMilliseconds;

                // ── Phase C: DB fetch (all content) ───────────────────
                long fetchMs;
                using (var db = new ZayitDb(dbPath))
                {
                    var swC = Stopwatch.StartNew();
                    int n = 0;
                    foreach (var _ in db.FetchSearchResults(ids)) n++;
                    swC.Stop();
                    fetchMs = swC.ElapsedMilliseconds;
                }

                // ── Phase D: snippet for ALL results ─────────────────
                // This is the worst case — every result gets a snippet.
                var swD = Stopwatch.StartNew();
                int snippets = 0;
                foreach (var result in index.Search(query))
                {
                    index.GenerateSnippet(result);
                    snippets++;
                }
                swD.Stop();
                long snippetAllMs = swD.ElapsedMilliseconds;

                // ── Time to first batch (realistic UX metric) ─────────
                // How long until the user sees the first FirstBatchSize results
                // with snippets — this is what subsecond means in practice.
                var swFirst = Stopwatch.StartNew();
                int firstCount = 0;
                long firstBatchMs = -1;
                foreach (var result in index.Search(query))
                {
                    index.GenerateSnippet(result);
                    firstCount++;
                    if (firstCount == FirstBatchSize)
                    {
                        swFirst.Stop();
                        firstBatchMs = swFirst.ElapsedMilliseconds;
                        break;
                    }
                }
                if (firstBatchMs < 0)
                {
                    swFirst.Stop();
                    firstBatchMs = swFirst.ElapsedMilliseconds; // fewer than batch size
                }

                Console.WriteLine(
                    $"  {TestHelpers.Truncate(query, 28),-28}  {ids.Count,7:N0}" +
                    $"  {expandMs,7} ms" +
                    $"  {indexMs,6} ms" +
                    $"  {fetchMs,6} ms" +
                    $"  {snippetAllMs,9} ms" +
                    $"  {firstBatchMs,8} ms" +
                    $"  {snippetAllMs,6} ms");
            }

            Console.WriteLine();
            Console.WriteLine("  Legend:");
            Console.WriteLine("    A:Expand   = fuzzy/wildcard term expansion (SQLite LIKE scans)");
            Console.WriteLine("    B:Index    = posting-list intersection, returns IDs only");
            Console.WriteLine("    C:Fetch    = read all content rows from SQLite");
            Console.WriteLine("    D:Snip/all = generate snippets for ALL results (worst case)");
            Console.WriteLine("    1st batch  = time until first 200 results with snippets (UX latency)");
            Console.WriteLine("    Total      = full pipeline with all snippets");
            Console.WriteLine();
            Console.WriteLine("╚══ DONE ══");
        }
    }
}
