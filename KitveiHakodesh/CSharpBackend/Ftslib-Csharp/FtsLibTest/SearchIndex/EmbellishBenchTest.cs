using FtsLib.SeforimDb;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FtsLibTest
{
    /// <summary>
    /// Measures the cost the short-snippet EMBELLISHMENT adds to the search snippet step.
    ///
    /// For each query, over a real result set, it times two things per rep and keeps the best:
    ///   BASE   = parallel GenerateSnippet for every hit           (what the service did before)
    ///   EMB    = the same, PLUS: detect snippets shorter than the target context, batch-fetch
    ///            their surrounding lines in ONE query, and re-render only those with
    ///            GenerateSnippetWithNeighbors                     (the new BuildBatch path)
    ///
    /// It also reports the fraction of hits that were short (drove the neighbor fetch), so the
    /// added cost can be read against how often it actually fires.
    ///
    ///   FtsLibTest.exe embellishbench [tier]   -- contextWords defaults to 30 (= the app default)
    /// </summary>
    internal static class EmbellishBenchTest
    {
        private static readonly string[] Queries =
        {
            "משה", "אלהים", "תורה", "רבי", "ארץ", "בית", "מלך", "עולם",
            "משה רבינו", "ארץ ישראל", "בית המקדש", "תלמוד תורה",
        };
        private const int Reps = 5;
        private const int ContextWords = 30;   // app default (searchContextMarginWords)
        private static int NeighborRadius = 3; // neighbor lines per side (overridable via arg 4)

        public static void Run(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            string tier = args.Length > 1 ? args[1] : "500k";

            (string Label, int Limit) t;
            try { t = TestHelpers.ResolveTier(tier); }
            catch (ArgumentException ex) { Console.WriteLine(ex.Message); return; }

            // Optional: threshold (word count below which a snippet is "short") as arg 3,
            // neighbor radius as arg 4. Default sweeps thresholds to show the cost curve.
            int[] thresholds = args.Length > 2 && int.TryParse(args[2], out int th)
                ? new[] { th }
                : new[] { ContextWords * 2, ContextWords, ContextWords / 2, 8 };
            if (args.Length > 3 && int.TryParse(args[3], out int rr) && rr > 0) NeighborRadius = rr;

            string dbPath = BuildTest.ResolveDbPath();
            string indexDir = TestHelpers.IndexDir(t.Label);
            if (!File.Exists(dbPath)) { Console.WriteLine("DB not found: " + dbPath); return; }
            if (!(Directory.Exists(indexDir) && Directory.GetFiles(indexDir, "seg_*.dat").Length > 0))
            {
                Console.WriteLine("No index at " + indexDir + " — run `bench " + t.Label + "` first to build it.");
                return;
            }

            Console.WriteLine("========================================================");
            Console.WriteLine($" EmbellishBench  tier={t.Label}  contextWords={ContextWords}  neighborRadius={NeighborRadius}");
            Console.WriteLine($" runtime = {Environment.Version}   cores = {Environment.ProcessorCount}");
            Console.WriteLine("========================================================");

            var index = new SeforimIndex(indexDir, dbPath);

            // `embellishbench 500k dump` — correctness spot-check: print the first short
            // lines' plain vs embellished snippet so neighbor pull-in + highlights are visible.
            if (args.Length > 2 && string.Equals(args[2], "dump", StringComparison.OrdinalIgnoreCase))
            {
                DumpSamples(index);
                return;
            }

            // Pre-fetch + warm each query's result set once (shared across thresholds).
            var sets = new Dictionary<string, List<SearchResult>>();
            foreach (var q in Queries)
            {
                var list = index.Search(q).ToList();
                sets[q] = list;
                Parallel.For(0, list.Count, k => index.GenerateSnippet(list[k], false, ContextWords));
            }

            // Baseline (parallel snippet only) is threshold-independent — measure once per query.
            var baseMs = new Dictionary<string, double>();
            double sumBase = 0;
            foreach (var q in Queries)
            {
                var list = sets[q];
                double best = double.MaxValue;
                for (int rep = 0; rep < Reps; rep++)
                {
                    var snips = new SnippetResult[list.Count];
                    var sw = Stopwatch.StartNew();
                    Parallel.For(0, list.Count, k => snips[k] = index.GenerateSnippet(list[k], false, ContextWords));
                    sw.Stop();
                    if (sw.Elapsed.TotalMilliseconds < best) best = sw.Elapsed.TotalMilliseconds;
                }
                baseMs[q] = best; sumBase += best;
            }

            long totalHits = 0;
            foreach (var q in Queries) totalHits += sets[q].Count;

            Console.WriteLine();
            Console.WriteLine($"  baseline snip (parallel, all {totalHits:N0} hits) = {sumBase:F0}ms");
            Console.WriteLine();
            Console.WriteLine("  threshold   short   short%   EMB(ms)   +cost(ms)   +%");
            Console.WriteLine("  ------------------------------------------------------------");

            foreach (int target in thresholds)
            {
                double sumEmb = 0;
                long totalShort = 0;
                foreach (var q in Queries)
                {
                    var list = sets[q];
                    double bestEmb = double.MaxValue;
                    int shortCount = 0;
                    for (int rep = 0; rep < Reps; rep++)
                    {
                        var embSnips = new SnippetResult[list.Count];
                        var sw = Stopwatch.StartNew();
                        Parallel.For(0, list.Count, k =>
                            embSnips[k] = index.GenerateSnippet(list[k], false, ContextWords));

                        var shortIdx = new List<int>();
                        var shortIds = new List<int>();
                        for (int k = 0; k < list.Count; k++)
                            if (embSnips[k].IsMatch && embSnips[k].WindowWordCount < target)
                            { shortIdx.Add(k); shortIds.Add(list[k].LineId); }

                        if (shortIds.Count > 0)
                        {
                            var neighbors = index.FetchNeighborContext(shortIds, NeighborRadius);
                            Parallel.ForEach(shortIdx, k =>
                            {
                                if (!neighbors.TryGetValue(list[k].LineId, out var ctx)) return;
                                embSnips[k] = index.GenerateSnippetWithNeighbors(
                                    list[k], ctx.Prev, ctx.Next, false, ContextWords);
                            });
                        }
                        sw.Stop();
                        if (sw.Elapsed.TotalMilliseconds < bestEmb) bestEmb = sw.Elapsed.TotalMilliseconds;
                        shortCount = shortIdx.Count;
                    }
                    sumEmb += bestEmb; totalShort += shortCount;
                }
                double totCost = sumEmb - sumBase;
                double totCostPct = sumBase > 0 ? totCost / sumBase * 100 : 0;
                double totShortPct = totalHits > 0 ? (double)totalShort / totalHits * 100 : 0;
                Console.WriteLine($"  {target,9} {totalShort,7:N0} {totShortPct,7:F1}% {sumEmb,9:F0} {totCost,10:F0} {totCostPct,5:F0}%");
            }
            Console.WriteLine("  ------------------------------------------------------------");
        }

        // Correctness eyeball: for a couple of queries, find short-snippet hits and print
        // the plain single-line snippet next to the neighbor-embellished one. The embellished
        // one should be longer, still carry <mark>…</mark>, and read as continuous text.
        private static void DumpSamples(SeforimIndex index)
        {
            int cutoff = 15, radius = NeighborRadius, shown = 0;
            foreach (var q in new[] { "משה", "תורה", "מלך" })
            {
                var list = index.Search(q).ToList();
                foreach (var r in list)
                {
                    var plain = index.GenerateSnippet(r, false, ContextWords);
                    if (!plain.IsMatch || plain.WindowWordCount >= cutoff) continue;

                    var ctx = index.FetchNeighborContext(new List<int> { r.LineId }, radius);
                    if (!ctx.TryGetValue(r.LineId, out var nb)) continue;
                    var emb = index.GenerateSnippetWithNeighbors(r, nb.Prev, nb.Next, false, ContextWords);

                    Console.WriteLine($"── query='{q}' lineId={r.LineId} windowWords={plain.WindowWordCount}");
                    Console.WriteLine($"   PLAIN: {plain.Html}");
                    Console.WriteLine($"   EMBEL: {emb.Html}");
                    Console.WriteLine($"   marks: plain={CountMarks(plain.Html)} embel={CountMarks(emb.Html)}  len: plain={plain.Html.Length} embel={emb.Html.Length}");
                    Console.WriteLine();
                    if (++shown >= 6) return;
                    break; // one sample per query
                }
            }
        }

        private static int CountMarks(string html)
        {
            int n = 0, i = 0;
            while ((i = html.IndexOf("<mark>", i, StringComparison.Ordinal)) >= 0) { n++; i += 6; }
            return n;
        }
    }
}
