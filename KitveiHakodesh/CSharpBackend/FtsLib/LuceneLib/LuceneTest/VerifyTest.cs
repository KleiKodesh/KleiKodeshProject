using System;
using System.Collections.Generic;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using LuceneLib.Indexing;
using LuceneLib.Search;
using LuceneLib.SeforimDb;
using LuceneLib.Tokenization;

namespace LuceneTest
{
    internal static class VerifyTest
    {
        public static void Run(string indexDir, string queryText, string dbPath = null)
        {
            Console.WriteLine($"=== VERIFY ALL RESULTS: {queryText} ===");

            if (!System.IO.Directory.Exists(indexDir))
            {
                Console.WriteLine("Index not found — run build first.");
                return;
            }

            // Tokenize the query to get the bare terms we expect in each line
            var expectedTerms = new List<string>();
            using (var analyzer = new HebrewAnalyzer())
            using (var ts = analyzer.GetTokenStream("text", new System.IO.StringReader(queryText)))
            {
                var termAttr = ts.GetAttribute<ICharTermAttribute>();
                ts.Reset();
                while (ts.IncrementToken())
                    expectedTerms.Add(termAttr.ToString());
                ts.End();
            }
            Console.WriteLine($"  Expected terms in every result: [{string.Join(", ", expectedTerms)}]");

            // Collect all matching docIds from the index
            var docIds = new List<int>();
            using (var dir = FSDirectory.Open(indexDir))
            using (var reader = DirectoryReader.Open(dir))
            {
                var searcher = new IndexSearcher(reader);
                Query q;
                using (var a = new HebrewAnalyzer())
                    q = HebrewQueryBuilder.Build(queryText, a);
                if (q == null) { Console.WriteLine("  (empty / invalid query)"); return; }
                Console.WriteLine($"  Parsed query: {q}");

                TotalHitCountCollector counter = new TotalHitCountCollector();
                searcher.Search(q, counter);
                int total = counter.TotalHits;
                Console.WriteLine($"  Total hits  : {total:N0}");
                if (total == 0) return;

                TopDocs top = searcher.Search(q, total);
                foreach (ScoreDoc sd in top.ScoreDocs)
                {
                    var field = searcher.Doc(sd.Doc).GetField(LuceneIndexWriter.FieldRowId);
                    if (field != null) docIds.Add(field.GetInt32Value().Value);
                }
            }

            // Now fetch each line from the DB and verify all terms appear
            int pass = 0, fail = 0;
            using (var db = new ZayitDb(dbPath))
            {
                if (!db.IsOpen) { Console.WriteLine("Database not found."); return; }

                foreach (int id in docIds)
                {
                    string content = db.GetLineById(id) ?? string.Empty;

                    // Tokenize the raw content with the same analyzer
                    var contentTerms = new HashSet<string>();
                    using (var analyzer = new HebrewAnalyzer())
                    using (var ts = analyzer.GetTokenStream("text", new System.IO.StringReader(content)))
                    {
                        var termAttr = ts.GetAttribute<ICharTermAttribute>();
                        ts.Reset();
                        while (ts.IncrementToken())
                            contentTerms.Add(termAttr.ToString());
                        ts.End();
                    }

                    var missing = new List<string>();
                    foreach (var t in expectedTerms)
                        if (!contentTerms.Contains(t))
                            missing.Add(t);

                    if (missing.Count == 0)
                    {
                        pass++;
                    }
                    else
                    {
                        fail++;
                        Console.WriteLine($"  FAIL rowId={id}  missing=[{string.Join(", ", missing)}]  text={content.Substring(0, Math.Min(120, content.Length))}");
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine($"  PASS: {pass:N0} / {pass + fail:N0}");
            if (fail == 0)
                Console.WriteLine("  All results verified correct.");
            else
                Console.WriteLine($"  FAIL: {fail:N0} false positives detected.");
        }
    }
}
