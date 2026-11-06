using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// Each book's PRECEDENCE in the library — one number per book, computed here and nowhere
    /// else. The catalog index orders its hits by it, the full-text index stores lines in it,
    /// and <see cref="SeforimDbQueries.GetAllBooks"/> ships it to the frontend as
    /// <c>treeOrder</c>, so every consumer ranks by the same order and none can drift.
    ///
    /// It is NOT the browse order. The catalog view lists a folder's subfolders above its own
    /// books, which is right for browsing but wrong for ranking: in the shipped library a base
    /// text sits directly in its folder (Tur in "Tur", the five Shulchan Aruch volumes in
    /// "Shulchan Aruch") and its commentaries sit in a "mefarshim" subfolder, so a folders-first
    /// walk put every commentary ahead of the text it comments on. The other way round is just
    /// as wrong elsewhere: in the Tanakh/Talmud/Mishnah commentary folders the own books are
    /// one-offs and introductions while the subfolders hold the major works.
    ///
    /// The walk therefore numbers, per folder: (1) the folder's own BASE texts, (2) its
    /// subfolders recursively, (3) its remaining own books. A base text is a book the database
    /// flags <c>isBaseBook</c>, and nothing else (a title equal to the folder's does not count).
    ///
    /// Everything else matches the frontend's buildTree: categories in load order — which puts
    /// a category with a NEGATIVE orderIndex (the library's "mefarshim" marker) after its
    /// siblings — custom (negative-id) entries last per level, and books whose category row is
    /// missing under a synthetic last root.
    /// </summary>
    public static class SeforimDbBookTreeOrder
    {
        /// <summary>A category row, in the load order of <see cref="SeforimDbSqlStrings.CatalogIndexCategories"/>.</summary>
        public readonly record struct CategoryInput(int Id, int? ParentId, string Title);

        /// <summary>A book row, in the load order of <see cref="SeforimDbSqlStrings.CatalogIndexBooks"/>.
        /// <paramref name="IsDependent"/>: the database's <c>dependenceType</c> is set
        /// (commentary, targum, …) — kept for callers; the order reads only <paramref name="IsBaseBook"/>.</summary>
        public readonly record struct BookInput(int Id, int CategoryId, string Title, bool IsBaseBook, bool IsDependent);

        /// <summary>bookId → tree order (0-based), read from the database.</summary>
        public static Dictionary<int, int> Load(SqliteConnection connection) => Load(connection, "main");

        /// <summary>Same, for one ATTACHed library's schema (see <see cref="SeforimDbAttachedLibraries"/>):
        /// the ids are that library's LOCAL ids and the order is 0-based within the library.</summary>
        public static Dictionary<int, int> Load(SqliteConnection connection, string schema)
        {
            bool hasOrderIndex = SeforimDbSchema.ColumnExists(connection, "category", "orderIndex", schema);
            bool hasIsBaseBook = SeforimDbSchema.ColumnExists(connection, "book", "isBaseBook", schema);
            bool hasDependenceType = SeforimDbSchema.ColumnExists(connection, "book", "dependenceType", schema);

            var categories = new List<CategoryInput>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = SeforimDbSqlStrings.CatalogIndexCategories(hasOrderIndex, schema);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    categories.Add(new CategoryInput(r.GetInt32(0), r.IsDBNull(1) ? null : r.GetInt32(1), r.IsDBNull(2) ? "" : r.GetString(2)));
            }

            var books = new List<BookInput>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = SeforimDbSqlStrings.CatalogIndexBooks(hasIsBaseBook, hasDependenceType, schema);
                using var r = cmd.ExecuteReader();
                while (r.Read()) books.Add(ReadBookInput(r));
            }

            return Compute(categories, books);
        }

        /// <summary>Reads a <see cref="SeforimDbSqlStrings.CatalogIndexBooks"/> row's tree-order
        /// columns (id, categoryId, title, authors, isBaseBook, dependenceType).</summary>
        public static BookInput ReadBookInput(SqliteDataReader r) => new(
            r.GetInt32(0),
            r.IsDBNull(1) ? 0 : r.GetInt32(1),
            r.IsDBNull(2) ? "" : r.GetString(2),
            IsBaseBook: !r.IsDBNull(4) && r.GetInt32(4) != 0,
            IsDependent: !r.IsDBNull(5) && r.GetString(5).Trim().Length > 0);

        /// <summary>bookId → tree order (0-based). Both lists in the frontend's load order.</summary>
        public static Dictionary<int, int> Compute(List<CategoryInput> categories, List<BookInput> books)
        {
            var children = new Dictionary<int, List<int>>();        // categoryId → child category ids
            var catBooks = new Dictionary<int, List<BookInput>>();  // categoryId → own books
            var titles = new Dictionary<int, string>();
            foreach (var c in categories) titles[c.Id] = c.Title;

            var roots = new List<int>();
            foreach (var c in categories)
            {
                if (c.ParentId is { } pid && titles.ContainsKey(pid))
                    (children.TryGetValue(pid, out var l) ? l : children[pid] = []).Add(c.Id);
                else roots.Add(c.Id);
            }

            var orphaned = new List<BookInput>();
            foreach (var b in books)
            {
                if (titles.ContainsKey(b.CategoryId))
                    (catBooks.TryGetValue(b.CategoryId, out var l) ? l : catBooks[b.CategoryId] = []).Add(b);
                else orphaned.Add(b);
            }

            foreach (var l in children.Values) StableSortByCustomLast(l, id => id);
            foreach (var l in catBooks.Values) StableSortByCustomLast(l, b => b.Id);
            StableSortByCustomLast(roots, id => id);

            var orders = new Dictionary<int, int>(books.Count);
            int counter = 0;
            void Walk(int categoryId)
            {
                catBooks.TryGetValue(categoryId, out var own);
                string folderTitle = titles[categoryId];

                // 1. The folder's own base texts — ahead of the subfolders that comment on them.
                if (own != null)
                    foreach (var b in own)
                        if (IsBaseText(b, folderTitle)) orders[b.Id] = counter++;

                // 2. Subfolders, in load order (a negative-orderIndex "mefarshim" folder last).
                if (children.TryGetValue(categoryId, out var cs))
                    foreach (int child in cs) Walk(child);

                // 3. The folder's remaining own books.
                if (own != null)
                    foreach (var b in own)
                        if (!orders.ContainsKey(b.Id)) orders[b.Id] = counter++;
            }
            foreach (int root in roots) Walk(root);
            foreach (var b in orphaned) orders[b.Id] = counter++; // synthetic last root

            return orders;
        }

        /// <summary>True when <paramref name="book"/> is flagged <c>isBaseBook</c>. The flag is the
        /// only rule: a book titled like its folder is not promoted (that rule misordered books).</summary>
        public static bool IsBaseText(BookInput book, string folderTitle) => book.IsBaseBook;

        private static void StableSortByCustomLast<T>(List<T> items, Func<T, int> id)
        {
            var sorted = items.OrderBy(x => id(x) < 0 ? 1 : 0).ToList();
            items.Clear();
            items.AddRange(sorted);
        }
    }
}
