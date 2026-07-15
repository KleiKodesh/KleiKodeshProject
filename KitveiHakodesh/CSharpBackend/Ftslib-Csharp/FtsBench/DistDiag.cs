using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FtsLib.Indexing;
using FtsLib.Search;

namespace FtsBench
{
    /// <summary>
    /// Reads real .dat segment files and reports, from the ACTUAL posting data:
    ///   • posting-list length (docCount) histogram
    ///   • gap (delta) byte-length histogram
    ///   • current on-disk posting bytes vs analytically-estimated alternatives:
    ///       - LEB (current, with int.MinValue offset on the first value)
    ///       - LEB-noOffset (first value encoded directly)
    ///       - GroupVarint (4-value groups, 2-bit length codes)
    ///       - BitPack-FOR (block=128, exact max-bit per block)
    ///       - BitPack-Patched (block=128, per-block optimal width + exceptions)
    ///   • skip-table byte overhead
    ///
    /// Nothing here mutates the index — segments are opened read-only.
    /// </summary>
    internal static class DistDiag
    {
        private const int Block = 128;         // bit-pack block size
        private const int ExcCost = 5;         // patched-PFor exception cost: 1 pos byte + up to 4 value bytes

        public static void Run(string[] args)
        {
            string dir = args.Length > 1 ? args[1] : Paths.Index500k;
            if (!Directory.Exists(dir)) { Console.WriteLine($"No such dir: {dir}"); return; }

            var dats = new List<string>(Directory.GetFiles(dir, "seg_*.dat"));
            dats.Sort(StringComparer.Ordinal);
            if (dats.Count == 0) { Console.WriteLine($"No seg_*.dat under {dir}"); return; }

            Console.WriteLine($"=== Distribution / compression-potential — {dir} ===");
            Console.WriteLine($"    {dats.Count} segment(s)");
            var sw = Stopwatch.StartNew();

            long terms = 0, postings = 0;
            long lebBytes = 0;             // == actual chunk bytes (current codec)
            long lebNoOffset = 0;          // LEB with first value direct
            long groupVarint = 0;
            long bitpackFor = 0;
            long bitpackPatched = 0;
            long skipEntries = 0, skipBytes = 0, termHeaderBytes = 0, termTextBytes = 0;

            // docCount histogram (power-of-two buckets) and gap-byte histogram.
            var lenHist = new long[24];     // bucket i -> counts with docCount in [2^i, 2^(i+1))
            var gapBytesHist = new long[6]; // index 1..5 = gaps needing that many LEB bytes
            long gapSum = 0, maxGap = 0, maxFirstId = 0;

            // Reusable per-block bitwidth histogram (0..32).
            var wHist = new int[33];
            var blockVals = new uint[Block];

            foreach (var dat in dats)
            {
                using (var r = new SegmentReader(dat))
                {
                    while (r.MoveNext())
                    {
                        terms++;
                        int count = r.CurrentCount;
                        postings += count;
                        lebBytes += r.CurrentChunkLen;

                        termTextBytes += System.Text.Encoding.UTF8.GetByteCount(r.CurrentTerm);
                        termHeaderBytes += 20; // 5 int/uint fields per term record
                        skipEntries += r.CurrentSkipLen / 3;
                        skipBytes += (r.CurrentSkipLen / 3) * 12;

                        lenHist[Bucket(count)]++;

                        // Decode this term's postings from the raw chunk.
                        byte[] buf = r.CurrentChunk;
                        int len = r.CurrentChunkLen;
                        int pos = 0;
                        if (count == 0 || len == 0) continue;

                        // First value.
                        uint enc0 = VarInt.Read(buf, ref pos, len);
                        int firstId = (int)((long)enc0 + int.MinValue);
                        uint firstDirect = (uint)firstId;           // ids are non-negative
                        if (firstId > maxFirstId) maxFirstId = firstId;

                        int firstDirectLen = VarLen(firstDirect);
                        lebNoOffset += firstDirectLen;              // first value cost, direct
                        lebNoOffset += (len - pos);                 // remaining bytes identical (they're gaps)

                        // Start bit-pack / group-varint accounting for this term.
                        int blockN = 0;
                        long gvTerm = 0, forTerm = 0, patchedTerm = 0;
                        Array.Clear(wHist, 0, wHist.Length);

                        void FlushBlock()
                        {
                            if (blockN == 0) return;
                            // Group varint: ceil(n/4) control bytes + sum of gvLen(v) (1..4).
                            // BitPack-FOR: n * maxBits / 8 (+1 header byte for width).
                            // BitPack-Patched: choose w minimizing n*w/8 + exc*(ExcCost) (+1 header).
                            int maxW = 0;
                            for (int w = 32; w >= 0; w--) { if (wHist[w] > 0) { maxW = w; break; } }

                            // FOR: exact max width.
                            forTerm += 1 + (long)((blockN * maxW + 7) / 8);

                            // Patched: scan widths, exceptions = count of values with bits > w.
                            long best = long.MaxValue;
                            long exc = 0; // values with bits > w, computed incrementally from high w downward
                            for (int w = maxW; w >= 0; w--)
                            {
                                long cost = 1 + (long)((blockN * w + 7) / 8) + exc * ExcCost;
                                if (cost < best) best = cost;
                                exc += wHist[w]; // values with exactly w bits become exceptions at w-1
                            }
                            patchedTerm += best;

                            // Group varint over the block's values.
                            for (int i = 0; i < blockN; i++)
                                gvTerm += GvLen(blockVals[i]);
                            gvTerm += (blockN + 3) / 4; // control bytes

                            blockN = 0;
                            Array.Clear(wHist, 0, wHist.Length);
                        }

                        void Feed(uint v)
                        {
                            blockVals[blockN++] = v;
                            wHist[BitsFor(v)]++;
                            if (blockN == Block) FlushBlock();
                        }

                        Feed(firstDirect);

                        uint enc = enc0;
                        int prevId = firstId;
                        for (int k = 1; k < count && pos < len; k++)
                        {
                            uint gap = VarInt.Read(buf, ref pos, len); // gap == id delta directly
                            gapBytesHist[VarLen(gap)]++;
                            gapSum += gap;
                            if (gap > maxGap) maxGap = gap;
                            Feed(gap);
                        }
                        FlushBlock();

                        groupVarint += gvTerm;
                        bitpackFor += forTerm;
                        bitpackPatched += patchedTerm;
                    }
                }
                Console.Write(".");
            }
            Console.WriteLine();
            sw.Stop();

            Console.WriteLine($"    scanned in {sw.Elapsed.TotalSeconds:F1}s");
            Console.WriteLine();
            Console.WriteLine($"  terms                : {terms:N0}");
            Console.WriteLine($"  postings             : {postings:N0}");
            Console.WriteLine($"  avg postings/term    : {(double)postings / Math.Max(1, terms):F1}");
            Console.WriteLine($"  max firstId          : {maxFirstId:N0}");
            Console.WriteLine($"  max gap              : {maxGap:N0}   avg gap: {(double)gapSum / Math.Max(1, postings - terms):F1}");
            Console.WriteLine();

            Console.WriteLine("  === posting bytes: current vs candidates ===");
            Row("LEB (current)",       lebBytes,       lebBytes);
            Row("LEB no-offset",       lebNoOffset,    lebBytes);
            Row("GroupVarint",         groupVarint,    lebBytes);
            Row("BitPack-FOR/128",     bitpackFor,     lebBytes);
            Row("BitPack-Patched/128", bitpackPatched, lebBytes);
            Console.WriteLine();

            Console.WriteLine($"  first-value offset waste (LEB current − no-offset): {(lebBytes - lebNoOffset):N0} bytes " +
                              $"({100.0 * (lebBytes - lebNoOffset) / lebBytes:F2}% of posting bytes)");
            Console.WriteLine();

            Console.WriteLine("  === other on-disk overhead ===");
            Console.WriteLine($"  skip entries         : {skipEntries:N0}  ->  {skipBytes:N0} bytes (12 B/entry, int32 triplet)");
            Console.WriteLine($"  term header fields   : {termHeaderBytes:N0} bytes (20 B/term)");
            Console.WriteLine($"  term text (UTF-8)    : {termTextBytes:N0} bytes");
            long datTotal = lebBytes + skipBytes + termHeaderBytes + termTextBytes;
            Console.WriteLine($"  approx .dat total    : {datTotal:N0} bytes  (postings {100.0*lebBytes/datTotal:F1}%, " +
                              $"skip {100.0*skipBytes/datTotal:F1}%, hdr {100.0*termHeaderBytes/datTotal:F1}%, text {100.0*termTextBytes/datTotal:F1}%)");
            Console.WriteLine();

            Console.WriteLine("  === docCount histogram ===");
            for (int i = 0; i < lenHist.Length; i++)
                if (lenHist[i] > 0)
                    Console.WriteLine($"    [{(1 << i),9:N0} .. {((1 << (i + 1)) - 1),9:N0}]  {lenHist[i],12:N0}  ({100.0 * lenHist[i] / terms:F1}%)");
            Console.WriteLine();

            Console.WriteLine("  === gap LEB-byte histogram ===");
            long gapTotal = 0; for (int i = 1; i <= 5; i++) gapTotal += gapBytesHist[i];
            for (int i = 1; i <= 5; i++)
                if (gapBytesHist[i] > 0)
                    Console.WriteLine($"    {i} byte(s): {gapBytesHist[i],12:N0}  ({100.0 * gapBytesHist[i] / Math.Max(1, gapTotal):F1}%)");

            void Row(string name, long bytes, long baseline)
            {
                double pct = 100.0 * bytes / baseline;
                string delta = bytes == baseline ? "        —" : $"{(pct - 100.0):+0.0;-0.0}%";
                Console.WriteLine($"    {name,-22} {bytes,14:N0} B   ({pct,6:F1}% of current, {delta})");
            }
        }

        // ── helpers ──────────────────────────────────────────────────

        private static int Bucket(int n)
        {
            if (n <= 0) return 0;
            int b = 0;
            while ((1 << (b + 1)) <= n && b < 23) b++;
            return b;
        }

        /// <summary>LEB128 byte length of a uint (1..5).</summary>
        public static int VarLen(uint v)
        {
            int n = 1;
            while (v >= 0x80) { v >>= 7; n++; }
            return n;
        }

        /// <summary>Group-varint byte length of a value (1..4; 32-bit max is 4 bytes).</summary>
        private static int GvLen(uint v)
        {
            if (v < (1u << 8)) return 1;
            if (v < (1u << 16)) return 2;
            if (v < (1u << 24)) return 3;
            return 4;
        }

        /// <summary>Number of significant bits (0 -> 1, so a zero still needs a slot).</summary>
        private static int BitsFor(uint v)
        {
            int b = 0;
            while (v != 0) { v >>= 1; b++; }
            return b == 0 ? 1 : b;
        }
    }

    internal static class Paths
    {
        // Real prebuilt indexes live in the MAIN worktree (git-ignored, not in this branch checkout).
        private const string MainBin =
            @"C:\Users\Public\Documents\KleiKodeshProject\KitveiHakodesh\CSharpBackend\Ftslib-Csharp\FtsLibTest\bin\Release";
        public static string Index500k => System.IO.Path.Combine(MainBin, "index_500k");
        public static string IndexFull => System.IO.Path.Combine(MainBin, "index_full");
    }
}
