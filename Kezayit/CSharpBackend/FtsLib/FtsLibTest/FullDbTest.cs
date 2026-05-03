using FtsLib;
using FtsLib.Index;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualBasic;

namespace FtsLibTest
{
    internal static class FullDbTest
    {
        private const string SearchQuery = "כי ביצחק";

        // ----------------------------------------------------------------
        // Logger
        // ----------------------------------------------------------------
        private enum LogLevel { Info, Ok, Warn, Error, Debug }
        private static readonly Stopwatch _wallClock = Stopwatch.StartNew();

        private static void Log(string msg, LogLevel level = LogLevel.Info)
        {
            ConsoleColor col;
            string prefix;
            switch (level)
            {
                case LogLevel.Ok:    col = ConsoleColor.Green;    prefix = "  ✓ "; break;
                case LogLevel.Warn:  col = ConsoleColor.Yellow;   prefix = "  ! "; break;
                case LogLevel.Error: col = ConsoleColor.Red;      prefix = "  ✗ "; break;
                case LogLevel.Debug: col = ConsoleColor.DarkGray; prefix = "  · "; break;
                default:             col = ConsoleColor.Cyan;     prefix = "  » "; break;
            }
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"[{_wallClock.Elapsed:mm\\:ss\\.ff}] ");
            Console.ForegroundColor = col;
            Console.Write(prefix);
            Console.ResetColor();
            Console.WriteLine(msg);
        }

        private static void Section(string title)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"  ── {title} ──");
            Console.ResetColor();
        }

        private static void Divider() =>
            Console.WriteLine("  " + new string('─', 60));

        // ----------------------------------------------------------------
        // Win32 memory
        // ----------------------------------------------------------------
        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_MEMORY_COUNTERS
        {
            public uint cb, PageFaultCount;
            public UIntPtr PeakWorkingSetSize, WorkingSetSize,
                           QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
                           QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage,
                           PagefileUsage, PeakPagefileUsage;
        }

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetProcessMemoryInfo(IntPtr h,
            out PROCESS_MEMORY_COUNTERS c, uint s);

        private static long RamMB()
        {
            if (GetProcessMemoryInfo(Process.GetCurrentProcess().Handle,
                    out var mc, (uint)Marshal.SizeOf<PROCESS_MEMORY_COUNTERS>()))
                return (long)mc.WorkingSetSize.ToUInt64() / (1024 * 1024);
            return GC.GetTotalMemory(false) / (1024 * 1024);
        }

        // ----------------------------------------------------------------
        // Entry point
        // ----------------------------------------------------------------
        public static void Run(int lineLimit = 0) // 0 = no limit (full DB)
        {
            _wallClock.Restart();

            Section("FULL DB TEST");
            Log($"Started  : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Log($"PID      : {Process.GetCurrentProcess().Id}");
            Log($"RAM      : {RamMB()} MB");

            // ---- DB ----
            Section("Database");
            string dbPath = ResolveDbPath();
            Log($"Path : {dbPath}");
            if (!File.Exists(dbPath)) { Log("Not found.", LogLevel.Error); return; }
            var fi = new FileInfo(dbPath);
            Log($"Size : {fi.Length / (1024.0 * 1024.0):F1} MB  (modified {fi.LastWriteTime:yyyy-MM-dd HH:mm})");

            var swConn = Stopwatch.StartNew();
            var conn   = OpenConnection(dbPath);
            swConn.Stop();
            Log($"Connected in {swConn.ElapsedMilliseconds} ms", LogLevel.Ok);

            using (conn)
            {
                // ---- count ----
                Section("Row Count");
                var swCount = Stopwatch.StartNew();
                long totalLines = CountLines(conn);
                swCount.Stop();
                Log($"{totalLines:N0} rows  ({swCount.ElapsedMilliseconds} ms)", LogLevel.Ok);

                // ---- index ----
                Section("Index Build");
                long memBefore = RamMB();
                Log($"Streaming all rows...");

                var swBuild = Stopwatch.StartNew();
                // Pass totalLines so ETA is accurate for the full scan
                var (index, linesIndexed, termCount) = BuildIndex(conn,
                    lineLimit > 0 ? lineLimit : totalLines, lineLimit);
                swBuild.Stop();

                long memAfter = RamMB();
                Log($"Lines      : {linesIndexed:N0}", LogLevel.Ok);
                Log($"Terms      : {termCount:N0}", LogLevel.Ok);
                Log($"Time       : {swBuild.ElapsedMilliseconds:N0} ms", LogLevel.Ok);
                Log($"Throughput : {linesIndexed * 1000.0 / Math.Max(swBuild.ElapsedMilliseconds, 1):N0} lines/sec", LogLevel.Debug);
                Log($"RAM after  : {memAfter} MB  (Δ +{memAfter - memBefore} MB)", LogLevel.Ok);

                // ---- search ----
                Section("Search");
                var tokenizer  = new Tokenizer();
                var queryTerms = new List<string>(tokenizer.Extract(SearchQuery));

                Log($"Query : \"{SearchQuery}\"");
                Log($"Terms : [{string.Join(", ", queryTerms)}]");
                foreach (var term in queryTerms)
                {
                    Log($"  '{term}' → {index.GetTermCount(term):N0} lines  (posting bytes: {index.GetTermBytes(term):N0})", LogLevel.Debug);
                    int actualCount = 0;
                    foreach (var _ in index.IterateTerm(term)) actualCount++;
                    Log($"    iterator count: {actualCount:N0}", LogLevel.Debug);
                }

                var searchTimes = new List<long>(5);
                List<int> matchIds = null;
                Log("Running 5 iterations...");
                for (int i = 0; i < 5; i++)
                {
                    var sw = Stopwatch.StartNew();
                    matchIds = new List<int>(index.Search(queryTerms));
                    sw.Stop();
                    searchTimes.Add(sw.ElapsedMilliseconds);
                    Log($"  Run {i + 1}: {sw.ElapsedMilliseconds} ms  →  {matchIds.Count} results", LogLevel.Debug);
                }
                Log($"Results : {matchIds.Count}  |  " +
                    $"min={Min(searchTimes)} ms  avg={Avg(searchTimes):F1} ms  max={Max(searchTimes)} ms",
                    LogLevel.Ok);

                // ---- fetch & print results ----
                Section("Results");
                var swFetch = Stopwatch.StartNew();
                var results = FetchResultRows(conn, matchIds);
                swFetch.Stop();
                Log($"Fetched {results.Count} rows in {swFetch.ElapsedMilliseconds} ms", LogLevel.Ok);

                if (results.Count == 0)
                {
                    Log("No results found.", LogLevel.Warn);
                }
                else
                {
                    Console.WriteLine();
                    Divider();
                    Console.OutputEncoding = System.Text.Encoding.UTF8;
                    for (int i = 0; i < results.Count; i++)
                    {
                        var row = results[i];
                        string loc = row.HeRef ?? $"שורה {row.LineIndex}";
                        // Strip HTML tags from content for display
                        string plain = StripHtml(row.Content);

                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Write($"  [{i + 1}] ");
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.Write(row.BookTitle);
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"  {loc}");
                        Console.ResetColor();
                        Console.WriteLine($"      {plain}");
                        Console.WriteLine();
                    }
                    Divider();
                }

                // ---- summary ----
                Section("Summary");
                Log($"DB size        : {fi.Length / (1024.0 * 1024.0):F1} MB");
                Log($"Lines indexed  : {linesIndexed:N0}");
                Log($"Unique terms   : {termCount:N0}");
                Log($"Index time     : {swBuild.ElapsedMilliseconds:N0} ms");
                Log($"Search (avg)   : {Avg(searchTimes):F1} ms");
                Log($"Results found  : {matchIds.Count}");
                Log($"RAM used       : {memAfter - memBefore} MB");
                Log($"Total time     : {_wallClock.Elapsed:mm\\:ss\\.ff}", LogLevel.Ok);
            }
        }

        // ----------------------------------------------------------------
        // Index builder
        // ----------------------------------------------------------------
        private static (IndexManager index, long linesIndexed, int termCount)
            BuildIndex(SQLiteConnection conn, long totalLines, int lineLimit = 0)
        {
            var index     = new IndexManager();
            var tokenizer = new Tokenizer();
            long linesIndexed = 0;
            const long milestone = 100_000;
            var swWindow = Stopwatch.StartNew();
            var swTotal  = Stopwatch.StartNew();

            using (var cmd = conn.CreateCommand())
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

                        if (linesIndexed % milestone == 0)
                        {
                            long   seg  = swWindow.ElapsedMilliseconds;
                            double rate = milestone * 1000.0 / Math.Max(seg, 1);
                            // Use totalLines for pct/ETA only if we're doing a full scan
                            double pct  = totalLines > 0 ? 100.0 * linesIndexed / totalLines : 0;
                            long   rem  = totalLines > 0
                                ? (long)((totalLines - linesIndexed) / Math.Max(rate, 1) * 1000)
                                : 0;
                            string progress = totalLines > 0
                                ? $"{linesIndexed:N0} / {totalLines:N0}  ({pct:F1}%)  ETA {TimeSpan.FromMilliseconds(rem):mm\\:ss}"
                                : $"{linesIndexed:N0}  elapsed {swTotal.Elapsed:mm\\:ss}";
                            Log($"{progress}  {rate:N0} lines/s  RAM {RamMB()} MB", LogLevel.Debug);
                            swWindow.Restart();
                        }
                    }
                }
            }

            return (index, linesIndexed, index.TermCount);
        }

        // ----------------------------------------------------------------
        // Fetch result rows
        // ----------------------------------------------------------------
        private static List<ResultRow> FetchResultRows(SQLiteConnection conn, List<int> ids)
        {
            if (ids.Count == 0) return new List<ResultRow>();

            var rows = new List<ResultRow>(ids.Count);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    $@"SELECT l.id, l.bookId, l.lineIndex, l.heRef, l.content, b.title
                         FROM line l JOIN book b ON b.id = l.bookId
                        WHERE l.id IN ({string.Join(",", ids)})";
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        rows.Add(new ResultRow(
                            r.GetInt32(0), r.GetInt32(1), r.GetInt32(2),
                            r.IsDBNull(3) ? null : r.GetString(3),
                            r.IsDBNull(4) ? string.Empty : r.GetString(4),
                            r.IsDBNull(5) ? string.Empty : r.GetString(5)));
            }

            rows.Sort((a, b) =>
            {
                int c = a.BookId.CompareTo(b.BookId);
                return c != 0 ? c : a.LineIndex.CompareTo(b.LineIndex);
            });
            return rows;
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------
        private static long CountLines(SQLiteConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM line";
                return (long)cmd.ExecuteScalar();
            }
        }

        private static string StripHtml(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            bool inTag = false;
            foreach (char c in s)
            {
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        private static SQLiteConnection OpenConnection(string dbPath)
        {
            var conn = new SQLiteConnection(
                $"Data Source={dbPath};Version=3;Read Only=True;Page Size=4096;Cache Size=100000;Temp Store=Memory;");
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA mmap_size=2147483648; PRAGMA cache_size=100000; PRAGMA temp_store=MEMORY;";
                cmd.ExecuteNonQuery();
            }
            return conn;
        }

        private static string ResolveDbPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string def  = Path.Combine(appData, "io.github.kdroidfilter.seforimapp", "databases", "seforim.db");
            return Interaction.GetSetting("ZayitApp", "Database", "Path", def);
        }

        private static long   Min(List<long> v) { long m = v[0]; foreach (var x in v) if (x < m) m = x; return m; }
        private static long   Max(List<long> v) { long m = v[0]; foreach (var x in v) if (x > m) m = x; return m; }
        private static double Avg(List<long> v) { long s = 0; foreach (var x in v) s += x; return (double)s / v.Count; }

        private struct ResultRow
        {
            public int    Id, BookId, LineIndex;
            public string HeRef, Content, BookTitle;
            public ResultRow(int id, int bookId, int lineIndex,
                             string heRef, string content, string bookTitle)
            { Id=id; BookId=bookId; LineIndex=lineIndex; HeRef=heRef; Content=content; BookTitle=bookTitle; }
        }
    }
}
