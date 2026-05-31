using System;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using LuceneLib.Indexing;
using LuceneLib.Search;
using LuceneLib.Tokenization;

namespace LuceneTest
{
    internal static class HitsTest
    {
        public static void Run(string indexDir, string queryText, int maxPrint = 10)
        {
            Console.WriteLine($"=== HITS: {queryText} ===");

            if (!System.IO.Directory.Exists(indexDir))
            {
                Console.WriteLine("Index not found — run build first.");
                return;
            }

            using (var dir = FSDirectory.Open(indexDir))
            using (var reader = DirectoryReader.Open(dir))
            {
                var searcher = new IndexSearcher(reader);
                Query q;
                using (var analyzer = new HebrewAnalyzer())
                    q = HebrewQueryBuilder.Build(queryText, analyzer);
                if (q == null) { Console.WriteLine("  (empty / invalid query)"); return; }
                Console.WriteLine($"  Parsed query: {q}");

                // Get exact total count first
                TotalHitCountCollector counter = new TotalHitCountCollector();
                searcher.Search(q, counter);
                int total = counter.TotalHits;
                Console.WriteLine($"  Total hits: {total:N0}");

                if (total == 0) return;

                // Print first N
                TopDocs top = searcher.Search(q, Math.Min(maxPrint, total));
                for (int i = 0; i < top.ScoreDocs.Length; i++)
                {
                    var doc   = searcher.Doc(top.ScoreDocs[i].Doc);
                    var field = doc.GetField(LuceneIndexWriter.FieldRowId);
                    int rowId = field?.GetInt32Value() ?? -1;
                    Console.WriteLine($"  [{i + 1}] rowId={rowId}  score={top.ScoreDocs[i].Score:F4}");
                }
            }
        }
    }
}
