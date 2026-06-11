using FtsLib.SeforimDb;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Finds IDs returned by SearchIds() but not by Search() for a given query.
    /// Used to diagnose the SearchIds/Search mismatch.
    ///
    /// Usage: FtsLibTest.exe diffids [tier] "query"
    /// </summary>
    internal static class DiffIds
    {
        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string tierLabel = args.Length > 1 ? args[1] : "500k";
            string query     = args.Length > 2 ? args[2] : "*ישראל";

            string label;
            try   { label = TestHelpers.ResolveTier(tierLabel).Label; }
            catch (ArgumentException ex) { Console.WriteLine(ex.Message); return; }

            string dbPath   = BuildTest.ResolveDbPath();
            string indexDir = TestHelpers.IndexDir(label);

            Console.WriteLine($"Query    : {query}");
            Console.WriteLine($"Index    : {indexDir}");
            Console.WriteLine($"DB       : {dbPath}");
            Console.WriteLine();

            var index = new SeforimIndex(indexDir, dbPath);

            var searchIds = new HashSet<int>();
            foreach (var id in index.SearchIds(query)) searchIds.Add(id);

            var searchResultIds = new HashSet<int>();
            foreach (var r in index.Search(query)) searchResultIds.Add(r.LineId);

            Console.WriteLine($"SearchIds count : {searchIds.Count:N0}");
            Console.WriteLine($"Search count    : {searchResultIds.Count:N0}");
            Console.WriteLine();

            var onlyInIds    = new List<int>();
            var onlyInSearch = new List<int>();

            foreach (var id in searchIds)
                if (!searchResultIds.Contains(id)) onlyInIds.Add(id);
            foreach (var id in searchResultIds)
                if (!searchIds.Contains(id)) onlyInSearch.Add(id);

            if (onlyInIds.Count == 0 && onlyInSearch.Count == 0)
            {
                Console.WriteLine("No mismatch — sets are identical.");
                return;
            }

            if (onlyInIds.Count > 0)
            {
                Console.WriteLine($"IDs in SearchIds but NOT in Search ({onlyInIds.Count}):");
                using (var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;Read Only=True;"))
                {
                    conn.Open();
                    foreach (var id in onlyInIds)
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText =
                                "SELECT l.id, b.title, l.content FROM line l " +
                                "LEFT JOIN book b ON b.id = l.bookId WHERE l.id = @id";
                            cmd.Parameters.AddWithValue("@id", id);
                            using (var r = cmd.ExecuteReader())
                            {
                                if (r.Read())
                                    Console.WriteLine($"  id={id}  book={r.GetString(1)}  content={Truncate(r.GetString(2), 80)}");
                                else
                                    Console.WriteLine($"  id={id}  *** NOT FOUND IN line TABLE ***");
                            }
                        }
                    }
                }
            }

            if (onlyInSearch.Count > 0)
            {
                Console.WriteLine($"IDs in Search but NOT in SearchIds ({onlyInSearch.Count}):");
                foreach (var id in onlyInSearch)
                    Console.WriteLine($"  id={id}");
            }
        }

        private static string Truncate(string s, int max) =>
            s == null ? "" : s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
