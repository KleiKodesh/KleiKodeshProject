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
        private const string DownloadUrlFormat =
            "https://download.hebrewbooks.org/downloadhandler.ashx?req={0}";

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
            _progress.TryGetValue(bookId, out var progress) ? progress : (HebrewBookDownloadProgress?)null;

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

            if (!IsValidBookId(bookId))
                return HebrewBookAcquireResult.Failed(HebrewBookAcquireFailure.InvalidBookId);

            string? inUserFolder = LocalFolderHit(localFolder, bookId);
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
            try
            {
                string url = string.Format(DownloadUrlFormat, bookId);
                using var response = await _http
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, downloadToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return response.StatusCode == HttpStatusCode.NotFound
                        ? HebrewBookAcquireResult.Failed(HebrewBookAcquireFailure.NotFoundUpstream)
                        : HebrewBookAcquireResult.Failed(
                            HebrewBookAcquireFailure.HttpStatus, ((int)response.StatusCode).ToString());
                }

                long total = response.Content.Headers.ContentLength ?? 0;
                _progress[bookId] = new HebrewBookDownloadProgress(0, total);

                using var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

                byte[] signature = new byte[PdfSignatureLength];
                int signatureLength = await ReadFullyAsync(body, signature, downloadToken).ConfigureAwait(false);
                if (!IsPdfSignature(signature, signatureLength))
                    return HebrewBookAcquireResult.Failed(HebrewBookAcquireFailure.NotFoundUpstream);

                string destination = ChooseDestination(localFolder, bookId, cachedPath);

                // Written to a .part first and moved into place only once complete, so a
                // cancelled or failed download can never leave a truncated PDF that the
                // cache-hit check above would later trust.
                partPath = destination + ".part";
                long received = signatureLength;

                using (var file = new FileStream(
                    partPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    CopyBufferBytes, useAsync: true))
                {
                    await file.WriteAsync(signature, 0, signatureLength, downloadToken).ConfigureAwait(false);
                    _progress[bookId] = new HebrewBookDownloadProgress(received, total);

                    byte[] buffer = new byte[CopyBufferBytes];
                    int read;
                    while ((read = await body.ReadAsync(buffer, 0, buffer.Length, downloadToken).ConfigureAwait(false)) > 0)
                    {
                        await file.WriteAsync(buffer, 0, read, downloadToken).ConfigureAwait(false);
                        received += read;
                        _progress[bookId] = new HebrewBookDownloadProgress(received, total);
                    }
                }

                MoveIntoPlace(partPath, destination);
                partPath = null;

                if (destination == cachedPath) EvictCache();
                return HebrewBookAcquireResult.Success(destination);
            }
            catch (OperationCanceledException)
                when (cancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return HebrewBookAcquireResult.Failed(HebrewBookAcquireFailure.Cancelled);
            }
            catch (OperationCanceledException)
            {
                throw; // the host is shutting down — not this download's failure to report
            }
            catch (HttpRequestException ex)
            {
                return HebrewBookAcquireResult.Failed(HebrewBookAcquireFailure.Network, ex.Message);
            }
            catch (IOException ex)
            {
                return HebrewBookAcquireResult.Failed(HebrewBookAcquireFailure.Unexpected, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return HebrewBookAcquireResult.Failed(HebrewBookAcquireFailure.Unexpected, ex.Message);
            }
            finally
            {
                _progress.TryRemove(bookId, out _);
                _downloads.TryRemove(bookId, out _);
                if (partPath != null) await DeleteWithRetryAsync(partPath).ConfigureAwait(false);
            }
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
                if (!IsValidBookId(bookId)) continue;
                try
                {
                    if (File.Exists(Path.Combine(localFolder, bookId + ".pdf"))) present.Add(bookId);
                }
                catch (Exception) { /* disconnected drive or permission — skip this one */ }
            }

            return present;
        }

        /// <summary>Removes a downloaded PDF from the user's folder.</summary>
        public HebrewBookDeleteOutcome DeleteDownloaded(string bookId, string? localFolder)
        {
            if (!IsValidBookId(bookId)) return HebrewBookDeleteOutcome.InvalidBookId;
            if (string.IsNullOrWhiteSpace(localFolder)) return HebrewBookDeleteOutcome.NoLocalFolderConfigured;

            try
            {
                string path = Path.Combine(localFolder, bookId + ".pdf");
                if (!File.Exists(path)) return HebrewBookDeleteOutcome.NotThere;
                File.Delete(path);
                return HebrewBookDeleteOutcome.Deleted;
            }
            catch (Exception)
            {
                return HebrewBookDeleteOutcome.DeleteFailed;
            }
        }

        /// <summary>
        /// Book ids are numeric upstream. Anything else could steer the request URL or escape
        /// the folder the {id}.pdf name is built in, so it never gets that far. EVERY entry
        /// point that turns a caller's id into a path or a URL checks this first.
        /// </summary>
        private static bool IsValidBookId(string? bookId)
        {
            if (string.IsNullOrWhiteSpace(bookId)) return false;
            foreach (char c in bookId!)
                if (c < '0' || c > '9') return false;
            return true;
        }

        private static string? LocalFolderHit(string? localFolder, string bookId)
        {
            if (string.IsNullOrWhiteSpace(localFolder)) return null;
            try
            {
                string candidate = Path.Combine(localFolder, bookId + ".pdf");
                return File.Exists(candidate) ? candidate : null;
            }
            catch (Exception)
            {
                return null; // drive disconnected or the path is malformed — fall through to download
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
