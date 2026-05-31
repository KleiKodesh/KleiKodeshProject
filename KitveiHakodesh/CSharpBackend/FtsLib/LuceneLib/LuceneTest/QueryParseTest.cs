using System;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using LuceneLib.Search;
using LuceneLib.Tokenization;

namespace LuceneTest
{
    internal static class QueryParseTest
    {
        public static void Run(string queryText)
        {
            Console.WriteLine($"=== QUERY PARSE: {queryText} ===");
            using (var analyzer = new HebrewAnalyzer())
            {
                Query q = HebrewQueryBuilder.Build(queryText, analyzer);
                if (q == null) { Console.WriteLine("  (empty / invalid query)"); return; }
                Console.WriteLine($"  Parsed query type : {q.GetType().Name}");
                Console.WriteLine($"  Parsed query      : {q}");
            }
        }

        /// <summary>
        /// Shows what the query expands to after Rewrite() against the real index.
        /// This is exactly what QueryScorer sees when building highlight terms.
        /// </summary>
        public static void RunRewrite(string indexDir, string queryText)
        {
            Console.WriteLine($"=== QUERY REWRITE: {queryText} ===");

            if (!System.IO.Directory.Exists(indexDir))
            {
                Console.WriteLine("Index not found — run build first.");
                return;
            }

            using (var dir    = FSDirectory.Open(indexDir))
            using (var reader = DirectoryReader.Open(dir))
            using (var analyzer = new HebrewAnalyzer())
            {
                Query q = HebrewQueryBuilder.Build(queryText, analyzer);
                if (q == null) { Console.WriteLine("  (empty / invalid query)"); return; }
                Console.WriteLine($"  Before rewrite : {q}");

                Query rewritten = q.Rewrite(reader);
                Console.WriteLine($"  After  rewrite : {rewritten}");
                Console.WriteLine($"  Rewritten type : {rewritten.GetType().Name}");
            }
        }
    }
}
