using System;
using System.Collections.Generic;
using FtsLib.Search;

namespace FtsBench
{
    /// <summary>
    /// Encode / decode throughput + correctness for the posting codec candidates,
    /// driven by REAL posting lists decoded from a .dat index.
    /// </summary>
    internal static class CodecBench
    {
        public static void Run(string[] args)
        {
            string dir = args.Length > 1 ? args[1] : Paths.Index500k;
            int reps = args.Length > 2 ? int.Parse(args[2]) : 7;

            Console.WriteLine($"=== Codec bench — {dir} ===");
            Console.Write("  loading real postings ... ");
            var lists = Bench.LoadPostings(dir);
            long totalPostings = Bench.TotalPostings(lists);
            int maxLen = 0; foreach (var a in lists) if (a.Length > maxLen) maxLen = a.Length;
            Console.WriteLine($"{lists.Count:N0} lists, {totalPostings:N0} postings, longest {maxLen:N0}");

            // ── Pre-encode buffers for the decode benchmarks ──
            var lebBufs = new byte[lists.Count][];
            var lebLens = new int[lists.Count];
            var counts  = new int[lists.Count];
            var gvBufs  = new byte[lists.Count][];

            var prod = new PostingStream();
            for (int i = 0; i < lists.Count; i++)
            {
                var ids = lists[i];
                prod.Reset();
                foreach (int id in ids) prod.Add(id);
                var b = new byte[prod.ByteLength];
                Array.Copy(prod.Buffer, b, prod.ByteLength);
                lebBufs[i] = b; lebLens[i] = prod.ByteLength; counts[i] = ids.Length;
                gvBufs[i] = GroupVarint.Encode(ids);
            }

            // ── Correctness gates ──
            Console.WriteLine();
            Console.WriteLine("  correctness:");
            VerifyTightIdenticalToProd(lists);
            VerifyGvRoundTrips(lists, gvBufs, counts, maxLen);
            VerifyLebRoundTrips(lists, lebBufs, lebLens, counts, maxLen);

            // ── Encode throughput (indexing) ──
            Console.WriteLine();
            Console.WriteLine("  ENCODE throughput (indexing path):");
            long encBytesProd = 0, encBytesTight = 0;
            var prodEnc = new PostingStream();
            var (pb, pm) = Bench.Time(reps, () =>
            {
                long bytes = 0;
                for (int i = 0; i < lists.Count; i++)
                {
                    prodEnc.Reset();
                    var ids = lists[i];
                    for (int k = 0; k < ids.Length; k++) prodEnc.Add(ids[k]);
                    bytes += prodEnc.ByteLength;
                }
                encBytesProd = bytes;
            });
            Bench.Line("PostingStream (current)", pb, pm, totalPostings, "postings/s");

            var tightEnc = new TightPostingStream();
            var (tb, tm) = Bench.Time(reps, () =>
            {
                long bytes = 0;
                for (int i = 0; i < lists.Count; i++)
                {
                    tightEnc.Reset();
                    var ids = lists[i];
                    for (int k = 0; k < ids.Length; k++) tightEnc.Add(ids[k]);
                    bytes += tightEnc.ByteLength;
                }
                encBytesTight = bytes;
            });
            Bench.Line("TightPostingStream (inline)", tb, tm, totalPostings, "postings/s");
            Console.WriteLine($"      -> encode speedup: {pb / tb:F2}x   (bytes identical: {encBytesProd == encBytesTight})");

            // ── Decode throughput (search) ──
            Console.WriteLine();
            Console.WriteLine("  DECODE throughput (search path):");
            var scratch = new int[maxLen];
            long chk = 0;
            var (lb, lm) = Bench.Time(reps, () =>
            {
                long c = 0;
                for (int i = 0; i < lebBufs.Length; i++) c += DecodeLeb(lebBufs[i], lebLens[i], counts[i], scratch);
                chk = c;
            });
            Bench.Line("LEB (VarInt.Read)", lb, lm, totalPostings, "postings/s");

            long chk2 = 0;
            var (gb, gm) = Bench.Time(reps, () =>
            {
                long c = 0;
                for (int i = 0; i < gvBufs.Length; i++) { GroupVarint.Decode(gvBufs[i], counts[i], scratch); c += scratch[counts[i] > 0 ? counts[i] - 1 : 0]; }
                chk2 = c;
            });
            Bench.Line("GroupVarint", gb, gm, totalPostings, "postings/s");
            Console.WriteLine($"      -> decode speedup GV vs LEB: {lb / gb:F2}x   (checksums match: {chk == chk2})");
        }

        // Decode LEB into scratch, return last-id checksum. Mirrors Bench.Decode w/o allocation.
        private static long DecodeLeb(byte[] buf, int len, int count, int[] outIds)
        {
            if (count == 0) return 0;
            int pos = 0;
            uint enc = VarInt.Read(buf, ref pos, len);
            outIds[0] = (int)((long)enc + int.MinValue);
            for (int k = 1; k < count && pos < len; k++)
            {
                enc += VarInt.Read(buf, ref pos, len);
                outIds[k] = (int)((long)enc + int.MinValue);
            }
            return outIds[count - 1];
        }

        private static void VerifyTightIdenticalToProd(List<int[]> lists)
        {
            var prod = new PostingStream();
            var tight = new TightPostingStream();
            long mism = 0;
            for (int i = 0; i < lists.Count; i++)
            {
                var ids = lists[i];
                prod.Reset(); tight.Reset();
                foreach (int id in ids) { prod.Add(id); tight.Add(id); }
                if (prod.ByteLength != tight.ByteLength) { mism++; continue; }
                for (int k = 0; k < prod.ByteLength; k++)
                    if (prod.Buffer[k] != tight.Buffer[k]) { mism++; break; }
            }
            Console.WriteLine($"    TightPostingStream byte-identical to PostingStream : {(mism == 0 ? "PASS" : $"FAIL ({mism} lists differ)")}");
        }

        private static void VerifyGvRoundTrips(List<int[]> lists, byte[][] gv, int[] counts, int maxLen)
        {
            var scratch = new int[maxLen];
            long mism = 0;
            for (int i = 0; i < lists.Count; i++)
            {
                GroupVarint.Decode(gv[i], counts[i], scratch);
                var ids = lists[i];
                for (int k = 0; k < ids.Length; k++) if (scratch[k] != ids[k]) { mism++; break; }
            }
            Console.WriteLine($"    GroupVarint round-trips ids                        : {(mism == 0 ? "PASS" : $"FAIL ({mism} lists differ)")}");
        }

        private static void VerifyLebRoundTrips(List<int[]> lists, byte[][] leb, int[] lens, int[] counts, int maxLen)
        {
            var scratch = new int[maxLen];
            long mism = 0;
            for (int i = 0; i < lists.Count; i++)
            {
                DecodeLeb(leb[i], lens[i], counts[i], scratch);
                var ids = lists[i];
                for (int k = 0; k < ids.Length; k++) if (scratch[k] != ids[k]) { mism++; break; }
            }
            Console.WriteLine($"    LEB round-trips ids (loader sanity)                : {(mism == 0 ? "PASS" : $"FAIL ({mism} lists differ)")}");
        }
    }
}
