using FtsLib.SeforimDb;
using FtsLib.Snippets;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Snippet-generation micro-benchmark. The perf profile shows snippet build dominates for
    /// result-heavy queries. This isolates SnippetBuilder.Build over a fixed set of real lines
    /// (no DB/I-O in the timed loop) and reports BOTH time and bytes allocated per result — to
    /// test the hypothesis that per-token Normalized-string allocation is the cost.
    ///
    /// Usage:  FtsLibTest.exe snipbench [maxLines=200000] [reps=5] [query=תורה]
    /// </summary>
    internal static class SnipBench
    {
        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            int maxLines = args.Length > 1 && int.TryParse(args[1], out var ml) ? ml : 200000;
            int reps = args.Length > 2 && int.TryParse(args[2], out var rp) ? rp : 5;
            string query = args.Length > 3 ? args[3] : "תורה";

            string db = BuildTest.ResolveDbPath();
            var lines = new List<string>(maxLines);
            long chars = 0;
            using (var c = new SqliteConnection($"Data Source={db};Mode=ReadOnly;Cache=Shared"))
            {
                c.Open(); var cmd = c.CreateCommand();
                cmd.CommandText = $"SELECT content FROM line WHERE content IS NOT NULL LIMIT {maxLines}";
                using var r = cmd.ExecuteReader();
                while (r.Read()) { var s = r.GetString(0); lines.Add(s); chars += s.Length; }
            }
            Console.WriteLine($"snipbench: {lines.Count:N0} lines, {chars:N0} chars, query=\"{query}\", {reps} reps\n");

            var prepared = PreparedQueryGroups.FromLiteralTerms(new[] { query });
            var builder = new SnippetBuilder();

            long sink = 0;
            foreach (var ln in lines) sink += builder.Build(ln, prepared).Html.Length; // warmup

            double bestMs = double.MaxValue; long allocPerRep = 0;
            for (int rep = 0; rep < reps; rep++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < lines.Count; i++) sink += builder.Build(lines[i], prepared).Html.Length;
                sw.Stop();
                long alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
                double ms = sw.Elapsed.TotalMilliseconds;
                if (ms < bestMs) { bestMs = ms; allocPerRep = alloc; }
                Console.WriteLine($"  rep {rep + 1}: {ms,8:F1} ms   alloc {alloc / (1024.0 * 1024),8:F1} MB   {alloc / lines.Count,6} B/line");
            }
            Console.WriteLine($"\nBEST: {bestMs,8:F1} ms   {lines.Count / (bestMs / 1000) / 1000,6:F1} k-snippets/s   alloc {allocPerRep / lines.Count} B/line");
            Console.WriteLine($"(sink={sink})");
        }
    }
}
