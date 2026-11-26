using System;
using System.Collections.Generic;
using System.Linq;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// The commentary panel's structure for the tapped line(s): which books link to the lines
    /// (forward, pointing back, and the base texts a reverse lookup finds), each book's title,
    /// section and tree order, and the ordered row groups - SOURCE, TARGUM, COMMENTARY by
    /// section, EIN_MISHPAT, OTHER by section, REFERENCE. The page only shows the result; it
    /// holds no catalog and none of these rules.
    ///
    /// THINKING only (see Core/CLAUDE.md 5a): the data comes from ONE call on ONE connection
    /// (<see cref="ReadCommentaryListData"/>), the rules below are plain functions over it.
    ///
    /// A book is one group PER connection type it is linked with, each with its own line subset
    /// (so a book linked as both a commentary and a reference sits in both sections). That is
    /// what the page's buildCommentaryGroupsFromCombined used to do, rule for rule; the
    /// related-books list and the filter tree (BuildStaticGroups) shelve a book ONCE, by its
    /// primary type - a different question.
    /// </summary>
    public sealed partial class SeforimDbQueries
    {
        /// <summary>The panel's groups for a set of lines (<paramref name="lineIds"/>) or, when that is
        /// empty, for the lines of <paramref name="bookId"/> with lineIndex in
        /// [<paramref name="fromIndex"/>, <paramref name="toIndex"/>) - a section sent as its bounds.
        /// <paramref name="bookId"/> is the book the lines belong to (the catalogue's commentary
        /// verdict is skipped when it is 0).</summary>
        public CommentaryList GetCommentaryList(List<int>? lineIds, int bookId, int fromIndex, int toIndex)
        {
            EnsureBookTreeOrder();   // before the read: it may write to the library file
            var data = ReadCommentaryListData(lineIds, bookId, fromIndex, toIndex);
            // Nothing asked, or nothing linked to the lines: no list.
            if (data == null
                || (data.LinkGroups.Rows.Count == 0 && data.LinkGroups.Incoming.Count == 0 && data.ReverseRows.Count == 0))
                return new CommentaryList();

            var facts = new CommentaryBookFacts(data.Facts);
            var entries = BuildSourceEntries(data.ReverseRows, facts);
            entries.AddRange(BuildLinkedEntries(data.LinkGroups, data.TypeNames, facts));
            return new CommentaryList { Groups = BuildCommentaryGroups(entries) };
        }

        private sealed class ListEntry
        {
            public int BookId;
            public string Title = "";
            public List<string> ConnectionTypes = new();
            public List<(int LineId, int LineIndex)> Lines = new();
            public string Category = "";
            public int TreeOrder;
            public string PrimaryType = "";
        }

        /// <summary>The SOURCE entries: the base-text lines the reverse lookup found, per book in the
        /// order first seen.</summary>
        private static List<ListEntry> BuildSourceEntries(List<ReverseLineRefRow> rows, CommentaryBookFacts facts)
        {
            var byBook = new List<(int BookId, List<int> LineIds)>();
            var at = new Dictionary<int, int>();
            var lineIndexById = new Dictionary<int, int>();
            var seen = new HashSet<(int, int)>();
            foreach (var row in rows)
            {
                if (!at.TryGetValue(row.SourceBookId, out int slot))
                {
                    at[row.SourceBookId] = slot = byBook.Count;
                    byBook.Add((row.SourceBookId, new List<int>()));
                }
                if (seen.Add((row.SourceBookId, row.SourceLineId))) byBook[slot].LineIds.Add(row.SourceLineId);
                lineIndexById[row.SourceLineId] = row.LineIndex;
            }
            return byBook.Select(b => NewEntry(b.BookId, new List<string> { "SOURCE" }, "SOURCE", b.LineIds, lineIndexById, facts)).ToList();
        }

        /// <summary>The entries of the forward links and of the links pointing back, one per
        /// (book, canonical connection type), in the order first seen.</summary>
        private static List<ListEntry> BuildLinkedEntries(CommentaryLinkGroups linkGroups, Dictionary<int, string> typeNames, CommentaryBookFacts facts)
        {
            // A book the catalogue files as a commentary on this one is shown as one whatever the
            // link type says (Pilpula Charifta on the Rosh is type OTHER), in either direction.
            var catalogCommentary = new HashSet<int>(linkGroups.CatalogCommentaryBookIds);
            string CanonicalTypeFor(int bookId, int typeId) =>
                catalogCommentary.Contains(bookId)
                    ? "COMMENTARY"
                    : CanonicalConnectionType(typeNames.TryGetValue(typeId, out var name) ? name : typeId.ToString());

            var byBookConnection = new List<(int BookId, string Type, List<int> LineIds, HashSet<int> Seen)>();
            var at = new Dictionary<(int, string), int>();
            var lineIndexById = new Dictionary<int, int>();
            var typesByBook = new Dictionary<int, List<string>>();

            void Add(int bookId, string type, int[] lineIds, int[] lineIndexes)
            {
                // Several raw type ids can normalize to one canonical name, and a book can be linked
                // from both ends, so an entry may collect the lines of more than one group.
                if (!at.TryGetValue((bookId, type), out int slot))
                {
                    at[(bookId, type)] = slot = byBookConnection.Count;
                    byBookConnection.Add((bookId, type, new List<int>(), new HashSet<int>()));
                    if (!typesByBook.TryGetValue(bookId, out var types)) typesByBook[bookId] = types = new List<string>();
                    if (!types.Contains(type)) types.Add(type);
                }
                var (_, _, ids, seen) = byBookConnection[slot];
                for (int i = 0; i < lineIds.Length; i++)
                {
                    if (seen.Add(lineIds[i])) ids.Add(lineIds[i]);
                    lineIndexById[lineIds[i]] = i < lineIndexes.Length ? lineIndexes[i] : 0;
                }
            }

            foreach (var row in linkGroups.Rows)
            {
                string type = CanonicalTypeFor(row.TargetBookId, row.ConnectionTypeId);
                // SOURCE comes from the reverse lookup: its forward-direction links are unreliable.
                if (type == "SOURCE") continue;
                Add(row.TargetBookId, type, row.LineIds, row.LineIndexes);
            }
            foreach (var row in linkGroups.Incoming)
            {
                string type = CanonicalTypeFor(row.SourceBookId, row.ConnectionTypeId);
                if (type == "SOURCE") continue;
                Add(row.SourceBookId, type, row.LineIds, row.LineIndexes);
            }

            // Insertion order: it breaks ties in the sorts below.
            return byBookConnection
                .Select(e => NewEntry(e.BookId, typesByBook[e.BookId], e.Type, e.LineIds, lineIndexById, facts))
                .ToList();
        }

        private static ListEntry NewEntry(int bookId, List<string> connectionTypes, string primaryType, List<int> lineIds,
            Dictionary<int, int> lineIndexById, CommentaryBookFacts facts)
        {
            var book = facts.Get(bookId);
            return new ListEntry
            {
                BookId = bookId,
                Title = !string.IsNullOrEmpty(book?.Title) ? CommentaryDisplayTitle(book!.Title) : bookId.ToString(),
                ConnectionTypes = connectionTypes,
                Lines = lineIds.Select(id => (id, lineIndexById.TryGetValue(id, out int index) ? index : 0))
                    .OrderBy(line => line.Item2).ToList(),   // stable: equal indexes keep their order
                Category = book?.Category ?? SeforimDbBookCommentaryCategory.OtherCategory,
                TreeOrder = book?.TreeOrder ?? 999999,
                PrimaryType = primaryType,
            };
        }

        /// <summary>buildCommentaryGroupsFromEntries: groups by primary type, in the panel's order.</summary>
        private static List<CommentaryListGroup> BuildCommentaryGroups(List<ListEntry> entries)
        {
            var result = new List<CommentaryListGroup>();

            CommentaryListGroup Group(ListEntry e, string path, string label, string? subLabel, string category) => new()
            {
                BookId = e.BookId,
                BookTitle = e.Title,
                Path = path,
                ConnectionTypes = e.ConnectionTypes.ToArray(),
                LineIds = e.Lines.Select(l => l.LineId).ToArray(),
                LineIndexes = e.Lines.Select(l => l.LineIndex).ToArray(),
                Category = category,
                SectionLabel = label,
                SubSectionLabel = subLabel,
            };

            void AddFlat(string type)
            {
                string label = SectionLabelFor(type);
                foreach (var e in entries.Where(e => e.PrimaryType == type).OrderBy(e => e.TreeOrder))
                    result.Add(Group(e, $"{e.Title} · {label}", label, null, e.Category));
            }

            void AddMergedByCategory(string type)
            {
                string label = SectionLabelFor(type);
                var byCategory = new List<(string Category, List<ListEntry> Items)>();
                foreach (var e in entries.Where(e => e.PrimaryType == type))
                {
                    int at = byCategory.FindIndex(c => c.Category == e.Category);
                    if (at < 0) byCategory.Add((e.Category, new List<ListEntry> { e }));
                    else byCategory[at].Items.Add(e);
                }
                foreach (var (category, items) in byCategory.OrderBy(c => SeforimDbBookCommentaryCategory.Rank(c.Category)))
                    foreach (var e in items.OrderBy(e => e.TreeOrder))
                        result.Add(Group(e, $"{e.Title} · {label} · {category}", label, category, category));
            }

            AddFlat("SOURCE");
            AddFlat("TARGUM");
            AddMergedByCategory("COMMENTARY");
            AddFlat("EIN_MISHPAT");
            AddMergedByCategory("OTHER");
            AddFlat("REFERENCE");
            return result;
        }
    }
}
