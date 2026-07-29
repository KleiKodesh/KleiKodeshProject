using FtsLib.SeforimDb;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Dumps the complete sorted result-ID set of a query to a file, plus the
    /// SearchIds wall time to stdout. Built for A/B verification: run the same
    /// query with two binaries against the same index and diff the output files —
    /// any optimization must produce byte-identical dumps.
    ///
    /// Usage:
    ///   FtsLibTest.exe dumpids &lt;indexDir&gt; &lt;outFile&gt; &lt;query&gt;
    /// </summary>
    internal static class DumpIds
    {
        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length < 4)
            {
                Console.WriteLine("Usage: FtsLibTest.exe dumpids <indexDir> <outFile> <query>");
                return;
            }

            string indexDir = args[1];
            string outFile  = args[2];
            string query    = args[3];

            string dbPath = BuildTest.ResolveDbPath();
            var index = new SeforimIndex(indexDir, dbPath);

            // Warm-up pass so timings compare the steady state, not cold caches.
            int warm = 0;
            foreach (var _ in index.SearchIds("תורה")) warm++;

            var sw  = Stopwatch.StartNew();
            var ids = new List<int>();
            foreach (var id in index.SearchIds(query)) ids.Add(id);
            sw.Stop();

            ids.Sort();
            var sb = new StringBuilder(ids.Count * 8);
            foreach (var id in ids) sb.Append(id).Append('\n');
            File.WriteAllText(outFile, sb.ToString());

            Console.WriteLine($"DUMPIDS query=\"{query}\" ids={ids.Count} ms={sw.ElapsedMilliseconds} out={outFile}");
        }
    }
}
