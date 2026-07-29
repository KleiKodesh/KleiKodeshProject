using FtsLib.Indexing;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Estimates how large an FST term dictionary would be for a given index tier,
    /// based on the actual terms stored in the segment .db files.
    ///
    /// FST size model (from Lucene / Luwak research):
    ///   - A minimal FST over a sorted term set shares prefix/suffix nodes.
    ///   - Empirically, a minimal FST for a natural-language vocabulary costs
    ///     roughly 2–4 bytes per term (after prefix/suffix sharing), compared to
    ///     the raw UTF-8 byte cost of storing every term in full.
    ///   - For Hebrew (2-byte UTF-8 per character), terms average ~6–10 chars
    ///     = 12–20 raw bytes each.  With sharing, the FST compresses to ~3–5 bytes
    ///     per term on average.
    ///   - Each FST node also needs to store the output value (posting offset +
    ///     length + count = 16 bytes per term).  These outputs are stored on the
    ///     final arc of each term, so they add ~16 bytes × term_count to the total.
    ///
    /// This diagnostic measures:
    ///   1. Total distinct terms across all segments (after dedup across segments).
    ///   2. Total raw UTF-8 bytes of all terms.
    ///   3. Shared-prefix savings (how many bytes are shared with the previous term
    ///      in sorted order — a proxy for FST node sharing).
    ///   4. Estimated FST size = (non-shared bytes × 1.5 overhead) + (16 bytes × terms).
    ///
    /// Usage: FtsLibTest.exe fstsize [tier]
    /// </summary>
    internal static class FstSizeDiag
    {
        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string tierLabel = args.Length > 1 ? args[1] : "full";
            string label     = TestHelpers.ResolveTier(tierLabel).Label;
            string indexDir  = TestHelpers.IndexDir(label);

            Console.WriteLine($"FST size estimate — tier: {label.ToUpper()}");
            Console.WriteLine($"Index dir: {indexDir}");
            Console.WriteLine();

            if (!Directory.Exists(indexDir))
            {
                Console.WriteLine("Index directory not found. Run 'build' first.");
                return;
            }

            var datFiles = Directory.GetFiles(indexDir, "seg_*.dat");
            if (datFiles.Length == 0)
            {
                Console.WriteLine("No segment files found.");
                return;
            }
            Array.Sort(datFiles);

            // ── Per-segment stats ─────────────────────────────────────

            Console.WriteLine($"  {"Segment",-20}  {"Terms",10}  {"Raw bytes",12}  {"DB size",10}");
            Console.WriteLine($"  {new string('─', 20)}  {new string('─', 10)}  {new string('─', 12)}  {new string('─', 10)}");

            long totalTerms    = 0;
            long totalRawBytes = 0;

            foreach (var dat in datFiles)
            {
                string db = Path.ChangeExtension(dat, ".db");
                if (!File.Exists(db)) continue;

                long segTerms    = 0;
                long segRawBytes = 0;

                using (var conn = new SQLiteConnection($"Data Source={db};Version=3;Read Only=True;"))
                {
                    conn.Open();

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*), SUM(LENGTH(term)) FROM term_index";
                        using (var r = cmd.ExecuteReader())
                        {
                            if (r.Read() && !r.IsDBNull(0))
                            {
                                segTerms    = r.GetInt64(0);
                                segRawBytes = r.IsDBNull(1) ? 0 : r.GetInt64(1);
                            }
                        }
                    }
                }

                long dbSize = new FileInfo(db).Length;
                Console.WriteLine($"  {Path.GetFileName(dat),-20}  {segTerms,10:N0}  {segRawBytes,12:N0}  {FormatBytes(dbSize),10}");

                totalTerms    += segTerms;
                totalRawBytes += segRawBytes;
            }

            Console.WriteLine();

            // ── Dedup estimate ────────────────────────────────────────
            // After a full merge there is one segment, so no cross-segment duplication.
            // For multi-segment indexes, terms can appear in multiple segments.
            // We report both the raw total and a note about dedup.

            int segCount = datFiles.Length;
            Console.WriteLine($"  Segments:          {segCount}");
            Console.WriteLine($"  Total terms (sum): {totalTerms:N0}  (may include cross-segment duplicates)");
            Console.WriteLine();

            // ── Prefix sharing analysis ───────────────────────────────
            // Read all terms from the largest segment (most representative for a
            // merged index) and measure actual prefix sharing in sorted order.

            string largestDat = datFiles[datFiles.Length - 1]; // sorted by name, largest seg ID last
            // Find the segment with the most terms instead
            string bestDat    = datFiles[0];
            long   bestCount  = 0;
            foreach (var dat in datFiles)
            {
                string db = Path.ChangeExtension(dat, ".db");
                if (!File.Exists(db)) continue;
                using (var conn = new SQLiteConnection($"Data Source={db};Version=3;Read Only=True;"))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM term_index";
                        long c = (long)cmd.ExecuteScalar();
                        if (c > bestCount) { bestCount = c; bestDat = dat; }
                    }
                }
            }

            string bestDb = Path.ChangeExtension(bestDat, ".db");
            Console.WriteLine($"  Analysing prefix sharing in: {Path.GetFileName(bestDat)} ({bestCount:N0} terms)");
            Console.WriteLine();

            long sharedPrefixBytes  = 0;
            long uniqueSuffixBytes  = 0;
            long termCountInSeg     = 0;
            long rawBytesInSeg      = 0;
            long minLen = long.MaxValue;
            long maxLen = 0;
            var  lenBuckets = new Dictionary<int, int>(); // length → count

            string prevTerm = "";
            using (var conn = new SQLiteConnection($"Data Source={bestDb};Version=3;Read Only=True;"))
            {
                conn.Open();
                // Terms are stored in sorted order in the DB (inserted sorted during SegmentWriter)
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT term FROM term_index ORDER BY term";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            string term     = r.GetString(0);
                            byte[] termUtf8 = Encoding.UTF8.GetBytes(term);
                            int    termLen  = termUtf8.Length;

                            // Shared prefix length with previous term (in bytes)
                            byte[] prevUtf8 = Encoding.UTF8.GetBytes(prevTerm);
                            int    shared   = 0;
                            int    minL     = Math.Min(termLen, prevUtf8.Length);
                            while (shared < minL && termUtf8[shared] == prevUtf8[shared])
                                shared++;

                            sharedPrefixBytes += shared;
                            uniqueSuffixBytes += termLen - shared;
                            rawBytesInSeg     += termLen;
                            termCountInSeg++;

                            if (termLen < minLen) minLen = termLen;
                            if (termLen > maxLen) maxLen = termLen;

                            int bucket = (termLen / 2) * 2; // round down to even
                            if (!lenBuckets.ContainsKey(bucket)) lenBuckets[bucket] = 0;
                            lenBuckets[bucket]++;

                            prevTerm = term;
                        }
                    }
                }
            }

            double avgLen          = termCountInSeg > 0 ? (double)rawBytesInSeg / termCountInSeg : 0;
            double sharingRatio    = rawBytesInSeg  > 0 ? (double)sharedPrefixBytes / rawBytesInSeg : 0;

            Console.WriteLine($"  Term count:            {termCountInSeg:N0}");
            Console.WriteLine($"  Raw UTF-8 bytes:       {FormatBytes(rawBytesInSeg)}");
            Console.WriteLine($"  Avg term length:       {avgLen:F1} bytes  ({avgLen / 2:F1} Hebrew chars)");
            Console.WriteLine($"  Min / Max term length: {minLen} / {maxLen} bytes");
            Console.WriteLine($"  Shared prefix bytes:   {FormatBytes(sharedPrefixBytes)}  ({sharingRatio:P1} of raw)");
            Console.WriteLine($"  Unique suffix bytes:   {FormatBytes(uniqueSuffixBytes)}");
            Console.WriteLine();

            // ── FST size estimate ─────────────────────────────────────
            //
            // A minimal FST (like Lucene's FST or PetroProtsyk's) stores:
            //   - One node per unique suffix byte sequence (after sharing).
            //   - Each arc: ~1 byte label + ~1-2 bytes for flags/target pointer.
            //   - Output values on final arcs: 8 bytes (int64 offset) per term.
            //
            // Conservative model: 3 bytes per unique suffix byte + 8 bytes per term.
            // Optimistic model:   2 bytes per unique suffix byte + 8 bytes per term.
            // Pessimistic model:  5 bytes per unique suffix byte + 8 bytes per term.

            long outputBytes      = termCountInSeg * 8;  // 8-byte offset per term
            long fstOptimistic    = (long)(uniqueSuffixBytes * 2.0) + outputBytes;
            long fstConservative  = (long)(uniqueSuffixBytes * 3.0) + outputBytes;
            long fstPessimistic   = (long)(uniqueSuffixBytes * 5.0) + outputBytes;

            // Current SQLite .db size for comparison
            long currentDbSize = new FileInfo(bestDb).Length;

            Console.WriteLine("  FST size estimates (for this segment's term set):");
            Console.WriteLine($"    Optimistic  (2 bytes/unique-suffix-byte + 8 bytes/term): {FormatBytes(fstOptimistic)}");
            Console.WriteLine($"    Conservative(3 bytes/unique-suffix-byte + 8 bytes/term): {FormatBytes(fstConservative)}");
            Console.WriteLine($"    Pessimistic (5 bytes/unique-suffix-byte + 8 bytes/term): {FormatBytes(fstPessimistic)}");
            Console.WriteLine();
            Console.WriteLine($"  Current SQLite .db size (same segment):  {FormatBytes(currentDbSize)}");
            Console.WriteLine($"  FST vs SQLite (conservative):            {(double)fstConservative / currentDbSize:P1}");
            Console.WriteLine();

            // ── Term length distribution ──────────────────────────────
            Console.WriteLine("  Term length distribution (UTF-8 bytes):");
            var bucketKeys = new List<int>(lenBuckets.Keys);
            bucketKeys.Sort();
            foreach (var k in bucketKeys)
            {
                int count  = lenBuckets[k];
                double pct = termCountInSeg > 0 ? (double)count / termCountInSeg * 100 : 0;
                string bar = new string('█', (int)(pct / 2));
                Console.WriteLine($"    {k,3}-{k+1,2} bytes: {count,8:N0}  ({pct,5:F1}%)  {bar}");
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F2} GB";
            if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F2} MB";
            if (bytes >= 1_024)         return $"{bytes / 1_024.0:F1} KB";
            return $"{bytes} B";
        }
    }
}
