using KitveiHakodeshLib.Bridge;
using KitveiHakodeshLib.Settings;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KitveiHakodeshLib.HebrewBooks
{
    /// <summary>
    /// Handles HebrewBooks PDF restore, download, and Save As flows.
    ///
    /// Lookup order for opening a book is the user's folder, then the app cache, then a
    /// download over HttpClient — the same order, and the same means, as
    /// KitveiHakodeshService.HebrewBooksService.
    ///
    /// Save As is the one flow that still navigates the WebView2 at the download endpoint,
    /// because DownloadStarting is what supplies the native Save dialog's file path.
    /// </summary>
    public class HebrewBooksHandler
    {
        private static readonly string HbCacheDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KitveiHakodesh", "hebrewbooks-cache");

        private readonly WebBridge _bridge;
        private readonly WebView2 _webView;
        private readonly Control _owner;

        private HbSaveAsInfo? _pendingSaveAs;

        // Process-global map from folder path → stable virtual host name.
        // Shared across all AppViewer instances so the same folder always gets the
        // same hostname (e.g. "kitvei-hb-local-1"), no matter how many viewers exist.
        // Each AppViewer's WebView2 still needs its own SetVirtualHostNameToFolderMapping
        // call, tracked in _registeredOnThisWebView below.
        private static readonly Dictionary<string, string> _globalFolderHosts =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static int _globalHostCounter;
        private static readonly object _globalHostLock = new object();

        // Host names already registered on THIS instance's WebView2.
        // Avoids calling SetVirtualHostNameToFolderMapping more than once per host per WebView.
        private readonly HashSet<string> _registeredOnThisWebView = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private struct HbSaveAsInfo { public string BookId; public string BookTitle; }

        public HebrewBooksHandler(WebBridge bridge, WebView2 webView, Control owner)
        {
            _bridge = bridge;
            _webView = webView;
            _owner = owner;
        }

        // Called from AppViewer when navigation to hebrewbooks.org/message.aspx is detected,
        // meaning the requested book does not exist on the server. Only Save As can land here
        // now — a book opened for reading is fetched over HttpClient, which recognises a missing
        // book by its non-PDF body without navigating anywhere.
        internal void NotifyBookNotFound()
        {
            _pendingSaveAs = null;
        }

        public void HandleRestoreHbPdf(JsonElement root, string id)
        {
            try
            {
                string bookId      = root.GetProperty("bookId").GetString();
                if (!IsValidBookId(bookId)) { _bridge.Reply(id, new { error = "invalid book id" }); return; }
                string bookTitle   = root.GetProperty("bookTitle").GetString();
                string tabId       = root.GetProperty("tabId").GetString();
                string localFolder = root.TryGetProperty("localFolder", out var lf) ? (lf.GetString() ?? "") : "";
                // Fall back to registry-configured folder when frontend sends nothing (e.g. first-run via installer).
                if (string.IsNullOrWhiteSpace(localFolder)) localFolder = AppSettings.LoadHbLocalFolder();

                // Check local folder first — same priority order as HandleTriggerHbDownload.
                string localPath = GetLocalFolderPath(localFolder, bookId);
                if (localPath != null)
                {
                    Log("Restore — local folder hit: " + localPath);
                    _bridge.Reply(id, new { url = RegisterLocalBookHost(localPath, bookId) });
                    return;
                }

                string cached = GetCachePath(bookId);
                if (File.Exists(cached)) { _bridge.Reply(id, new { url = CacheUrl(bookId) }); return; }

                // Cache miss — must re-download. The download runs over HttpClient and pushes
                // hbPdfReady/hbPdfCancelled when it lands, so the reply here only tells the
                // frontend to keep showing the placeholder.
                _bridge.Reply(id, new { redownload = true });
                StartDownload(bookId, bookTitle, tabId, localFolder);
            }
            catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); }
        }

        public void HandleTriggerHbDownload(JsonElement root, string id)
        {
            try
            {
                _bridge.Reply(id, new { ok = true });
                string bookId      = root.GetProperty("bookId").GetString();
                string bookTitle   = root.GetProperty("bookTitle").GetString();
                // The frontend still sends a "url", but the download URL is built here from the
                // validated book id — a caller must not be able to steer where we fetch from.
                string tabId       = root.GetProperty("tabId").GetString();
                if (!IsValidBookId(bookId)) { _bridge.PushEvent(new { @event = "hbPdfCancelled", tabId, notFound = true }); return; }
                string localFolder = root.TryGetProperty("localFolder", out var lf) ? (lf.GetString() ?? "") : "";
                // Fall back to registry-configured folder when frontend sends nothing.
                if (string.IsNullOrWhiteSpace(localFolder)) localFolder = AppSettings.LoadHbLocalFolder();
                bool   isOnline    = !root.TryGetProperty("isOnline", out var on) || on.GetBoolean();

                // 1. Local folder hit — serve directly, no download.
                string localPath = GetLocalFolderPath(localFolder, bookId);
                if (localPath != null)
                {
                    Log("Local folder hit: " + localPath);
                    _bridge.PushEvent(new { @event = "hbPdfReady", url = RegisterLocalBookHost(localPath, bookId), bookId, bookTitle, tabId });
                    return;
                }

                // 2. Cache hit.
                string cached = GetCachePath(bookId);
                if (File.Exists(cached))
                {
                    _bridge.PushEvent(new { @event = "hbPdfReady", url = CacheUrl(bookId), bookId, bookTitle, tabId });
                    return;
                }

                // 3. Download required. The frontend's own offline check is still worth honouring
                //    here: it saves a request that would only fail, and it names the reason.
                if (!isOnline)
                {
                    // Through PushCancelled, not a raw push: if a tab is parked waiting on this
                    // book it has to be released too, and the wire shape stays the same as every
                    // other hbPdfCancelled.
                    PushCancelled(tabId, noInternet: true, bookId: bookId);
                    return;
                }

                StartDownload(bookId, bookTitle, tabId, localFolder);
            }
            catch (Exception ex) { _bridge.PushEvent(new { @event = "hbPdfError", error = ex.Message }); }
        }

        /// <summary>
        /// Checks which of the supplied book IDs have a corresponding {bookId}.pdf in the
        /// configured local folder. Returns { existingIds: string[] } — a subset of the
        /// requested IDs that were found on disk. I/O errors (disconnected drive, permission
        /// denied) are swallowed per-file so the rest of the batch still completes.
        /// </summary>
        public void HandleCheckHbLocalFiles(JsonElement root, string id)
        {
            try
            {
                string localFolder = root.TryGetProperty("localFolder", out var lf) ? (lf.GetString() ?? "") : "";
                if (string.IsNullOrWhiteSpace(localFolder)) localFolder = AppSettings.LoadHbLocalFolder();

                var existingIds = new List<string>();

                if (!string.IsNullOrWhiteSpace(localFolder) && Directory.Exists(localFolder))
                {
                    var bookIds = root.GetProperty("bookIds");
                    foreach (var element in bookIds.EnumerateArray())
                    {
                        string bookId = element.GetString();
                        if (!IsValidBookId(bookId)) continue;
                        try
                        {
                            if (File.Exists(Path.Combine(localFolder, bookId + ".pdf")))
                                existingIds.Add(bookId);
                        }
                        catch (Exception) { /* disconnected drive or permission error — skip */ }
                    }
                }

                _bridge.Reply(id, new { existingIds });
            }
            catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); }
        }

        /// <summary>
        /// Deletes {bookId}.pdf from the configured local folder.
        /// Returns { ok: true } on success, { notFound: true } if the file does not exist,
        /// or { error: "..." } if deletion fails for any other reason.
        /// </summary>
        public void HandleDeleteHbLocalFile(JsonElement root, string id)
        {
            try
            {
                string bookId      = root.GetProperty("bookId").GetString();
                if (!IsValidBookId(bookId)) { _bridge.Reply(id, new { error = "invalid book id" }); return; }
                string localFolder = root.TryGetProperty("localFolder", out var lf) ? (lf.GetString() ?? "") : "";
                if (string.IsNullOrWhiteSpace(localFolder)) localFolder = AppSettings.LoadHbLocalFolder();

                if (string.IsNullOrWhiteSpace(localFolder))
                {
                    _bridge.Reply(id, new { error = "לא הוגדרה תיקיית שמירה" });
                    return;
                }

                string filePath = Path.Combine(localFolder, bookId + ".pdf");
                if (!File.Exists(filePath))
                {
                    _bridge.Reply(id, new { notFound = true });
                    return;
                }

                File.Delete(filePath);
                _bridge.Reply(id, new { ok = true });
            }
            catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); }
        }

        public void HandleTriggerHbSaveAs(JsonElement root, string id)
        {
            try
            {
                _bridge.Reply(id, new { ok = true });
                string bookId    = root.GetProperty("bookId").GetString();
                string bookTitle = root.GetProperty("bookTitle").GetString();
                string url       = root.GetProperty("url").GetString();
                if (!IsValidBookId(bookId)) return;

                _pendingSaveAs = new HbSaveAsInfo { BookId = bookId, BookTitle = bookTitle };
                NavigateSafe(url);
            }
            catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); }
        }

        public void OnDownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs e)
        {
            try
            {
                Log("OnDownloadStarting: uri=" + e.DownloadOperation.Uri + " pendingSaveAs=" + _pendingSaveAs.HasValue);
                if (_pendingSaveAs.HasValue)
                {
                    var saveAs = _pendingSaveAs.Value;
                    _pendingSaveAs = null;

                    string suggestedName = MakeSafeFileName(saveAs.BookTitle + "." + saveAs.BookId) + ".pdf";
                    string dest = null;
                    _owner.Invoke(new Action(() =>
                    {
                        using (var dlg = new SaveFileDialog())
                        {
                            dlg.Title    = "שמור ספר";
                            dlg.Filter   = "PDF (*.pdf)|*.pdf";
                            dlg.FileName = suggestedName;
                            if (dlg.ShowDialog() == DialogResult.OK) dest = dlg.FileName;
                        }
                    }));

                    if (dest == null) { e.Cancel = true; return; }

                    e.ResultFilePath = dest;
                    return;
                }

                // Anything else reaching DownloadStarting is not ours. Book downloads no longer
                // come through here at all — they are fetched over HttpClient — so the only
                // WebView2 download left is the Save As above.
            }
            catch (Exception ex)
            {
                Log("OnDownloadStarting exception: " + ex.Message);
                // Cancel the download rather than leaving state inconsistent
                try { e.Cancel = true; } catch { }
            }
        }

        /// <summary>
        /// Opens Windows Explorer with {bookId}.pdf selected and highlighted —
        /// identical to VS Code's "Reveal in File Explorer" / "Reveal in Explorer" command.
        /// Uses explorer.exe /select so the file is scrolled into view and focused.
        /// </summary>
        public void HandleRevealHbLocalFile(JsonElement root, string id)
        {
            try
            {
                string bookId      = root.GetProperty("bookId").GetString();
                if (!IsValidBookId(bookId)) { _bridge.Reply(id, new { error = "invalid book id" }); return; }
                string localFolder = root.TryGetProperty("localFolder", out var lf) ? (lf.GetString() ?? "") : "";
                if (string.IsNullOrWhiteSpace(localFolder)) localFolder = AppSettings.LoadHbLocalFolder();

                if (string.IsNullOrWhiteSpace(localFolder))
                {
                    _bridge.Reply(id, new { error = "לא הוגדרה תיקיית שמירה" });
                    return;
                }

                string filePath = Path.Combine(localFolder, bookId + ".pdf");
                if (!File.Exists(filePath))
                {
                    _bridge.Reply(id, new { notFound = true });
                    return;
                }

                // /select tells Explorer to open the containing folder and pre-select
                // the file — exactly the same behaviour as VS Code "Reveal in Explorer".
                Process.Start("explorer.exe", "/select,\"" + filePath + "\"");
                _bridge.Reply(id, new { ok = true });
            }
            catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); }
        }

        /// <summary>
        /// Live byte progress for the download placeholder's "x / y MB" line. WebView2's native
        /// download dialog used to show this; now that the bytes come through HttpClient, the
        /// frontend polls for them instead. { active: false } means nothing is downloading —
        /// already finished, or never started.
        /// </summary>
        public void HandleHbDownloadProgress(JsonElement root, string id)
        {
            try
            {
                string bookId = root.TryGetProperty("bookId", out var b) ? (b.GetString() ?? "") : "";
                long received, total;
                if (string.IsNullOrEmpty(bookId) || !TryGetDownloadProgress(bookId, out received, out total))
                {
                    _bridge.Reply(id, new { active = false });
                    return;
                }
                _bridge.Reply(id, new { active = true, received, total });
            }
            catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); }
        }

        /// <summary>
        /// The ביטול button. Aborts the transfer for real — the streamed copy unwinds at its
        /// next chunk and deletes its .part — rather than only dismissing the placeholder.
        /// </summary>
        public void HandleCancelHbDownload(JsonElement root, string id)
        {
            try
            {
                string bookId = root.TryGetProperty("bookId", out var b) ? (b.GetString() ?? "") : "";
                bool cancelled = !string.IsNullOrEmpty(bookId) && CancelDownload(bookId);
                _bridge.Reply(id, new { ok = true, cancelled });
            }
            catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); }
        }

        /// <summary>
        /// Book ids are numeric on hebrewbooks.org. Anything else could steer the download URL
        /// or escape the folder we build the {id}.pdf filename in, so it never gets that far.
        /// Same guard as the service's HebrewBooksService.IsValidBookId - the two legs must
        /// agree on what a book id is.
        /// </summary>
        private static bool IsValidBookId(string bookId)
        {
            if (string.IsNullOrWhiteSpace(bookId)) return false;
            foreach (char c in bookId) if (c < '0' || c > '9') return false;
            return true;
        }

        /// <summary>
        /// Returns the full path to {bookId}.pdf inside the configured local folder if it
        /// exists and is accessible, otherwise null. Swallows I/O errors (e.g. disconnected
        /// flash drive) and returns null so the caller falls back to the download path.
        /// </summary>
        private static string GetLocalFolderPath(string localFolder, string bookId)
        {
            if (string.IsNullOrWhiteSpace(localFolder)) return null;
            try
            {
                string candidate = Path.Combine(localFolder, bookId + ".pdf");
                return File.Exists(candidate) ? candidate : null;
            }
            catch (Exception ex)
            {
                // Drive disconnected, path invalid, permission denied — fall back to download.
                Log("GetLocalFolderPath error for \"" + localFolder + "\": " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Returns a virtual-host http URL for the given local PDF file.
        /// The hostname is allocated once per folder path in a process-global map so
        /// all AppViewer instances share the same stable hostname for the same folder.
        /// Each AppViewer's WebView2 registers the mapping independently the first
        /// time it is needed — SetVirtualHostNameToFolderMapping is per-WebView2,
        /// not process-global.
        /// </summary>
        private string RegisterLocalBookHost(string filePath, string bookId)
        {
            string folder = Path.GetDirectoryName(filePath);
            string hostName;

            lock (_globalHostLock)
            {
                if (!_globalFolderHosts.TryGetValue(folder, out hostName))
                {
                    hostName = "kitvei-hb-local-" + (++_globalHostCounter);
                    _globalFolderHosts[folder] = hostName;
                }
            }

            // Register on this WebView2 instance if not already done.
            if (!_registeredOnThisWebView.Contains(hostName))
            {
                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    hostName, folder, CoreWebView2HostResourceAccessKind.Allow);
                _registeredOnThisWebView.Add(hostName);
                Log("Registered virtual host \"" + hostName + "\" → \"" + folder + "\" on this WebView");
            }

            return "http://" + hostName + "/" + bookId + ".pdf";
        }

        // ── Download ────────────────────────────────────────────────────────────────
        //
        // Downloads run over HttpClient, in this process. They used to run by pointing the
        // WebView2 at the download endpoint and intercepting DownloadStarting — but that
        // WebView2 is the one rendering the whole app, so a navigation that failed (offline,
        // DNS, reset) replaced the UI with the WebView error page: no tabs, no close button,
        // no way to open a window. Fetching the bytes ourselves cannot take the app down with
        // it, and it is how KitveiHakodeshService has always done it.
        //
        // A UA header is all the endpoint wants; it does not require a real browser.

        private const string DownloadUrlFormat =
            "https://download.hebrewbooks.org/downloadhandler.ashx?req={0}";

        /// <summary>Enough bytes for the %PDF- signature. A book that is not on the server comes
        /// back as an HTML message page with a 200, so the status code alone cannot tell us
        /// whether this is really a PDF.</summary>
        private const int PdfSignatureLength = 5;

        private const int CopyBufferBytes = 1 << 16;

        /// <summary>One client for the process. A new HttpClient per download exhausts sockets;
        /// the UA header is what keeps the endpoint from treating us as a bot.</summary>
        private static readonly HttpClient _http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            // net48 does not negotiate TLS 1.2 by default on every OS/config, and the endpoint
            // requires it — without this the request fails before it is sent.
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch (Exception) { }

            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("KitveiHakodesh/1.0");
            return client;
        }

        /// <summary>Bytes received / total for in-flight downloads, keyed by book id. An entry
        /// exists only while a download is running, so "no entry" means "not downloading".</summary>
        private static readonly Dictionary<string, HbProgress> _progress =
            new Dictionary<string, HbProgress>(StringComparer.Ordinal);

        /// <summary>Cancellation per in-flight download, keyed by book id, so the ביטול button
        /// aborts the real transfer and cleans up its .part — not just the placeholder.</summary>
        private static readonly Dictionary<string, CancellationTokenSource> _downloads =
            new Dictionary<string, CancellationTokenSource>(StringComparer.Ordinal);

        /// <summary>Tabs that asked for a book already being fetched, keyed by book id. They run
        /// no transfer of their own; they are handed the same outcome when the one in flight
        /// finishes, so a second tab on the same book never spins on the placeholder.</summary>
        private static readonly Dictionary<string, List<HbWaitingTab>> _waitingTabs =
            new Dictionary<string, List<HbWaitingTab>>(StringComparer.Ordinal);

        private static readonly object _downloadStateLock = new object();

        private struct HbProgress { public long Received; public long Total; }

        private struct HbWaitingTab { public string TabId; public string BookTitle; }

        /// <summary>Live byte progress for <c>hbDownloadProgress</c>, or null when nothing is
        /// downloading for this id.</summary>
        public bool TryGetDownloadProgress(string bookId, out long received, out long total)
        {
            lock (_downloadStateLock)
            {
                HbProgress p;
                if (_progress.TryGetValue(bookId, out p)) { received = p.Received; total = p.Total; return true; }
            }
            received = 0; total = 0;
            return false;
        }

        /// <summary>Aborts an in-flight download. Returns whether there was one to abort;
        /// calling it when nothing is running is not an error.</summary>
        public bool CancelDownload(string bookId)
        {
            CancellationTokenSource cancellation;
            lock (_downloadStateLock)
            {
                if (!_downloads.TryGetValue(bookId, out cancellation)) return false;
            }
            try { cancellation.Cancel(); }
            catch (ObjectDisposedException) { /* it already ended — same outcome */ }
            return true;
        }

        /// <summary>
        /// Fetches a book's PDF and pushes hbPdfReady when it lands, or hbPdfCancelled when it
        /// does not. Fire-and-forget: the caller has already replied to the frontend, which sits
        /// on the download placeholder until one of those events arrives.
        /// </summary>
        private void StartDownload(string bookId, string bookTitle, string tabId, string localFolder)
        {
            Log("Downloading " + bookId + " over HttpClient");
            var ignored = DownloadAsync(bookId, bookTitle, tabId, localFolder);
        }

        private async Task DownloadAsync(string bookId, string bookTitle, string tabId, string localFolder)
        {
            // One transfer per book at a time. Two tabs opening the same book (a double-click, or
            // a restore racing a fresh trigger) would otherwise write the same .part path, and
            // the loser's cleanup would tear down the winner's progress and cancel entries.
            //
            // The second caller does not start a transfer, but it still owns a tab sitting on the
            // download placeholder, so it registers its tab against the in-flight download and is
            // told the outcome when that one lands. Dropping it here would spin that tab forever.
            var cancellation = new CancellationTokenSource();
            lock (_downloadStateLock)
            {
                if (_downloads.ContainsKey(bookId))
                {
                    Log("Download for " + bookId + " already in flight — joining it");
                    cancellation.Dispose();
                    List<HbWaitingTab> waiting;
                    if (!_waitingTabs.TryGetValue(bookId, out waiting))
                    {
                        waiting = new List<HbWaitingTab>();
                        _waitingTabs[bookId] = waiting;
                    }
                    waiting.Add(new HbWaitingTab { TabId = tabId, BookTitle = bookTitle });
                    return;
                }
                _downloads[bookId] = cancellation;
                _progress[bookId] = new HbProgress { Received = 0, Total = 0 };
            }

            string partPath = null;
            try
            {
                CancellationToken token = cancellation.Token;
                string url = string.Format(DownloadUrlFormat, bookId);

                using (var response = await _http
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token)
                    .ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        Log("Download failed for " + bookId + ": HTTP " + (int)response.StatusCode);
                        PushCancelled(tabId, notFound: response.StatusCode == HttpStatusCode.NotFound, bookId: bookId);
                        return;
                    }

                    long total = response.Content.Headers.ContentLength ?? 0; // 0 = server didn't say
                    SetProgress(bookId, 0, total);

                    using (var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        // Peek the signature WITHOUT buffering the body — a missing book answers
                        // with an HTML message page rather than a 404.
                        byte[] signature = new byte[PdfSignatureLength];
                        int signatureLength = await ReadFullyAsync(body, signature, token).ConfigureAwait(false);
                        if (!IsPdfSignature(signature, signatureLength))
                        {
                            Log("Download for " + bookId + " was not a PDF — treating as not found");
                            PushCancelled(tabId, notFound: true, bookId: bookId);
                            return;
                        }

                        bool intoLocalFolder;
                        string destination = ChooseDestination(localFolder, bookId, out intoLocalFolder);

                        // Written to a .part first and moved into place only once complete, so a
                        // failed or cancelled download can never leave a truncated PDF that the
                        // cache-hit check would later trust.
                        partPath = destination + ".part";
                        long received = signatureLength;

                        using (var file = new FileStream(
                            partPath, FileMode.Create, FileAccess.Write, FileShare.None,
                            CopyBufferBytes, useAsync: true))
                        {
                            await file.WriteAsync(signature, 0, signatureLength, token).ConfigureAwait(false);
                            SetProgress(bookId, received, total);

                            byte[] buffer = new byte[CopyBufferBytes];
                            int read;
                            while ((read = await body.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                            {
                                await file.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
                                received += read;
                                SetProgress(bookId, received, total);
                            }
                        }

                        MoveIntoPlace(partPath, destination);
                        partPath = null;
                        if (!intoLocalFolder) EvictCache(keepBookId: bookId);

                        // Registering the virtual host touches the WebView2, so it has to happen
                        // on the UI thread — as does the push, to stay ordered with it.
                        var joined = TakeWaitingTabs(bookId);
                        InvokeOnOwner(() =>
                        {
                            string resultUrl = intoLocalFolder
                                ? RegisterLocalBookHost(destination, bookId)
                                : CacheUrl(bookId);
                            _bridge.PushEvent(new { @event = "hbPdfReady", url = resultUrl, bookId, bookTitle, tabId });
                            // Tabs that asked for this same book while it was downloading get the
                            // very same file, under their own tab id and title.
                            if (joined != null)
                                foreach (var w in joined)
                                    _bridge.PushEvent(new { @event = "hbPdfReady", url = resultUrl, bookId, bookTitle = w.BookTitle, tabId = w.TabId });
                        });
                    }
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // The ביטול button. The tab is already reset by the caller, so say nothing more
                // than "this one is over".
                Log("Download cancelled for " + bookId);
                PushCancelled(tabId, cancelled: true, bookId: bookId);
            }
            catch (OperationCanceledException)
            {
                // Not our token: on net48 an HttpClient.Timeout expiry arrives as a
                // TaskCanceledException, which derives from this. A book too slow to finish
                // inside the timeout is a failure to report, not a cancel to stay quiet about.
                Log("Download timed out for " + bookId);
                PushCancelled(tabId, noInternet: true, bookId: bookId);
            }
            catch (HttpRequestException ex)
            {
                // No internet, DNS failure, connection reset — the case that used to strand the
                // whole window on the WebView error page.
                Log("Download network error for " + bookId + ": " + ex.Message);
                PushCancelled(tabId, noInternet: true, bookId: bookId);
            }
            catch (Exception ex)
            {
                Log("Download error for " + bookId + ": " + ex.Message);
                PushCancelled(tabId, bookId: bookId);
            }
            finally
            {
                // Giving up ownership and taking the waiting list must happen under ONE lock.
                // Between them, another call for this same book can take the lock, find
                // _downloads empty, become the new owner and start collecting its own waiters —
                // which this block would then cancel out from under a download that is still
                // running. Releasing and draining together makes the handoff clean.
                //
                // Anything drained here joined too late for the outcome paths above to tell it,
                // and must not be left on the placeholder. Normally empty.
                List<HbWaitingTab> stragglers;
                lock (_downloadStateLock)
                {
                    _progress.Remove(bookId);
                    _downloads.Remove(bookId);
                    if (!_waitingTabs.TryGetValue(bookId, out stragglers)) stragglers = null;
                    else _waitingTabs.Remove(bookId);
                }
                cancellation.Dispose();
                if (partPath != null) try { File.Delete(partPath); } catch (Exception) { }

                if (stragglers != null)
                    InvokeOnOwner(() =>
                    {
                        foreach (var w in stragglers)
                            _bridge.PushEvent(new { @event = "hbPdfCancelled", tabId = w.TabId, notFound = false, noInternet = false, cancelled = false });
                    });
            }
        }

        private static void SetProgress(string bookId, long received, long total)
        {
            lock (_downloadStateLock)
            {
                if (_downloads.ContainsKey(bookId))
                    _progress[bookId] = new HbProgress { Received = received, Total = total };
            }
        }

        /// <summary>The user's folder when it is set and we can create it, else the app cache.
        /// Creating it is the writability test: a folder we cannot make is one we cannot write a
        /// PDF into either — which is what a disconnected external drive looks like.</summary>
        private static string ChooseDestination(string localFolder, string bookId, out bool intoLocalFolder)
        {
            if (!string.IsNullOrWhiteSpace(localFolder))
            {
                try
                {
                    Directory.CreateDirectory(localFolder);
                    intoLocalFolder = true;
                    return Path.Combine(localFolder, bookId + ".pdf");
                }
                catch (Exception ex)
                {
                    Log("Local folder unavailable, falling back to cache: " + ex.Message);
                }
            }

            intoLocalFolder = false;
            Directory.CreateDirectory(HbCacheDir);
            return GetCachePath(bookId);
        }

        private static void MoveIntoPlace(string partPath, string destination)
        {
            try { if (File.Exists(destination)) File.Delete(destination); } catch (Exception) { }
            File.Move(partPath, destination);
        }

        /// <summary>Reads until the buffer is full or the stream ends — one ReadAsync is not
        /// guaranteed to return all 5 signature bytes.</summary>
        private static async Task<int> ReadFullyAsync(Stream stream, byte[] buffer, CancellationToken token)
        {
            int filled = 0;
            while (filled < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer, filled, buffer.Length - filled, token).ConfigureAwait(false);
                if (read == 0) break;
                filled += read;
            }
            return filled;
        }

        private static bool IsPdfSignature(byte[] buffer, int length) =>
            length >= PdfSignatureLength &&
            buffer[0] == (byte)'%' && buffer[1] == (byte)'P' && buffer[2] == (byte)'D' &&
            buffer[3] == (byte)'F' && buffer[4] == (byte)'-';

        /// <summary>Reports a download that will not produce a file, to the tab that started it
        /// and to any that joined it — none of them are getting a PDF, so none may be left on the
        /// placeholder. <paramref name="bookId"/> is null for a failure with nobody joined.
        ///
        /// `cancelled` is deliberately NOT passed on to the joined tabs. It means "this tab
        /// already tore itself down before asking us to stop", which is true only of the tab that
        /// pressed ביטול; the frontend takes it as "leave the tab alone". A joined tab never
        /// pressed anything and is still sitting on the placeholder, so it needs an ordinary
        /// failure it will actually act on.</summary>
        private void PushCancelled(string tabId, bool notFound = false, bool noInternet = false, bool cancelled = false, string bookId = null)
        {
            var joined = bookId == null ? null : TakeWaitingTabs(bookId);
            InvokeOnOwner(() =>
            {
                _bridge.PushEvent(new { @event = "hbPdfCancelled", tabId, notFound, noInternet, cancelled });
                if (joined != null)
                    foreach (var w in joined)
                        _bridge.PushEvent(new { @event = "hbPdfCancelled", tabId = w.TabId, notFound, noInternet, cancelled = false });
            });
        }

        /// <summary>Takes the tabs that joined this book's in-flight download, clearing the list.
        /// Called once as the download settles, so each of them gets the same outcome.</summary>
        private static List<HbWaitingTab> TakeWaitingTabs(string bookId)
        {
            lock (_downloadStateLock)
            {
                List<HbWaitingTab> waiting;
                if (!_waitingTabs.TryGetValue(bookId, out waiting)) return null;
                _waitingTabs.Remove(bookId);
                return waiting;
            }
        }

        /// <summary>Runs an action on the UI thread. The download completes on a pool thread, but
        /// the bridge and the WebView2 are the owner's.</summary>
        private void InvokeOnOwner(Action action)
        {
            try
            {
                if (_owner.IsDisposed || _webView.IsDisposed) return;
                if (_owner.InvokeRequired) _owner.Invoke(action);
                else action();
            }
            catch (ObjectDisposedException) { /* the window closed mid-download */ }
            catch (InvalidOperationException) { /* handle gone between the check and the invoke */ }
        }

        private static void Log(string msg) => System.Diagnostics.Debug.WriteLine("[HbHandler] " + msg);

        private void NavigateSafe(string url)
        {
            if (_owner.IsDisposed || _webView.IsDisposed) return;
            try
            {
                _owner.Invoke(new Action(() =>
                {
                    if (!_owner.IsDisposed && !_webView.IsDisposed && _webView.CoreWebView2 != null)
                        _webView.CoreWebView2.Navigate(url);
                }));
            }
            catch (Exception) { }
        }


        private static string GetCachePath(string bookId) =>
            Path.Combine(HbCacheDir, bookId + ".pdf");

        private static string CacheUrl(string bookId) =>
            "http://KitveiHakodesh-vue-app/hebrewbooks-cache/" + bookId + ".pdf";

        /// <param name="keepBookId">A book that must survive this pass — the one just downloaded,
        /// whose URL is about to be handed to the frontend. NTFS has last-access updates off by
        /// default, so the existing files' stamps are stale and a fresh arrival can sort oldest
        /// and be deleted before it is ever opened.</param>
        private static void EvictCache(string keepBookId = null)
        {
            if (!Directory.Exists(HbCacheDir)) return;
            var files = new DirectoryInfo(HbCacheDir).GetFiles("*.pdf");
            if (files.Length <= 10) return;
            Array.Sort(files, (a, b) => a.LastAccessTimeUtc.CompareTo(b.LastAccessTimeUtc));
            string keepName = keepBookId == null ? null : keepBookId + ".pdf";
            int over = files.Length - 10;
            for (int i = 0; i < files.Length && over > 0; i++)
            {
                if (keepName != null && string.Equals(files[i].Name, keepName, StringComparison.OrdinalIgnoreCase)) continue;
                try { files[i].Delete(); } catch { }
                over--;
            }
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Length > 80 ? name.Substring(0, 80) : name;
        }

        /// <summary>
        /// Releases the virtual host mappings this instance registered on its WebView2, so
        /// WebView2 does not keep folder handles after teardown. Mirrors
        /// <c>LocalFileHandler.DisposeAllHosts</c>, which this class was left out of.
        /// The static folder-to-host NAME registry is intentionally kept: it only maps a
        /// folder to a stable host string, holds no OS resource, and reusing the same name
        /// across instances is the point of it being static.
        /// </summary>
        public void DisposeAllHosts()
        {
            foreach (string hostName in _registeredOnThisWebView)
            {
                try { _webView.CoreWebView2?.ClearVirtualHostNameToFolderMapping(hostName); } catch { }
            }
            _registeredOnThisWebView.Clear();
        }
    }
}

