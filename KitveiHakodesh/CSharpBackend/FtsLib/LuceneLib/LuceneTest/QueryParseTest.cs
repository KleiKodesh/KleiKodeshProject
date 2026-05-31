using System;
using Lucene.Net.Search;
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
    }
}
