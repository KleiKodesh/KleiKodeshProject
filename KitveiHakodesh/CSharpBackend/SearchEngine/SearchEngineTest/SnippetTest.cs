using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SearchEngine.Search;
using SearchEngine.SeforimDb;

namespace SearchEngineTest
{
    internal static class SnippetTest
    {
        public static void Run(string indexDir, string queryText, string dbPath = null, int maxShow = 50)
        {
            Console.WriteLine($"=== SNIPPET TEST: {queryText} ===");

            if (!System.IO.Directory.Exists(indexDir))
            {
                Console.WriteLine("Index not found — run build first.");
                return;
            }

            using (var searcher = new LuceneSearcher(indexDir))
            using (var db = new ZayitDb(dbPath))
            {
                if (!db.IsOpen) { Console.WriteLine("Database not found."); return; }

                Console.WriteLine($"  Query: {queryText}");

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var results = new List<(int DocId, string Fragment)>();
                int luceneHits = 0;

                foreach (var (rowId, bookId, bookTitle, tocPath, fragment) in searcher.SearchWithSnippets(
                    queryText,
                    id => db.GetLineById(id),
                    preTag:  "<mark>",
                    postTag: "</mark>"))
                {
                    luceneHits++;
                    results.Add((rowId, fragment));

                    if (luceneHits <= maxShow)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"  [{luceneHits}] rowId={rowId}  book={bookTitle}");

                        int markCount = Regex.Matches(fragment ?? "", "<mark>").Count;
                        Console.WriteLine($"      Marks: {markCount}");

                        string display = fragment ?? "";
                        if (display.Length > 150)
                            display = display.Substring(0, 150) + "...";
                        Console.WriteLine($"      Snippet: {display}");
                    }
                }

                sw.Stop();

                Console.WriteLine();
                Console.WriteLine($"  Total results: {luceneHits:N0}");
                Console.WriteLine($"  Search + snippet generation: {sw.Elapsed.TotalMilliseconds:F1} ms");

                if (luceneHits > 0)
                    Console.WriteLine($"  Average per result: {sw.Elapsed.TotalMilliseconds / luceneHits:F2} ms");
            }
        }
    }
}
