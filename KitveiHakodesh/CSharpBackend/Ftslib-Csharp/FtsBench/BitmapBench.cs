using System;
using System.Numerics;

namespace FtsBench
{
    /// <summary>
    /// RoaringBitmap BitmapContainer micro-benchmarks: the SIMD levers.
    ///   • popcount  : scalar Hamming (production CountBits) vs Harley-Seal SIMD
    ///   • OR        : scalar word-loop vs Vector&lt;ulong&gt; (production OrWith)
    ///   • full OrWith: current (vecOR + scalar recount) vs (vecOR + Harley-Seal recount)
    /// </summary>
    internal static class BitmapBench
    {
        private const int Words = 1024;   // one BitmapContainer

        public static void Run(string[] args)
        {
            int reps = args.Length > 1 ? int.Parse(args[1]) : 9;
            int nContainers = 4096;       // ~ number of dense OrWith recounts in a big union

            Console.WriteLine($"=== Bitmap SIMD bench (Vector<ulong>.Count={Vector<ulong>.Count}, hwaccel={Vector.IsHardwareAccelerated}) ===");
            Console.WriteLine($"    {nContainers} containers × {Words} words (8 KB each)");

            // Build realistic containers at mixed densities from a seeded RNG.
            var rng = new Random(12345);
            var cons = new ulong[nContainers][];
            for (int c = 0; c < nContainers; c++)
            {
                var w = new ulong[Words];
                // vary density: some sparse, some ~half, some dense
                double density = (c % 4) switch { 0 => 0.02, 1 => 0.25, 2 => 0.5, _ => 0.9 };
                for (int i = 0; i < Words; i++)
                {
                    ulong v = 0;
                    for (int bit = 0; bit < 64; bit++) if (rng.NextDouble() < density) v |= 1UL << bit;
                    w[i] = v;
                }
                cons[c] = w;
            }

            // ── Correctness: Harley-Seal == scalar on every container ──
            long mismatches = 0, totalScalar = 0;
            for (int c = 0; c < nContainers; c++)
            {
                int s = PopCount.Scalar(cons[c]);
                int h = PopCount.HarleySeal(cons[c]);
                totalScalar += s;
                if (s != h) mismatches++;
            }
            Console.WriteLine();
            Console.WriteLine($"  correctness: Harley-Seal == scalar on all {nContainers} containers : {(mismatches == 0 ? "PASS" : $"FAIL ({mismatches})")}");
            Console.WriteLine($"               (total set bits = {totalScalar:N0})");

            EdgeCases();
            RealRoaringOrPath();

            long words = (long)nContainers * Words;

            // ── Popcount ──
            Console.WriteLine();
            Console.WriteLine("  POPCOUNT (recount of a full container):");
            long acc = 0;
            var (sb, sm) = Bench.Time(reps, () => { long a = 0; for (int c = 0; c < nContainers; c++) a += PopCount.Scalar(cons[c]); acc = a; });
            Bench.Line("scalar Hamming (current)", sb, sm, words, "words/s");
            var (hb, hm) = Bench.Time(reps, () => { long a = 0; for (int c = 0; c < nContainers; c++) a += PopCount.HarleySeal(cons[c]); acc = a; });
            Bench.Line("Harley-Seal SIMD", hb, hm, words, "words/s");
            Console.WriteLine($"      -> popcount speedup: {sb / hb:F2}x");

            // ── OR (isolate the merge, no recount) ──
            Console.WriteLine();
            Console.WriteLine("  OR (merge only, no recount):");
            var dstS = new ulong[Words];
            var dstV = new ulong[Words];
            // OR every container into a running dst; report words merged.
            var (osb, osm) = Bench.Time(reps, () =>
            {
                Array.Clear(dstS, 0, Words);
                for (int c = 0; c < nContainers; c++) ScalarOr(dstS, cons[c]);
            });
            Bench.Line("scalar word-loop", osb, osm, words, "words/s");
            var (ovb, ovm) = Bench.Time(reps, () =>
            {
                Array.Clear(dstV, 0, Words);
                for (int c = 0; c < nContainers; c++) VectorOr(dstV, cons[c]);
            });
            Bench.Line("Vector<ulong> (current)", ovb, ovm, words, "words/s");
            Console.WriteLine($"      -> OR speedup: {osb / ovb:F2}x");

            // ── Full OrWith: merge + recount, current vs proposed ──
            Console.WriteLine();
            Console.WriteLine("  FULL OrWith (merge + cardinality recount):");
            var dst1 = new ulong[Words];
            var dst2 = new ulong[Words];
            long card1 = 0, card2 = 0;
            var (fcb, fcm) = Bench.Time(reps, () =>
            {
                Array.Clear(dst1, 0, Words); long card = 0;
                for (int c = 0; c < nContainers; c++) { VectorOr(dst1, cons[c]); card = PopCount.Scalar(dst1); }
                card1 = card;
            });
            Bench.Line("current (vecOR + scalar recount)", fcb, fcm, words, "words/s");
            var (fhb, fhm) = Bench.Time(reps, () =>
            {
                Array.Clear(dst2, 0, Words); long card = 0;
                for (int c = 0; c < nContainers; c++) { VectorOr(dst2, cons[c]); card = PopCount.HarleySeal(dst2); }
                card2 = card;
            });
            Bench.Line("proposed (vecOR + Harley-Seal)", fhb, fhm, words, "words/s");
            Console.WriteLine($"      -> full OrWith speedup: {fcb / fhb:F2}x   (final cardinality equal: {card1 == card2})");
        }

        // Popcount edge cases the random fill may not hit.
        private static void EdgeCases()
        {
            var cases = new (string name, ulong[] bits)[]
            {
                ("all-zero",  Fill(0UL)),
                ("all-ones",  Fill(0xFFFFFFFFFFFFFFFFUL)),
                ("alternating", Fill(0xAAAAAAAAAAAAAAAAUL)),
                ("single-bit-w0", Single(0, 0)),
                ("single-bit-w1023", Single(1023, 63)),
            };
            long bad = 0;
            foreach (var (name, bits) in cases)
                if (PopCount.Scalar(bits) != PopCount.HarleySeal(bits)) { bad++; Console.WriteLine($"      edge FAIL: {name}"); }
            Console.WriteLine($"  correctness: Harley-Seal == scalar on edge cases                 : {(bad == 0 ? "PASS" : $"FAIL ({bad})")}");

            ulong[] Fill(ulong w) { var a = new ulong[Words]; for (int i = 0; i < Words; i++) a[i] = w; return a; }
            ulong[] Single(int word, int bit) { var a = new ulong[Words]; a[word] = 1UL << bit; return a; }
        }

        // Exercises the PRODUCTION RoaringBitmap.Or -> BitmapContainer.OrWith -> CountBits
        // (Harley-Seal) path end-to-end through the real class, verifying cardinality.
        private static void RealRoaringOrPath()
        {
            var a = new FtsLib.Search.RoaringBitmap();
            for (int i = 0; i < 6000; i++) a.Add(i);          // dense block 0 -> BitmapContainer
            var b = new FtsLib.Search.RoaringBitmap();
            for (int i = 3000; i < 9000; i++) b.Add(i);       // overlapping dense block 0
            a.Or(b);                                          // BitmapContainer.OrWith -> CountBits
            bool ok = a.Count == 9000;                        // union of [0,6000) and [3000,9000) = [0,9000)
            Console.WriteLine($"  correctness: real RoaringBitmap.Or cardinality via CountBits      : {(ok ? "PASS" : $"FAIL (got {a.Count}, want 9000)")}");
        }

        private static void ScalarOr(ulong[] dst, ulong[] src)
        {
            for (int i = 0; i < Words; i++) dst[i] |= src[i];
        }

        private static void VectorOr(ulong[] dst, ulong[] src)
        {
            int vLen = Vector<ulong>.Count;
            int i = 0;
            if (Vector.IsHardwareAccelerated)
                for (; i <= Words - vLen; i += vLen)
                    Vector.BitwiseOr(new Vector<ulong>(dst, i), new Vector<ulong>(src, i)).CopyTo(dst, i);
            for (; i < Words; i++) dst[i] |= src[i];
        }
    }
}
