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
    /// Cross-runtime FTS benchmark. Uses ONLY the high-level SeforimIndex API (no SQLite
    /// types) so the exact same source compiles for the net48 (System.Data.SQLite) and
    /// net10 (Microsoft.Data.Sqlite) builds — letting us quantify the net10 gain on the
    /// SAME index with the SAME work.
    ///
    /// For each query it separates the three real costs and times snippet generation both
    /// sequentially and in parallel (snippet gen is per-hit, independent, CPU-bound):
    ///   FETCH   = Search(q).ToList()  — posting intersection + content read from SQLite
    ///   SNIP-1  = GenerateSnippet for every hit, single-threaded
    ///   SNIP-N  = GenerateSnippet for every hit, Parallel.For across cores
    ///
    ///   FtsLibTest.exe bench [tier]            -- build-if-missing + battery
    ///   FtsLibTest.exe bench [tier] rebuild    -- force a fresh build (build-rate bench)
    /// </summary>
    internal static class BenchTest
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
            bool rebuild = args.Length > 2 && string.Equals(args[2], "rebuild", StringComparison.OrdinalIgnoreCase);

            (string Label, int Limit) t;
            try { t = TestHelpers.ResolveTier(tier); }
            catch (ArgumentException ex) { Console.WriteLine(ex.Message); return; }

            string dbPath = BuildTest.ResolveDbPath();
            string indexDir = TestHelpers.IndexDir(t.Label);

            Console.WriteLine("========================================================");
            Console.WriteLine($" FtsBench  tier={t.Label}");
            Console.WriteLine($" runtime = {Environment.Version}   64-bit = {Environment.Is64BitProcess}   cores = {Environment.ProcessorCount}");
            Console.WriteLine($" Vector.IsHardwareAccelerated = {System.Numerics.Vector.IsHardwareAccelerated}");
            Console.WriteLine($" Vector<ulong>.Count = {System.Numerics.Vector<ulong>.Count}   Vector<int>.Count = {System.Numerics.Vector<int>.Count}");
            Console.WriteLine($" index = {indexDir}");
            Console.WriteLine("========================================================");

            if (!File.Exists(dbPath)) { Console.WriteLine("DB not found: " + dbPath); return; }

            if (rebuild && Directory.Exists(indexDir))
            {
                try { Directory.Delete(indexDir, recursive: true); } catch (Exception ex) { Console.WriteLine("wipe failed: " + ex.Message); }
            }

            bool haveIndex = Directory.Exists(indexDir) && Directory.GetFiles(indexDir, "seg_*.dat").Length > 0;
            var index = new SeforimIndex(indexDir, dbPath);

            if (!haveIndex)
            {
                Console.WriteLine($"[BUILD] building {(t.Limit == 0 ? "FULL" : t.Limit.ToString("N0"))} lines ...");
                long last = 0;
                var bsw = Stopwatch.StartNew();
                index.BuildIndex(limit: t.Limit, onProgress: c => { last = c; });
                bsw.Stop();
                double secs = bsw.Elapsed.TotalSeconds;
                Console.WriteLine($"[BUILD] {last:N0} lines in {secs:F2}s = {(secs > 0 ? last / secs : 0):N0} lines/s");
                index = new SeforimIndex(indexDir, dbPath);
            }
            else
            {
                Console.WriteLine("[BUILD] reusing existing index (pass 'rebuild' to force a build-rate bench)");
            }

            // Warm the DB page cache + JIT.
            foreach (var q in Queries) index.Search(q).ToList();

            long totalHits = 0;
            double sumFetch = 0, sumSeq = 0, sumPar = 0;
            Console.WriteLine();
            Console.WriteLine("  query             hits    FETCH(ms)   SNIP-1(ms)   SNIP-N(ms)   speedup");
            Console.WriteLine("  ----------------------------------------------------------------------------");
            foreach (var q in Queries)
            {
                double bestFetch = double.MaxValue, bestSeq = double.MaxValue, bestPar = double.MaxValue;
                int hits = 0;
                for (int i = 0; i < Reps; i++)
                {
                    var sw = Stopwatch.StartNew();
                    var list = index.Search(q).ToList();   // FETCH (intersection + content)
                    sw.Stop();
                    if (sw.Elapsed.TotalMilliseconds < bestFetch) bestFetch = sw.Elapsed.TotalMilliseconds;
                    hits = list.Count;

                    sw.Restart();
                    int m1 = 0;
                    for (int k = 0; k < list.Count; k++)
                        if (index.GenerateSnippet(list[k]).IsMatch) m1++;
                    sw.Stop();
                    if (sw.Elapsed.TotalMilliseconds < bestSeq) bestSeq = sw.Elapsed.TotalMilliseconds;

                    sw.Restart();
                    int m2 = 0;
                    Parallel.For(0, list.Count, () => 0, (k, _, local) =>
                        index.GenerateSnippet(list[k]).IsMatch ? local + 1 : local,
                        local => System.Threading.Interlocked.Add(ref m2, local));
                    sw.Stop();
                    if (sw.Elapsed.TotalMilliseconds < bestPar) bestPar = sw.Elapsed.TotalMilliseconds;
                }
                double speed = bestPar > 0 ? bestSeq / bestPar : 0;
                Console.WriteLine($"  {q,-15} {hits,7:N0}   {bestFetch,9:F1}   {bestSeq,10:F1}   {bestPar,10:F1}   {speed,6:F2}x");
                totalHits += hits; sumFetch += bestFetch; sumSeq += bestSeq; sumPar += bestPar;
            }
            Console.WriteLine("  ----------------------------------------------------------------------------");
            Console.WriteLine($"  TOTAL           {totalHits,7:N0}   {sumFetch,9:F1}   {sumSeq,10:F1}   {sumPar,10:F1}   {(sumPar > 0 ? sumSeq / sumPar : 0),6:F2}x");
            Console.WriteLine();
            Console.WriteLine($"[RESULT] fetch={sumFetch:F0}ms  snip-seq={sumSeq:F0}ms  snip-par={sumPar:F0}ms  end-to-end(seq)={sumFetch + sumSeq:F0}ms  end-to-end(par)={sumFetch + sumPar:F0}ms");
        }
    }
}
