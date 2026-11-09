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
    /// <summary>A file hit: the folder line with the Hebrew folder names (Torat Emet under an install's Books); empty = the folder as it is.</summary>
    public string DisplayFolder { get; set; } = "";
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
/// keyed by the catalog searcher; each file hit comes back keyed by the file index with the
/// SAME keys (<see cref="RankKeys"/>), measured inside its collector as a book-title hit:
/// Level 0, its file name (no extension) as the title, the folders on its path in the role of
/// the category path. Variants (spelling / he-prefix) need the catalog index's term
/// dictionary, so file keys use literal word matching only.
///
/// Like the catalog half, the file half is a WINDOW: the file index matches and orders every
/// file under the folders inside Lucene (see <see cref="LocalFileIndex.SearchUnderFolders"/>)
/// and hands back only its ordered top <see cref="LocalFileIndex.MaterializeCap"/>. Nothing
/// is measured here: this is a merge of two ranked lists on their keys.
///
/// Final order: the catalog's ranking keys (with proximity for a 2+ word query), then Level,
/// then the UserSpace tree order — root index in the user's list, then the catalog TreeOrder
/// (catalog hits) or the path, ordinal ignoring case (file hits).
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

    /// <summary>The merge of both halves (no index access, no measuring): catalog hits as the
    /// scoped catalog search returned them, file entries as the folder-scoped file index did -
    /// each already carrying its ranking keys, read from its own index while collecting.</summary>
    internal static List<UserSpaceSearchHit> Rank(
        string query, IReadOnlyList<UserSpaceRoot> roots,
        IReadOnlyList<CatalogTocHit>? catalogHits, IReadOnlyList<PathEntry> fileEntries, CancellationToken ct)
    {
        var hits = new List<UserSpaceSearchHit>();
        var ranked = new List<Ranked>();
        if (catalogHits is not null)
            foreach (var hit in catalogHits)
                ranked.Add(new Ranked(hit.Keys, hit.Level, hit.TreeOrder, CatalogRootIndex(hit, roots), hit, ""));

        foreach (var entry in fileEntries)
        {
            ct.ThrowIfCancellationRequested();
            string path = entry.Path ?? "";
            if (path.Length == 0) continue;
            int rootIndex = FolderRootIndex(path, roots);
            if (rootIndex < 0) continue;   // cannot happen: the index filter matched a root
            // A file is a book-title hit: level 0.
            ranked.Add(new Ranked(entry.Keys, 0, 0, rootIndex, null, path));
        }

        var tokens = SeforimDbCatalogTextNormalizer.TokenizeQuery(query);
        bool proximity = tokens.Sum(t => t.RankingWords.Length) >= 2;

        // Stable: an index permutation, ties keep arrival order (catalog order, then files).
        var order = Enumerable.Range(0, ranked.Count).ToArray();
        Array.Sort(order, (x, y) =>
        {
            var a = ranked[x];
            var b = ranked[y];
            int c = SeforimDbCatalogRankingKeys.Compare(a.Keys, b.Keys, proximity);
            if (c != 0) return c;
            c = a.Level.CompareTo(b.Level);   // the keys rank title-or-not only; depth is catalog order
            if (c != 0) return c;
            c = a.RootIndex.CompareTo(b.RootIndex);
            if (c != 0) return c;
            // Same root = same kind: a root is a category OR a folder, never both.
            c = a.FilePath.Length == 0 && b.FilePath.Length == 0
                ? a.TreeOrder.CompareTo(b.TreeOrder)
                : string.Compare(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : x.CompareTo(y);
        });

        foreach (int i in order)
        {
            var r = ranked[i];
            hits.Add(r.Hit is not null
                ? new UserSpaceSearchHit { BookId = r.Hit.BookId, LineIndex = r.Hit.LineIndex, TocPath = r.Hit.TocPath }
                : new UserSpaceSearchHit { BookId = 0, LineIndex = -1, FilePath = r.FilePath, Title = LocalFiles.ToratEmet.ToratEmetNames.FileTitle(r.FilePath) ?? "", DisplayFolder = LocalFiles.ToratEmet.ToratEmetNames.DisplayFolder(System.IO.Path.GetDirectoryName(r.FilePath)) });
        }
        return hits;
    }

    private readonly record struct Ranked(RankKeys Keys, int Level, long TreeOrder, int RootIndex, CatalogTocHit? Hit, string FilePath);

    /// <summary>The first root, in the user's order, that is a category above the hit's book.</summary>
    private static int CatalogRootIndex(CatalogTocHit hit, IReadOnlyList<UserSpaceRoot> roots)
    {
        for (int i = 0; i < roots.Count; i++)
            if (roots[i].CategoryId is { } id && Array.IndexOf(hit.CategoryAncestors, id) >= 0) return i;
        return int.MaxValue;
    }

    /// <summary>The first folder root, in the user's order, holding <paramref name="path"/>
    /// (the same prefix the index filter used), or -1.</summary>
    private static int FolderRootIndex(string path, IReadOnlyList<UserSpaceRoot> roots)
    {
        for (int i = 0; i < roots.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(roots[i].Folder)) continue;
            if (path.StartsWith(LocalFileIndex.FolderPrefix(roots[i].Folder!), StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}
