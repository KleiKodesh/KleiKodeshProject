using System;
using System.Collections.Generic;
using System.Linq;

namespace KitveiHakodesh.Core.SeforimDb
{
    public sealed partial class SeforimDbQueries
    {
        /// <summary>
        /// What a commentary list (the panel, related books, the commentary filter tree) knows about
        /// each book in it: its title, the section the folders put it in, its tree order.
        ///
        /// THINKING only (see Core/CLAUDE.md 5a): it turns the plain data the calls read
        /// (<see cref="BookFactsData"/>) into those three facts and remembers each folder's section
        /// for the life of one list. It holds no SQL, connection or reader; its owner hands it the
        /// data, or a function that reads the data for a set of books.
        /// </summary>
        private sealed class CommentaryBookFacts
        {
            public sealed record Facts(string Title, string Category, int TreeOrder);

            private readonly Func<List<int>, BookFactsData>? _read;
            private readonly Dictionary<int, Facts?> _byBook = new();
            private readonly Dictionary<int, string> _sectionByCategory = new();

            /// <summary>Facts read on demand: <see cref="Load"/> asks <paramref name="read"/> for the books not known yet.</summary>
            public CommentaryBookFacts(Func<List<int>, BookFactsData> read) => _read = read;

            /// <summary>Facts of data that was already read.</summary>
            public CommentaryBookFacts(BookFactsData read) => Ingest(read, read.Books.Keys);

            /// <summary>The book's facts, or null for a book the library does not have (or not loaded).</summary>
            public Facts? Get(int bookId) => _byBook.TryGetValue(bookId, out var facts) ? facts : null;

            /// <summary>Makes sure the books of <paramref name="bookIds"/> are known, reading the missing ones together.</summary>
            public void Load(IEnumerable<int> bookIds)
            {
                var missing = bookIds.Where(id => !_byBook.ContainsKey(id)).Distinct().ToList();
                if (missing.Count == 0) return;
                Ingest(_read!(missing), missing);
            }

            private void Ingest(BookFactsData data, IEnumerable<int> askedFor)
            {
                // A section is worked out once per folder; a folder with no chain (a row missing) is "other".
                foreach (var (_, book) in data.Books)
                    if (book.CategoryId is int categoryId && !_sectionByCategory.ContainsKey(categoryId))
                        _sectionByCategory[categoryId] = SeforimDbBookCommentaryCategory.Resolve(
                            data.CategoryChains.TryGetValue(categoryId, out var chain) ? chain : new List<string>());

                foreach (int id in askedFor)
                {
                    if (!data.Books.TryGetValue(id, out var book))
                    {
                        _byBook[id] = null;
                        continue;
                    }
                    string section = book.CategoryId is int categoryId && _sectionByCategory.TryGetValue(categoryId, out var known)
                        ? known
                        : SeforimDbBookCommentaryCategory.OtherCategory;
                    // A book with no stored order (the table is unavailable) sorts after those that have one.
                    _byBook[id] = new Facts(book.Title, section, data.TreeOrders.TryGetValue(id, out int order) ? order : int.MaxValue);
                }
            }
        }
    }
}
