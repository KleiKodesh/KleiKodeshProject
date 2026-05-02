using FtsLib;
using FtsLib.Index;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualBasic;

namespace FtsLibTest
{
    internal static class FullDbTest
    {
        private const string SearchQuery = "כי ביצחק";

        // ----------------------------------------------------------------
        // Structured console logger
        // ----------------------------------------------------------------
        private enum LogLevel { Info, Ok, Warn, Error, Debug }

        private static readonly Stopwatch _wallClock = Stopwatch.StartNew();

        private static void Log(string msg, LogLevel level = LogLevel.Info)
        {
            string elapsed = $"[{_wallClock.Elapsed:mm\\:ss\\.ff}]";

            ConsoleColor col;
            string prefix;
            switch (level)
            {
                case LogLevel.Ok:    col = ConsoleColor.Green;   prefix = "  ✓ "; break;
                case LogLevel.Warn:  col = ConsoleColor.Yellow;  prefix = "  ! "; break;
                case LogLevel.Error: col = ConsoleColor.Red;     prefix = "  ✗ "; break;
                case LogLevel.Debug: col = ConsoleColor.DarkGray;prefix = "  · "; break;
                default:             col = ConsoleColor.Cyan;    prefix = "  » "; break;
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(elapsed + " ");
            Console.ForegroundColor = col;
            Console.Write(prefix);
            Console.ResetColor();
            Console.WriteLine(msg);
        }

        private static void LogSection(string title)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"  ── {title} ──");
            Console.ResetColor();
        }

        // ----------------------------------------------------------------
        // Win32 memory info
        // ----------------------------------------------------------------
        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_MEMORY_COUNTERS
        {
            public uint  cb;
            public uint  PageFaultCount;
            public ulong PeakWorkingSetSize;
            public ulong WorkingSetSize;
            public ulong QuotaPeakPagedPoolUsage;
            public ulong QuotaPagedPoolUsage;
            public ulong QuotaPeakNonPagedPoolUsage;
            public ulong QuotaNonPagedPoolUsage;
            public ulong PagefileUsage;
            public ulong PeakPagefileUsage;
        }

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetProcessMemoryInfo(
            IntPtr hProcess,
            out PROCESS_MEMORY_COUNTERS counters,
            uint size);

        private static long WorkingSetMB()
        {
            if (GetProcessMemoryInfo(Process.GetCurrentProcess().Handle,
                    out var mc, (uint)Marshal.SizeOf<PROCESS_MEMORY_COUNTERS>()))
                return (long)(mc.WorkingSetSize / (1024 * 1024));
            return GC.GetTotalMemory(false) / (1024 * 1024);
        }

        // ----------------------------------------------------------------
        // Entry point
        // ----------------------------------------------------------------
        public static void Run()
        {
            _wallClock.Restart();

            LogSection("FULL DB TEST");
            Log($"Start time : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Log($"Process    : {Process.GetCurrentProcess().ProcessName} (PID {Process.GetCurrentProcess().Id})");
            Log($"CLR        : {System.Runtime.InteropServices.RuntimeEnvironment.GetSystemVersion()}");
            Log($"RAM at start: {WorkingSetMB()} MB");

            // ---- resolve DB ----
            LogSection("Database");
            string dbPath = ResolveDbPath();
            Log($"Path : {dbPath}");

            if (!File.Exists(dbPath))
            {
                Log("Database file not found.", LogLevel.Error);
                return;
            }

            var fi = new FileInfo(dbPath);
            Log($"Size : {fi.Length / (1024.0 * 1024.0):F1} MB  (modified {fi.LastWriteTime:yyyy-MM-dd HH:mm})");

            var report = new BenchmarkReport
            {
                DbPath    = dbPath,
                DbSizeMB  = fi.Length / (1024.0 * 1024.0),
                Query     = SearchQuery,
                Timestamp = DateTime.Now
            };

            var swConn = Stopwatch.StartNew();
            var conn   = OpenConnection(dbPath);
            swConn.Stop();
            Log($"Connection opened in {swConn.ElapsedMilliseconds} ms", LogLevel.Ok);

            using (conn)
            {
                // ---- count ----
                LogSection("Row Count");
                var swCount = Stopwatch.StartNew();
                report.TotalLinesInDb = CountLines(conn);
                swCount.Stop();
                Log($"{report.TotalLinesInDb:N0} rows in `line` table  ({swCount.ElapsedMilliseconds} ms)", LogLevel.Ok);

                // ---- index build ----
                LogSection("Index Build");
                long memBefore = WorkingSetMB();
                Log($"RAM before indexing : {memBefore} MB");
                Log($"Streaming all rows — content kept in index only, not in RAM");

                var swTotal = Stopwatch.StartNew();
                var (index, linesIndexed, termCount, dbReadMs, tokenizeMs) =
                    BuildIndex(conn, report.TotalLinesInDb);
                swTotal.Stop();

                long memAfter = WorkingSetMB();
                report.LinesLoaded   = linesIndexed;
                report.DbReadMs      = dbReadMs;
                report.TokenizeMs    = tokenizeMs;
                report.IndexBuildMs  = swTotal.ElapsedMilliseconds;
                report.UniqueTerms   = termCount;
                report.MemBeforeMB   = memBefore;
                report.MemAfterIdxMB = memAfter;

                Log($"Lines indexed  : {linesIndexed:N0}", LogLevel.Ok);
                Log($"Unique terms   : {termCount:N0}", LogLevel.Ok);
                Log($"Total time     : {swTotal.ElapsedMilliseconds:N0} ms", LogLevel.Ok);
                Log($"  DB read      : {dbReadMs:N0} ms  ({100.0 * dbReadMs / swTotal.ElapsedMilliseconds:F1}%)", LogLevel.Debug);
                Log($"  Tokenize     : {tokenizeMs:N0} ms  ({100.0 * tokenizeMs / swTotal.ElapsedMilliseconds:F1}%)", LogLevel.Debug);
                long insertMs = swTotal.ElapsedMilliseconds - dbReadMs - tokenizeMs;
                Log($"  Index insert : {insertMs:N0} ms  ({100.0 * insertMs / swTotal.ElapsedMilliseconds:F1}%)", LogLevel.Debug);
                Log($"Throughput     : {linesIndexed * 1000.0 / swTotal.ElapsedMilliseconds:N0} lines/sec", LogLevel.Debug);
                Log($"RAM after      : {memAfter} MB  (Δ +{memAfter - memBefore} MB)", LogLevel.Ok);

                // ---- search ----
                LogSection("Search");
                var tokenizer  = new Tokenizer();
                var queryTerms = new List<string>(tokenizer.Extract(SearchQuery));
                report.QueryTerms = queryTerms;

                Log($"Query  : \"{SearchQuery}\"");
                Log($"Terms  : [{string.Join(", ", queryTerms)}]");
                Log($"Running 5 search iterations...");

                var searchTimes = new List<long>(5);
                List<int> matchIds = null;
                for (int i = 0; i < 5; i++)
                {
                    var swS = Stopwatch.StartNew();
                    matchIds = new List<int>(index.Search(queryTerms));
                    swS.Stop();
                    searchTimes.Add(swS.ElapsedMilliseconds);
                    Log($"  Run {i + 1}: {swS.ElapsedMilliseconds} ms  →  {matchIds.Count} results", LogLevel.Debug);
                }

                report.SearchTimesMs = searchTimes;
                report.ResultCount   = matchIds.Count;
                Log($"Results : {matchIds.Count}  |  min={Min(searchTimes)}ms  avg={Avg(searchTimes):F1}ms  max={Max(searchTimes)}ms", LogLevel.Ok);

                // ---- fetch result rows ----
                LogSection("Fetching Result Rows");
                Log($"Fetching {matchIds.Count} rows from DB by ID...");
                var swFetch = Stopwatch.StartNew();
                var results = FetchResultRows(conn, matchIds);
                swFetch.Stop();
                Log($"Fetched and sorted {results.Count} rows in {swFetch.ElapsedMilliseconds} ms", LogLevel.Ok);

                // ---- render HTML ----
                LogSection("HTML Report");
                var swHtml = Stopwatch.StartNew();
                string htmlPath = RenderHtml(report, results);
                swHtml.Stop();
                Log($"Written to: {htmlPath}  ({swHtml.ElapsedMilliseconds} ms)", LogLevel.Ok);

                // ---- open browser ----
                Log("Opening in default browser...");
                OpenInBrowser(htmlPath);

                // ---- final summary ----
                LogSection("Done");
                Log($"Total wall time : {_wallClock.Elapsed:mm\\:ss\\.ff}", LogLevel.Ok);
                Log($"RAM at exit     : {WorkingSetMB()} MB");
            }
        }

        // ----------------------------------------------------------------
        // Index builder — streams DB rows, never stores content in RAM
        // ----------------------------------------------------------------
        private static (IndexManager index,
                        long linesIndexed,
                        int  termCount,
                        long dbReadMs,
                        long tokenizeMs)
            BuildIndex(SQLiteConnection conn, long totalLines)
        {
            var index     = new IndexManager();
            var tokenizer = new Tokenizer();
            long linesIndexed = 0;
            long dbReadMs     = 0;
            long tokenizeMs   = 0;
            const long milestone = 100_000;
            var swMilestone = Stopwatch.StartNew();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, content FROM line ORDER BY id";

                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var swDb = Stopwatch.StartNew();
                        int    lineId  = r.GetInt32(0);
                        string content = r.IsDBNull(1) ? string.Empty : r.GetString(1);
                        swDb.Stop();
                        dbReadMs += swDb.ElapsedMilliseconds;

                        var swTok = Stopwatch.StartNew();
                        var terms = tokenizer.Extract(content);
                        swTok.Stop();
                        tokenizeMs += swTok.ElapsedMilliseconds;

                        foreach (var term in terms)
                            index.Add(term, lineId);

                        linesIndexed++;

                        if (linesIndexed % milestone == 0)
                        {
                            long segMs   = swMilestone.ElapsedMilliseconds;
                            double rate  = milestone * 1000.0 / Math.Max(segMs, 1);
                            double pct   = totalLines > 0 ? 100.0 * linesIndexed / totalLines : 0;
                            long   remMs = totalLines > 0
                                ? (long)((totalLines - linesIndexed) / Math.Max(rate, 1) * 1000)
                                : 0;
                            string eta   = TimeSpan.FromMilliseconds(remMs).ToString(@"mm\:ss");

                            Log($"{linesIndexed:N0} / {totalLines:N0}  ({pct:F1}%)  " +
                                $"{rate:N0} lines/s  RAM {WorkingSetMB()} MB  ETA {eta}",
                                LogLevel.Debug);

                            swMilestone.Restart();
                            GC.Collect(0, GCCollectionMode.Optimized);
                        }
                    }
                }
            }

            return (index, linesIndexed, index.TermCount, dbReadMs, tokenizeMs);
        }

        // ----------------------------------------------------------------
        // Fetch full rows for matched IDs (back to DB, small result set)
        // ----------------------------------------------------------------
        private static List<ResultRow> FetchResultRows(SQLiteConnection conn, List<int> ids)
        {
            if (ids.Count == 0) return new List<ResultRow>();

            var rows = new List<ResultRow>(ids.Count);

            // Build a temp IN clause — result sets are small so this is fine
            var inClause = string.Join(",", ids);

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    $@"SELECT l.id, l.bookId, l.lineIndex, l.heRef, l.content, b.title
                         FROM line l
                         JOIN book b ON b.id = l.bookId
                        WHERE l.id IN ({inClause})";

                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        rows.Add(new ResultRow(
                            r.GetInt32(0),
                            r.GetInt32(1),
                            r.GetInt32(2),
                            r.IsDBNull(3) ? null : r.GetString(3),
                            r.IsDBNull(4) ? string.Empty : r.GetString(4),
                            r.IsDBNull(5) ? string.Empty : r.GetString(5)));
                    }
                }
            }

            rows.Sort((a, b) =>
            {
                int c = a.BookId.CompareTo(b.BookId);
                return c != 0 ? c : a.LineIndex.CompareTo(b.LineIndex);
            });

            return rows;
        }

        // ----------------------------------------------------------------
        // DB helpers
        // ----------------------------------------------------------------
        private static long CountLines(SQLiteConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM line";
                return (long)cmd.ExecuteScalar();
            }
        }

        // ----------------------------------------------------------------
        // HTML renderer
        // ----------------------------------------------------------------
        private static string RenderHtml(BenchmarkReport r, List<ResultRow> rows)
        {
            var sb = new StringBuilder(1024 * 64);

            sb.Append("<!DOCTYPE html>\n");
            sb.Append("<html dir=\"rtl\" lang=\"he\">\n<head>\n");
            sb.Append("<meta charset=\"utf-8\">\n");
            sb.Append("<title>FTS Full DB Report</title>\n");
            sb.Append(@"<style>
* { box-sizing:border-box; }
body { font-family:'David','Times New Roman',serif; font-size:17px;
       background:#f7f5f0; color:#1a1a1a; margin:0; padding:1.5em; }
h1   { font-size:1.7em; margin-bottom:.2em; color:#2c1a00; }
h2   { font-size:1.2em; margin:1.4em 0 .5em; color:#3a2a0a;
       border-bottom:1px solid #c8a96e; padding-bottom:.2em; }
.ts  { color:#888; font-size:.8em; margin-bottom:1.5em; }
.bench { display:grid; grid-template-columns:repeat(auto-fill,minmax(190px,1fr)); gap:.8em; margin-bottom:1.5em; }
.card  { background:#fff; border:1px solid #ddd; border-radius:6px; padding:.8em 1em; }
.card .val { font-size:1.55em; font-weight:bold; color:#5a3e1b; }
.card .lbl { font-size:.78em; color:#888; margin-top:.1em; }
.timeline  { display:flex; height:28px; border-radius:4px; overflow:hidden; font-size:.78em; margin-bottom:.4em; }
.tl-seg    { display:flex; align-items:center; justify-content:center;
             color:#fff; white-space:nowrap; overflow:hidden; padding:0 6px; }
.tl-db   { background:#4a7c59; }
.tl-tok  { background:#7c6a4a; }
.tl-idx  { background:#4a5e7c; }
.tl-srch { background:#7c4a4a; }
.tl-legend { display:flex; gap:1em; flex-wrap:wrap; font-size:.8em; margin-bottom:1em; }
.tl-dot    { width:12px; height:12px; border-radius:2px; display:inline-block; margin-left:4px; vertical-align:middle; }
.spark     { display:flex; align-items:flex-end; gap:3px; height:40px; margin:.4em 0; }
.spark-bar { background:#c8a96e; border-radius:2px 2px 0 0; width:18px; }
table { width:100%; border-collapse:collapse; margin-top:.5em; }
th    { background:#3a2a0a; color:#f5e6c8; padding:.5em .8em; text-align:right; }
tr:nth-child(even) { background:#f0ece0; }
td    { padding:.4em .8em; border-bottom:1px solid #ddd; vertical-align:top; }
.book { color:#5a3e1b; font-weight:bold; white-space:nowrap; }
.ref  { color:#888; font-size:.82em; white-space:nowrap; }
.content { line-height:1.7; }
mark  { background:#ffe066; border-radius:2px; padding:0 2px; }
.none { color:#999; font-style:italic; padding:2em; text-align:center; }
</style>
</head>
<body>
");
            sb.Append($"<h1>FTS Full-DB Benchmark Report</h1>\n");
            sb.Append($"<div class=\"ts\">{HtmlEncode(r.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"))}");
            sb.Append($" &nbsp;|&nbsp; {HtmlEncode(r.DbPath)}</div>\n");

            // ---- cards ----
            sb.Append("<h2>סיכום ביצועים</h2>\n<div class=\"bench\">\n");
            Card(sb, $"{r.LinesLoaded:N0}",          "שורות מאונדקסות");
            Card(sb, $"{r.UniqueTerms:N0}",           "מונחים ייחודיים");
            Card(sb, $"{r.DbSizeMB:F1} MB",           "גודל DB");
            Card(sb, $"{r.DbReadMs:N0} ms",           "זמן קריאת DB");
            Card(sb, $"{r.TokenizeMs:N0} ms",         "זמן טוקניזציה");
            Card(sb, $"{r.IndexBuildMs:N0} ms",       "זמן בניית אינדקס כולל");
            Card(sb, $"{Min(r.SearchTimesMs)} ms",    "חיפוש מינימום");
            Card(sb, $"{Avg(r.SearchTimesMs):F1} ms", "חיפוש ממוצע");
            Card(sb, $"{Max(r.SearchTimesMs)} ms",    "חיפוש מקסימום");
            Card(sb, $"{r.ResultCount:N0}",           "תוצאות נמצאו");
            Card(sb, $"{r.MemBeforeMB:N0} MB",        "RAM לפני");
            Card(sb, $"{r.MemAfterIdxMB:N0} MB",      "RAM לאחר אינדקס");
            Card(sb, $"{r.MemAfterIdxMB - r.MemBeforeMB:N0} MB", "גידול RAM");
            sb.Append("</div>\n");

            // ---- timeline ----
            long totalMs = r.DbReadMs + r.TokenizeMs
                         + Math.Max(r.IndexBuildMs - r.DbReadMs - r.TokenizeMs, 1)
                         + Math.Max(Max(r.SearchTimesMs), 1);
            double pDb   = 100.0 * r.DbReadMs   / totalMs;
            double pTok  = 100.0 * r.TokenizeMs  / totalMs;
            double pIdx  = 100.0 * Math.Max(r.IndexBuildMs - r.DbReadMs - r.TokenizeMs, 0) / totalMs;
            double pSrch = 100.0 * Max(r.SearchTimesMs) / totalMs;

            sb.Append("<h2>ציר זמן</h2>\n<div class=\"timeline\">\n");
            TimelineSeg(sb, "tl-db",   pDb,   $"DB {r.DbReadMs:N0}ms");
            TimelineSeg(sb, "tl-tok",  pTok,  $"Tokenize {r.TokenizeMs:N0}ms");
            TimelineSeg(sb, "tl-idx",  pIdx,  $"Index {r.IndexBuildMs - r.DbReadMs - r.TokenizeMs:N0}ms");
            TimelineSeg(sb, "tl-srch", pSrch, $"Search {Max(r.SearchTimesMs)}ms");
            sb.Append("</div>\n<div class=\"tl-legend\">");
            Legend(sb, "tl-db",   "קריאת DB");
            Legend(sb, "tl-tok",  "טוקניזציה");
            Legend(sb, "tl-idx",  "בניית אינדקס");
            Legend(sb, "tl-srch", "חיפוש");
            sb.Append("</div>\n");

            // ---- sparkline ----
            sb.Append("<h2>זמני חיפוש — 5 ריצות</h2>\n<div class=\"spark\">\n");
            long maxT = Math.Max(Max(r.SearchTimesMs), 1);
            foreach (var ms in r.SearchTimesMs)
            {
                int h = (int)(40.0 * ms / maxT);
                if (h < 4) h = 4;
                sb.Append($"<div class=\"spark-bar\" style=\"height:{h}px\" title=\"{ms}ms\"></div>\n");
            }
            sb.Append("</div>\n");
            sb.Append($"<div style=\"font-size:.82em;color:#888\">min={Min(r.SearchTimesMs)}ms");
            sb.Append($" &nbsp; avg={Avg(r.SearchTimesMs):F1}ms &nbsp; max={Max(r.SearchTimesMs)}ms</div>\n");

            // ---- query ----
            sb.Append("<h2>שאילתה</h2>\n");
            sb.Append($"<p><strong>{HtmlEncode(r.Query)}</strong> &nbsp;→&nbsp; מונחים: ");
            sb.Append(HtmlEncode(string.Join(", ", r.QueryTerms)));
            sb.Append($" &nbsp;|&nbsp; {r.ResultCount} תוצאות</p>\n");

            // ---- results ----
            sb.Append("<h2>תוצאות</h2>\n");
            if (rows.Count == 0)
            {
                sb.Append("<p class=\"none\">לא נמצאו תוצאות.</p>\n");
            }
            else
            {
                sb.Append("<table>\n<thead><tr><th>#</th><th>ספר</th><th>מיקום</th><th>תוכן</th></tr></thead>\n<tbody>\n");
                int n = 0;
                foreach (var row in rows)
                {
                    n++;
                    string hl  = HighlightTerms(row.Content, r.QueryTerms);
                    string ref_ = row.HeRef ?? $"שורה {row.LineIndex}";
                    sb.Append($"<tr><td>{n}</td>");
                    sb.Append($"<td class=\"book\">{HtmlEncode(row.BookTitle)}</td>");
                    sb.Append($"<td class=\"ref\">{HtmlEncode(ref_)}</td>");
                    sb.Append($"<td class=\"content\">{hl}</td></tr>\n");
                }
                sb.Append("</tbody>\n</table>\n");
            }

            sb.Append("</body></html>");

            string path = Path.Combine(Path.GetTempPath(), "fts_full_report.html");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Console.WriteLine($"HTML: {path}");
            return path;
        }

        // ---- HTML helpers ----
        private static void Card(StringBuilder sb, string val, string lbl) =>
            sb.Append($"<div class=\"card\"><div class=\"val\">{val}</div><div class=\"lbl\">{lbl}</div></div>\n");

        private static void TimelineSeg(StringBuilder sb, string cls, double pct, string label)
        {
            if (pct < 0.5) pct = 0.5;
            sb.Append($"<div class=\"tl-seg {cls}\" style=\"width:{pct:F1}%\">{HtmlEncode(label)}</div>\n");
        }

        private static void Legend(StringBuilder sb, string cls, string label) =>
            sb.Append($"<span><span class=\"tl-dot {cls}\"></span>{label}</span> ");

        private static string HighlightTerms(string content, List<string> terms)
        {
            // Strip HTML tags
            var plain = new StringBuilder(content.Length);
            bool inTag = false;
            foreach (char c in content)
            {
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (!inTag) plain.Append(c);
            }
            string text = HtmlEncode(plain.ToString());
            foreach (var term in terms)
            {
                int i = 0;
                while (i < text.Length)
                {
                    int idx = text.IndexOf(term, i, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) break;
                    string before = text.Substring(0, idx);
                    string match  = text.Substring(idx, term.Length);
                    string after  = text.Substring(idx + term.Length);
                    text = before + "<mark>" + match + "</mark>" + after;
                    i    = idx + 6 + term.Length + 7;
                }
            }
            return text;
        }

        private static string HtmlEncode(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("&", "&amp;").Replace("<", "&lt;")
                    .Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        // ---- stats ----
        private static long   Min(List<long> v) { long m = v[0]; foreach (var x in v) if (x < m) m = x; return m; }
        private static long   Max(List<long> v) { long m = v[0]; foreach (var x in v) if (x > m) m = x; return m; }
        private static double Avg(List<long> v) { long s = 0; foreach (var x in v) s += x; return (double)s / v.Count; }

        // ---- process ----
        private static void OpenInBrowser(string path)
        {
            try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
            catch (Exception ex) { Console.WriteLine($"Could not open browser: {ex.Message}"); }
        }

        private static string ResolveDbPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string def  = Path.Combine(appData, "io.github.kdroidfilter.seforimapp", "databases", "seforim.db");
            return Interaction.GetSetting("ZayitApp", "Database", "Path", def);
        }

        private static SQLiteConnection OpenConnection(string dbPath)
        {
            var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;Page Size=4096;Read Only=True;");
            conn.Open();
            return conn;
        }

        // ----------------------------------------------------------------
        // Data types
        // ----------------------------------------------------------------
        private struct ResultRow
        {
            public int    Id, BookId, LineIndex;
            public string HeRef, Content, BookTitle;

            public ResultRow(int id, int bookId, int lineIndex,
                             string heRef, string content, string bookTitle)
            {
                Id = id; BookId = bookId; LineIndex = lineIndex;
                HeRef = heRef; Content = content; BookTitle = bookTitle;
            }
        }

        private class BenchmarkReport
        {
            public string       DbPath;
            public double       DbSizeMB;
            public DateTime     Timestamp;
            public long         TotalLinesInDb;
            public long         LinesLoaded;
            public long         DbReadMs;
            public long         TokenizeMs;
            public long         IndexBuildMs;
            public int          UniqueTerms;
            public long         MemBeforeMB;
            public long         MemAfterIdxMB;
            public string       Query;
            public List<string> QueryTerms;
            public List<long>   SearchTimesMs;
            public int          ResultCount;
        }
    }
}
