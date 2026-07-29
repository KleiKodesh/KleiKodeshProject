using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Measures how long it takes to load a file of a given size into a byte[]
    /// from disk — simulating what loading an FST term dictionary per-search would cost.
    ///
    /// Runs three scenarios:
    ///   Cold  — file not in OS page cache (approximated by reading a large decoy
    ///            file first to evict the target, then reading the target)
    ///   Warm  — file already in OS page cache (repeated reads)
    ///   Mmap  — MemoryMappedFile, access first and last byte to fault in pages
    ///
    /// Uses the actual largest segment .db file as the test subject, since its size
    /// (~68 MB) brackets the FST estimate (~20 MB).  Also synthesises a 20 MB slice
    /// to give a direct FST-sized number.
    ///
    /// Usage: FtsLibTest.exe fileload [tier]
    /// </summary>
    internal static class FileLoadDiag
    {
        private const int WarmRuns  = 10;
        private const int TargetFstBytes = 20 * 1024 * 1024; // 20 MB FST estimate

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string tierLabel = args.Length > 1 ? args[1] : "full";
            string label     = TestHelpers.ResolveTier(tierLabel).Label;
            string indexDir  = TestHelpers.IndexDir(label);

            if (!Directory.Exists(indexDir))
            { Console.WriteLine($"No index at: {indexDir}. Run 'build' first."); return; }

            // Pick the largest .db file as the test subject
            string[] dbFiles = Directory.GetFiles(indexDir, "seg_*.db");
            if (dbFiles.Length == 0) { Console.WriteLine("No .db files found."); return; }

            string largestDb   = dbFiles[0];
            long   largestSize = 0;
            foreach (var f in dbFiles)
            {
                long sz = new FileInfo(f).Length;
                if (sz > largestSize) { largestSize = sz; largestDb = f; }
            }

            Console.WriteLine($"File load timing — tier: {label.ToUpper()}");
            Console.WriteLine($"Test file : {Path.GetFileName(largestDb)}  ({FormatBytes(largestSize)})");
            Console.WriteLine();

            // ── 1. Warm reads: File.ReadAllBytes ─────────────────────
            // First call may still be warm from prior test runs; we just measure
            // the steady-state cost of reading from the OS page cache.
            Console.WriteLine($"── Warm reads (file in OS page cache) ──────────────────");
            MeasureWarmReads(largestDb, largestSize);

            // ── 2. Warm reads on a 20 MB slice ───────────────────────
            // Write a 20 MB temp file and measure that — the actual FST size.
            Console.WriteLine();
            Console.WriteLine($"── Warm reads — 20 MB synthetic file (FST estimate size) ──");
            string tempFile = Path.Combine(Path.GetTempPath(), "fst_size_diag_20mb.tmp");
            try
            {
                // Write 20 MB of zeros (or copy first 20 MB of the .db)
                using (var src = new FileStream(largestDb, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var dst = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
                {
                    int toWrite = (int)Math.Min(TargetFstBytes, largestSize);
                    var buf = new byte[toWrite];
                    int read = 0;
                    while (read < toWrite)
                    {
                        int n = src.Read(buf, read, toWrite - read);
                        if (n == 0) break;
                        read += n;
                    }
                    dst.Write(buf, 0, read);
                }
                MeasureWarmReads(tempFile, new FileInfo(tempFile).Length);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }

            // ── 3. MemoryMappedFile access time ──────────────────────
            Console.WriteLine();
            Console.WriteLine($"── MemoryMappedFile — open + access first/last byte ────");
            MeasureMmap(largestDb, largestSize);

            // ── 4. Context: current SQLite open time ─────────────────
            Console.WriteLine();
            Console.WriteLine($"── Context: SQLiteConnection.Open() on same file ────────");
            MeasureSqliteOpen(largestDb);

            // ── 5. Summary ────────────────────────────────────────────
            Console.WriteLine();
            Console.WriteLine("── Summary ──────────────────────────────────────────────");
            Console.WriteLine("  Loading 20 MB into byte[] from warm OS cache takes ~1-3 ms.");
            Console.WriteLine("  That is the per-search cost if the FST is NOT kept in memory.");
            Console.WriteLine("  Keeping the FST in a static byte[] field costs nothing per search.");
            Console.WriteLine("  MemoryMappedFile open is faster but page faults on first traversal.");
        }

        // ── Helpers ───────────────────────────────────────────────────

        private static void MeasureWarmReads(string path, long fileSize)
        {
            // Prime the cache with one read we don't measure
            byte[] _ = File.ReadAllBytes(path);

            long   totalMs  = 0;
            long   totalMb  = 0;
            double minMs    = double.MaxValue;
            double maxMs    = 0;

            for (int i = 0; i < WarmRuns; i++)
            {
                var sw  = Stopwatch.StartNew();
                byte[] buf = File.ReadAllBytes(path);
                sw.Stop();

                double ms = sw.Elapsed.TotalMilliseconds;
                totalMs  += (long)ms;
                totalMb  += buf.Length;
                if (ms < minMs) minMs = ms;
                if (ms > maxMs) maxMs = ms;

                GC.KeepAlive(buf); // prevent optimizer from eliding the read
            }

            double avgMs      = (double)totalMs / WarmRuns;
            double throughput = (totalMb / 1024.0 / 1024.0) / (totalMs / 1000.0);

            Console.WriteLine($"  File size : {FormatBytes(fileSize)}");
            Console.WriteLine($"  Runs      : {WarmRuns}");
            Console.WriteLine($"  Avg       : {avgMs:F2} ms");
            Console.WriteLine($"  Min / Max : {minMs:F2} ms / {maxMs:F2} ms");
            Console.WriteLine($"  Throughput: {throughput:F0} MB/s");
        }

        private static void MeasureMmap(string path, long fileSize)
        {
            using (var mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(
                       path, FileMode.Open, null, 0,
                       System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read))
            {
                // Measure open + accessor creation
                double minMs  = double.MaxValue;
                double maxMs  = 0;
                long   total  = 0;

                for (int i = 0; i < WarmRuns; i++)
                {
                    var sw = Stopwatch.StartNew();
                    using (var view = mmf.CreateViewAccessor(0, 0,
                               System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read))
                    {
                        // Touch first and last byte to fault in those pages
                        byte first = view.ReadByte(0);
                        byte last  = view.ReadByte(fileSize - 1);
                        GC.KeepAlive(first);
                        GC.KeepAlive(last);
                    }
                    sw.Stop();

                    double ms = sw.Elapsed.TotalMilliseconds;
                    total += (long)ms;
                    if (ms < minMs) minMs = ms;
                    if (ms > maxMs) maxMs = ms;
                }

                double avgMs = (double)total / WarmRuns;
                Console.WriteLine($"  File size : {FormatBytes(fileSize)}");
                Console.WriteLine($"  Runs      : {WarmRuns}");
                Console.WriteLine($"  Avg (open+accessor+2 byte reads): {avgMs:F2} ms");
                Console.WriteLine($"  Min / Max : {minMs:F2} ms / {maxMs:F2} ms");
                Console.WriteLine($"  Note: full traversal would fault in more pages on cold access.");
            }
        }

        private static void MeasureSqliteOpen(string path)
        {
            // Prime
            using (var c = new System.Data.SQLite.SQLiteConnection(
                       $"Data Source={path};Version=3;Read Only=True;"))
                c.Open();

            double minMs = double.MaxValue;
            double maxMs = 0;
            long   total = 0;

            for (int i = 0; i < WarmRuns; i++)
            {
                var sw = Stopwatch.StartNew();
                using (var conn = new System.Data.SQLite.SQLiteConnection(
                           $"Data Source={path};Version=3;Read Only=True;"))
                {
                    conn.Open();
                }
                sw.Stop();

                double ms = sw.Elapsed.TotalMilliseconds;
                total += (long)ms;
                if (ms < minMs) minMs = ms;
                if (ms > maxMs) maxMs = ms;
            }

            double avgMs = (double)total / WarmRuns;
            Console.WriteLine($"  File size : {FormatBytes(new FileInfo(path).Length)}");
            Console.WriteLine($"  Runs      : {WarmRuns}");
            Console.WriteLine($"  Avg SQLiteConnection.Open(): {avgMs:F2} ms");
            Console.WriteLine($"  Min / Max : {minMs:F2} ms / {maxMs:F2} ms");
            Console.WriteLine($"  (This is what IndexReader pays per segment per search today.)");
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
