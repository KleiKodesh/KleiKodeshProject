using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// Each book's position in the catalog tree — identical to the frontend's
    /// buildTree + assignFullPaths (vue-frontend bookCatalogTree.ts), which is where the app's
    /// <c>treeOrder</c> comes from: categories nested in load order, custom (negative-id)
    /// entries sorted last per level, orphaned books under a synthetic last root, then a DFS
    /// that numbers a category's books before descending into its subcategories.
    ///
    /// One copy for every C# consumer: the catalog index orders its hits by it and the
    /// full-text index stores lines in it, so they cannot drift from each other — and both
    /// load categories and books with the same ORDER BY the frontend uses.
    /// </summary>
    public static class SeforimDbBookTreeOrder
    {
        /// <summary>bookId → tree order (0-based), read from the database.</summary>
        public static Dictionary<int, int> Load(SqliteConnection connection)
        {
            bool hasOrderIndex = false;
            using (var probe = connection.CreateCommand())
            {
                probe.CommandText = "PRAGMA table_info(category)";
                using var pr = probe.ExecuteReader();
                while (pr.Read())
                    if (string.Equals(pr.GetString(1), "orderIndex", StringComparison.Ordinal)) hasOrderIndex = true;
            }

            var categories = new List<(int Id, int? ParentId)>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = SeforimDbSqlStrings.CatalogIndexCategories(hasOrderIndex);
                using var r = cmd.ExecuteReader();
                while (r.Read()) categories.Add((r.GetInt32(0), r.IsDBNull(1) ? null : r.GetInt32(1)));
            }

            var books = new List<(int Id, int CategoryId)>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = SeforimDbSqlStrings.CatalogIndexBooks;   // id, categoryId, …
                using var r = cmd.ExecuteReader();
                while (r.Read()) books.Add((r.GetInt32(0), r.IsDBNull(1) ? 0 : r.GetInt32(1)));
            }

            return Compute(categories, books);
        }

        /// <summary>bookId → tree order (0-based). Both lists in the frontend's load order.</summary>
        public static Dictionary<int, int> Compute(
            List<(int Id, int? ParentId)> categories,
            List<(int Id, int CategoryId)> books)
        {
            var children = new Dictionary<int, List<int>>();   // categoryId → child category ids
            var catBooks = new Dictionary<int, List<int>>();   // categoryId → book ids
            var known = new HashSet<int>();
            foreach (var c in categories) known.Add(c.Id);

            var roots = new List<int>();
            foreach (var c in categories)
            {
                if (c.ParentId is { } pid && known.Contains(pid))
                    (children.TryGetValue(pid, out var l) ? l : children[pid] = []).Add(c.Id);
                else roots.Add(c.Id);
            }

            var orphaned = new List<int>();
            foreach (var b in books)
            {
                if (known.Contains(b.CategoryId))
                    (catBooks.TryGetValue(b.CategoryId, out var l) ? l : catBooks[b.CategoryId] = []).Add(b.Id);
                else orphaned.Add(b.Id);
            }

            static int CustomLast(int id) => id < 0 ? 1 : 0;
            foreach (var l in children.Values) StableSortByCustomLast(l);
            foreach (var l in catBooks.Values) StableSortByCustomLast(l);
            StableSortByCustomLast(roots);

            var orders = new Dictionary<int, int>(books.Count);
            int counter = 0;
            void Walk(int categoryId)
            {
                if (catBooks.TryGetValue(categoryId, out var bs))
                    foreach (int bookId in bs) orders[bookId] = counter++;
                if (children.TryGetValue(categoryId, out var cs))
                    foreach (int child in cs) Walk(child);
            }
            foreach (int root in roots) Walk(root);
            foreach (int bookId in orphaned) orders[bookId] = counter++; // synthetic last root

            return orders;

            static void StableSortByCustomLast(List<int> ids)
            {
                var sorted = ids.OrderBy(CustomLast).ToList();
                ids.Clear();
                ids.AddRange(sorted);
            }
        }
    }
}
