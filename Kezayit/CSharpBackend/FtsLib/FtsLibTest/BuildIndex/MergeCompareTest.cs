using FtsLib.SeforimDb;
using System;
using System.Diagnostics;
using System.IO;

namespace FtsLibTest
{
    /// <summary>
    /// Builds the full index with or without a force-merge at the end, then
    /// immediately runs the full search suite against it.
    ///
    /// Usage:
    ///   FtsLibTest.exe buildmerge   [tier]   — build + Optimize() + search
    ///   FtsLibTest.exe buildnomerge [tier]   — build only (no merge) + search
    ///
    /// Each command writes to its own index directory so both can coexist:
    ///   index_{tier}_merge     — with force-merge
    ///   index_{tier}_nomerge   — without force-merge
    ///
    /// The search suite is the same one used by SearchTest, so the results are
    /// directly comparable. The key assertion is whether line id=548 appears in
    /// the "כי ביצחק" results in both cases.
    /// </summary>
    internal static class MergeCompareTest
    {
        public static void Run(string[] args, bool forcemerge)
        {
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

            string suffix   = forcemerge ? "merge" : "nomerge";
            string indexDir = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                $"index_{label}_{suffix}");
            string dbPath   = BuildTest.ResolveDbPath();

            Console.WriteLine();
            Console.WriteLine($"╔══ BUILD ({suffix.ToUpper()}) — {label.ToUpper()} ══");
            Console.WriteLine($"║  Index : {indexDir}");
            Console.WriteLine($"║  DB    : {dbPath}");
            Console.WriteLine($"║  Force-merge after build: {forcemerge}");

            // ── Wipe previous run ─────────────────────────────────────
            // Always wipe both nomerge and merge dirs at the start of any run
            // so stale indexes from a previous tier never pollute results.
            string nomergeDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"index_{label}_nomerge");
            string mergeDir   = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"index_{label}_merge");
            foreach (string dir in new[] { nomergeDir, mergeDir })
            {
                if (Directory.Exists(dir))
                {
                    Console.WriteLine($"║  Removing {Path.GetFileName(dir)}…");
                    Directory.Delete(dir, recursive: true);
                }
            }
            Directory.CreateDirectory(indexDir);

            var index = new SeforimIndex(indexDir, dbPath);

            // ── Build ─────────────────────────────────────────────────
            var swBuild = Stopwatch.StartNew();
            long linesIndexed = 0;
            index.BuildIndex(
                limit: limit,
                onProgress: n => { linesIndexed = n; });
            swBuild.Stop();

            Console.WriteLine($"║  Build done: {linesIndexed:N0} lines in {TestHelpers.FormatElapsed(swBuild.Elapsed)}");

            // ── Optional force-merge ──────────────────────────────────
            if (forcemerge)
            {
                Console.WriteLine("║  Running Optimize() (force-merge)…");
                var swMerge = Stopwatch.StartNew();
                index.Optimize();
                swMerge.Stop();
                Console.WriteLine($"║  Optimize done in {TestHelpers.FormatElapsed(swMerge.Elapsed)}");
            }
            else
            {
                Console.WriteLine("║  Skipping Optimize() — searching multi-segment index");
            }

            // ── List resulting segments ───────────────────────────────
            Console.WriteLine("║  Segments on disk:");
            foreach (var f in Directory.GetFiles(indexDir, "seg_*.dat"))
                Console.WriteLine($"║    {Path.GetFileName(f)}  ({new FileInfo(f).Length / 1_048_576.0:F1} MB)");

            Console.WriteLine("╚══ BUILD DONE ══");
            Console.WriteLine();

            // ── Search suite ──────────────────────────────────────────
            string reportPath = BuildTest.TempReportPath($"search_{suffix}", label);
            string fragment   = SearchTest.RunAndGetFragment(label, dbPath, indexDir, index);

            var report = new HtmlReport($"Search ({suffix}) — {label.ToUpper()}");
            report.AddRawHtml(fragment);
            report.SaveAndOpen(reportPath);
        }
    }
}
