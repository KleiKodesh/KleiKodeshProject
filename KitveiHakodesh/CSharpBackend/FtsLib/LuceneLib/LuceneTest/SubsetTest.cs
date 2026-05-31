using System;
using System.Collections.Generic;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using LuceneLib.Indexing;
using LuceneLib.Search;
using LuceneLib.Tokenization;

namespace LuceneTest
{
    /// <summary>
    /// Verifies that every row returned by a literal query is also returned
    /// by a corresponding wildcard query.
    ///
    /// Usage:
    ///   LuceneTest diag subset "כי ביצחק" "כי *יצחק"
    ///
    /// The test passes when literalResults ⊆ wildcardResults.
    /// It also reports rows in wildcardResults that are NOT in literalResults
    /// (expected — the wildcard matches more forms).
    /// </summary>
    internal static class SubsetTest
    {
        public static void Run(string indexDir, string literalQuery, string wildcardQuery)
        {
            Console.WriteLine($"=== SUBSET TEST ===");
            Console.WriteLine($"  Literal  query : {literalQuery}");
            Console.WriteLine($"  Wildcard query : {wildcardQuery}");
            Console.WriteLine();

            if (!System.IO.Directory.Exists(indexDir))
            {
                Console.WriteLine("Index not found — run build first.");
                return;
            }

            using (var dir    = FSDirectory.Open(indexDir))
            using (var reader = DirectoryReader.Open(dir))
            {
                var searcher = new IndexSearcher(reader);

                var literalIds  = CollectIds(searcher, literalQuery);
                var wildcardIds = CollectIds(searcher, wildcardQuery);

                if (literalIds == null || wildcardIds == null)
                    return; // error already printed

                Console.WriteLine($"  Literal  hits : {literalIds.Count:N0}");
                Console.WriteLine($"  Wildcard hits : {wildcardIds.Count:N0}");
                Console.WriteLine();

                // Check literal ⊆ wildcard
                var missing = new List<int>();
                foreach (int id in literalIds)
                    if (!wildcardIds.Contains(id))
                        missing.Add(id);

                if (missing.Count == 0)
                {
                    Console.WriteLine("  PASS — every literal result is contained in the wildcard results.");
                }
                else
                {
                    Console.WriteLine($"  FAIL — {missing.Count:N0} literal row(s) are MISSING from the wildcard results:");
                    int show = Math.Min(missing.Count, 20);
                    for (int i = 0; i < show; i++)
                        Console.WriteLine($"    rowId={missing[i]}");
                    if (missing.Count > show)
                        Console.WriteLine($"    … and {missing.Count - show} more.");
                }

                // Extra info: wildcard-only rows (expected — wider match)
                int wildcardOnly = 0;
                foreach (int id in wildcardIds)
                    if (!literalIds.Contains(id))
                        wildcardOnly++;

                Console.WriteLine();
                Console.WriteLine($"  Wildcard-only rows (matched by wildcard but not literal): {wildcardOnly:N0}");
            }
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static HashSet<int> CollectIds(IndexSearcher searcher, string queryText)
        {
            Query q;
            using (var analyzer = new HebrewAnalyzer())
                q = HebrewQueryBuilder.Build(queryText, analyzer);

            if (q == null)
            {
                Console.WriteLine($"  (empty / invalid query: {queryText})");
                return null;
            }

            Console.WriteLine($"  Parsed [{queryText}] → {q}");

            var counter = new TotalHitCountCollector();
            searcher.Search(q, counter);
            int total = counter.TotalHits;

            if (total == 0)
                return new HashSet<int>();

            TopDocs top = searcher.Search(q, total);
            var ids = new HashSet<int>(total);
            foreach (ScoreDoc sd in top.ScoreDocs)
            {
                var field = searcher.Doc(sd.Doc).GetField(LuceneIndexWriter.FieldRowId);
                if (field != null)
                    ids.Add(field.GetInt32Value().Value);
            }
            return ids;
        }
    }
}
