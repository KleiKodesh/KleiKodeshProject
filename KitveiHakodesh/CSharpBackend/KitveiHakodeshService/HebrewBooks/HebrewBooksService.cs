using KitveiHakodeshService.Ipc;
using CoreHebrewBooks = KitveiHakodesh.Core.HebrewBooks;

namespace KitveiHakodeshService.HebrewBooks;

/// <summary>
/// Transport wrapper over Core's HebrewBooks pair — <see cref="CoreHebrewBooks.HebrewBooksCatalogDbQueries"/>
/// for catalog search and <see cref="CoreHebrewBooks.HebrewBooksDownloader"/> for PDF
/// acquisition. Both the catalog SQL and the download/cache policy are Core's; this class
/// finds the catalog once, keeps the cache beside the service exe, and translates Core's
/// typed failures into the wire shapes the dispatcher already speaks.
///
/// DOWNLOAD happens entirely here via <see cref="HttpClient"/> — never through the hosted
/// app's WebView2 download interception, which stays in KitveiHakodeshLib.
/// </summary>
public sealed class HebrewBooksService
{
    /// <summary>Portable cache beside the exe (like WordConversionService's convert-cache),
    /// NOT %LOCALAPPDATA%. Passed to Core explicitly: left to itself Core would probe for the
    /// first writable app root, and the service deliberately keeps its data next to its
    /// binary.</summary>
    private static readonly string CacheDir =
        Path.Combine(AppContext.BaseDirectory, "hb-cache");

    public const int DefaultLimit = CoreHebrewBooks.HebrewBooksCatalogDbQueries.DefaultSearchLimit;

    private readonly ILogger<HebrewBooksService> _logger;
    private readonly CoreHebrewBooks.HebrewBooksCatalogDbQueries? _catalog;
    private readonly CoreHebrewBooks.HebrewBooksDownloader _downloader;

    public HebrewBooksService(ILogger<HebrewBooksService> logger, HttpClient http)
    {
        _logger = logger;
        _downloader = new CoreHebrewBooks.HebrewBooksDownloader(http, CacheDir);

        string? path = CoreHebrewBooks.HebrewBooksCatalogDbQueries.Locate();
        if (path is null)
            logger.LogWarning("HebrewBooks catalog not found — catalog search will return empty.");
        else
            _catalog = new CoreHebrewBooks.HebrewBooksCatalogDbQueries(path);
    }

    /// <summary>The map last located and the seforim.db it belongs to. Re-located only when
    /// the library switches, so a book open costs one indexed lookup, not a shape probe.</summary>
    private sealed record EditionsMap(string? SeforimDbPath, CoreHebrewBooks.OtzariaHebrewBooksCatalogDbQueries? Map);

    // A reference swapped whole, so concurrent RPCs never see one library's path with another's map.
    private volatile EditionsMap _editionsMap = new(null, null);

    /// <summary>
    /// The HebrewBooks scans Otzaria's map links to this seforim.db book, best first. Empty
    /// when there is no map beside the library in use, or that library is Zayit's — its book
    /// ids are not the ones the map describes.
    /// </summary>
    public List<HebrewBooksEdition> EditionsOf(int bookId)
    {
        try
        {
            string? seforimDbPath = global::KitveiHakodesh.Core.Settings.SeforimDbPathResolver.Resolve().Path;
            var cached = _editionsMap;
            if (!string.Equals(cached.SeforimDbPath, seforimDbPath, StringComparison.OrdinalIgnoreCase))
            {
                cached = new EditionsMap(seforimDbPath, CoreHebrewBooks.OtzariaHebrewBooksCatalogDbQueries.LocateBeside(seforimDbPath));
                _editionsMap = cached;
            }
            return cached.Map?.EditionsOf(bookId) ?? new List<HebrewBooksEdition>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "getHebrewBooksEditions failed");
            return new List<HebrewBooksEdition>();
        }
    }

    /// <summary>Outcome of an acquire: the resolved on-disk PDF path, or a reason it failed.
    /// Exactly one of Path / NotFound / Error is meaningful.</summary>
    /// <param name="Detail">Full failure detail (exception ToString, response facts) for the console only.</param>
    public readonly record struct HbAcquireResult(string? Path, bool NotFound, string? Error, string? Detail = null);

    /// <summary>Bytes received / total for an in-flight download of <paramref name="bookId"/>,
    /// or null when no download is active for it.</summary>
    public (long received, long total)? GetProgress(string bookId)
    {
        var progress = _downloader.Progress(bookId);
        return progress is null ? null : (progress.Received, progress.Total);
    }

    /// <summary>Abort an in-flight download of <paramref name="bookId"/> if one is running.
    /// Returns true when a download was actually cancelled. Safe to call when nothing is running.</summary>
    public bool Cancel(string bookId)
    {
        bool cancelled = _downloader.Cancel(bookId);
        if (cancelled)
            _logger.LogInformation("HebrewBooks {Id}: user cancel — aborting in-flight download", bookId);
        return cancelled;
    }

    public HbSearchResult Search(string query, string? localFolder, int limit)
    {
        if (_catalog is null) return new HbSearchResult();

        try
        {
            return new HbSearchResult { Books = _catalog.Search(query, localFolder, limit) };
        }
        catch (Exception ex)
        {
            // Core throws on a real database failure (rule 4). The orchestrator is the layer
            // that decides the user sees an empty result list rather than a broken pane.
            _logger.LogError(ex, "hbSearch failed");
            return new HbSearchResult();
        }
    }

    public async Task<HbAcquireResult> AcquireAsync(string bookId, string? localFolder, bool allowDownload, CancellationToken ct)
    {
        var result = await _downloader.AcquireAsync(bookId, localFolder, allowDownload, ct).ConfigureAwait(false);
        if (result.Succeeded) return new HbAcquireResult(result.Path, false, null);

        // Core answers with a typed failure; the dispatcher's wire shape predates it and still
        // distinguishes the same cases by string. Mapped here rather than at every call site.
        var mapped = result.Failure switch
        {
            // Upstream has no such book. NotFound, so the caller says so rather than showing
            // an error the user can do nothing about.
            CoreHebrewBooks.HebrewBookAcquireFailure.NotFoundUpstream => new HbAcquireResult(null, true, null),

            // Restore-only: not on disk and downloading was not allowed. Neither a miss nor an
            // error — the caller re-runs the download path.
            CoreHebrewBooks.HebrewBookAcquireFailure.NotCachedAndDownloadDisallowed => new HbAcquireResult(null, false, null),

            CoreHebrewBooks.HebrewBookAcquireFailure.Cancelled => new HbAcquireResult(null, false, "cancelled"),
            CoreHebrewBooks.HebrewBookAcquireFailure.Network => new HbAcquireResult(null, false, "network error"),

            // A malformed or absent id never reached the network, so it is OUR error, not
            // "the book does not exist" — the dispatcher checks NotFound first and would
            // otherwise tell the user a book they never named is missing.
            CoreHebrewBooks.HebrewBookAcquireFailure.InvalidBookId => new HbAcquireResult(null, false, "invalid book id"),

            // Core carries the bare status number; the sentence around it is the host's wording.
            // The frontend prefixes its own "the download failed:" so this is the reason only.
            CoreHebrewBooks.HebrewBookAcquireFailure.HttpStatus =>
                new HbAcquireResult(null, false, $"HTTP {result.HttpStatusCode}"),

            // Banner gets the exception's first line ("Type: message"); the console gets all of it.
            _ => new HbAcquireResult(null, false, result.Detail?.Split('\n')[0].Trim() ?? "download failed"),
        };
        return mapped with { Detail = $"{result.Failure}: {result.Detail}" };
    }

    public List<string> CheckLocalFiles(IEnumerable<string> bookIds, string? localFolder) =>
        _downloader.WhichAreDownloaded(bookIds, localFolder);

    public (bool ok, bool notFound, bool noRecycleBin, string? error) DeleteLocalFile(string bookId, string? localFolder, bool toRecycleBin = false)
    {
        var outcome = _downloader.DeleteDownloaded(bookId, localFolder, toRecycleBin);
        return outcome switch
        {
            CoreHebrewBooks.HebrewBookDeleteOutcome.Deleted => (true, false, false, null),

            // The only true "nothing to delete": the folder is configured and holds no such file.
            CoreHebrewBooks.HebrewBookDeleteOutcome.NotThere => (false, true, false, null),
            CoreHebrewBooks.HebrewBookDeleteOutcome.NoRecycleBin => (false, false, true, null),

            // These two are errors the user can act on, NOT a missing file. Reporting them as
            // notFound would silently swallow "you have not chosen a save folder yet".
            CoreHebrewBooks.HebrewBookDeleteOutcome.NoLocalFolderConfigured =>
                (false, false, false, "לא הוגדרה תיקיית שמירה"),
            CoreHebrewBooks.HebrewBookDeleteOutcome.InvalidBookId => (false, false, false, "invalid book id"),

            _ => (false, false, false, "delete failed"),
        };
    }
}
