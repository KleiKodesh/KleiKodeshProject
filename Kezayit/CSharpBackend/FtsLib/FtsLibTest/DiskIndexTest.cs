using FtsLib;
using FtsLib.Index;
using FtsLib.Persistence;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualBasic;

namespace FtsLibTest
{
    /// <summary>
    /// Builds a disk index from 500k lines, saves it to postings.bin + index.db,
    /// then searches it using DiskIndexReader (no RAM index involved).
    /// </summary>
    internal static class DiskIndexTest
    {
        private const string SearchQuery = "כי ביצחק";

        private static readonly string PostingsPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "postings.bin");
        private static readonly string IndexDbPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "index.db");

        public static void Run(int lineLimit = 0) // 0 = full DB
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("  ── DISK INDEX TEST ──");
            Console.ResetColor();

            string dbPath = ResolveDbPath();
            if (!File.Exists(dbPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ✗ DB not found: {dbPath}");
                Console.ResetColor();
                return;
            }

            long ramStart = RamMB();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  · RAM at start: {ramStart} MB");
            Console.ResetColor();

            // ---- Phase 1: build RAM index ----
            Console.WriteLine($"  » Building index{(lineLimit > 0 ? $" from first {lineLimit:N0} lines" : " from full DB")}...");
            var index     = new IndexManager(useSkipList: false);
            var tokenizer = new Tokenizer();
            long linesIndexed = 0;

            var swBuild = Stopwatch.StartNew();
            using (var conn = OpenConnection(dbPath))
            using (var cmd  = conn.CreateCommand())
            {
                cmd.CommandText = lineLimit > 0
                    ? $"SELECT id, content FROM line ORDER BY id LIMIT {lineLimit}"
                    : "SELECT id, content FROM line ORDER BY id";
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
            long ramAfterIndex = RamMB();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  · RAM index built: {linesIndexed:N0} lines, " +
                              $"{index.TermCount:N0} terms, {swBuild.ElapsedMilliseconds:N0} ms  " +
                              $"RAM: {ramAfterIndex} MB (Δ +{ramAfterIndex - ramStart} MB)");
            Console.ResetColor();

            // ---- Phase 2: save to disk ----
            Console.WriteLine($"  » Saving to disk...");
            var swSave = Stopwatch.StartNew();
            index.SaveToDisk(PostingsPath, IndexDbPath);
            swSave.Stop();
            long ramAfterSave = RamMB();

            long postingsBytes = new FileInfo(PostingsPath).Length;
            long indexBytes    = new FileInfo(IndexDbPath).Length;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✓ Saved in {swSave.ElapsedMilliseconds:N0} ms  " +
                              $"postings.bin={postingsBytes / 1024:N0} KB  " +
                              $"index.db={indexBytes / 1024:N0} KB  " +
                              $"RAM: {ramAfterSave} MB");
            Console.ResetColor();

            // ---- free RAM index before searching from disk ----
            // (index goes out of scope here — GC will collect it)
            index = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            long ramAfterFree = RamMB();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  · RAM after freeing index: {ramAfterFree} MB (Δ -{ramAfterSave - ramAfterFree} MB freed)");
            Console.ResetColor();

            // ---- Phase 3: search from disk ----
            Console.WriteLine($"  » Searching from disk index...");
            var qt         = new Tokenizer();
            var queryTerms = new List<string>(qt.Extract(SearchQuery));

            using (var reader = new DiskIndexReader(PostingsPath, IndexDbPath))
            {
                Console.WriteLine($"  · Query: \"{SearchQuery}\"  →  [{string.Join(", ", queryTerms)}]");
                foreach (var term in queryTerms)
                    Console.WriteLine($"    '{term}' → {reader.GetTermCount(term):N0} lines");

                var times = new List<long>(5);
                int resultCount = 0;
                for (int i = 0; i < 5; i++)
                {
                    var sw = Stopwatch.StartNew();
                    var results = new List<int>(reader.Search(queryTerms));
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
                                  $"min={minT} ms  avg={avgT:F1} ms  max={maxT} ms  (disk index)");
                Console.ResetColor();
            }
        }

        // ---- RAM measurement ----
        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_MEMORY_COUNTERS
        {
            public uint cb, PageFaultCount;
            public UIntPtr PeakWorkingSetSize, WorkingSetSize,
                           QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
                           QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage,
                           PagefileUsage, PeakPagefileUsage;
        }
        [DllImport("psapi.dll")] static extern bool GetProcessMemoryInfo(
            IntPtr h, out PROCESS_MEMORY_COUNTERS c, uint s);

        private static long RamMB()
        {
            if (GetProcessMemoryInfo(Process.GetCurrentProcess().Handle,
                    out var mc, (uint)Marshal.SizeOf<PROCESS_MEMORY_COUNTERS>()))
                return (long)mc.WorkingSetSize.ToUInt64() / (1024 * 1024);
            return GC.GetTotalMemory(false) / (1024 * 1024);
        }

        private static string ResolveDbPath()        {
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
