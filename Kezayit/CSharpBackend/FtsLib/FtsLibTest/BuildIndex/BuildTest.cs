using FtsLib.SeforimDb;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace FtsLibTest
{
    /// <summary>
    /// Builds the FTS index from the seforim database and produces a detailed
    /// HTML benchmark report fragment.
    ///
    /// Usage (standalone):
    ///   FtsLibTest.exe build [tier]   tier = 500k | 1m | 3m | full  (default: 500k)
    ///
    /// Or call RunAndGetFragment() to get the HTML fragment for a combined report.
    /// </summary>
    internal static class BuildTest
    {
        private static readonly string[] SmokeQueries =
        {
            "כי ביצחק",
            "שויתי לנגדי תמיד",
            "תורה מצוה",
            "אברהם יצחק יעקב",
            "וידבר משה כן אל בני",
        };

        // ── Entry points ──────────────────────────────────────────────

        /// <summary>Standalone run: builds index, saves its own HTML report, opens it.</summary>
        public static void Run(string[] args)
        {
            string label;
            int    limit;
            string dbPath;
            string indexDir;
            try
            {
                string tierLabel = args.Length > 1 ? args[1] : "500k";
                var tier = TestHelpers.ResolveTier(tierLabel);
                label    = tier.Label;
                limit    = tier.Limit;
                dbPath   = ResolveDbPath();
                indexDir = TestHelpers.IndexDir(label);
            }
            catch (ArgumentException ex) { Console.WriteLine(ex.Message); return; }

            string path = TempReportPath("build", label);
            var report  = new HtmlReport($"Build Report — {label.ToUpper()}");
            RunCore(label, limit, dbPath, indexDir, report);
            report.SaveAndOpen(path);
        }

        /// <summary>
        /// Runs the build and returns the HTML fragment for embedding in a combined report.
        /// Also returns the SeforimIndex so the caller can reuse it for search.
        /// </summary>
        public static (string fragment, SeforimIndex index) RunAndGetFragment(
            string tierLabel, string dbPath, string indexDir)
        {
            var    tier   = TestHelpers.ResolveTier(tierLabel);
            string lbl    = tier.Label;
            int    lim    = tier.Limit;
            var    report = new HtmlReport($"Build Report — {lbl.ToUpper()}");
            var    index  = RunCore(lbl, lim, dbPath, indexDir, report);
            return (report.ToFragment(), index);
        }

        // ── Core ──────────────────────────────────────────────────────

        /// <summary>Runs the build, populates the report, returns the SeforimIndex.</summary>
        private static SeforimIndex RunCore(
            string     tierLabel,
            int        limit,
            string     dbPath,
            string     indexDir,
            HtmlReport report)
        {
            string limitStr = limit == 0 ? "all lines" : $"{limit:N0} lines";

            report.AddBanner($"FTS Index Build  ·  Tier: {tierLabel.ToUpper()}  ·  {limitStr}");
            report.AddMeta("Started",   DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AddMeta("DB path",   dbPath);
            report.AddMeta("Index dir", indexDir);
            report.AddMeta("Limit",     limitStr);

            Console.WriteLine();
            Console.WriteLine($"╔══ BUILD — {tierLabel.ToUpper()} ({limitStr}) ══");
            Console.WriteLine($"║  DB    : {dbPath}");
            Console.WriteLine($"║  Index : {indexDir}");

            // ── Clean previous index ──────────────────────────────────
            if (Directory.Exists(indexDir))
            {
                Console.WriteLine("║  Removing previous index…");
                Directory.Delete(indexDir, recursive: true);
            }

            // ── Build ─────────────────────────────────────────────────
            report.AddSection("Index Build — Progress");

            var  progressRows = new List<HtmlReport.ProgressRow>();
            long lastReport   = 0;
            long linesIndexed = 0;
            var  swBuild      = Stopwatch.StartNew();
            var  swInterval   = Stopwatch.StartNew();

            Console.WriteLine("║");
            Console.WriteLine($"║  {"Lines",12}  {"Elapsed",10}  {"Rate",12}  {"Δ lines",10}  {"Δ time",8}");
            Console.WriteLine($"║  {new string('─', 12)}  {new string('─', 10)}  {new string('─', 12)}  {new string('─', 10)}  {new string('─', 8)}");

            var index = new SeforimIndex(indexDir, dbPath);

            try
            {
                index.BuildIndex(
                    limit: limit,
                    onProgress: n =>
                    {
                        linesIndexed = n;
                        if (n - lastReport >= 100_000)
                        {
                            long   delta   = n - lastReport;
                            double dSec    = swInterval.Elapsed.TotalSeconds;
                            string rate    = dSec > 0 ? $"{delta / dSec:N0}/s" : "—";
                            string elapsed = TestHelpers.FormatElapsed(swBuild.Elapsed);
                            string dtime   = TestHelpers.FormatElapsed(swInterval.Elapsed);

                            Console.WriteLine(
                                $"║  {n,12:N0}  {elapsed,10}  {rate,12}  {delta,10:N0}  {dtime,8}");

                            progressRows.Add(new HtmlReport.ProgressRow
                            {
                                Lines     = n,
                                Elapsed   = elapsed,
                                Rate      = rate,
                                Delta     = delta,
                                DeltaTime = dtime,
                            });

                            lastReport = n;
                            swInterval.Restart();
                        }
                    });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"║  BUILD FAILED: {ex.Message}");
                report.AddAlert($"Build failed: {ex.Message}", isError: true);
                return index;
            }

            swBuild.Stop();
            report.AddProgressTable(progressRows);

            // ── Build summary ─────────────────────────────────────────
            report.AddSection("Build Summary");
            var summaryRows = new List<IReadOnlyList<string>>
            {
                new[] { "Lines indexed",  $"{linesIndexed:N0}" },
                new[] { "Total time",     TestHelpers.FormatElapsed(swBuild.Elapsed) },
                new[] { "Average rate",   TestHelpers.FormatRate(linesIndexed, swBuild.Elapsed) },
                new[] { "Finished",       DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
            };
            report.AddTable(new[] { "Metric", "Value" }, summaryRows);

            Console.WriteLine($"║");
            Console.WriteLine($"║  Lines indexed : {linesIndexed:N0}");
            Console.WriteLine($"║  Total time    : {TestHelpers.FormatElapsed(swBuild.Elapsed)}");
            Console.WriteLine($"║  Average rate  : {TestHelpers.FormatRate(linesIndexed, swBuild.Elapsed)}");

            // ── Index size on disk ────────────────────────────────────
            report.AddSection("Index Size on Disk");
            var diskRows  = new List<IReadOnlyList<string>>();
            long totalBytes = 0;
            if (Directory.Exists(indexDir))
            {
                foreach (var f in Directory.GetFiles(indexDir, "*", SearchOption.AllDirectories))
                {
                    var fi = new FileInfo(f);
                    totalBytes += fi.Length;
                    diskRows.Add(new[] { Path.GetFileName(f), FormatBytes(fi.Length) });
                }
            }
            diskRows.Add(new[] { "TOTAL", FormatBytes(totalBytes) });
            report.AddTable(new[] { "File", "Size" }, diskRows);

            Console.WriteLine($"║  Index size    : {FormatBytes(totalBytes)}");

            // ── Smoke search ──────────────────────────────────────────
            report.AddSection("Smoke Search (post-build)");
            var smokeRows = new List<IReadOnlyList<string>>();

            Console.WriteLine("║");
            Console.WriteLine($"║  {"Query",-40}  {"Results",10}  {"Time",8}  Status");
            Console.WriteLine($"║  {new string('─', 40)}  {new string('─', 10)}  {new string('─', 8)}  {new string('─', 8)}");

            try
            {
                foreach (var query in SmokeQueries)
                {
                    var swS  = Stopwatch.StartNew();
                    int cnt  = 0;
                    foreach (var _ in index.Search(query)) cnt++;
                    swS.Stop();

                    string status = cnt > 0 ? "✓" : "0 results";
                    smokeRows.Add(new[] { query, $"{cnt:N0}", $"{swS.ElapsedMilliseconds} ms", status });

                    Console.WriteLine(
                        $"║  {TestHelpers.Truncate(query, 40),-40}  " +
                        $"{cnt,10:N0}  {swS.ElapsedMilliseconds,7} ms  {status}");
                }
            }
            catch (Exception ex)
            {
                report.AddAlert($"Smoke search failed: {ex.Message}", isError: true);
                Console.WriteLine($"║  SMOKE SEARCH FAILED: {ex.Message}");
            }

            report.AddTable(
                new[] { "Query", "Results", "Time", "Status" },
                smokeRows,
                cellClass: (r, c) => c == 3 ? (smokeRows[r][3] == "✓" ? "ok" : "bogus") : null);

            Console.WriteLine("╚══ BUILD DONE ══");

            return index;
        }

        // ── Helpers ───────────────────────────────────────────────────

        internal static string TempReportPath(string kind, string tier) =>
            Path.Combine(Path.GetTempPath(),
                $"fts_{kind}_{tier}_{DateTime.Now:yyyyMMdd_HHmmss}.html");

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
            if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F1} MB";
            if (bytes >= 1_024)         return $"{bytes / 1_024.0:F1} KB";
            return $"{bytes} B";
        }

        internal static string ResolveDbPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string[] candidates =
            {
                Path.Combine(appData, "io.github.kdroidfilter.seforimapp", "databases", "seforim.db"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "seforim.db"),
            };
            foreach (var p in candidates)
                if (File.Exists(p)) return p;
            return candidates[0];
        }
    }
}
