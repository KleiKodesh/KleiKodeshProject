using KitveiHakodeshService.Catalog;

namespace KitveiHakodeshService.Tests;

/// <summary>
/// Faithful C# port of the frontend's MANUAL catalog search pipeline — the ground truth
/// the Lucene catalog TOC index is tested against.
///
/// Ported modules (vue-frontend/src):
///   - utils/normalizeText.ts + features/book-catalog/bookCatalogSearchNormalizer.ts
///     (via CatalogTocTextRules, plus the vowel-set decomposition here)
///   - features/book-catalog/bookCatalogTree.ts        → BuildTree/AssignFullPaths
///   - features/book-catalog/bookCatalogSearch.ts +
///     bookCatalogSearchMatcher.ts                     → FilterBooksByWords
///   - features/book-catalog/bookCatalogTocKeywords.ts → TocKeywords
///   - features/book-catalog/bookCatalogSearchTocHeuristics.ts +
///     features/book-view/toc/tocSearchUtils.ts        → Split*/RunStages/StripTocTitleRoots
///   - utils/segmentSearchTree.ts                      → SegmentSearchTree
///
/// Every quirk is preserved on purpose (catalog-best tier rule, stable sorts, the
/// raw-title startsWith re-sort, negative intra-segment distances, Talmud page-suffix
/// exact matches, candidate cap 50) — the port must produce exactly what the catalog
/// page produces today.
/// </summary>
public static class ManualCatalogPipeline
{
    public const int MaxTocCandidateBooks = 50;
    public const int ScoreExact = 3, ScorePrefix = 2, ScoreNone = 0;

    // ── Data model ──────────────────────────────────────────────────────────────

    public sealed class Book
    {
        public int Id;
        public int CategoryId;
        public string Title = "";
        public string? Authors;
        public int TreeOrder;
        public string ParentPath = "";

        // Precomputed search tokens (bookCatalogSearch.ts _tokenizeBook/_tokenizeTitle)
        public string[] PathTokens = [];
        public Decomp[] PathDecomps = [];
        public string?[] PathTokensHeStripped = [];
        public HashSet<string> TitleTokens = [];
    }

    public sealed class TocRow
    {
        public int Id;
        public int? ParentId;
        public int BookId;
        public int LineId;
        public int LineIndex; // -1 = none
        public string Text = "";
    }

    public readonly record struct ManualTocItem(int BookId, int TocEntryId, string TocPath, string Text);

    // ── חסר/מלא decomposition with vowel positions (bookCatalogSearchNormalizer.ts) ──

    public readonly struct Decomp(string skeleton, HashSet<string> vowelSet)
    {
        public readonly string Skeleton = skeleton;
        public readonly HashSet<string> VowelSet = vowelSet;
    }

    public static Decomp Decompose(string word)
    {
        var skeleton = new System.Text.StringBuilder(word.Length);
        var vowels = new HashSet<string>();
        int skeletonIndex = 0;
        for (int i = 0; i < word.Length; i++)
        {
            char c = word[i];
            bool hebBefore = i > 0 && word[i - 1] is >= 'א' and <= 'ת';
            bool hebAfter = i < word.Length - 1 && word[i + 1] is >= 'א' and <= 'ת';
            if ((c == 'י' || c == 'ו') && hebBefore && hebAfter)
            {
                vowels.Add($"{skeletonIndex}:{c}");
            }
            else
            {
                skeleton.Append(c);
                skeletonIndex++;
            }
        }
        return new Decomp(skeleton.ToString(), vowels);
    }

    public static bool AreVariants(in Decomp a, in Decomp b, string aOriginal, string bOriginal)
    {
        if (aOriginal == bOriginal) return true;
        if (a.Skeleton != b.Skeleton) return false;
        var (small, large) = a.VowelSet.Count <= b.VowelSet.Count ? (a.VowelSet, b.VowelSet) : (b.VowelSet, a.VowelSet);
        foreach (var k in small)
            if (!large.Contains(k)) return false;
        return true;
    }

    // ── Query words (useBookCatalogSearch.ts toQueryWords) ──────────────────────

    public static string[] ToQueryWords(string rawQuery) =>
        CatalogTocTextRules.ApplyBookVariants(CatalogTocTextRules.Normalize(rawQuery.Trim()))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    // ── Catalog tree (bookCatalogTree.ts buildTree + assignFullPaths) ───────────

    /// <summary>Assigns TreeOrder + ParentPath exactly like the frontend: category tree
    /// in load order, custom (negative-id) entries last per level, orphaned books under
    /// a synthetic "ספרים נוספים" root appended after the real roots.</summary>
    public static void AssignTreeOrderAndPaths(
        List<(int Id, int? ParentId, string Title)> categoriesInLoadOrder, List<Book> booksInLoadOrder)
    {
        var nodes = new Dictionary<int, CatNode>();
        var nodeOrder = new List<CatNode>();
        foreach (var c in categoriesInLoadOrder)
        {
            var n = new CatNode { Id = c.Id, ParentId = c.ParentId, Title = c.Title };
            nodes[c.Id] = n;
            nodeOrder.Add(n);
        }

        var orphaned = new List<Book>();
        foreach (var b in booksInLoadOrder)
        {
            if (nodes.TryGetValue(b.CategoryId, out var n)) n.Books.Add(b);
            else orphaned.Add(b);
        }

        var roots = new List<CatNode>();
        foreach (var n in nodeOrder)
        {
            if (n.ParentId is { } pid && nodes.TryGetValue(pid, out var parent)) parent.Children.Add(n);
            else roots.Add(n);
        }

        static int CustomLast(int id) => id < 0 ? 1 : 0;
        foreach (var n in nodeOrder)
        {
            n.Children = n.Children.OrderBy(c => CustomLast(c.Id)).ToList();
            n.Books = n.Books.OrderBy(b => CustomLast(b.Id)).ToList();
        }
        roots = roots.OrderBy(r => CustomLast(r.Id)).ToList();

        if (orphaned.Count > 0)
            roots.Add(new CatNode { Id = -999999, ParentId = null, Title = "ספרים נוספים", Books = orphaned });

        int counter = 0;
        void Walk(List<CatNode> level, string parentPath)
        {
            foreach (var node in level)
            {
                string nodePath = parentPath.Length > 0 ? parentPath + " / " + node.Title : node.Title;
                foreach (var book in node.Books)
                {
                    book.TreeOrder = counter++;
                    book.ParentPath = nodePath;
                }
                Walk(node.Children, nodePath);
            }
        }
        Walk(roots, "");
    }

    private sealed class CatNode
    {
        public int Id;
        public int? ParentId;
        public string Title = "";
        public List<CatNode> Children = [];
        public List<Book> Books = [];
    }

    /// <summary>Precompute each book's search tokens (bookCatalogSearch.ts _tokenizeBook).</summary>
    public static void PrepareBookTokens(List<Book> books)
    {
        foreach (var b in books)
        {
            string fullPath = b.ParentPath.Length > 0 ? b.ParentPath + " / " + b.Title : b.Title;
            string searchString = CatalogTocTextRules.ApplyBookVariants(CatalogTocTextRules.Normalize(fullPath));
            if (!string.IsNullOrEmpty(b.Authors))
                searchString += " " + CatalogTocTextRules.ApplyBookVariants(CatalogTocTextRules.Normalize(b.Authors));

            b.PathTokens = searchString.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            b.PathDecomps = new Decomp[b.PathTokens.Length];
            b.PathTokensHeStripped = new string?[b.PathTokens.Length];
            for (int i = 0; i < b.PathTokens.Length; i++)
            {
                b.PathDecomps[i] = Decompose(b.PathTokens[i]);
                b.PathTokensHeStripped[i] = CatalogTocTextRules.StripHePrefix(b.PathTokens[i]);
            }

            b.TitleTokens = CatalogTocTextRules.ApplyBookVariants(CatalogTocTextRules.Normalize(b.Title))
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet();
        }
    }

    // ── Book matcher (bookCatalogSearch.ts filterBooksByWords / _scoreBooks) ────

    /// <summary>Per-word best tier against one book — replicates the inverted index's
    /// lookup: token & its ה-stripped entry vs word & its ה-stripped lookup (exact when
    /// entry == lookup, prefix when entry startsWith lookup), plus skeleton variants.</summary>
    private static int WordTier(Book book, string word, string? wordHeStripped, in Decomp wordDecomp)
    {
        int best = ScoreNone;
        for (int i = 0; i < book.PathTokens.Length; i++)
        {
            string tok = book.PathTokens[i];
            string? tokStripped = book.PathTokensHeStripped[i];

            if (tok == word || tokStripped == word
                || (wordHeStripped is not null && (tok == wordHeStripped || tokStripped == wordHeStripped)))
                return ScoreExact;

            if (AreVariants(book.PathDecomps[i], wordDecomp, tok, word))
                return ScoreExact;

            if (best < ScorePrefix)
            {
                if (tok.StartsWith(word, StringComparison.Ordinal)
                    || (tokStripped is not null && tokStripped.StartsWith(word, StringComparison.Ordinal))
                    || (wordHeStripped is not null
                        && (tok.StartsWith(wordHeStripped, StringComparison.Ordinal)
                            || (tokStripped is not null && tokStripped.StartsWith(wordHeStripped, StringComparison.Ordinal)))))
                    best = ScorePrefix;
            }
        }
        return best;
    }

    /// <summary>filterBooksByWords: qualify at catalog-best tier per word, rank by
    /// (total desc, titleMatchCount desc, titleTokenCount asc, treeOrder asc), then the
    /// no-exact raw-title startsWith promotion. Returns ranked books, best first.</summary>
    public static List<Book> FilterBooksByWords(List<Book> allBooks, string[] words)
    {
        if (words.Length == 0) return [];

        var wordStripped = new string?[words.Length];
        var wordDecomps = new Decomp[words.Length];
        for (int i = 0; i < words.Length; i++)
        {
            wordStripped[i] = CatalogTocTextRules.StripHePrefix(words[i]);
            wordDecomps[i] = Decompose(words[i]);
        }

        var tiers = new int[allBooks.Count, words.Length];
        var catalogBest = new int[words.Length];
        for (int wi = 0; wi < words.Length; wi++)
        {
            for (int bi = 0; bi < allBooks.Count; bi++)
            {
                int t = WordTier(allBooks[bi], words[wi], wordStripped[wi], wordDecomps[wi]);
                tiers[bi, wi] = t;
                if (t > catalogBest[wi]) catalogBest[wi] = t;
            }
            if (catalogBest[wi] == ScoreNone) return [];
        }

        var scored = new List<(Book Book, int Total, int TitleMatchCount)>();
        for (int bi = 0; bi < allBooks.Count; bi++)
        {
            int total = 0;
            bool qualified = true;
            for (int wi = 0; wi < words.Length; wi++)
            {
                if (tiers[bi, wi] < catalogBest[wi]) { qualified = false; break; }
                total += tiers[bi, wi];
            }
            if (!qualified) continue;

            var book = allBooks[bi];
            int titleMatchCount = 0;
            foreach (var w in words)
            {
                foreach (var tok in book.TitleTokens)
                    if (tok == w || tok.StartsWith(w, StringComparison.Ordinal)) { titleMatchCount++; break; }
            }
            scored.Add((book, total, titleMatchCount));
        }

        var ranked = scored
            .OrderByDescending(s => s.Total)
            .ThenByDescending(s => s.TitleMatchCount)
            .ThenBy(s => s.Book.TitleTokens.Count)
            .ThenBy(s => s.Book.TreeOrder)
            .ToList();

        bool hasExact = catalogBest.Any(b => b >= ScoreExact);
        if (!hasExact)
        {
            string rawQuery = string.Join(' ', words);
            ranked = ranked
                .OrderBy(s => s.Book.Title.StartsWith(rawQuery, StringComparison.Ordinal) ? 0 : 1)
                .ToList(); // stable — preserves the previous ordering within each group
        }

        return ranked.Select(s => s.Book).ToList();
    }

    // ── TOC keywords (bookCatalogTocKeywords.ts) ────────────────────────────────

    private static readonly string[] TocKeywordSource =
    [
        "פרק", "פסוק", "דף", "עמוד", "הלכה", "הלכות", "משנה", "סימן", "סעיף", "שער",
        "חלק", "פסקה", "פרשה", "פרשת", "מזמור", "רמז", "מצוה", "כלל", "אות",
        "תשובה", "שאלה", "אגרת", "מאמר", "דרוש",
        "הקדמה", "פתיחה", "קונטרס",
    ];

    public static readonly HashSet<string> TocKeywords = TocKeywordSource
        .SelectMany(k => CatalogTocTextRules.ApplyBookVariants(CatalogTocTextRules.Normalize(k))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        .ToHashSet();

    // ── Query splits (bookCatalogSearchTocHeuristics.ts) ────────────────────────

    public static (string[] BookWords, string[] TocWords)? SplitLongestBookPrefix(
        string[] words, Func<string[], bool> matchesAnyBook)
    {
        for (int trim = 1; trim < words.Length; trim++)
        {
            var bookWords = words[..^trim];
            if (matchesAnyBook(bookWords)) return (bookWords, words[^trim..]);
        }
        return null;
    }

    public static (string[] BookWords, string[] TocWords)? SplitAtTocKeyword(
        string[] words, Func<string[], bool> matchesAnyBook)
    {
        for (int i = 1; i < words.Length; i++)
        {
            if (!TocKeywords.Contains(words[i])) continue;
            var bookWords = words[..i];
            if (matchesAnyBook(bookWords)) return (bookWords, words[i..]);
        }
        return null;
    }

    // ── Root stripping (tocSearchUtils.ts stripTocTitleRoots) ───────────────────

    /// <summary>Book-scoped and query-independent — call once per book and cache.</summary>
    public static List<TocRow> StripTocTitleRoots(List<TocRow> rows, string bookTitle, int bookId)
    {
        if (string.IsNullOrEmpty(bookTitle) || rows.Count == 0) return rows;
        bool forceStrip = CatalogTocTextRules.ForceStripBookIds.Contains(bookId);
        var rootIds = new HashSet<int>();
        foreach (var r in rows)
            if (r.ParentId is null && (forceStrip || CatalogTocTextRules.IsTitleVariant(bookTitle, r.Text)))
                rootIds.Add(r.Id);
        if (rootIds.Count == 0) return rows;

        var result = new List<TocRow>(rows.Count);
        foreach (var r in rows)
        {
            if (rootIds.Contains(r.Id)) continue;
            result.Add(r.ParentId is { } pid && rootIds.Contains(pid)
                ? new TocRow { Id = r.Id, ParentId = null, BookId = r.BookId, LineId = r.LineId, LineIndex = r.LineIndex, Text = r.Text }
                : r);
        }
        return result;
    }

    // ── SegmentSearchTree (utils/segmentSearchTree.ts) ──────────────────────────

    public sealed class SegmentSearchTree
    {
        private readonly Dictionary<int, List<List<string>>> _segments = [];
        private readonly Dictionary<int, int?> _parentIds = [];
        public readonly Dictionary<int, string> DisplayPaths = [];

        public SegmentSearchTree(List<TocRow> nodes)
        {
            var byId = new Dictionary<int, TocRow>();
            foreach (var n in nodes) byId[n.Id] = n;

            var segCache = new Dictionary<int, List<List<string>>>();
            var displayCache = new Dictionary<int, string>();

            List<List<string>> GetSegments(int id)
            {
                if (segCache.TryGetValue(id, out var cached)) return cached;
                if (!byId.TryGetValue(id, out var node)) return [];
                var parentSegs = node.ParentId is { } pid ? GetSegments(pid) : [];
                var result = new List<List<string>>(parentSegs.Count + 1);
                result.AddRange(parentSegs);
                result.Add(CatalogTocTextRules.TokenizeSegmentText(node.Text));
                segCache[id] = result;
                return result;
            }

            string GetDisplay(int id)
            {
                if (displayCache.TryGetValue(id, out var cached)) return cached;
                if (!byId.TryGetValue(id, out var node)) return "";
                string parent = node.ParentId is { } pid ? GetDisplay(pid) : "";
                string result = parent.Length > 0 ? parent + " / " + node.Text : node.Text;
                displayCache[id] = result;
                return result;
            }

            foreach (var node in nodes)
            {
                _segments[node.Id] = GetSegments(node.Id);
                DisplayPaths[node.Id] = GetDisplay(node.Id);
                _parentIds[node.Id] = node.ParentId;
            }
        }

        private (int Score, int[] SegIndices) Score(int nodeId, string[] words, bool lastWordExact)
        {
            if (!_segments.TryGetValue(nodeId, out var segs)) return (int.MaxValue, []);

            var segIndices = new int[words.Length];
            var tokenIndices = new int[words.Length];
            int segFrom = 0;

            for (int wi = 0; wi < words.Length; wi++)
            {
                string w = words[wi];
                bool requireExact = lastWordExact && wi == words.Length - 1;
                bool found = false;

                for (int si = segFrom; si < segs.Count && !found; si++)
                {
                    var seg = segs[si];
                    for (int ti = 0; ti < seg.Count; ti++)
                    {
                        string tok = seg[ti];
                        bool isTalmudSuffix = tok.Length == w.Length + 1
                            && (tok.EndsWith('.') || tok.EndsWith(':'))
                            && tok.StartsWith(w, StringComparison.Ordinal);
                        bool isExact = tok == w || isTalmudSuffix;
                        bool matches = requireExact ? isExact : tok.StartsWith(w, StringComparison.Ordinal);
                        if (matches)
                        {
                            segIndices[wi] = si;
                            tokenIndices[wi] = ti;
                            segFrom = si;
                            found = true;
                            break;
                        }
                    }
                }

                if (!found) return (int.MaxValue, []);
            }

            const int SegmentCrossingPenalty = 10;
            int score = 0;
            for (int i = 1; i < words.Length; i++)
            {
                if (segIndices[i] == segIndices[i - 1]) score += tokenIndices[i] - tokenIndices[i - 1];
                else score += (segIndices[i] - segIndices[i - 1]) * SegmentCrossingPenalty;
            }
            return (score, segIndices);
        }

        public List<TocRow> Search(List<TocRow> nodes, string query)
        {
            var words = query.Trim().ToLowerInvariant()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return [];

            List<(TocRow Node, int Score, int[] SegIndices)> ScoreAll(bool lastWordExact)
            {
                var outList = new List<(TocRow, int, int[])>();
                foreach (var node in nodes)
                {
                    var (score, segIndices) = Score(node.Id, words, lastWordExact);
                    if (score != int.MaxValue) outList.Add((node, score, segIndices));
                }
                return outList;
            }

            var scored = ScoreAll(true);
            if (scored.Count == 0) scored = ScoreAll(false);
            if (scored.Count == 0) return [];

            scored = scored.OrderBy(s => s.Score).ToList(); // stable, like JS sort

            // Pass 2: bond detection against the best result.
            var best = scored[0];
            var bonded = new bool[words.Length - 1];
            for (int i = 0; i < words.Length - 1; i++)
                bonded[i] = best.SegIndices[i] == best.SegIndices[i + 1];

            var filtered = scored.Where(s =>
            {
                for (int i = 0; i < bonded.Length; i++)
                    if (bonded[i] && s.SegIndices[i] != s.SegIndices[i + 1]) return false;
                return true;
            }).ToList();

            // Pass 3: ancestry dedup.
            var matchedIds = filtered.Select(s => s.Node.Id).ToHashSet();
            var deduplicated = filtered.Where(s =>
            {
                int? parentId = _parentIds.GetValueOrDefault(s.Node.Id);
                while (parentId is { } pid)
                {
                    if (matchedIds.Contains(pid)) return false;
                    parentId = _parentIds.GetValueOrDefault(pid);
                }
                return true;
            }).ToList();

            return deduplicated.Select(s => s.Node).ToList();
        }
    }

    // ── Full manual search (useBookCatalogSearch.ts phase 2) ────────────────────

    public sealed class ManualSearchResult
    {
        /// <summary>Phase-1 books (empty when the fallback trigger ran).</summary>
        public List<Book> MatchedBooks = [];
        public List<ManualTocItem> TocItems = [];
        /// <summary>Which trigger produced TocItems: "keyword", "fallback", or "none".</summary>
        public string Trigger = "none";
    }

    /// <summary>Run the manual pipeline exactly as the catalog page does on a debounced
    /// query: book match first, then the keyword (additive) or fallback TOC heuristics.</summary>
    public static ManualSearchResult Search(
        string rawQuery, List<Book> allBooks, Dictionary<int, List<TocRow>> strippedRowsByBook)
    {
        var result = new ManualSearchResult();
        var words = ToQueryWords(rawQuery);
        if (words.Length == 0) return result;

        bool MatchesAnyBook(string[] ws) => FilterBooksByWords(allBooks, ws).Count > 0;

        var matchedBooks = FilterBooksByWords(allBooks, words);
        result.MatchedBooks = matchedBooks;

        (string[] BookWords, string[] TocWords)? split;
        if (matchedBooks.Count > 0)
        {
            split = SplitAtTocKeyword(words, MatchesAnyBook);
            if (split is null) return result; // pure book query — Phase 1 already rendered
            result.Trigger = "keyword";
        }
        else
        {
            split = SplitLongestBookPrefix(words, MatchesAnyBook);
            if (split is null) return result;
            result.Trigger = "fallback";
        }

        result.TocItems = RunStages(split.Value, allBooks, strippedRowsByBook);
        return result;
    }

    /// <summary>Stages 2–4 (fetch rows for capped candidates, score, build items).</summary>
    private static List<ManualTocItem> RunStages(
        (string[] BookWords, string[] TocWords) split,
        List<Book> allBooks, Dictionary<int, List<TocRow>> strippedRowsByBook)
    {
        var (bookWords, tocWords) = split;
        if (tocWords.Length == 0) return [];

        var candidateBooks = FilterBooksByWords(allBooks, bookWords).Take(MaxTocCandidateBooks).ToList();
        if (candidateBooks.Count == 0) return [];
        var bookMap = candidateBooks.ToDictionary(b => b.Id);

        // fetchTocRowsForBooks: candidates assembled in tree order, roots pre-stripped.
        var allRows = new List<TocRow>();
        foreach (var book in candidateBooks.OrderBy(b => b.TreeOrder))
            if (strippedRowsByBook.TryGetValue(book.Id, out var rows))
                allRows.AddRange(rows);

        var tree = new SegmentSearchTree(allRows);
        var matched = tree.Search(allRows, string.Join(' ', tocWords));

        var items = new List<ManualTocItem>(matched.Count);
        foreach (var node in matched)
        {
            if (!bookMap.ContainsKey(node.BookId)) continue;
            items.Add(new ManualTocItem(
                node.BookId, node.Id, tree.DisplayPaths.GetValueOrDefault(node.Id, node.Text), node.Text));
        }
        return items;
    }
}
