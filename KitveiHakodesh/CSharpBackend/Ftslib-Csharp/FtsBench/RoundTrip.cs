using System;
using System.Collections.Generic;
using FtsLib.Search;

namespace FtsBench
{
    /// <summary>
    /// Self-consistency test for the format-v2 posting codec (no-offset first value):
    /// encode ascending id sets with the production PostingStream, then decode via
    /// PostingIterator.MoveNext, DrainInto, and SkipTo (with and without a skip table
    /// built exactly like RamIndexEntry) and check every path reproduces the ids.
    /// Uses only synthetic in-memory data (the shipped .dat is still v1).
    /// </summary>
    internal static class RoundTrip
    {
        private const int SkipInterval = 128;

        public static void Run(string[] args)
        {
            Console.WriteLine("=== format-v2 codec round-trip (no-offset) ===");
            var rng = new Random(999);
            var cases = new List<int[]>
            {
                new int[0],
                new[] { 0 },
                new[] { 6543313 },
                new[] { 0, 1, 2, 3 },
                Asc(rng, 2, 5),
                Asc(rng, 127, 50),
                Asc(rng, 128, 50),
                Asc(rng, 129, 50),
                Asc(rng, 5000, 500),      // triggers multiple skip entries
                Asc(rng, 100000, 30),     // long list, small gaps
                Asc(rng, 200, 60000),     // big gaps (multi-byte deltas)
                AscTo(rng, 4000, 6_543_313),
            };

            long bad = 0;
            foreach (var ids in cases)
                if (!Check(ids)) { bad++; Console.WriteLine($"  FAIL len={ids.Length}"); }

            Console.WriteLine($"  {cases.Count - bad}/{cases.Count} cases round-trip " +
                              $"(MoveNext + DrainInto + SkipTo, ±skip table) : {(bad == 0 ? "PASS" : "FAIL")}");
        }

        private static bool Check(int[] ids)
        {
            var stream = new PostingStream();
            var skip   = new List<int>();
            for (int i = 0; i < ids.Length; i++)
            {
                int newCount = stream.Count + 1;
                if (newCount > 1 && (newCount - 1) % SkipInterval == 0)
                {
                    skip.Add(ids[i]);
                    skip.Add(stream.NextByteOffset);
                    skip.Add((int)stream.LastEncoded);
                }
                stream.Add(ids[i]);
            }
            int[] skipArr = skip.Count > 0 ? skip.ToArray() : null;
            int   skipLen = skip.Count;

            // MoveNext
            var got = new List<int>();
            var it1 = new PostingIterator(stream.Buffer, stream.ByteLength, skipArr, skipLen);
            while (it1.MoveNext()) got.Add(it1.Current);
            if (!Eq(got, ids)) return false;

            // DrainInto
            var bm  = new RoaringBitmap();
            var it2 = new PostingIterator(stream.Buffer, stream.ByteLength, skipArr, skipLen);
            it2.DrainInto(bm);
            if (!Eq(new List<int>(bm.GetValues()), ids)) return false;

            // SkipTo — must land on the first id >= target (lower_bound), both with and
            // without the skip table (skipArr forces the binary-search branch).
            if (ids.Length > 0)
            {
                int[] targets =
                {
                    ids[0], ids[0] - 1, -5,
                    ids[ids.Length / 2], ids[ids.Length - 1],
                    ids[ids.Length - 1] + 1,
                };
                foreach (int t in targets)
                {
                    if (!SkipCheck(stream, skipArr, skipLen, ids, t)) return false;
                    if (!SkipCheck(stream, null,    0,       ids, t)) return false;
                }
            }
            return true;
        }

        private static bool SkipCheck(PostingStream s, int[] skip, int skipLen, int[] ids, int target)
        {
            var it  = new PostingIterator(s.Buffer, s.ByteLength, skip, skipLen);
            bool ok = it.SkipTo(target);
            int lb  = LowerBound(ids, target);
            if (lb < ids.Length) return ok && it.Current == ids[lb];
            return !ok; // target beyond last id -> exhausted
        }

        private static int LowerBound(int[] a, int t)
        {
            int lo = 0, hi = a.Length;
            while (lo < hi) { int m = (lo + hi) >> 1; if (a[m] < t) lo = m + 1; else hi = m; }
            return lo;
        }

        private static bool Eq(List<int> a, int[] b)
        {
            if (a.Count != b.Length) return false;
            for (int i = 0; i < b.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static int[] Asc(Random rng, int count, int maxGap)
        {
            var a = new int[count];
            int v = rng.Next(0, 100);
            for (int i = 0; i < count; i++) { a[i] = v; v += 1 + rng.Next(maxGap); }
            return a;
        }

        private static int[] AscTo(Random rng, int count, int maxId)
        {
            var set = new SortedSet<int>();
            while (set.Count < count) set.Add(rng.Next(0, maxId));
            var a = new int[set.Count]; set.CopyTo(a); return a;
        }
    }
}
