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
    /// net10-only benchmark for the parallel content fetch (SeforimIndex.SearchParallel).
    /// Quantifies the Phase-2 win: the serial content read (Search().ToList()) vs the
    /// multi-connection parallel read (SearchParallel()), and the full end-to-end search
    /// (fetch + parallel snippet) for each. Also asserts the two paths return the SAME
    /// ordered result set (correctness), so the optimization is provably lossless.
    ///
    ///   FtsLibTest.exe fetchbench [tier]
    /// </summary>
    internal static class FetchBenchTest
    {
        private static readonly string[] Queries =
        {
            "משה", "אלהים", "תורה", "רבי", "ארץ", "בית", "מלך", "עולם",
            "משה רבינו", "ארץ ישראל", "בית המקדש", "תלמוד תורה",
        };
        private const int Reps = 5;

        public static void Run(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            string tier = args.Length > 1 ? args[1] : "500k";

            (string Label, int Limit) t;
            try { t = TestHelpers.ResolveTier(tier); }
            catch (ArgumentException ex) { Console.WriteLine(ex.Message); return; }

            string dbPath   = BuildTest.ResolveDbPath();
            string indexDir = TestHelpers.IndexDir(t.Label);

            Console.WriteLine("========================================================");
            Console.WriteLine($" FetchBench  tier={t.Label}");
            Console.WriteLine($" runtime = {Environment.Version}   cores = {Environment.ProcessorCount}   64-bit = {Environment.Is64BitProcess}");
            Console.WriteLine($" index = {indexDir}");
            Console.WriteLine("========================================================");

            if (!File.Exists(dbPath)) { Console.WriteLine("DB not found: " + dbPath); return; }
            if (!(Directory.Exists(indexDir) && Directory.GetFiles(indexDir, "seg_*.dat").Length > 0))
            {
                Console.WriteLine("index not found — run `bench " + t.Label + "` first to build it."); return;
            }

            var index = new SeforimIndex(indexDir, dbPath);

            // Warm DB page cache + JIT for both paths.
            foreach (var q in Queries) { index.Search(q).ToList(); index.SearchParallel(q); }

            long totalHits = 0;
            double sumSer = 0, sumPar = 0, sumE2ESer = 0, sumE2EPar = 0;
            bool allSame = true;

            Console.WriteLine();
            Console.WriteLine("  query             hits    FETCH-ser   FETCH-par   speedup    E2E-ser    E2E-par   speedup   same");
            Console.WriteLine("  --------------------------------------------------------------------------------------------------------");
            foreach (var q in Queries)
            {
                double bestSer = double.MaxValue, bestPar = double.MaxValue;
                double bestE2ESer = double.MaxValue, bestE2EPar = double.MaxValue;
                int hits = 0;
                bool same = true;

                for (int i = 0; i < Reps; i++)
                {
                    // Serial fetch.
                    var sw = Stopwatch.StartNew();
                    var serList = index.Search(q).ToList();
                    sw.Stop();
                    if (sw.Elapsed.TotalMilliseconds < bestSer) bestSer = sw.Elapsed.TotalMilliseconds;
                    hits = serList.Count;

                    // Parallel fetch.
                    sw.Restart();
                    var parList = index.SearchParallel(q);
                    sw.Stop();
                    if (sw.Elapsed.TotalMilliseconds < bestPar) bestPar = sw.Elapsed.TotalMilliseconds;

                    // Correctness: identical ordered (id) sequence.
                    if (i == 0) same = SameOrder(serList, parList);

                    // End-to-end serial: serial fetch + serial snippet.
                    sw.Restart();
                    for (int k = 0; k < serList.Count; k++) index.GenerateSnippet(serList[k]);
                    sw.Stop();
                    double e2eSer = bestSer + sw.Elapsed.TotalMilliseconds;
                    if (e2eSer < bestE2ESer) bestE2ESer = e2eSer;

                    // End-to-end parallel: parallel fetch + parallel snippet.
                    sw.Restart();
                    Parallel.For(0, parList.Count, k => index.GenerateSnippet(parList[k]));
                    sw.Stop();
                    double e2ePar = bestPar + sw.Elapsed.TotalMilliseconds;
                    if (e2ePar < bestE2EPar) bestE2EPar = e2ePar;
                }

                if (!same) allSame = false;
                double fSpeed = bestPar > 0 ? bestSer / bestPar : 0;
                double eSpeed = bestE2EPar > 0 ? bestE2ESer / bestE2EPar : 0;
                Console.WriteLine($"  {q,-15} {hits,7:N0}   {bestSer,9:F1}   {bestPar,9:F1}   {fSpeed,6:F2}x   {bestE2ESer,8:F1}   {bestE2EPar,8:F1}   {eSpeed,6:F2}x   {(same ? "ok" : "DIFF")}");
                totalHits += hits; sumSer += bestSer; sumPar += bestPar; sumE2ESer += bestE2ESer; sumE2EPar += bestE2EPar;
            }
            Console.WriteLine("  --------------------------------------------------------------------------------------------------------");
            Console.WriteLine($"  {"TOTAL",-15} {totalHits,7:N0}   {sumSer,9:F1}   {sumPar,9:F1}   {(sumPar > 0 ? sumSer / sumPar : 0),6:F2}x   {sumE2ESer,8:F1}   {sumE2EPar,8:F1}   {(sumE2EPar > 0 ? sumE2ESer / sumE2EPar : 0),6:F2}x   {(allSame ? "ok" : "DIFF")}");
            Console.WriteLine();
            Console.WriteLine($"[RESULT] fetch: serial={sumSer:F0}ms  parallel={sumPar:F0}ms  ({(sumPar > 0 ? sumSer / sumPar : 0):F2}x)");
            Console.WriteLine($"[RESULT] end-to-end: serial={sumE2ESer:F0}ms  parallel={sumE2EPar:F0}ms  ({(sumE2EPar > 0 ? sumE2ESer / sumE2EPar : 0):F2}x)");
            Console.WriteLine($"[CORRECTNESS] parallel fetch order-identical to serial: {(allSame ? "PASS" : "FAIL")}");
        }

        private static bool SameOrder(List<SearchResult> a, IReadOnlyList<SearchResult> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].LineId != b[i].LineId) return false;
            return true;
        }
    }
}
