using FtsLib.SeforimDb;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Ad-hoc query runner with כתיב expansion enabled.
    ///
    /// Usage:
    ///   FtsLibTest.exe ketivquery [tier] "query"
    ///
    /// Example:
    ///   FtsLibTest.exe ketivquery 500k "שישים גבורים"
    /// </summary>
    internal static class KetivQueryTest
    {
        private const int MaxResults = 20;

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            if (args.Length < 3)
            {
                Console.WriteLine("Usage: FtsLibTest.exe ketivquery [tier] query terms...");
                return;
            }

            string tierLabel = args[1];
            string query     = string.Join(" ", args, 2, args.Length - 2);

            string label;
            try   { label = TestHelpers.ResolveTier(tierLabel).Label; }
            catch (ArgumentException ex) { Console.WriteLine(ex.Message); return; }

            string dbPath   = BuildTest.ResolveDbPath();
            string indexDir = TestHelpers.IndexDir(label);

            if (!Directory.Exists(indexDir) ||
                Directory.GetFiles(indexDir, "seg_*.dat").Length == 0)
            {
                Console.WriteLine($"No index found at: {indexDir}");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"╔══ KETIV QUERY: \"{query}\"  [{label.ToUpper()}] ══");
            Console.WriteLine($"║  Index : {indexDir}");

            var index   = new SeforimIndex(indexDir, dbPath);
            var results = new List<SearchResult>();
            var sw      = Stopwatch.StartNew();
            foreach (var r in index.Search(query, cap: 0, expandKetiv: true))
                results.Add(r);
            sw.Stop();

            Console.WriteLine($"║  Results : {results.Count:N0}  ({sw.ElapsedMilliseconds} ms)");
            Console.WriteLine("║");
            Console.WriteLine($"║  {"#",-6}  {"Line ID",8}  {"Score",6}  Book");
            Console.WriteLine($"║  {new string('─', 6)}  {new string('─', 8)}  {new string('─', 6)}  {new string('─', 40)}");

            int shown = 0;
            foreach (var r in results)
            {
                if (shown >= MaxResults) break;
                var snippet = index.GenerateSnippet(r);
                string scoreStr = snippet.Score == int.MaxValue ? "n/a" : snippet.Score.ToString();
                Console.WriteLine($"║  {shown + 1,-6}  {r.LineId,8}  {scoreStr,6}  {TestHelpers.Truncate(r.BookTitle, 40)}");
                if (snippet.IsMatch)
                    Console.WriteLine($"║          {StripTags(snippet.Html, 120)}");
                shown++;
            }

            if (results.Count > MaxResults)
                Console.WriteLine($"║  … and {results.Count - MaxResults:N0} more results");

            Console.WriteLine("╚══ DONE ══");
        }

        private static string StripTags(string html, int maxLen)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            var sb = new System.Text.StringBuilder(html.Length);
            bool inTag = false;
            foreach (char c in html)
            {
                if (c == '<') { inTag = true;  continue; }
                if (c == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            string s = sb.ToString().Trim();
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "…";
        }
    }
}
