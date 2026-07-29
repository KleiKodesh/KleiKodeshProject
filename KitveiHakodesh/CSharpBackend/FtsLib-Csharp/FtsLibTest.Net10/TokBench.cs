using FtsLib.Tokenization;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Tokenization micro-benchmark. Isolates the scanner hot path: loads a large sample of
    /// real content into memory ONCE (no I/O in the timed loop), then tokenizes it repeatedly
    /// via the real indexing tokenizer (<see cref="Tokenizer.Extract"/>). Reports MB/s and
    /// Mchars/s. Use to compare tokenizer builds (e.g. before/after a refactor) — run several
    /// times; take the MIN (least noise). Sums token counts so nothing is optimized away.
    ///
    /// Usage:  FtsLibTest.exe tokbench [maxLines=200000] [reps=5]
    /// </summary>
    internal static class TokBench
    {
        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            int maxLines = args.Length > 1 && int.TryParse(args[1], out var ml) ? ml : 200000;
            int reps = args.Length > 2 && int.TryParse(args[2], out var rp) ? rp : 5;

            string db = BuildTest.ResolveDbPath();
            Console.WriteLine($"tokbench: loading up to {maxLines:N0} lines from {db}");

            var docs = new List<string>(maxLines);
            long totalChars = 0;
            using (var c = new SqliteConnection($"Data Source={db};Mode=ReadOnly;Cache=Shared"))
            {
                c.Open();
                var cmd = c.CreateCommand();
                cmd.CommandText = $"SELECT content FROM line WHERE content IS NOT NULL LIMIT {maxLines}";
                using var r = cmd.ExecuteReader();
                while (r.Read()) { string s = r.GetString(0); docs.Add(s); totalChars += s.Length; }
            }
            double mb = totalChars * 2.0 / (1024 * 1024); // UTF-16 chars → bytes
            Console.WriteLine($"loaded {docs.Count:N0} docs, {totalChars:N0} chars ({mb:F1} MB UTF-16)");

            var tok = new Tokenizer();
            long sink = 0;

            // warmup (JIT + caches)
            foreach (var d in docs) sink += tok.Extract(d).Count;
            Console.WriteLine("warmup done; timing…");

            double bestMs = double.MaxValue, worstMs = 0, sumMs = 0;
            for (int rep = 0; rep < reps; rep++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < docs.Count; i++) sink += tok.Extract(docs[i]).Count;
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds;
                bestMs = Math.Min(bestMs, ms); worstMs = Math.Max(worstMs, ms); sumMs += ms;
                Console.WriteLine($"  rep {rep + 1}: {ms,8:F1} ms   {mb / (ms / 1000),7:F1} MB/s   {totalChars / (ms / 1000) / 1e6,6:F1} Mchars/s");
            }
            Console.WriteLine();
            Console.WriteLine($"BEST : {bestMs,8:F1} ms   {mb / (bestMs / 1000),7:F1} MB/s   {totalChars / (bestMs / 1000) / 1e6,6:F1} Mchars/s");
            Console.WriteLine($"mean : {sumMs / reps,8:F1} ms   worst {worstMs:F1} ms");
            Console.WriteLine($"(sink={sink})");
        }
    }
}
