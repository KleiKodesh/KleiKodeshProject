using FtsLib;
using FtsLib.Index;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualBasic;

namespace FtsLibTest
{
    /// <summary>
    /// Indexes the first 500k lines WITHOUT skip lists, then runs the same
    /// search as QuickTest. Compare search times to measure skip list speedup.
    /// </summary>
    internal static class NoSkipTest
    {
        private const string SearchQuery = "כי ביצחק";
        private const int    LineLimit   = 500_000;

        public static void Run()
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("  ── NO-SKIP TEST (500k lines, no skip list) ──");
            Console.ResetColor();

            string dbPath = ResolveDbPath();
            if (!File.Exists(dbPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ✗ DB not found: {dbPath}");
                Console.ResetColor();
                return;
            }

            using (var conn = OpenConnection(dbPath))
            {
                // ---- build index WITHOUT skip lists ----
                var index     = new IndexManager(useSkipList: false);
                var tokenizer = new Tokenizer();
                long linesIndexed = 0;
                var swBuild = Stopwatch.StartNew();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"SELECT id, content FROM line ORDER BY id LIMIT {LineLimit}";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            int    lineId  = r.GetInt32(0);
                            string content = r.IsDBNull(1) ? string.Empty : r.GetString(1);
                            foreach (var term in tokenizer.Extract(content))
                                index.Add(term, lineId);
                            linesIndexed++;
                        }
                    }
                }
                swBuild.Stop();

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  · Indexed {linesIndexed:N0} lines in {swBuild.ElapsedMilliseconds:N0} ms  " +
                                  $"({index.TermCount:N0} terms)");
                Console.ResetColor();

                // ---- search (5 runs) ----
                var qt        = new Tokenizer();
                var queryTerms = new List<string>(qt.Extract(SearchQuery));

                Console.WriteLine($"  · Query: \"{SearchQuery}\"  →  [{string.Join(", ", queryTerms)}]");
                foreach (var term in queryTerms)
                    Console.WriteLine($"    '{term}' → {index.GetTermCount(term):N0} lines");

                var times = new List<long>(5);
                int resultCount = 0;
                for (int i = 0; i < 5; i++)
                {
                    var sw = Stopwatch.StartNew();
                    var results = new List<int>(index.Search(queryTerms));
                    sw.Stop();
                    times.Add(sw.ElapsedMilliseconds);
                    resultCount = results.Count;
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"  · Run {i + 1}: {sw.ElapsedMilliseconds} ms  →  {results.Count} results");
                    Console.ResetColor();
                }

                long   minT = times[0]; foreach (var t in times) if (t < minT) minT = t;
                long   maxT = times[0]; foreach (var t in times) if (t > maxT) maxT = t;
                double avgT = 0; foreach (var t in times) avgT += t; avgT /= times.Count;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  ✓ Results: {resultCount}  |  " +
                                  $"min={minT} ms  avg={avgT:F1} ms  max={maxT} ms  (NO skip list)");
                Console.ResetColor();
            }
        }

        private static string ResolveDbPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string def  = Path.Combine(appData, "io.github.kdroidfilter.seforimapp", "databases", "seforim.db");
            return Interaction.GetSetting("ZayitApp", "Database", "Path", def);
        }

        private static SQLiteConnection OpenConnection(string dbPath)
        {
            var conn = new SQLiteConnection(
                $"Data Source={dbPath};Version=3;Read Only=True;Page Size=4096;Cache Size=100000;Temp Store=Memory;");
            conn.Open();
            return conn;
        }
    }
}
