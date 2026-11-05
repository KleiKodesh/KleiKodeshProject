using KitveiHakodesh.Core.LocalFiles;
using KitveiHakodesh.Core.SeforimDbCatalog;

namespace KitveiHakodesh.Core.UserSpace;

/// <summary>One UserSpace root: EITHER a catalog category (by id) OR a folder on the computer
/// (absolute path). The list order of the roots is the user's order.</summary>
public sealed record UserSpaceRoot(int? CategoryId, string? Folder);

/// <summary>One UserSpace search hit (userSpaceSearch wire shape). A catalog hit has
/// BookId &gt; 0, LineIndex and TocPath (same meaning as <see cref="CatalogTocHit"/>) and an
/// empty FilePath; a file hit has BookId 0 and FilePath = the full path (lower-cased, as the
/// file index stores it — the same text locateDocuments returns).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class UserSpaceSearchHit
{
    public int BookId { get; set; }
    public int LineIndex { get; set; } = -1;
    public string TocPath { get; set; } = "";
    public string FilePath { get; set; } = "";
    /// <summary>A file hit: the Hebrew name to show instead of the file name (Torat Emet books); empty = the file name.</summary>
    public string Title { get; set; } = "";
}

/// <summary>Result of a UserSpace search (userSpaceSearch wire shape). The not-ready fields
/// are the catalog index's and mean what they mean on <see cref="CatalogTocSearchResult"/>;
/// with no category root they read ready. File hits are returned even while the catalog
/// index is not ready.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class UserSpaceSearchResult
{
    public bool Ready { get; set; } = true;
    public List<UserSpaceSearchHit> Hits { get; set; } = new();
    public bool Superseded { get; set; }
    public string? Error { get; set; }
    public bool DbMissing { get; set; }
    public int BuiltBooks { get; set; }
    public int TotalBooks { get; set; }
    public string? BuildFailure { get; set; }
    public bool RetryingAfterBuildFailure { get; set; }
    public bool Waiting { get; set; }
}

/// <summary>
/// The UserSpace search: ONE query over the user's roots — catalog categories and local
/// folders — ranked as ONE list by the catalog search's own rules, as if each file were a
/// book in the catalog.
///
/// Both halves filter at the Lucene level (category-ancestor clause on the catalog index,
/// folder-prefix clause on the file index) and run concurrently. Catalog hits come back
/// already keyed by the catalog searcher; each file hit gets the SAME keys from
/// <see cref="SeforimDbCatalogRankingKeys"/>, treated as a book-title hit: Level 0, its file
/// name (no extension) as the title, its folders below the root in the role of the category
/// path. Variants (spelling / he-prefix) need the catalog index's term dictionary, so file
/// keys use literal word matching only.
///
/// Like the catalog half, the file half is a WINDOW: the file index matches and orders every
/// file under the folders inside Lucene (on index-side approximations of these same keys, see
/// <see cref="LocalFileIndex.SearchUnderFolders"/>) and hands back only its ordered top
/// <see cref="LocalFileIndex.MaterializeCap"/>; the exact keys below re-sort that window.
///
/// Final order: the catalog's accuracy keys (with proximity for a 2+ word query), then
/// IsLiteral, Level, then the UserSpace tree order — root index in the user's list, then the
/// catalog TreeOrder (catalog hits) or the path, ordinal ignoring case (file hits).
/// </summary>
public static class UserSpaceSearch
{
    public static async Task<UserSpaceSearchResult> SearchAsync(
        string query, IReadOnlyList<UserSpaceRoot> roots,
        SeforimDbCatalogIndexCoordinator catalog, LocalFileIndexCoordinator files, CancellationToken ct)
    {
        var result = new UserSpaceSearchResult();
        var categoryIds = new List<int>();
        var folders = new List<string>();
        foreach (var root in roots)
        {
            if (root.CategoryId is { } id) categoryIds.Add(id);
            else if (!string.IsNullOrWhiteSpace(root.Folder)) folders.Add(root.Folder);
        }

        // Both halves at once; each is a single filtered index query.
        var fileTask = files.SearchUnderFoldersAsync(query, folders, ct);
        CatalogTocSearchResult? catalogResult = categoryIds.Count == 0
            ? null
            : await Task.Run(() => catalog.Search(query, categoryIds), ct);
        // A file-index failure (its first build failed) costs only the file half: the catalog
        // hits still come back.
        List<PathEntry> fileEntries;
        try { fileEntries = await fileTask; }
        catch (Exception) when (!ct.IsCancellationRequested) { fileEntries = []; }

        if (catalogResult is not null)
        {
            result.Ready = catalogResult.Ready;
            result.Superseded = catalogResult.Superseded;
            result.Error = catalogResult.Error;
            result.DbMissing = catalogResult.DbMissing;
            result.BuiltBooks = catalogResult.BuiltBooks;
            result.TotalBooks = catalogResult.TotalBooks;
            result.BuildFailure = catalogResult.BuildFailure;
            result.RetryingAfterBuildFailure = catalogResult.RetryingAfterBuildFailure;
            result.Waiting = catalogResult.Waiting;
        }
        if (result.Superseded) return result;

        result.Hits = Rank(query, roots, catalogResult?.Results, fileEntries, ct);
        return result;
    }

    /// <summary>The merge and ranking of both halves (no index access): catalog hits as the
    /// scoped catalog search returned them, file entries as the folder-scoped file index did.</summary>
    internal static List<UserSpaceSearchHit> Rank(
        string query, IReadOnlyList<UserSpaceRoot> roots,
        IReadOnlyList<CatalogTocHit>? catalogHits, IReadOnlyList<PathEntry> fileEntries, CancellationToken ct)
    {
        var hits = new List<UserSpaceSearchHit>();
        var ranked = new List<Ranked>();
        if (catalogHits is not null)
            foreach (var hit in catalogHits)
                ranked.Add(new Ranked(hit, CatalogRootIndex(hit, roots), ""));

        var tokens = SeforimDbCatalogTextNormalizer.TokenizeQuery(query);
        if (tokens.Count > 0 && fileEntries.Count > 0)
        {
            var keys = new FileKeyContext(tokens, query);
            foreach (var entry in fileEntries)
            {
                ct.ThrowIfCancellationRequested();
                string path = entry.Path ?? "";
                if (path.Length == 0) continue;
                int rootIndex = FolderRootIndex(path, roots, out string rootFolder);
                if (rootIndex < 0) continue;   // cannot happen: the index filter matched a root
                ranked.Add(new Ranked(keys.Measure(path, rootFolder), rootIndex, path));
            }
        }

        bool proximity = tokens.Sum(t => t.RankingWords.Length) >= 2;
        Comparison<CatalogTocHit> accuracy = proximity
            ? SeforimDbCatalogRankingKeys.CompareProximity
            : SeforimDbCatalogRankingKeys.CompareFieldAccuracy;

        // Stable: an index permutation, ties keep arrival order (catalog order, then files).
        var order = Enumerable.Range(0, ranked.Count).ToArray();
        Array.Sort(order, (x, y) =>
        {
            var a = ranked[x];
            var b = ranked[y];
            int c = accuracy(a.Hit, b.Hit);
            if (c != 0) return c;
            if (a.Hit.IsLiteral != b.Hit.IsLiteral) return a.Hit.IsLiteral ? -1 : 1;
            c = a.Hit.Level.CompareTo(b.Hit.Level);
            if (c != 0) return c;
            c = a.RootIndex.CompareTo(b.RootIndex);
            if (c != 0) return c;
            // Same root = same kind: a root is a category OR a folder, never both.
            c = a.FilePath.Length == 0 && b.FilePath.Length == 0
                ? a.Hit.TreeOrder.CompareTo(b.Hit.TreeOrder)
                : string.Compare(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : x.CompareTo(y);
        });

        foreach (int i in order)
        {
            var r = ranked[i];
            hits.Add(r.FilePath.Length == 0
                ? new UserSpaceSearchHit { BookId = r.Hit.BookId, LineIndex = r.Hit.LineIndex, TocPath = r.Hit.TocPath }
                : new UserSpaceSearchHit { BookId = 0, LineIndex = -1, FilePath = r.FilePath, Title = LocalFiles.ToratEmet.ToratEmetNames.FileTitle(r.FilePath) ?? "" });
        }
        return hits;
    }

    private readonly record struct Ranked(CatalogTocHit Hit, int RootIndex, string FilePath);

    /// <summary>The first root, in the user's order, that is a category above the hit's book.</summary>
    private static int CatalogRootIndex(CatalogTocHit hit, IReadOnlyList<UserSpaceRoot> roots)
    {
        for (int i = 0; i < roots.Count; i++)
            if (roots[i].CategoryId is { } id && Array.IndexOf(hit.CategoryAncestors, id) >= 0) return i;
        return int.MaxValue;
    }

    /// <summary>The first folder root, in the user's order, holding <paramref name="path"/>
    /// (the same prefix the index filter used), or -1.</summary>
    private static int FolderRootIndex(string path, IReadOnlyList<UserSpaceRoot> roots, out string rootPrefix)
    {
        for (int i = 0; i < roots.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(roots[i].Folder)) continue;
            string prefix = LocalFileIndex.FolderPrefix(roots[i].Folder!);
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                rootPrefix = prefix;
                return i;
            }
        }
        rootPrefix = "";
        return -1;
    }

    /// <summary>Per-search query data and the measurement that gives a file the catalog's
    /// ranking keys. Every key comes from <see cref="SeforimDbCatalogRankingKeys"/>; nothing
    /// here re-implements a rule, it only says what plays title, path and category for a file.</summary>
    private sealed class FileKeyContext
    {
        private readonly List<SeforimDbCatalogTextNormalizer.QueryToken> _tokens;
        private readonly HashSet<string> _queryWords;
        private readonly Dictionary<string, int> _queryCounts;
        private readonly int _queryWordCount;
        private readonly List<string> _orderWords = [];
        private readonly HashSet<string>? _queryQuotedKeys;
        private readonly SeforimDbCatalogRankingKeys.PathScratch? _scratch;

        public FileKeyContext(List<SeforimDbCatalogTextNormalizer.QueryToken> tokens, string query)
        {
            _tokens = tokens;
            // No VariantLookup: it reads the catalog index's term dictionary, which knows
            // nothing of file names — file keys match words literally.
            _queryWords = SeforimDbCatalogRankingKeys.QueryWordSet(tokens, null);
            _queryCounts = SeforimDbCatalogRankingKeys.QueryWordCounts(tokens);
            foreach (var n in _queryCounts.Values) _queryWordCount += n;
            foreach (var t in tokens) _orderWords.AddRange(t.RankingWords);
            _queryQuotedKeys = SeforimDbCatalogTextNormalizer.GetQuotedAbbreviationKeys(query);
            if (_orderWords.Count >= 2) _scratch = new SeforimDbCatalogRankingKeys.PathScratch(_orderWords.Count);
        }

        /// <summary>A file as a book-title hit: Level 0, title = file name without extension,
        /// the folders from the root's own folder down to the file as its category path - the
        /// root's name counts like a catalog root category's name: searching by folder is the point.</summary>
        public CatalogTocHit Measure(string path, string rootPrefix)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            var titleTokens = LocalFileNameWords.NameTokens(path);

            int lastSep = path.LastIndexOf('\\');
            // rootPrefix is "c:\...\root\": its own name starts after the separator before it.
            int rootStart = rootPrefix.Length >= 2 ? rootPrefix.LastIndexOf('\\', rootPrefix.Length - 2) + 1 : 0;
            string relativeFolders = lastSep > rootStart
                ? path.Substring(rootStart, lastSep - rootStart)
                : "";
            var folderTokens = LocalFileNameWords.FolderTokens(relativeFolders);

            var quotedKeys = _queryQuotedKeys is null ? null : SeforimDbCatalogTextNormalizer.GetQuotedAbbreviationKeys(name);
            bool titleCovered = SeforimDbCatalogRankingKeys.IsCovered(titleTokens, _queryWords);
            var hit = new CatalogTocHit
            {
                Level = 0,
                PathTokens = titleTokens,
                // Literal = every query word is a whole word of the name or of the folders below
                // the root; a file matched only by substrings is a non-literal match. The
                // "author" slot of the shared test carries the folders here.
                IsLiteral = SeforimDbCatalogRankingKeys.EveryTokenPresent(_tokens, titleTokens, folderTokens, null),
                MatchesTitleOrAuthor = SeforimDbCatalogRankingKeys.EveryTokenPresent(_tokens, titleTokens, null, null),
                TitleCovered = titleCovered,
                TitleDistinctWords = SeforimDbCatalogRankingKeys.DistinctFound(titleTokens, _queryWords),
                TitleLengthGap = Math.Abs(titleTokens.Count - _queryWordCount),
                LeafCovered = titleCovered,   // level 0: the leaf IS the title
                WordsMatched = SeforimDbCatalogRankingKeys.CountMatched(titleTokens, _queryCounts, null),
                QuotedFormMatch = quotedKeys != null && _queryQuotedKeys!.Overlaps(quotedKeys),
            };
            _scratch?.Measure(titleTokens, _orderWords, hit);
            return hit;
        }
    }
}
