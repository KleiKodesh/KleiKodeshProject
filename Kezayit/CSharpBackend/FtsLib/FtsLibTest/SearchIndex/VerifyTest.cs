using FtsLib.Seforim;
using System;
using System.Collections.Generic;
using System.IO;

namespace FtsLibTest
{
    /// <summary>
    /// Quick verification: checks whether a set of known line IDs appear in a query's results.
    /// Usage: FtsLibTest.exe verify [tier] "query" id1 id2 ...
    /// </summary>
    internal static class VerifyTest
    {
        public static void Run(string[] args)
        {
            if (args.Length < 4)
            {
                Console.WriteLine("Usage: FtsLibTest.exe verify [tier] \"query\" id1 [id2...]");
                return;
            }

            string label   = TestHelpers.ResolveTier(args[1]).Label;
            // args[2] onward until we hit a numeric arg = query tokens
            // Find where the IDs start
            int idStart = 3;
            while (idStart < args.Length && !int.TryParse(args[idStart], out _))
                idStart++;
            string query   = string.Join(" ", args, 2, idStart - 2);
            string dbPath  = BuildTest.ResolveDbPath();
            string indexDir = TestHelpers.IndexDir(label);

            if (!Directory.Exists(indexDir) ||
                Directory.GetFiles(indexDir, "seg_*.dat").Length == 0)
            {
                Console.WriteLine($"No index at: {indexDir}");
                return;
            }

            var wantIds = new HashSet<int>();
            for (int i = idStart; i < args.Length; i++)
                if (int.TryParse(args[i], out int id)) wantIds.Add(id);

            Console.WriteLine($"Query   : \"{query}\"");
            Console.WriteLine($"Checking: {string.Join(", ", wantIds)}");
            Console.WriteLine();

            var index   = new SeforimIndex(indexDir, dbPath);
            var found   = new HashSet<int>();
            int total   = 0;

            foreach (var r in index.Search(query))
            {
                total++;
                if (wantIds.Contains(r.LineId))
                {
                    found.Add(r.LineId);
                    var snippet = index.GenerateSnippet(r);
                    string plain = StripTags(snippet.Html, 120);
                    Console.WriteLine($"  ✓ FOUND  id={r.LineId}  [{r.BookTitle}]");
                    Console.WriteLine($"    {plain}");
                    Console.WriteLine();
                }
            }

            Console.WriteLine($"Total results : {total:N0}");
            Console.WriteLine();

            foreach (var id in wantIds)
            {
                string mark = found.Contains(id) ? "✓ found" : "✗ NOT found";
                Console.WriteLine($"  id={id} : {mark}");
            }
        }

        private static string StripTags(string html, int max)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            var sb = new System.Text.StringBuilder();
            bool inTag = false;
            foreach (char c in html)
            {
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            string s = sb.ToString().Trim();
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
