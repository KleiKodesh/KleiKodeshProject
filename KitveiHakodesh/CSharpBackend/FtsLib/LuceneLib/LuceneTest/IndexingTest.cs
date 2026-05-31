using System;
using System.IO;
using LuceneLib.Indexing;
using LuceneLib.Search;
using LuceneLib.SeforimDb;

namespace LuceneTest
{
    /// <summary>
    /// Indexes all rows from the Zayit database into a Lucene index,
    /// then runs a query and prints the matching doc IDs.
    /// </summary>
    internal static class IndexingTest
    {
        private static readonly string IndexDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lucene_index");

        public static void RunBuild(string dbPath = null, bool deleteExistingIndex = true)
        {
            Console.WriteLine("=== BUILD INDEX ===");
            Console.WriteLine($"Index path: {IndexDir}");
            if (deleteExistingIndex)
                Console.WriteLine("(will delete existing index if present)");

            using (var db     = new ZayitDb(dbPath))
            using (var writer = new LuceneIndexWriter(IndexDir, deleteExistingIndex: deleteExistingIndex))
            {
                if (!db.IsOpen)
                {
                    Console.WriteLine("Database not found — aborting.");
                    return;
                }

                long total = db.CountLines();
                Console.WriteLine($"Rows to index: {total:N0}");
                Console.WriteLine();

                var sw = System.Diagnostics.Stopwatch.StartNew();
                writer.IndexAll(db, totalRows: total);
                sw.Stop();

                Console.WriteLine($"Build complete in {sw.Elapsed.TotalSeconds:F2}s");
            }
        }

        public static void RunSearch(string query, string dbPath = null)
        {
            Console.WriteLine($"=== SEARCH: {query} ===");

            if (!Directory.Exists(IndexDir))
            {
                Console.WriteLine("Index not found — run build first.");
                return;
            }

            using (var searcher = new LuceneSearcher(IndexDir))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int count = 0;

                foreach (int id in searcher.Search(query))
                {
                    count++;
                    Console.WriteLine($"  [{count}] docId={id}");
                }

                sw.Stop();
                Console.WriteLine($"Hits: {count:N0}  ({sw.Elapsed.TotalMilliseconds:F1} ms)");
            }
        }

        public static void CheckLine(int lineId, string dbPath = null)
        {
            Console.WriteLine($"=== CHECK LINE {lineId} ===");

            using (var db = new ZayitDb(dbPath))
            {
                if (!db.IsOpen)
                {
                    Console.WriteLine("Database not found.");
                    return;
                }

                string content = db.GetLineById(lineId);
                if (content == null)
                    Console.WriteLine($"Line {lineId} not found.");
                else
                    Console.WriteLine($"Line {lineId}: {content}");
            }
        }
    }
}
