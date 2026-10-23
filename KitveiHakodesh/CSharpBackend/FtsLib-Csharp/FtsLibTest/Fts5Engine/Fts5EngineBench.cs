using FtsLib.Fts5;
using FtsLib.SeforimDb;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace FtsLibTest.Fts5Engine
{
    /// <summary>
    /// <c>fts5engine build [dbPath] [outDir] [limit]</c> — builds an <see cref="Fts5Index"/>.
    /// <c>fts5engine compare [ftsLibIndexDir] [fts5Dir] [dbPath]</c> — runs PerformanceTest's
    /// query battery through FtsLib (SeforimIndex) and the FTS5 engine and reports, per query:
    /// whether the matching line ids are IDENTICAL, time to drain every id (SearchIds), and
    /// time to the first batch the app shows: Search with NO cap, consumed as a stream, first
    /// 200 results with snippets and the case's ordered/word-distance filter — exactly how
    /// Core's SeforimDbFtsSearcher consumes it. Plus on-disk sizes. Labels only.
    /// Defaults: the full-tier FtsLib index, &lt;that&gt;_fts5, TestHelpers.ResolveDbPath().
    /// </summary>
    internal static class Fts5EngineBench
    {
        public static void Run(string[] args)
        {
            string mode = args.Length > 1 ? args[1].ToLowerInvariant() : "";
            if (mode == "build")
            {
                string dbPath = args.Length > 2 ? args[2] : TestHelpers.ResolveDbPath();
                string outDir = args.Length > 3 ? args[3] : DefaultFts5Dir();
                int limit = args.Length > 4 ? int.Parse(args[4]) : 0;
                Fts5Index.Build(() => new ZayitDb(dbPath), outDir, limit);
                PrintSizes("fts5", outDir);
                return;
            }
            if (mode == "resume")
            {
                // The service's resumable build (Fts5SeforimIndex.BuildIndex) over an existing
                // directory — for hard-kill tests: kill it, run it again, until "DONE".
                string dbPath = args[2], dir = args[3];
                int limit = args.Length > 4 ? int.Parse(args[4]) : 0;
                var index = new Fts5SeforimIndex(dir, () => new ZayitDb(dbPath));
                index.GetResumeState(out int lastId, out _, out long offset);
                Console.WriteLine($"START resume after line {lastId} ({offset} lines done)");
                index.BuildIndex(limit, n => { if ((offset + n) % 50_000 == 0) Console.WriteLine($"lines {offset + n}"); },
                                 onMergeProgress: (d, e) => Console.WriteLine($"final phase {d}/{e}"));
                Console.WriteLine("DONE");
                return;
            }
            if (mode == "live")
            {
                string dbPath = args.Length > 2 ? args[2] : TestHelpers.ResolveDbPath();
                string outDir = args.Length > 3 ? args[3] : DefaultFts5Dir() + "_live";
                int limit = args.Length > 4 ? int.Parse(args[4]) : 0;
                Live(dbPath, outDir, limit);
                return;
            }
            Console.WriteLine("Usage: fts5engine build [dbPath] [outDir] [limit] | fts5engine resume dbPath dir [limit] | fts5engine live [dbPath] [outDir] [limit]");
        }

        /// <summary>
        /// Searching DURING a build, and resume: starts a fresh Fts5SeforimIndex build, cancels
        /// it after a few committed batches, resumes it, and meanwhile runs a literal, a
        /// wildcard and a fuzzy case every second — printing elapsed time, build progress and
        /// hit counts (labels only). Ends with the same cases on the finished index.
        /// </summary>
        static void Live(string dbPath, string dir, int limit)
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            var index = new Fts5SeforimIndex(dir, () => new ZayitDb(dbPath));
            var picks = new[] { 0, 11, 13, 20 };   // literal, prefix wildcard, suffix wildcard, fuzzy
            long done = 0; int commits = 0; bool finished = false; Exception failure = null;
            var clock = Stopwatch.StartNew();

            var builder = new System.Threading.Thread(() =>
            {
                try
                {
                    using (var cts = new System.Threading.CancellationTokenSource())
                    {
                        try
                        {
                            index.BuildIndex(limit, n => done = n, () => { if (++commits == 3) cts.Cancel(); }, ct: cts.Token);
                            Console.WriteLine("  (build finished before the planned interruption)");
                        }
                        catch (OperationCanceledException) { Console.WriteLine($"  [{clock.Elapsed.TotalSeconds,5:F1}s] build INTERRUPTED after {commits} commits"); }
                    }
                    index.GetResumeState(out int lastId, out _, out long offset);
                    Console.WriteLine($"  [{clock.Elapsed.TotalSeconds,5:F1}s] resuming after line {lastId:N0} ({offset:N0} lines already done)");
                    index.BuildIndex(limit, n => done = offset + n, () => commits++);
                }
                catch (Exception ex) { failure = ex; }
                finally { finished = true; }
            });
            builder.Start();

            void Probe(string when)
            {
                var line = new System.Text.StringBuilder($"  [{clock.Elapsed.TotalSeconds,5:F1}s] {when,-9} lines {done,9:N0}");
                foreach (int i in picks)
                {
                    var sw = Stopwatch.StartNew();
                    int n;
                    try { n = index.SearchIds(QueryCases.Cases[i].Query).Count(); }
                    catch (Exception ex) { line.Append($" | #{i} ERROR {ex.GetType().Name}"); continue; }
                    line.Append($" | #{i} {n,7:N0} {sw.Elapsed.TotalMilliseconds,6:F0}ms");
                }
                Console.WriteLine(line);
            }

            while (!finished) { Probe("building"); System.Threading.Thread.Sleep(1000); }
            builder.Join();
            if (failure != null) { Console.WriteLine("BUILD FAILED: " + failure); return; }
            Probe("finished");
            Probe("finished");
            PrintSizes("fts5 live", dir);
        }

        static string DefaultFts5Dir() => TestHelpers.IndexDir("full").TrimEnd('\\', '/') + "_fts5";

        static void PrintSizes(string name, string dir)
        {
            if (!Directory.Exists(dir)) { Console.WriteLine($"{name}: {dir} missing"); return; }
            var files = Directory.GetFiles(dir);
            long total = files.Sum(f => new FileInfo(f).Length);
            Console.WriteLine($"{name} index MB {total / 1048576.0:F1}  (" +
                string.Join(", ", files.Select(f => $"{Path.GetFileName(f)} {new FileInfo(f).Length / 1048576.0:F1}")) + ")");
        }
    }
}
