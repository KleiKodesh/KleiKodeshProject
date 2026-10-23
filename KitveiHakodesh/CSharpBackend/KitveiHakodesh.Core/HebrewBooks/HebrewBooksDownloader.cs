using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KitveiHakodesh.Core.Common;

namespace KitveiHakodesh.Core.HebrewBooks
{
    /// <summary>
    /// Gets a listed book's PDF onto this machine and keeps track of where it landed.
    /// Searching the catalog is <see cref="HebrewBooksCatalogDbQueries"/>'s job.
    ///
    /// Lookup order is the user's own folder, then the app cache, then a download. A fresh
    /// download is written to the user's folder when one is set and writable, else to the
    /// cache, which is LRU-evicted; the user's folder is never evicted from.
    ///
    /// Downloads are STREAMED. A scanned book is 10-40 MB and the whole point of the byte
    /// progress is that the caller can show it while the bytes are still arriving, so nothing
    /// here buffers a whole PDF.
    /// </summary>
    public sealed class HebrewBooksDownloader
    {
        /// <summary>How many downloaded PDFs the app cache keeps. The user's own folder is
        /// not subject to this — we never delete from a folder the user chose.</summary>
        private const int MaxCachedPdfs = 10;

        private const int CopyBufferBytes = 1 << 16;

        /// <summary>Enough bytes to see the %PDF- signature. A book that is not on the server
        /// comes back as an HTML message page with a 200, so the status code alone does not
        /// tell us whether this is a PDF.</summary>
        private const int PdfSignatureLength = 5;

        private readonly HttpClient _http;
        private readonly string _cacheDirectory;

        /// <summary>The official handler, then the hb-mirror.json fallbacks.</summary>
        private readonly HebrewBooksDownloadSources _sources = HebrewBooksDownloadSources.Load();

        /// <summary>Byte progress per in-flight download, keyed by book id. Entries exist only
        /// while a download is running, so "no entry" means "not downloading" — already
        /// finished, or never started.</summary>
        private readonly ConcurrentDictionary<string, HebrewBookDownloadProgress> _progress =
            new ConcurrentDictionary<string, HebrewBookDownloadProgress>();

        /// <summary>Cancellation source per in-flight download. <see cref="Cancel"/> trips one
        /// so the streamed copy aborts at its next chunk and deletes its .part — a real abort,
        /// not just a dismissed dialog.</summary>
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _downloads =
            new ConcurrentDictionary<string, CancellationTokenSource>();

        /// <summary>0 until stale .part files have been swept. A service that was killed
        /// mid-download leaves them behind, so the first acquire after a launch cleans up.</summary>
        private int _sweptStaleParts;

        /// <param name="http">Shared client — the host owns its lifetime and its handler. One
        /// per app, not one per download.</param>
        /// <param name="cacheDirectory">Where downloads go when the user has set no folder of
        /// their own. Defaults to an "hb-cache" folder in the first writable app root.</param>
        public HebrewBooksDownloader(HttpClient http, string? cacheDirectory = null)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _cacheDirectory = cacheDirectory ?? AppFileLocator.ResolveWritablePath("hb-cache");
        }

        public string CacheDirectory => _cacheDirectory;

        /// <summary>Byte progress for an in-flight download, or null when none is running for
        /// this id.</summary>
        public HebrewBookDownloadProgress? Progress(string bookId) =>
            _progress.TryGetValue(bookId, out var progress) ? progress : null;

        /// <summary>Aborts an in-flight download. Returns whether there was one to abort;
        /// calling it when nothing is running is not an error.</summary>
        public bool Cancel(string bookId)
        {
            if (!_downloads.TryGetValue(bookId, out var cancellation)) return false;

            try { cancellation.Cancel(); }
            catch (ObjectDisposedException) { /* the download already ended — same outcome */ }
            return true;
        }

        /// <summary>
        /// Resolves a book to an on-disk PDF, downloading it if it is not already here.
        ///
        /// Never throws for the ordinary failure modes — offline, no such book, cancelled —
        /// those come back as <see cref="HebrewBookAcquireResult.Failure"/> so the caller can
        /// tell them apart and word each one itself. A host shutdown still propagates:
        /// <paramref name="cancellationToken"/> firing is not this download's failure.
        /// </summary>
        /// <param name="allowDownload">False to answer only from disk. A restore-on-launch
        /// pass uses this so reopening tabs never silently re-downloads over a metered link.</param>
        public async Task<HebrewBookAcquireResult> AcquireAsync(
            string bookId,
            string? localFolder,
            bool allowDownload,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _sweptStaleParts, 1) == 0) SweepStaleParts();

            if (!HebrewBooksLocalFolder.IsValidBookId(bookId))
                return HebrewBookAcquireResult.Failed(HebrewBookAcquireFailure.InvalidBookId);

            string? inUserFolder = HebrewBooksLocalFolder.LocalFolderHit(localFolder, bookId);
            if (inUserFolder != null) return HebrewBookAcquireResult.Success(inUserFolder);

            string cached = Path.Combine(_cacheDirectory, bookId + ".pdf");
            if (File.Exists(cached)) return HebrewBookAcquireResult.Success(cached);

            if (!allowDownload)
                return HebrewBookAcquireResult.Failed(HebrewBookAcquireFailure.NotCachedAndDownloadDisallowed);

            return await DownloadAsync(bookId, localFolder, cached, cancellationToken).ConfigureAwait(false);
        }

        private async Task<HebrewBookAcquireResult> DownloadAsync(
            string bookId,
            string? localFolder,
            string cachedPath,
            CancellationToken cancellationToken)
        {
            // Linked so either the host shutting down or the user pressing cancel stops the
            // copy; the two are told apart in the catch below, because a user cancel closes
            // the tab quietly while a shutdown is not this download's business to report.
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _downloads[bookId] = cancellation;
            CancellationToken downloadToken = cancellation.Token;

            string? partPath = null;
            // Known once the body is confirmed a PDF; a disk failure reports the folder it was
            // writing to, so the user can tell a full drive from a folder they cannot write.
            string? destination = null;
            try
            {
                destination = ChooseDestination(localFolder, bookId, cachedPath);
                // Written to a .part first and moved into place only once complete, so a
                // cancelled or failed download can never leave a truncated PDF that the
                // cache-hit check above would later trust.
                partPath = destination + ".part";

                // The official handler first, then the mirrors. The handler is authoritative:
                // its 404 means no such book and ends the search, and when every source fails
                // its failure is the one reported. Each mirror's failure is appended to that
                // detail so the console shows why the fallbacks did not save it either.
                HebrewBookAcquireResult? officialFailure = null;
                foreach (HttpRequestMessage request in _sources.RequestsFor(bookId))
                {
                    HebrewBookAcquireResult attempt;
                    using (request)
                    {
                        // A source that cannot be reached at all (refused, DNS, TLS, timeout) is
                        // that source failing, not the download: the next one must still be tried —
                        // a network that blocks hebrewbooks.org outright is the case mirrors exist for.
                        try
                        {
                            attempt = await TryDownloadFromAsync(request, bookId, partPath, downloadToken).ConfigureAwait(false);
                        }
                        catch (HttpRequestException ex)
                        {
                            attempt = HebrewBookAcquireResult.Failed(HebrewBookAcquireFailure.Network, ex.ToString());
                        }
                        catch (OperationCanceledException ex) when (!cancellation.IsCancellationRequested)
                        {
                            // Neither the user nor the host cancelled — HttpClient's own timeout.
                            attempt = HebrewBookAcquireResult.Failed(HebrewBookAcquireFailure.Network, "Timeout: " + ex);
                        }
                    }

                    if (attempt.Succeeded)
                    {
                        MoveIntoPlace(partPath, destination);
                        partPath = null;
                        if (destination == cachedPath) EvictCache();
                        return HebrewBookAcquireResult.Success(destination);
                    }

                    if (officialFailure == null)
                    {
                        if (attempt.Failure == HebrewBookAcquireFailure.NotFoundUpstream
                            && attempt.HttpStatusCode == (int)HttpStatusCode.NotFound)
                            return attempt;
                        officialFailure = attempt;
                    }
                    else
                    {
                        officialFailure.Detail += $"\n\nFallback {request.RequestUri?.Host} failed: {attempt.Failure}: {attempt.Detail}";
                    }
                }

                return officialFailure!;
            }
            catch (OperationCanceledException)
                when (cancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return HebrewBookAcquireResult.Failed(HebrewBookAcquireFailure.Cancelled);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Neither the user nor the host cancelled — HttpClient's own timeout.
                return HebrewBookAcquireResult.Failed(HebrewBookAcquireFailure.Network, "Timeout: " + ex);
            }
            catch (OperationCanceledException)
            {
                throw; // the host is shutting down — not this download's failure to report
            }
            catch (HttpRequestException ex)
            {
                return HebrewBookAcquireResult.Failed(HebrewBookAcquireFailure.Network, ex.ToString());
            }
            catch (IOException ex)
            {
                return HebrewBookAcquireResult.Failed(HebrewBookAcquireFailure.Unexpected, DiskFailureDetail(ex, destination));
            }
            catch (UnauthorizedAccessException ex)
            {
                return HebrewBookAcquireResult.Failed(HebrewBookAcquireFailure.Unexpected, DiskFailureDetail(ex, destination));
            }
            catch (Exception ex)
            {
                return HebrewBookAcquireResult.Failed(HebrewBookAcquireFailure.Unexpected, ex.ToString());
            }
            finally
            {
                _progress.TryRemove(bookId, out _);
                _downloads.TryRemove(bookId, out _);
                if (partPath != null) await DeleteWithRetryAsync(partPath).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// One source's attempt: streams its PDF into <paramref name="partPath"/> and returns
        /// Success, or returns why this source did not yield a PDF. The .part is overwritten by
        /// each attempt, so a failed source leaves nothing the next one could mistake for its own.
        /// </summary>
        private async Task<HebrewBookAcquireResult> TryDownloadFromAsync(
            HttpRequestMessage request, string bookId, string partPath, CancellationToken downloadToken)
        {
            string url = request.RequestUri?.ToString() ?? "";
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, downloadToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    var notFound = HebrewBookAcquireResult.Failed(HebrewBookAcquireFailure.NotFoundUpstream, $"HTTP 404 from '{url}'");
                    notFound.HttpStatusCode = 404;
                    return notFound;
                }
                var failed = HebrewBookAcquireResult.Failed(
                    HebrewBookAcquireFailure.HttpStatus,
                    await DescribeFailedResponseAsync(_http, url, response, downloadToken).ConfigureAwait(false));
                failed.HttpStatusCode = (int)response.StatusCode;
                return failed;
            }

            long total = response.Content.Headers.ContentLength ?? 0;
            _progress[bookId] = new HebrewBookDownloadProgress { Received = 0, Total = total };

            using var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

            byte[] signature = new byte[PdfSignatureLength];
            int signatureLength = await ReadFullyAsync(body, signature, downloadToken).ConfigureAwait(false);
            if (!IsPdfSignature(signature, signatureLength))
            {
                var notPdf = HebrewBookAcquireResult.Failed(HebrewBookAcquireFailure.NotFoundUpstream,
                    $"Not a PDF. Status {(int)response.StatusCode}, Content-Type '{response.Content.Headers.ContentType}', " +
                    $"final URL '{response.RequestMessage?.RequestUri}', first bytes '{System.Text.Encoding.UTF8.GetString(signature, 0, signatureLength)}'");
                // hebrewbooks.org answers a missing book by redirecting to its own message page
                // (message.aspx) with a 200 — that is its 404, so it is recorded as one. Any
                // other non-PDF (a filter's block page) is left for the mirrors to try.
                if (response.RequestMessage?.RequestUri?.AbsolutePath.EndsWith("/message.aspx", StringComparison.OrdinalIgnoreCase) == true)
                    notPdf.HttpStatusCode = (int)HttpStatusCode.NotFound;
                return notPdf;
            }

            long received = signatureLength;
            using (var file = new FileStream(
                partPath, FileMode.Create, FileAccess.Write, FileShare.None,
                CopyBufferBytes, useAsync: true))
            {
                await file.WriteAsync(signature, 0, signatureLength, downloadToken).ConfigureAwait(false);
                _progress[bookId] = new HebrewBookDownloadProgress { Received = received, Total = total };

                byte[] buffer = new byte[CopyBufferBytes];
                int read;
                while ((read = await body.ReadAsync(buffer, 0, buffer.Length, downloadToken).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer, 0, read, downloadToken).ConfigureAwait(false);
                    received += read;
                    _progress[bookId] = new HebrewBookDownloadProgress { Received = received, Total = total };
                }
            }

            return HebrewBookAcquireResult.Success(partPath);
        }

        /// <summary>Which of <paramref name="bookIds"/> already have a PDF in the user's folder.
        /// A per-file I/O error skips that id rather than failing the batch — one disconnected
        /// drive should not blank the whole list.</summary>
        public List<string> WhichAreDownloaded(IEnumerable<string> bookIds, string? localFolder)
        {
            var present = new List<string>();
            if (string.IsNullOrWhiteSpace(localFolder) || !Directory.Exists(localFolder)) return present;

            foreach (string bookId in bookIds)
            {
                if (!HebrewBooksLocalFolder.IsValidBookId(bookId)) continue;
                try
                {
                    if (File.Exists(Path.Combine(localFolder, bookId + ".pdf"))) present.Add(bookId);
                }
                catch (Exception) { /* disconnected drive or permission — skip this one */ }
            }

            return present;
        }

        /// <summary>Removes a downloaded PDF from the user's folder - permanently, or into the
        /// Recycle Bin when <paramref name="toRecycleBin"/> is set. The bin is the calling
        /// process's account's, so the installed (LocalSystem) service never recycles: the
        /// user-side host calls <see cref="RecycleBin"/> itself; this path serves dev only.</summary>
        public HebrewBookDeleteOutcome DeleteDownloaded(string bookId, string? localFolder, bool toRecycleBin = false)
        {
            if (!HebrewBooksLocalFolder.IsValidBookId(bookId)) return HebrewBookDeleteOutcome.InvalidBookId;
            if (string.IsNullOrWhiteSpace(localFolder)) return HebrewBookDeleteOutcome.NoLocalFolderConfigured;

            try
            {
                string path = Path.Combine(localFolder, bookId + ".pdf");
                if (!File.Exists(path)) return HebrewBookDeleteOutcome.NotThere;
                if (toRecycleBin)
                {
                    switch (RecycleBin.Send(path))
                    {
                        case RecycleBinOutcome.Recycled: return HebrewBookDeleteOutcome.Deleted;
                        case RecycleBinOutcome.NoRecycleBin: return HebrewBookDeleteOutcome.NoRecycleBin;
                        default: return HebrewBookDeleteOutcome.DeleteFailed;
                    }
                }
                File.Delete(path);
                return HebrewBookDeleteOutcome.Deleted;
            }
            catch (Exception)
            {
                return HebrewBookDeleteOutcome.DeleteFailed;
            }
        }

        /// <summary>The user's folder when it is set and we can create it, else the app cache.
        /// Creating it is the writability test: a folder we cannot make is one we cannot write
        /// a PDF into either.</summary>
        private string ChooseDestination(string? localFolder, string bookId, string cachedPath)
        {
            if (!string.IsNullOrWhiteSpace(localFolder))
            {
                try
                {
                    Directory.CreateDirectory(localFolder);
                    return Path.Combine(localFolder, bookId + ".pdf");
                }
                catch (Exception) { /* unwritable — the cache below still works */ }
            }

            Directory.CreateDirectory(_cacheDirectory);
            return cachedPath;
        }

        private static bool IsPdfSignature(byte[] head, int length) =>
            length >= PdfSignatureLength
            && head[0] == (byte)'%' && head[1] == (byte)'P' && head[2] == (byte)'D'
            && head[3] == (byte)'F' && head[4] == (byte)'-';

        /// <summary>
        /// Everything needed to diagnose a refused download on a machine we cannot reach: which
        /// URL, where redirects ended, every header (Server/Via/cf-* tell a filtering proxy or
        /// Cloudflare from hebrewbooks.org itself) and the start of the error page's text.
        /// </summary>
        private static async Task<string> DescribeFailedResponseAsync(
            HttpClient http, string url, HttpResponseMessage response, CancellationToken ct)
        {
            var text = new System.Text.StringBuilder();
            text.Append($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n");
            text.Append($"Request URL: {url}\n");
            text.Append($"Final URL: {response.RequestMessage?.RequestUri}\n");
            text.Append("Headers:\n");
            foreach (var header in response.Headers)
                text.Append($"  {header.Key}: {string.Join(", ", header.Value)}\n");
            foreach (var header in response.Content.Headers)
                text.Append($"  {header.Key}: {string.Join(", ", header.Value)}\n");

            try
            {
                using var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                byte[] buffer = new byte[4096];
                int length = await ReadFullyAsync(body, buffer, ct).ConfigureAwait(false);
                string html = System.Text.Encoding.UTF8.GetString(buffer, 0, length);
                string plain = System.Text.RegularExpressions.Regex.Replace(html, "<script[\\s\\S]*?</script>|<style[\\s\\S]*?</style>|<[^>]+>", " ");
                plain = System.Text.RegularExpressions.Regex.Replace(WebUtility.HtmlDecode(plain), "\\s+", " ").Trim();
                if (plain.Length > 600) plain = plain.Substring(0, 600) + "…";
                text.Append($"Body ({length} bytes read): {plain}");
            }
            catch (Exception ex) when (ex is IOException || ex is HttpRequestException)
            {
                text.Append($"Body unreadable: {ex.GetType().Name}: {ex.Message}");
            }

            // Cloudflare never says why it challenged; the site owner sees that by CF-RAY. Its
            // public trace endpoint does show what it saw of us: public IP (filter / VPN /
            // carrier), colo, WARP/Gateway, TLS and HTTP versions, the User-Agent received.
            if (response.Headers.Contains("Cf-Mitigated") && response.RequestMessage?.RequestUri is Uri requested)
                text.Append("\nCloudflare trace:\n").Append(await CloudflareTraceAsync(http, requested, ct).ConfigureAwait(false));

            return text.ToString();
        }

        private static async Task<string> CloudflareTraceAsync(HttpClient http, Uri site, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            string traceUrl = site.GetLeftPart(UriPartial.Authority) + "/cdn-cgi/trace";
            try
            {
                using var trace = await http.GetAsync(traceUrl, timeout.Token).ConfigureAwait(false);
                string body = await trace.Content.ReadAsStringAsync().ConfigureAwait(false);
                return trace.IsSuccessStatusCode
                    ? body.Trim()
                    : $"HTTP {(int)trace.StatusCode} from '{traceUrl}'";
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return $"timed out: '{traceUrl}'";
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException)
            {
                return $"failed: '{traceUrl}': {ex.Message}";
            }
        }

        /// <summary>Reads until the buffer is full or the stream ends, returning how much
        /// arrived. A single ReadAsync may return fewer bytes than asked for, which would make
        /// a signature check on its result wrong rather than merely incomplete.</summary>
        private static async Task<int> ReadFullyAsync(Stream source, byte[] buffer, CancellationToken ct)
        {
            int filled = 0;
            while (filled < buffer.Length)
            {
                int read = await source.ReadAsync(buffer, filled, buffer.Length - filled, ct).ConfigureAwait(false);
                if (read == 0) break;
                filled += read;
            }
            return filled;
        }

        /// <summary>Swaps the completed .part into its real name. net48 has no overwriting
        /// File.Move, so the existing file goes first — by this point we hold a complete PDF
        /// and whatever is there is the one we set out to replace.</summary>
        /// <summary>The exception's own message plus the folder being written, when known. A
        /// FileStream failure usually names the file already; "not enough space on the disk"
        /// and friends do not, and the folder is what the user needs to fix either way.</summary>
        private static string DiskFailureDetail(Exception ex, string? destination)
        {
            string? folder = destination is null ? null : Path.GetDirectoryName(destination);
            return string.IsNullOrEmpty(folder) ? ex.ToString() : $"{ex} ({folder})";
        }

        private static void MoveIntoPlace(string partPath, string destination)
        {
            if (File.Exists(destination)) File.Delete(destination);
            File.Move(partPath, destination);
        }

        /// <summary>Keeps the app cache to <see cref="MaxCachedPdfs"/>, dropping least-recently-read
        /// first. Best-effort: a file being read right now stays and gets evicted next time.</summary>
        private void EvictCache()
        {
            try
            {
                if (!Directory.Exists(_cacheDirectory)) return;

                var cached = new DirectoryInfo(_cacheDirectory).GetFiles("*.pdf");
                if (cached.Length <= MaxCachedPdfs) return;

                Array.Sort(cached, (a, b) => a.LastAccessTimeUtc.CompareTo(b.LastAccessTimeUtc));
                for (int i = 0; i < cached.Length - MaxCachedPdfs; i++)
                {
                    try { cached[i].Delete(); }
                    catch (Exception) { /* open or already gone */ }
                }
            }
            catch (Exception) { /* the cache folder vanished — nothing to evict */ }
        }

        /// <summary>Clears .part files left by a download that was killed rather than cancelled
        /// — the host crashing or being force-quit mid-write. Runs once per instance.</summary>
        private void SweepStaleParts()
        {
            try
            {
                if (!Directory.Exists(_cacheDirectory)) return;
                foreach (string part in Directory.EnumerateFiles(_cacheDirectory, "*.part"))
                {
                    try { File.Delete(part); }
                    catch (Exception) { /* open or already gone */ }
                }
            }
            catch (Exception) { /* the cache folder vanished — nothing to sweep */ }
        }

        /// <summary>Deletes a file, retrying briefly. A just-cancelled async FileStream can
        /// leave the OS closing its handle for a few milliseconds, so a single Delete races it.</summary>
        private static async Task DeleteWithRetryAsync(string path)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }
                catch (IOException) { await Task.Delay(50).ConfigureAwait(false); }
                catch (UnauthorizedAccessException) { await Task.Delay(50).ConfigureAwait(false); }
                catch (Exception) { return; }
            }
        }
    }
}
