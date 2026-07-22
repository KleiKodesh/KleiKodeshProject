using System.Text;
using KitveiHakodeshService.Ipc;
using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.HebrewBooks;

/// <summary>
/// HebrewBooks catalog search + PDF acquisition.
///
/// SEARCH is over the bundled SQLite catalog (HebrewBooks/HebrewBooksCatalog.db, copied to
/// output as content), reproducing the net4.8 KitveiHakodeshLib HebrewBooksDb.Search contract
/// exactly: per-word LIKE against lower(title|author|categories), ORDER BY title, LIMIT n,
/// nikud stripped, and hasLocalFile stamped when a local folder is given.
///
/// DOWNLOAD happens ENTIRELY in the service via <see cref="HttpClient"/> — never through a
/// browser download interception (the hosted app's WebView2 trick). The lookup order mirrors
/// the hosted HebrewBooksHandler: user's local folder → app cache → download. The download
/// target is download.hebrewbooks.org/downloadhandler.ashx?req={id}; a book that doesn't exist
/// on the server responds with a non-PDF body (message page / redirect), which we detect by the
/// %PDF- magic and surface as "not found" rather than caching garbage. Successful downloads go
/// to the user's local folder when one is set and writable, else to a portable "hb-cache" folder
/// beside the service exe (LRU-evicted), matching WordConversionService's convert-cache scheme.
/// Uses Microsoft.Data.Sqlite (the service's only SQLite library).
/// </summary>
public sealed class HebrewBooksService(ILogger<HebrewBooksService> logger, HttpClient http)
{
    private static readonly string DbPath =
        Path.Combine(AppContext.BaseDirectory, "HebrewBooks", "HebrewBooksCatalog.db");

    // Portable cache beside the exe (like WordConversionService's convert-cache), not %LOCALAPPDATA%.
    private static readonly string CacheDir =
        Path.Combine(AppContext.BaseDirectory, "hb-cache");
    private const int MaxCachedPdfs = 10;

    public const int DefaultLimit = 200;

    /// <summary>Outcome of an acquire: the resolved on-disk PDF path, or a reason it failed.
    /// Exactly one of Path / NotFound / Error is meaningful.</summary>
    public readonly record struct HbAcquireResult(string? Path, bool NotFound, string? Error);

    public HbSearchResult Search(string query, string? localFolder, int limit)
    {
        var result = new HbSearchResult();
        if (string.IsNullOrWhiteSpace(query)) return result;

        if (!File.Exists(DbPath))
        {
            logger.LogWarning("HebrewBooks catalog not found at {Path}", DbPath);
            return result;
        }

        string[] words = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return result;
        if (limit <= 0) limit = DefaultLimit;

        // Filter in SQLite: one AND'd LIKE per word against a concatenated, lowercased
        // search column so no non-matching rows are materialised.
        const string searchExpr =
            "lower(coalesce(title,'') || ' ' || coalesce(author,'') || ' ' || coalesce(categories,''))";

        var where = new StringBuilder();
        for (int i = 0; i < words.Length; i++)
        {
            if (i > 0) where.Append(" AND ");
            where.Append(searchExpr).Append(" LIKE @w").Append(i);
        }

        string sql =
            "SELECT id, title, author, placeOfPublication, year, pageCount, categories " +
            "FROM hebrewBooks WHERE " + where + " ORDER BY title LIMIT " + limit;

        bool checkLocal = !string.IsNullOrWhiteSpace(localFolder);

        try
        {
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = DbPath,
                Mode = SqliteOpenMode.ReadOnly,
            }.ConnectionString;

            using var conn = new SqliteConnection(cs);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            for (int i = 0; i < words.Length; i++)
                cmd.Parameters.AddWithValue("@w" + i, "%" + words[i] + "%");

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var book = new HebrewBook
                {
                    Id = reader.GetInt32(0),
                    Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Author = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    PrintingPlace = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    PrintingYear = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Pages = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    Categories = reader.IsDBNull(6) ? "" : reader.GetString(6),
                };

                if (checkLocal)
                {
                    try { book.HasLocalFile = File.Exists(Path.Combine(localFolder!, book.Id + ".pdf")); }
                    catch { /* disconnected drive / permission — leave false */ }
                }

                result.Books.Add(book);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "HebrewBooks search failed");
        }

        return result;
    }

    /// <summary>
    /// Resolve a book's PDF to a local path, downloading it in-process if necessary.
    /// Order: user's local folder → app cache → HTTP download. On a fresh download, the file
    /// is saved to the local folder when one is set and writable, otherwise to the app cache.
    /// A server "not found" (non-PDF body) is reported without caching. Never throws for the
    /// normal failure modes — they come back as <see cref="HbAcquireResult"/> fields.
    /// </summary>
    public async Task<HbAcquireResult> AcquireAsync(string bookId, string? localFolder, bool allowDownload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(bookId)) return new(null, false, "empty book id");
        // Book ids are numeric on hebrewbooks.org — reject anything else so a caller can't
        // steer the request URL or the on-disk filename.
        foreach (char c in bookId) if (c is < '0' or > '9') return new(null, false, "invalid book id");

        // 1. Local folder hit.
        string? local = LocalFolderHit(localFolder, bookId);
        if (local != null) return new(local, false, null);

        // 2. App cache hit.
        string cached = Path.Combine(CacheDir, bookId + ".pdf");
        if (File.Exists(cached)) return new(cached, false, null);

        if (!allowDownload) return new(null, false, null); // restore w/o network: report miss, caller re-triggers

        // 3. Download in-process.
        try
        {
            string url = "https://download.hebrewbooks.org/downloadhandler.ashx?req=" + bookId;
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
                return new(null, resp.StatusCode == System.Net.HttpStatusCode.NotFound, $"download failed (HTTP {(int)resp.StatusCode})");

            byte[] bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            // A missing book returns an HTML message page / redirect, not a PDF — detect by magic.
            if (bytes.Length < 5 || bytes[0] != '%' || bytes[1] != 'P' || bytes[2] != 'D' || bytes[3] != 'F' || bytes[4] != '-')
            {
                logger.LogInformation("HebrewBooks {Id}: server returned a non-PDF body ({Len}B) — treating as not found", bookId, bytes.Length);
                return new(null, true, null);
            }

            // Prefer the user's local folder when set and writable; else the app cache.
            string dest = cached;
            if (!string.IsNullOrWhiteSpace(localFolder))
            {
                try { Directory.CreateDirectory(localFolder); dest = Path.Combine(localFolder, bookId + ".pdf"); }
                catch (Exception ex) { logger.LogWarning(ex, "HebrewBooks local folder unwritable, using cache"); }
            }
            if (dest == cached) Directory.CreateDirectory(CacheDir);

            await File.WriteAllBytesAsync(dest, bytes, ct);
            if (dest == cached) EvictCache(); // never touch the user's own folder
            return new(dest, false, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "HebrewBooks {Id} download network error", bookId);
            return new(null, false, "network error"); // caller maps to noInternet-style message
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "HebrewBooks {Id} download failed", bookId);
            return new(null, false, ex.Message);
        }
    }

    /// <summary>Which of <paramref name="bookIds"/> already have {id}.pdf in the local folder.
    /// I/O errors per file are swallowed so a disconnected drive doesn't fail the batch.</summary>
    public List<string> CheckLocalFiles(IEnumerable<string> bookIds, string? localFolder)
    {
        var existing = new List<string>();
        if (string.IsNullOrWhiteSpace(localFolder) || !Directory.Exists(localFolder)) return existing;
        foreach (string id in bookIds)
        {
            if (string.IsNullOrEmpty(id)) continue;
            try { if (File.Exists(Path.Combine(localFolder, id + ".pdf"))) existing.Add(id); }
            catch { /* disconnected drive / permission — skip */ }
        }
        return existing;
    }

    /// <summary>Delete {id}.pdf from the user's local folder. Returns (ok, notFound, error).</summary>
    public (bool ok, bool notFound, string? error) DeleteLocalFile(string bookId, string? localFolder)
    {
        if (string.IsNullOrWhiteSpace(localFolder)) return (false, false, "לא הוגדרה תיקיית שמירה");
        try
        {
            string path = Path.Combine(localFolder, bookId + ".pdf");
            if (!File.Exists(path)) return (false, true, null);
            File.Delete(path);
            return (true, false, null);
        }
        catch (Exception ex) { return (false, false, ex.Message); }
    }

    /// <summary>Full path to {id}.pdf in the local folder if present and reachable, else null.</summary>
    private static string? LocalFolderHit(string? localFolder, string bookId)
    {
        if (string.IsNullOrWhiteSpace(localFolder)) return null;
        try
        {
            string candidate = Path.Combine(localFolder, bookId + ".pdf");
            return File.Exists(candidate) ? candidate : null;
        }
        catch { return null; } // drive disconnected / invalid path — fall through to download
    }

    private void EvictCache()
    {
        try
        {
            if (!Directory.Exists(CacheDir)) return;
            var files = new DirectoryInfo(CacheDir).GetFiles("*.pdf");
            if (files.Length <= MaxCachedPdfs) return;
            Array.Sort(files, (a, b) => a.LastAccessTimeUtc.CompareTo(b.LastAccessTimeUtc));
            for (int i = 0; i < files.Length - MaxCachedPdfs; i++)
                try { files[i].Delete(); } catch { /* in use / gone */ }
        }
        catch (Exception ex) { logger.LogDebug(ex, "hb-cache eviction failed"); }
    }

    /// <summary>Lowercase + strip Hebrew nikud (U+05B0–U+05C2), matching the Vue/C# normalizer.</summary>
    private static string Normalize(string text)
    {
        text = text.ToLowerInvariant();
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c >= 'ְ' && c <= 'ׂ') continue; // strip nikud
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }
}
