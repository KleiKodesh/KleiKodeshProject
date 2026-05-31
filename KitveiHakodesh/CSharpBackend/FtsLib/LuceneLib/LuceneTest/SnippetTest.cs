using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using LuceneLib.Search;
using LuceneLib.SeforimDb;

namespace LuceneTest
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
                
                // Stage 1: Lucene search
                var sw1 = System.Diagnostics.Stopwatch.StartNew();
                var results = new List<(int DocId, LuceneLib.Snippets.SnippetResult Snippet)>();
                int luceneHits = 0;
                int matchCount = 0;
                int noMatchCount = 0;
                
                foreach (var (rowId, snippet) in searcher.SearchWithSnippets(
                    queryText,
                    id => db.GetLineById(id),
                    preTag: "<mark>",
                    postTag: "</mark>",
                    batchSize: 100))
                {
                    luceneHits++;
                    results.Add((rowId, snippet));
                    
                    if (snippet.IsMatch)
                        matchCount++;
                    else
                        noMatchCount++;
                    
                    if (luceneHits <= maxShow)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"  [{luceneHits}] rowId={rowId}");
                        Console.WriteLine($"      Score={snippet.Score}  WordDist={snippet.WordDistance}  Match={snippet.IsMatch}");
                        
                        // Count marks in snippet
                        int markCount = Regex.Matches(snippet.Html ?? "", "<mark>").Count;
                        Console.WriteLine($"      Marks: {markCount}");
                        
                        // Show snippet (truncated)
                        string display = snippet.Html ?? "";
                        if (display.Length > 150)
                            display = display.Substring(0, 150) + "...";
                        Console.WriteLine($"      Snippet: {display}");
                    }
                }
                sw1.Stop();

                Console.WriteLine();
                Console.WriteLine($"  ═══════════════════════════════════════════════════════");
                Console.WriteLine($"  Total results: {luceneHits:N0}");
                Console.WriteLine($"  Matched (IsMatch=true): {matchCount:N0}");
                Console.WriteLine($"  Not matched (IsMatch=false): {noMatchCount:N0}");
                Console.WriteLine($"  Match rate: {(luceneHits > 0 ? 100.0 * matchCount / luceneHits : 0):F1}%");
                Console.WriteLine($"  Lucene search + snippet generation: {sw1.Elapsed.TotalMilliseconds:F1} ms");
                
                if (luceneHits > 0)
                {
                    double avgPerResult = sw1.Elapsed.TotalMilliseconds / luceneHits;
                    Console.WriteLine($"  Average per result: {avgPerResult:F2} ms");
                }
            }
        }
    }
}
