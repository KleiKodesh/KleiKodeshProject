using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// The book view's related-books list: every book that stands in a fixed relationship to
    /// the open one (its base texts, targumim, commentaries), plus the commentaries of each of
    /// its base texts. Built on demand when the list is opened; nothing is kept.
    ///
    /// The groups are the commentary panel's static filter groups (the frontend's
    /// buildStaticCommentaryFilterGroups) and the sections are the dropdown's own rules, so a
    /// book sits in the same section, in the same order, as it always did.
    /// </summary>
    public sealed partial class SeforimDbQueries
    {
        // -- Connection types ------------------------------------------------------

        /// <summary>The link types that make a book DEPEND on another (comment on, translate,
        /// expound it) - the frontend's BASE_TEXT_REVERSE_DB_NAMES. Both directions of the list
        /// use them: forward finds what depends on a book, reverse finds what it depends on.</summary>
        internal static readonly string[] DependantConnectionTypeNames =
            { "COMMENTARY", "SUPER_COMMENTARY", "TARGUM", "MIDRASH", "PARSHANUT", "DIBUR_HAMATCHIL" };

        private static readonly string[] ConnectionTypePriority =
            { "SOURCE", "TARGUM", "COMMENTARY", "EIN_MISHPAT", "MESORAH_HASHAS", "OTHER", "REFERENCE" };

        /// <summary>normalizeConnectionTypeName: DB name -> the canonical type the lists group by.</summary>
        private static string CanonicalConnectionType(string dbName) => dbName switch
        {
            "SOURCE" => "SOURCE",
            "TARGUM" => "TARGUM",
            "COMMENTARY" or "SUPER_COMMENTARY" or "PARSHANUT" or "MIDRASH" => "COMMENTARY",
            "REFERENCE" or "MISHNAH_IN_TALMUD" => "REFERENCE",
            "EIN_MISHPAT" => "EIN_MISHPAT",
            "MESORAH_HASHAS" => "MESORAH_HASHAS",
            _ => "OTHER",
        };

        private static string PrimaryConnectionType(IReadOnlyList<string> types)
        {
            foreach (var type in ConnectionTypePriority)
                if (types.Contains(type)) return type;
            return types.Count > 0 ? types[0] : "OTHER";
        }

        /// <summary>CONNECTION_TYPE_SECTION_LABELS (escaped Hebrew).</summary>
        private static string SectionLabelFor(string canonicalType) => canonicalType switch
        {
            "SOURCE" => "\u05DE\u05E7\u05D5\u05E9\u05E8\u05D9\u05DD",
            "TARGUM" => "\u05EA\u05E8\u05D2\u05D5\u05DE\u05D9\u05DD",
            "COMMENTARY" => "\u05DE\u05E4\u05E8\u05E9\u05D9\u05DD",
            "EIN_MISHPAT" => "\u05E2\u05D9\u05DF \u05DE\u05E9\u05E4\u05D8",
            "MESORAH_HASHAS" => "\u05DE\u05E1\u05D5\u05E8\u05EA \u05D4\u05E9\u05F4\u05E1",
            "OTHER" => "\u05E7\u05E9\u05E8\u05D9\u05DD",
            _ => "\u05E6\u05D9\u05D5\u05E0\u05D9\u05DD",
        };

        /// <summary>Connection type ids and names, read per call (a handful of rows).</summary>
        internal static Dictionary<int, string> ReadConnectionTypeNames(SqliteConnection conn)
        {
            var names = new Dictionary<int, string>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name FROM connection_type";
            using var r = cmd.ExecuteReader();
            while (r.Read()) names[r.GetInt32(0)] = r.GetString(1);
            return names;
        }

        internal static List<int> DependantTypeIds(Dictionary<int, string> typeNames) =>
            DependantConnectionTypeNames
                .Select(name => typeNames.FirstOrDefault(kv => kv.Value == name))
                .Where(kv => kv.Value != null)
                .Select(kv => kv.Key)
                .ToList();

        // -- The list --------------------------------------------------------------

        public RelatedBooksList GetRelatedBooks(int bookId)
        {
            using var conn = Open();
            var schema = SchemaOf(conn);
            var typeNames = ReadConnectionTypeNames(conn);
            var dependantTypeIds = DependantTypeIds(typeNames);
            var books = BooksById();
            var categories = CategoriesById();
            var categoryCache = new Dictionary<int, string>();

            var bases = new BaseTextLookup(conn, schema, dependantTypeIds);

            // The open book's own groups: what depends on it, and what it depends on.
            var ownGroups = BuildStaticGroups(
                ReadStaticForward(conn, bookId, dependantTypeIds), bases.BasesOf(new[] { bookId })[bookId],
                typeNames, books, categories, categoryCache);

            // Its base texts, once each, in list order.
            var sourceIds = new List<int>();
            foreach (var g in ownGroups)
                if (g.PrimaryType == "SOURCE" && !sourceIds.Contains(g.BookId)) sourceIds.Add(g.BookId);

            // Each base text's own groups. Their base texts are in ONE query for all of them.
            var sourceBases = bases.BasesOf(sourceIds);
            int commentaryTypeId = typeNames.FirstOrDefault(kv => kv.Value == "COMMENTARY").Key;
            var sourceGroups = sourceIds.ToDictionary(id => id, id =>
            {
                // What depends on the base text by its links, plus every book the library lists
                // as based on it or on one of those (a commentary on its commentary).
                var forward = ReadStaticForward(conn, id, dependantTypeIds);
                var direct = bases.BooksBasedOn(new[] { id });
                var listed = direct.Concat(bases.BooksBasedOn(direct)).Distinct();
                var linked = new HashSet<int>(forward.Select(f => f.Item1));
                foreach (int extra in listed)
                    if (extra != id && !linked.Contains(extra)) forward.Add((extra, commentaryTypeId));
                return BuildStaticGroups(forward, sourceBases[id], typeNames, books, categories, categoryCache);
            });

            // The primary base of every book in those groups - again ONE query - so a book
            // that merely cites the base text (Rif Shabbat in Berachot's list) is dropped.
            var memberIds = sourceGroups.Values.SelectMany(gs => gs.Select(g => g.BookId)).Distinct().ToList();
            var memberBases = bases.BasesOf(memberIds, primaryOnly: true);

            return BuildSections(bookId, ownGroups, sourceIds, sourceGroups, memberBases, books);
        }

        /// <summary>The dropdown's sections: the open book's groups by category, then right after
        /// the base-text section(s) one section per base text with that text's commentaries.</summary>
        private static RelatedBooksList BuildSections(
            int bookId, List<StaticGroup> ownGroups, List<int> sourceIds,
            Dictionary<int, List<StaticGroup>> sourceGroups, Dictionary<int, List<int>> memberBases,
            IReadOnlyDictionary<int, BookRow> books)
        {
            var sections = new List<RelatedBooksSection>();
            var byLabel = new Dictionary<string, RelatedBooksSection>();
            var seen = new HashSet<int>();

            foreach (var group in ownGroups)
            {
                string label = !string.IsNullOrEmpty(group.SubSectionLabel) ? group.SubSectionLabel! : group.SectionLabel;
                if (!byLabel.TryGetValue(label, out var section))
                {
                    section = new RelatedBooksSection { Label = label };
                    byLabel[label] = section;
                    sections.Add(section);
                }
                if (!seen.Add(group.BookId)) continue;
                section.Books.Add(new RelatedBook
                {
                    BookId = group.BookId,
                    Title = group.Title,
                    IsBaseText = group.PrimaryType == "SOURCE",
                });
            }

            int lastSourceSection = -1;
            for (int i = 0; i < sections.Count; i++)
                if (sections[i].Books.Any(b => b.IsBaseText)) lastSourceSection = i;
            int insertAt = lastSourceSection + 1;

            foreach (int sourceId in sourceIds)
            {
                var groups = sourceGroups[sourceId];
                int? PrimaryBase(int id) => memberBases.TryGetValue(id, out var list) && list.Count > 0 ? list[0] : null;
                // loadOwnCommentaryGroups: a book whose own primary base is another book is a
                // citation, not a commentary - unless that base is itself on this source.
                var onSource = new HashSet<int>(groups.Where(g => PrimaryBase(g.BookId) == sourceId).Select(g => g.BookId));

                var section = new RelatedBooksSection
                {
                    CommentariesOfBookId = sourceId,
                    CommentariesOfTitle = OwnGroupTitle(ownGroups, sourceId),
                };
                foreach (var group in groups)
                {
                    int? baseBookId = PrimaryBase(group.BookId);
                    if (!(baseBookId == null || baseBookId == sourceId || onSource.Contains(baseBookId.Value))) continue;
                    if (group.BookId == bookId || seen.Contains(group.BookId)) continue;
                    if (group.PrimaryType == "SOURCE") continue;
                    seen.Add(group.BookId);
                    section.Books.Add(new RelatedBook
                    {
                        BookId = group.BookId,
                        Title = group.Title,
                        ViaSourceBookId = sourceId,
                        ChainBaseBookId = baseBookId != null && baseBookId != sourceId ? baseBookId : null,
                    });
                }
                sections.Insert(insertAt++, section);
            }

            return new RelatedBooksList { Sections = sections.Where(s => s.Books.Count > 0).ToList() };
        }

        private static string OwnGroupTitle(List<StaticGroup> groups, int bookId)
        {
            foreach (var g in groups)
                if (g.BookId == bookId && g.PrimaryType == "SOURCE") return g.Title;
            return bookId.ToString();
        }

        // -- Static groups (buildStaticCommentaryFilterGroups) ---------------------

        private sealed class StaticGroup
        {
            public int BookId;
            public string Title = "";
            public string SectionLabel = "";
            public string? SubSectionLabel;
            public string PrimaryType = "";
        }

        private sealed class GroupEntry
        {
            public int BookId;
            public string Title = "";
            public string Category = "";
            public int TreeOrder;
            public string PrimaryType = "";
        }

        private static List<StaticGroup> BuildStaticGroups(
            List<(int TargetBookId, int TypeId)> forwardRows, List<int> reverseBookIds,
            Dictionary<int, string> typeNames, IReadOnlyDictionary<int, BookRow> books,
            IReadOnlyDictionary<int, CategoryRow> categories, Dictionary<int, string> categoryCache)
        {
            if (forwardRows.Count == 0 && reverseBookIds.Count == 0) return new List<StaticGroup>();

            GroupEntry Entry(int id, string primary)
            {
                books.TryGetValue(id, out var book);
                string category = SeforimDbBookCommentaryCategory.OtherCategory;
                if (book != null && !categoryCache.TryGetValue(book.CategoryId, out category!))
                    categoryCache[book.CategoryId] = category = SeforimDbBookCommentaryCategory.Resolve(book.CategoryId, categories);
                return new GroupEntry
                {
                    BookId = id,
                    Title = !string.IsNullOrEmpty(book?.Title) ? CommentaryDisplayTitle(book!.Title) : id.ToString(),
                    Category = category,
                    TreeOrder = book?.TreeOrder ?? 999999,
                    PrimaryType = primary,
                };
            }

            var entries = new List<GroupEntry>();
            foreach (int id in reverseBookIds) entries.Add(Entry(id, "SOURCE"));

            var typesByBook = new Dictionary<int, List<string>>();
            var bookOrder = new List<int>();
            foreach (var (target, typeId) in forwardRows)
            {
                if (!typesByBook.TryGetValue(target, out var types))
                {
                    typesByBook[target] = types = new List<string>();
                    bookOrder.Add(target);
                }
                string canonical = CanonicalConnectionType(typeNames.TryGetValue(typeId, out var n) ? n : typeId.ToString());
                if (!types.Contains(canonical)) types.Add(canonical);
            }
            foreach (int id in bookOrder) entries.Add(Entry(id, PrimaryConnectionType(typesByBook[id])));

            // buildCommentaryGroupsFromEntries
            var result = new List<StaticGroup>();
            void AddFlat(string type)
            {
                string label = SectionLabelFor(type);
                foreach (var e in entries.Where(e => e.PrimaryType == type).OrderBy(e => e.TreeOrder))
                    result.Add(new StaticGroup { BookId = e.BookId, Title = e.Title, SectionLabel = label, PrimaryType = type });
            }
            void AddMergedByCategory(string type)
            {
                string label = SectionLabelFor(type);
                var byCategory = new List<(string Category, List<GroupEntry> Items)>();
                foreach (var e in entries.Where(e => e.PrimaryType == type))
                {
                    int at = byCategory.FindIndex(c => c.Category == e.Category);
                    if (at < 0) byCategory.Add((e.Category, new List<GroupEntry> { e }));
                    else byCategory[at].Items.Add(e);
                }
                foreach (var (category, items) in byCategory.OrderBy(c => SeforimDbBookCommentaryCategory.Rank(c.Category)))
                    foreach (var e in items.OrderBy(e => e.TreeOrder))
                        result.Add(new StaticGroup
                        {
                            BookId = e.BookId, Title = e.Title, SectionLabel = label,
                            SubSectionLabel = category, PrimaryType = type,
                        });
            }
            AddFlat("SOURCE");
            AddFlat("TARGUM");
            AddMergedByCategory("COMMENTARY");
            AddFlat("EIN_MISHPAT");
            AddMergedByCategory("OTHER");
            AddFlat("REFERENCE");
            return result;
        }

        private static readonly Regex AlWord = new("\\s+\u05E2\u05DC\\s+", RegexOptions.CultureInvariant);

        /// <summary>commentaryDisplayTitle: "X al Y" shown as "X Y" (first occurrence only).</summary>
        internal static string CommentaryDisplayTitle(string title)
        {
            string shown = AlWord.Replace(title, " ", 1).Trim();
            return shown.Length > 0 ? shown : title;
        }

        // -- Forward rows (getStaticFilterBooks) -----------------------------------

        /// <summary>What depends on a book, one row per (book, type) in the order the links were
        /// first stored - the order getStaticFilterBooks' DISTINCT scan returned them in, which
        /// decides the order of equal-ranked categories. Then the catalogue commentaries.</summary>
        private const string StaticForwardSql = @"
            SELECT l.targetBookId, l.connectionTypeId
            FROM link l
            WHERE l.sourceBookId = @bookId
              AND l.connectionTypeId IN ({0})
            GROUP BY l.targetBookId, l.connectionTypeId
            ORDER BY MIN(l.id)";

        private static List<(int, int)> ReadStaticForward(SqliteConnection conn, int bookId, List<int> typeIds)
        {
            var rows = new List<(int, int)>();
            if (typeIds.Count == 0) return rows;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = string.Format(StaticForwardSql, SeforimDbSqlStrings.InPlaceholders("c", typeIds.Count));
                cmd.Parameters.AddWithValue("@bookId", bookId);
                BindList(cmd, "c", typeIds);
                using var r = cmd.ExecuteReader();
                while (r.Read()) rows.Add((r.GetInt32(0), r.GetInt32(1)));
            }

            using var typeCmd = conn.CreateCommand();
            typeCmd.CommandText = "SELECT id FROM connection_type WHERE name = 'COMMENTARY'";
            if (typeCmd.ExecuteScalar() is not long commentaryTypeId) return rows;
            using var catalogCmd = conn.CreateCommand();
            catalogCmd.CommandText = SeforimDbSqlStrings.GetLinkedCatalogCommentaryBooks;
            catalogCmd.Parameters.AddWithValue("@bookId", bookId);
            using var cr = catalogCmd.ExecuteReader();
            while (cr.Read()) rows.Add((cr.GetInt32(0), (int)commentaryTypeId));
            return rows;
        }

        // -- Catalog lookups -------------------------------------------------------

        private Dictionary<int, BookRow>? _booksById;
        private Dictionary<int, CategoryRow>? _categoriesById;

        /// <summary>The cached catalog by id (dropped with it by ClearCatalogCache).</summary>
        private IReadOnlyDictionary<int, BookRow> BooksById()
        {
            var list = GetAllBooks();
            lock (_catalogCacheLock)
            {
                if (_booksById != null && ReferenceEquals(list, _booksCache)) return _booksById;
                var map = new Dictionary<int, BookRow>(list.Count);
                foreach (var b in list) map[b.Id] = b;
                if (ReferenceEquals(list, _booksCache)) _booksById = map;
                return map;
            }
        }

        private IReadOnlyDictionary<int, CategoryRow> CategoriesById()
        {
            var list = GetAllCategories();
            lock (_catalogCacheLock)
            {
                if (_categoriesById != null && ReferenceEquals(list, _categoriesCache)) return _categoriesById;
                var map = new Dictionary<int, CategoryRow>(list.Count);
                foreach (var c in list) map[c.Id] = c;
                if (ReferenceEquals(list, _categoriesCache)) _categoriesById = map;
                return map;
            }
        }
    }
}
