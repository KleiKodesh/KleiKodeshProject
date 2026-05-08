using FtsLib.SeforimDb;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Builds a fresh multi-segment index (no force-merge) into index_{tier}_fresh.
    /// This index is the stable input for concurrentmerge — it is never auto-wiped.
    ///
    /// Usage:
    ///   FtsLibTest.exe buildfresh [tier]
    ///
    /// Always wipes any existing index_{tier}_fresh before building.
    /// </summary>
    internal static class BuildFreshTest
    {
        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string tierLabel = args.Length > 1 ? args[1] : "full";
            string label;
            int    limit;
            try
            {
                var tier = TestHelpers.ResolveTier(tierLabel);
                label = tier.Label;
                limit = tier.Limit;
            }
            catch (ArgumentException ex) { Console.WriteLine(ex.Message); return; }

            string indexDir = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                $"index_{label}_fresh");
            string dbPath = BuildTest.ResolveDbPath();

            if (Directory.Exists(indexDir))
            {
                Console.WriteLine($"Wiping: {indexDir}");
                Directory.Delete(indexDir, recursive: true);
            }
            Directory.CreateDirectory(indexDir);

            Console.WriteLine($"Building fresh index (no merge): {indexDir}");
            var index = new SeforimIndex(indexDir, dbPath);
            var sw    = Stopwatch.StartNew();
            long n    = 0;
            index.BuildIndex(limit: limit, onProgress: c => n = c);
            sw.Stop();

            Console.WriteLine($"Done: {n:N0} lines in {TestHelpers.FormatElapsed(sw.Elapsed)}");

            var segs = Directory.GetFiles(indexDir, "seg_*.dat");
            Console.WriteLine($"Segments: {segs.Length}");
            foreach (var f in segs)
                Console.WriteLine($"  {Path.GetFileName(f),22}  {new FileInfo(f).Length / 1_048_576.0:F1} MB");

            Console.WriteLine("Ready for: FtsLibTest.exe concurrentmerge " + label);
        }
    }
}
