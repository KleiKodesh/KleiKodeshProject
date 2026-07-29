using FtsLib.SeforimDb;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Tests the optional line-ID filter on SeforimIndex.Search / SearchIds.
    ///
    /// Part 1 — correctness: for every query and every filter shape,
    ///   SearchIds(query, filterIds: F) must equal SearchIds(query) ∩ F,
    ///   in the same ascending order. Shapes: null (no filter), empty set,
    ///   subset of matches, subset + noise IDs, disjoint set, full match set.
    ///
    /// Part 2 — speed: heavy queries with a small filter must beat both the
    ///   unfiltered search and the naive post-filter approach (drain everything,
    ///   intersect afterwards). This verifies the filter actually DRIVES the
    ///   intersection (candidate-driven / leap-frog driver) rather than merely
    ///   trimming its output.
    ///
    /// Usage:
    ///   FtsLibTest.exe filtertest [tier]
    /// </summary>
    internal static class FilterTest
    {
        private static readonly string[] CorrectnessQueries =
        {
            "כי ביצחק",          // two literals (AND)
            "תורה מצוה",         // two common literals
            "משה | אהרן",        // OR group
            "משה* תורה",         // wildcard + literal
            "בני*",              // single wildcard group
            "כי יצחק~",          // literal + fuzzy
        };

        private static readonly string[] SpeedQueries =
        {
            "תורה מצוה",         // plain AND — standard path, filter as driver
            "משה* תורה",         // wildcard + literal
            "*ישראל",            // suffix wildcard, huge expansion
            "בני*",              // prefix wildcard, huge expansion
            "*כי* ביצחק",        // the F03 pathology — 27.5k-term union
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
            Console.WriteLine($"╔══ ID-FILTER TEST — {label.ToUpper()} ══");
            Console.WriteLine($"║  Index : {indexDir}");
            Console.WriteLine();

            var index = new SeforimIndex(indexDir, dbPath);

            // Warm up caches so timings compare configurations, not cold IO.
            int warm = index.SearchIds("תורה").Count();
            Console.WriteLine($"║  Warm-up: {warm:N0} ids");
            Console.WriteLine();

            int failures = RunCorrectness(index);
            RunSpeed(index);

            Console.WriteLine();
            Console.WriteLine(failures == 0
                ? "╚══ DONE — ALL CORRECTNESS CHECKS PASSED ══"
                : $"╚══ DONE — {failures} CORRECTNESS FAILURES ══");
            if (failures != 0) Environment.ExitCode = 1;
        }

        // ── Correctness ───────────────────────────────────────────────

        private static int RunCorrectness(SeforimIndex index)
        {
            Console.WriteLine("── Correctness ─────────────────────────────────────────────");
            int failures = 0;

            foreach (var query in CorrectnessQueries)
            {
                var baseline    = index.SearchIds(query).ToList();
                var baselineSet = new HashSet<int>(baseline);

                if (baseline.Count == 0)
                {
                    Console.WriteLine($"  {TestHelpers.Truncate(query, 24),-24}  SKIP (no matches)");
                    continue;
                }

                // Filter shapes. Noise IDs are baseline ids shifted by +1 — some may
                // legitimately match; the expected set is computed per shape below,
                // so that is fine.
                var subsetSmall = TakeEveryNth(baseline, Math.Max(1, baseline.Count / 50));
                var subsetBig   = TakeEveryNth(baseline, 3);
                var noise       = subsetSmall.Select(id => id + 1).ToList();
                var withNoise   = subsetSmall.Concat(noise).ToList();
                // Beyond-max positive ids never match; negative ids are invalid
                // and must be ignored by the engine (not hang or throw).
                int beyondMax   = baseline[baseline.Count - 1] + 1_000_000;
                var disjoint    = subsetSmall.Select((_, i) => beyondMax + i).ToList();
                var negatives   = subsetSmall.Select(id => -id - 1).Concat(subsetSmall).ToList();

                var shapes = new (string name, List<int> filter)[]
                {
                    ("subset-small", subsetSmall),
                    ("subset-big",   subsetBig),
                    ("with-noise",   withNoise),
                    ("disjoint",     disjoint),
                    ("negatives",    negatives),
                    ("empty",        new List<int>()),
                    ("all-matches",  baseline),
                };

                var bad = new List<string>();
                foreach (var (name, filter) in shapes)
                {
                    var expected = filter.Where(baselineSet.Contains).Distinct().OrderBy(x => x).ToList();
                    var actual   = index.SearchIds(query, filterIds: filter).ToList();
                    if (!actual.SequenceEqual(expected))
                        bad.Add($"{name} (expected {expected.Count}, got {actual.Count})");
                }

                // Null filter must be identical to no filter at all.
                if (!index.SearchIds(query, filterIds: null).SequenceEqual(baseline))
                    bad.Add("null-filter");

                // Search (full results) must honor the filter too.
                var viaSearch = index.Search(query, filterIds: subsetSmall).Select(r => r.LineId).OrderBy(x => x).ToList();
                var expSearch = subsetSmall.Where(baselineSet.Contains).Distinct().OrderBy(x => x).ToList();
                if (!viaSearch.SequenceEqual(expSearch))
                    bad.Add($"via-Search (expected {expSearch.Count}, got {viaSearch.Count})");

                if (bad.Count == 0)
                    Console.WriteLine($"  {TestHelpers.Truncate(query, 24),-24}  OK    ({baseline.Count:N0} baseline ids)");
                else
                {
                    failures += bad.Count;
                    Console.WriteLine($"  {TestHelpers.Truncate(query, 24),-24}  FAIL  {string.Join("; ", bad)}");
                }
            }

            return failures;
        }

        private static List<int> TakeEveryNth(List<int> src, int n)
        {
            var result = new List<int>();
            for (int i = 0; i < src.Count; i += n) result.Add(src[i]);
            return result;
        }

        // ── Speed ─────────────────────────────────────────────────────

        private const int Iterations = 2; // per configuration, min reported

        private static void RunSpeed(SeforimIndex index)
        {
            Console.WriteLine();
            Console.WriteLine("── Speed (index phase only, ids — min of 2 runs) ──────────");
            Console.WriteLine($"  {"Query",-24}  {"Base IDs",9}  {"Unfilt",8}  {"PostFilt",8}  {"Filt/1k",8}  {"Filt/range",10}  {"Speedup",6}");
            Console.WriteLine($"  {new string('─',24)}  {new string('─',9)}  {new string('─',8)}  {new string('─',8)}  {new string('─',8)}  {new string('─',10)}  {new string('─',6)}");

            foreach (var query in SpeedQueries)
            {
                var baseline = index.SearchIds(query).ToList();
                if (baseline.Count == 0)
                {
                    Console.WriteLine($"  {TestHelpers.Truncate(query, 24),-24}  (no matches — skipped)");
                    continue;
                }

                // Small filter: ~1000 ids spread across the corpus — 500 known
                // matches + 500 shifted ids. Models "search inside one sefer".
                var smallHits = TakeEveryNth(baseline, Math.Max(1, baseline.Count / 500));
                var smallFilter = smallHits.Concat(smallHits.Select(id => id + 1)).Distinct().ToList();

                // Range filter: a contiguous slice of the id space (~10% of the
                // baseline's span, capped) — models "search inside a category".
                int lo = baseline[baseline.Count / 2];
                int width = Math.Min(200_000, Math.Max(1000, (baseline[baseline.Count - 1] - baseline[0]) / 10));
                var rangeFilter = new List<int>(width);
                for (int id = lo; id < lo + width; id++) rangeFilter.Add(id);

                var baselineSet = new HashSet<int>(baseline);

                long unfiltMs = TimeMin(() => index.SearchIds(query).Count());

                // Naive post-filtering: what a caller would do today — drain the
                // full result stream and intersect afterwards.
                var smallSet = new HashSet<int>(smallFilter);
                long postMs = TimeMin(() => index.SearchIds(query).Count(smallSet.Contains));

                List<int> gotSmall = null, gotRange = null;
                long filtSmallMs = TimeMin(() => (gotSmall = index.SearchIds(query, filterIds: smallFilter).ToList()).Count);
                long filtRangeMs = TimeMin(() => (gotRange = index.SearchIds(query, filterIds: rangeFilter).ToList()).Count);

                // Sanity: the timed runs must also be correct.
                bool ok =
                    gotSmall.SequenceEqual(smallFilter.Where(baselineSet.Contains).OrderBy(x => x)) &&
                    gotRange.SequenceEqual(rangeFilter.Where(baselineSet.Contains).OrderBy(x => x));

                string speedup = filtSmallMs > 0 ? $"{(double)unfiltMs / filtSmallMs:F1}x" : $">{unfiltMs}x";
                Console.WriteLine(
                    $"  {TestHelpers.Truncate(query, 24),-24}  {baseline.Count,9:N0}" +
                    $"  {unfiltMs,5} ms  {postMs,5} ms  {filtSmallMs,5} ms  {filtRangeMs,7} ms  {speedup,6}" +
                    (ok ? "" : "   ⚠ WRONG RESULTS"));
            }

            Console.WriteLine();
            Console.WriteLine("  Legend:");
            Console.WriteLine("    Unfilt     = SearchIds, no filter (full drain)");
            Console.WriteLine("    PostFilt   = SearchIds unfiltered, intersected afterwards (naive)");
            Console.WriteLine("    Filt/1k    = SearchIds with ~1k-id filter (integrated)");
            Console.WriteLine("    Filt/range = SearchIds with contiguous range filter (integrated)");
            Console.WriteLine("    Speedup    = Unfilt / Filt-1k");
        }

        private static long TimeMin(Func<int> run)
        {
            long best = long.MaxValue;
            for (int i = 0; i < Iterations; i++)
            {
                var sw = Stopwatch.StartNew();
                run();
                sw.Stop();
                if (sw.ElapsedMilliseconds < best) best = sw.ElapsedMilliseconds;
            }
            return best;
        }
    }
}
