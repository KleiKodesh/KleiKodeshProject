using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FtsLib.Indexing;
using FtsLib.Search;

namespace FtsBench
{
    /// <summary>Timing helpers + real posting-list loader shared by all benchmarks.</summary>
    internal static class Bench
    {
        /// <summary>
        /// Runs <paramref name="body"/> <paramref name="reps"/> times after warmup,
        /// returns (bestMs, medianMs). A full GC is forced before each rep so
        /// allocation-heavy candidates are not flattered by leftover headroom.
        /// </summary>
        public static (double best, double median) Time(int reps, Action body, int warmup = 3)
        {
            for (int i = 0; i < warmup; i++) body();
            var ms = new double[reps];
            var sw = new Stopwatch();
            for (int i = 0; i < reps; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                sw.Restart();
                body();
                sw.Stop();
                ms[i] = sw.Elapsed.TotalMilliseconds;
            }
            Array.Sort(ms);
            return (ms[0], ms[reps / 2]);
        }

        public static void Line(string name, double bestMs, double medMs, long items, string unit)
        {
            double rate = items / (bestMs / 1000.0);
            Console.WriteLine($"    {name,-28} best {bestMs,9:F2} ms  median {medMs,9:F2} ms   {rate,14:N0} {unit}");
        }

        /// <summary>Decode one term's postings (delta+offset LEB) into absolute ids.</summary>
        public static int[] Decode(byte[] buf, int len, int count)
        {
            var arr = new int[count];
            if (count == 0) return arr;
            int pos = 0;
            uint enc = VarInt.Read(buf, ref pos, len);
            arr[0] = (int)((long)enc + int.MinValue);
            for (int k = 1; k < count && pos < len; k++)
            {
                enc += VarInt.Read(buf, ref pos, len);
                arr[k] = (int)((long)enc + int.MinValue);
            }
            return arr;
        }

        /// <summary>
        /// Loads decoded posting lists from every seg_*.dat under <paramref name="dir"/>.
        /// Returns absolute-id arrays (ascending). Optionally cap the number of terms.
        /// </summary>
        public static List<int[]> LoadPostings(string dir, int maxTerms = int.MaxValue)
        {
            var lists = new List<int[]>();
            var dats = new List<string>(Directory.GetFiles(dir, "seg_*.dat"));
            dats.Sort(StringComparer.Ordinal);
            foreach (var dat in dats)
            {
                using (var r = new SegmentReader(dat))
                {
                    while (r.MoveNext())
                    {
                        lists.Add(Decode(r.CurrentChunk, r.CurrentChunkLen, r.CurrentCount));
                        if (lists.Count >= maxTerms) return lists;
                    }
                }
            }
            return lists;
        }

        public static long TotalPostings(List<int[]> lists)
        {
            long n = 0;
            foreach (var a in lists) n += a.Length;
            return n;
        }
    }
}
