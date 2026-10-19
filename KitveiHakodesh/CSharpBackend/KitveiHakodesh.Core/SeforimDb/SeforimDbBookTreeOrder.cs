using System;
using System.Collections.Generic;
using System.Linq;
using KitveiHakodesh.Core.SeforimDbCatalog;
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
    /// flags <c>isBaseBook</c>, or a non-dependent book titled like its own folder (the folder
    /// IS the book: "Sefer HaMitzvot" in "Sefer HaMitzvot"). Measured on the shipped database
    /// this reorders seventeen folders, all of the base-text-plus-mefarshim shape, and leaves the
    /// commentary folders untouched.
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
        /// (commentary, targum, …) — such a book is never a base text whatever its title.</summary>
        public readonly record struct BookInput(int Id, int CategoryId, string Title, bool IsBaseBook, bool IsDependent);

        /// <summary>bookId → tree order (0-based), read from the database.</summary>
        public static Dictionary<int, int> Load(SqliteConnection connection)
        {
            bool hasOrderIndex = SeforimDbSchema.ColumnExists(connection, "category", "orderIndex");
            bool hasIsBaseBook = SeforimDbSchema.ColumnExists(connection, "book", "isBaseBook");
            bool hasDependenceType = SeforimDbSchema.ColumnExists(connection, "book", "dependenceType");

            var categories = new List<CategoryInput>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = SeforimDbSqlStrings.CatalogIndexCategories(hasOrderIndex);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    categories.Add(new CategoryInput(r.GetInt32(0), r.IsDBNull(1) ? null : r.GetInt32(1), r.IsDBNull(2) ? "" : r.GetString(2)));
            }

            var books = new List<BookInput>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = SeforimDbSqlStrings.CatalogIndexBooks(hasIsBaseBook, hasDependenceType);
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

        /// <summary>
        /// True when <paramref name="book"/> is the text its folder exists for: flagged
        /// <c>isBaseBook</c>, or not a dependent work and titled like the folder itself, with a
        /// leading "sefer" / "midrash" / "masechet" word ignored on either side. The title rule
        /// closes the gaps in the flag (Sefer HaMitzvot, Sefer Yetzirah, Chovot HaLevavot, Moreh
        /// Nevuchim are flagged 0); the dependent guard keeps a commentary that got a folder of
        /// its own ("Mishnah Berurah" in "Mishnah Berurah") out of it, since the only other
        /// books in such a folder would be ITS commentaries.
        /// </summary>
        public static bool IsBaseText(BookInput book, string folderTitle)
        {
            if (book.IsBaseBook) return true;
            if (book.IsDependent) return false;
            return TitleTokensEqual(book.Title, folderTitle);
        }

        // "sefer", "midrash", "masechet".
        private static readonly string[] GenericTitleHeads =
        [
            "ספר",
            "מדרש",
            "מסכת",
        ];

        private static bool TitleTokensEqual(string bookTitle, string folderTitle)
        {
            var a = StripGenericHead(SeforimDbCatalogTextNormalizer.Tokenize(bookTitle));
            var b = StripGenericHead(SeforimDbCatalogTextNormalizer.Tokenize(folderTitle));
            if (a.Count == 0 || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            return true;
        }

        private static List<string> StripGenericHead(List<string> tokens)
        {
            if (tokens.Count > 1 && Array.IndexOf(GenericTitleHeads, tokens[0]) >= 0)
                tokens.RemoveAt(0);
            return tokens;
        }

        private static void StableSortByCustomLast<T>(List<T> items, Func<T, int> id)
        {
            var sorted = items.OrderBy(x => id(x) < 0 ? 1 : 0).ToList();
            items.Clear();
            items.AddRange(sorted);
        }
    }
}
