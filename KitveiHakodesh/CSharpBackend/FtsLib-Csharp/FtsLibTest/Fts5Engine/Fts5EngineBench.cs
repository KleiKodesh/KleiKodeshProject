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
    /// Defaults: the full-tier FtsLib index, &lt;that&gt;_fts5, BuildTest.ResolveDbPath().
    /// </summary>
    internal static class Fts5EngineBench
    {
        const int FirstBatch = 200;

        public static void Run(string[] args)
        {
            string mode = args.Length > 1 ? args[1].ToLowerInvariant() : "";
            if (mode == "build")
            {
                string dbPath = args.Length > 2 ? args[2] : BuildTest.ResolveDbPath();
                string outDir = args.Length > 3 ? args[3] : DefaultFts5Dir();
                int limit = args.Length > 4 ? int.Parse(args[4]) : 0;
                Fts5Index.Build(() => new ZayitDb(dbPath), outDir, limit);
                PrintSizes("fts5", outDir);
                return;
            }
            if (mode == "compare")
            {
                string ftsLibDir = args.Length > 2 ? args[2] : TestHelpers.IndexDir("full");
                string fts5Dir = args.Length > 3 ? args[3] : DefaultFts5Dir();
                string dbPath = args.Length > 4 ? args[4] : BuildTest.ResolveDbPath();
                Compare(ftsLibDir, fts5Dir, dbPath);
                return;
            }
            Console.WriteLine("Usage: fts5engine build [dbPath] [outDir] [limit] | fts5engine compare [ftsLibIndexDir] [fts5Dir] [dbPath]");
        }

        static string DefaultFts5Dir() => TestHelpers.IndexDir("full").TrimEnd('\\', '/') + "_fts5";

        static void Compare(string ftsLibDir, string fts5Dir, string dbPath)
        {
            PrintSizes("ftslib", ftsLibDir);
            PrintSizes("fts5", fts5Dir);

            var ftsLib = new SeforimIndex(ftsLibDir, () => new ZayitDb(dbPath));
            using var fts5 = new Fts5SearchEngine(fts5Dir, () => new ZayitDb(dbPath));
            var cases = PerformanceTest.Cases;

            var rows = new List<(string label, int ids, bool same, double libIds, double f5Ids, double libFirst, double f5First, int libPassed, int f5Passed)>();
            for (int pass = 0; pass < 2; pass++) // pass 0 warms caches + JIT; pass 1 is measured
            {
                rows.Clear();
                foreach (var c in cases)
                {
                    var sw = Stopwatch.StartNew();
                    var libIds = ftsLib.SearchIds(c.Query).ToList();
                    double libIdsMs = sw.Elapsed.TotalMilliseconds;

                    sw.Restart();
                    var f5Ids = fts5.SearchIds(c.Query).ToList();
                    double f5IdsMs = sw.Elapsed.TotalMilliseconds;

                    bool same = libIds.SequenceEqual(f5Ids);

                    sw.Restart();
                    int libPassed = FirstBatchPassed(ftsLib.Search(c.Query).Take(FirstBatch), r => ftsLib.GenerateSnippet(r, c.RequireOrdered), c.MaxWordDistance);
                    double libFirstMs = sw.Elapsed.TotalMilliseconds;

                    sw.Restart();
                    int f5Passed = FirstBatchPassed(fts5.Search(c.Query).Take(FirstBatch), r => Fts5SearchEngine.GenerateSnippet(r, c.RequireOrdered), c.MaxWordDistance);
                    double f5FirstMs = sw.Elapsed.TotalMilliseconds;

                    rows.Add((Ascii(c.Label), libIds.Count, same, libIdsMs, f5IdsMs, libFirstMs, f5FirstMs, libPassed, f5Passed));
                }
            }

            Console.WriteLine();
            Console.WriteLine($"{"#",3} {"case",-46}{"ids",10} {"same",5}{"lib ids",10}{"fts5 ids",10}{"lib 1st",10}{"fts5 1st",10}{"passed",9}");
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                string label = r.label.Length > 45 ? r.label.Substring(0, 45) : r.label;
                string passed = r.libPassed == r.f5Passed ? r.libPassed.ToString() : $"{r.libPassed}!={r.f5Passed}";
                Console.WriteLine($"{i,3} {label,-46}{r.ids,10:N0} {(r.same ? "yes" : "NO"),5}{r.libIds,10:F1}{r.f5Ids,10:F1}{r.libFirst,10:F1}{r.f5First,10:F1}{passed,9}");
            }
            Console.WriteLine();
            Console.WriteLine($"identical id sets: {rows.Count(r => r.same)}/{rows.Count}");
            Console.WriteLine($"sum ids ms        lib {rows.Sum(r => r.libIds),9:F0}   fts5 {rows.Sum(r => r.f5Ids),9:F0}");
            Console.WriteLine($"sum first-200 ms  lib {rows.Sum(r => r.libFirst),9:F0}   fts5 {rows.Sum(r => r.f5First),9:F0}");
            Console.WriteLine($"median ids ms     lib {Median(rows.Select(r => r.libIds)),9:F1}   fts5 {Median(rows.Select(r => r.f5Ids)),9:F1}");
        }

        static int FirstBatchPassed(IEnumerable<SearchResult> results, Func<SearchResult, SnippetResult> snippet, int maxWordDistance)
        {
            int passed = 0;
            foreach (var r in results)
            {
                var s = snippet(r);
                if (s.IsMatch && s.WordDistance <= maxWordDistance) passed++;
            }
            return passed;
        }

        static void PrintSizes(string name, string dir)
        {
            if (!Directory.Exists(dir)) { Console.WriteLine($"{name}: {dir} missing"); return; }
            var files = Directory.GetFiles(dir);
            long total = files.Sum(f => new FileInfo(f).Length);
            Console.WriteLine($"{name} index MB {total / 1048576.0:F1}  (" +
                string.Join(", ", files.Select(f => $"{Path.GetFileName(f)} {new FileInfo(f).Length / 1048576.0:F1}")) + ")");
        }

        static double Median(IEnumerable<double> values)
        {
            var s = values.OrderBy(v => v).ToArray();
            return s.Length == 0 ? 0 : s[s.Length / 2];
        }

        static string Ascii(string s) => new string(s.Select(ch => ch < 128 ? ch : '#').ToArray());
    }
}
