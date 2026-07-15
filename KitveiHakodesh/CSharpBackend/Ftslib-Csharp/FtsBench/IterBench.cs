using System;
using System.Collections.Generic;
using FtsLib.Search;

namespace FtsBench
{
    /// <summary>
    /// Result-materialization bench: the search pipeline drains a result RoaringBitmap
    /// through layered IEnumerable&lt;int&gt; (RoaringBitmap.GetValues -> Container.GetValues,
    /// both `yield`) into a List&lt;int&gt;. This compares that against the allocation-free
    /// bulk GetValuesInto(int[]). Realistic result-set sizes/densities; correctness-gated.
    /// </summary>
    internal static class IterBench
    {
        public static void Run(string[] args)
        {
            int reps = args.Length > 1 ? int.Parse(args[1]) : 9;
            Console.WriteLine($"=== Result-iteration bench (Vector<ulong>.Count={System.Numerics.Vector<ulong>.Count}) ===");

            var scenarios = new (string name, int count, int range)[]
            {
                ("moderate  20k in 0..500k", 20_000,   500_000),
                ("dense    500k in 0..2M",  500_000,   2_000_000),
                ("spread   500k in 0..6.5M",500_000,   6_543_318),
                ("pathology  2M in 0..6.5M",2_000_000, 6_543_318),
            };

            foreach (var (name, count, range) in scenarios)
            {
                var bm = BuildBitmap(count, range);
                int n = bm.Count;

                // ── correctness: yield sequence == bulk sequence ──
                var viaYield = new List<int>(n);
                foreach (var id in bm.GetValues()) viaYield.Add(id);
                var buf = new int[n];
                int wrote = bm.GetValuesInto(buf);
                bool ok = wrote == n && wrote == viaYield.Count;
                if (ok) for (int i = 0; i < n; i++) if (buf[i] != viaYield[i]) { ok = false; break; }

                Console.WriteLine();
                Console.WriteLine($"  {name}   (actual {n:N0} ids)   correctness: {(ok ? "PASS" : "FAIL")}");

                long sink = 0;
                var (ab, am) = Bench.Time(reps, () =>
                {
                    var l = new List<int>();                       // current SearchIds pattern (no pre-cap)
                    foreach (var id in bm.GetValues()) l.Add(id);
                    sink += l.Count;
                });
                Bench.Line("yield -> List (no cap)", ab, am, n, "ids/s");

                var (bb, bm2) = Bench.Time(reps, () =>
                {
                    var l = new List<int>(n);                      // yield, but List pre-sized
                    foreach (var id in bm.GetValues()) l.Add(id);
                    sink += l.Count;
                });
                Bench.Line("yield -> List (pre-cap)", bb, bm2, n, "ids/s");

                var (cb, cm) = Bench.Time(reps, () =>
                {
                    var arr = new int[n];                          // bulk into fresh array
                    sink += bm.GetValuesInto(arr);
                });
                Bench.Line("bulk GetValuesInto(new[])", cb, cm, n, "ids/s");

                var reuse = new int[n];
                var (db, dm) = Bench.Time(reps, () => { sink += bm.GetValuesInto(reuse); });
                Bench.Line("bulk GetValuesInto(reused)", db, dm, n, "ids/s");

                Console.WriteLine($"      -> speedup vs current (no-cap yield): pre-cap {ab / bb:F2}x, bulk-new {ab / cb:F2}x, bulk-reused {ab / db:F2}x");

                // ── allocation pressure proxy: Gen0 collections over K iterations ──
                AllocProbe(bm, n);
                if (sink == long.MinValue) Console.WriteLine("unreachable");
            }
        }

        private static void AllocProbe(RoaringBitmap bm, int n)
        {
            const int K = 40;
            long sink = 0;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            int g0 = GC.CollectionCount(0);
            for (int i = 0; i < K; i++) { var l = new List<int>(); foreach (var id in bm.GetValues()) l.Add(id); sink += l.Count; }
            int yieldG0 = GC.CollectionCount(0) - g0;

            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var reuse = new int[n];
            g0 = GC.CollectionCount(0);
            for (int i = 0; i < K; i++) sink += bm.GetValuesInto(reuse);
            int bulkG0 = GC.CollectionCount(0) - g0;

            Console.WriteLine($"      -> Gen0 GCs over {K} iters: yield->List {yieldG0}  vs  bulk-reused {bulkG0}");
            if (sink == long.MinValue) Console.WriteLine("x");
        }

        // Build an ascending RoaringBitmap of ~count ids spread over [0,range) (seeded, deterministic).
        private static RoaringBitmap BuildBitmap(int count, int range)
        {
            var bm  = new RoaringBitmap();
            var rng = new Random(count ^ range);
            double p = (double)count / range;
            for (int id = 0; id < range; id++)
                if (rng.NextDouble() < p) bm.Add(id);
            return bm;
        }
    }
}
