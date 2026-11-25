using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using MessagePack;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>A book named by title, optionally inside a category path ("A / B", root first).</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class BookNameQuery
    {
        public string Title { get; set; } = "";
        public string? Section { get; set; }
    }

    /// <summary>
    /// Finds library books BY NAME the way the daily-learning cycles name them: titles folded
    /// (no niqqud/geresh/punctuation; vav/yod dropped after each word's first letter, so plene and
    /// defective spellings meet), optionally inside a category path. The exact spelling beats a
    /// folded-only match, then the book nearest the section root, then library (tree) order.
    /// The folded-title index is built once per loaded book list and dropped with it.
    /// </summary>
    public sealed partial class SeforimDbQueries
    {
        private sealed record NamedBook(int Id, string ExactTitle, string[] PathSegments);
        private Dictionary<string, List<NamedBook>>? _booksByFoldedTitle;
        private List<BookRow>? _booksByFoldedTitleSource;

        /// <summary>For each group of names (tried in order), the first book found; 0 = none in the library.</summary>
        public List<int> FindBooksByName(List<List<BookNameQuery>> groups)
        {
            var index = FoldedTitleIndex();
            var result = new List<int>(groups.Count);
            foreach (var names in groups)
            {
                int found = 0;
                foreach (var name in names)
                    if ((found = PickBook(index, name)) != 0) break;
                result.Add(found);
            }
            return result;
        }

        private static int PickBook(Dictionary<string, List<NamedBook>> index, BookNameQuery name)
        {
            if (!index.TryGetValue(FoldTitle(name.Title), out var candidates)) return 0;
            string[] section = string.IsNullOrEmpty(name.Section) ? [] : FoldPath(name.Section);
            string exact = NormalizeHeading(name.Title);
            int bestId = 0, bestRank = int.MaxValue;
            foreach (var book in candidates)
            {
                bool inSection = true;
                for (int i = 0; i < section.Length && inSection; i++)
                    inSection = i < book.PathSegments.Length && book.PathSegments[i] == section[i];
                if (!inSection) continue;
                int rank = (book.ExactTitle == exact ? 0 : 1000) + book.PathSegments.Length;
                if (rank < bestRank) { bestRank = rank; bestId = book.Id; }
            }
            return bestId;
        }

        private Dictionary<string, List<NamedBook>> FoldedTitleIndex()
        {
            var books = GetAllBooks();
            lock (_catalogCacheLock)
                if (_booksByFoldedTitle != null && ReferenceEquals(_booksByFoldedTitleSource, books)) return _booksByFoldedTitle;

            // Category id -> its folded path, root first.
            var categories = new Dictionary<int, CategoryRow>();
            foreach (var c in GetAllCategories()) categories[c.Id] = c;
            var paths = new Dictionary<int, string[]>();
            string[] PathOf(int categoryId)
            {
                if (paths.TryGetValue(categoryId, out var known)) return known;
                var segments = new List<string>();
                var seen = new HashSet<int>();
                for (int? id = categoryId; id is int cid && seen.Add(cid) && categories.TryGetValue(cid, out var row); id = row.ParentId)
                    segments.Insert(0, FoldTitle(row.Title));
                return paths[categoryId] = segments.ToArray();
            }

            var index = new Dictionary<string, List<NamedBook>>(System.StringComparer.Ordinal);
            foreach (var book in books) // tree order: the first of equal ranks wins
            {
                string key = FoldTitle(book.Title);
                if (!index.TryGetValue(key, out var list)) index[key] = list = [];
                list.Add(new NamedBook(book.Id, NormalizeHeading(book.Title), PathOf(book.CategoryId)));
            }
            lock (_catalogCacheLock) { _booksByFoldedTitle = index; _booksByFoldedTitleSource = books; }
            return index;
        }

        private static readonly Regex Niqqud = new("[֑-ֽֿ-ׇ]", RegexOptions.CultureInvariant);
        private static readonly Regex Geresh = new("[׳״'\"]", RegexOptions.CultureInvariant);
        private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant);
        private static readonly Regex NonWord = new("[^א-תa-zA-Z0-9]+", RegexOptions.CultureInvariant);

        /// <summary>No niqqud or geresh, maqaf as a space, single spaces.</summary>
        private static string NormalizeHeading(string text) =>
            Spaces.Replace(Geresh.Replace(Niqqud.Replace(text.Replace('־', ' '), ""), ""), " ").Trim();

        private static string FoldTitle(string text)
        {
            var words = NonWord.Replace(NormalizeHeading(text), " ").Trim().Split(' ');
            var sb = new StringBuilder(text.Length);
            foreach (var word in words)
            {
                if (sb.Length > 0) sb.Append(' ');
                if (word.Length == 0) continue;
                sb.Append(word[0]);
                for (int i = 1; i < word.Length; i++)
                    if (word[i] != 'ו' && word[i] != 'י') sb.Append(word[i]);
            }
            return sb.ToString();
        }

        private static string[] FoldPath(string path) => System.Array.ConvertAll(path.Split(" / "), FoldTitle);
    }
}
