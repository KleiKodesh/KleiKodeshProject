using KitveiHakodeshLib.Bridge;
using KitveiHakodeshLib.Settings;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace KitveiHakodeshLib.HebrewBooks
{
    /// <summary>
    /// Handles HebrewBooks PDF restore, download-to-cache, and Save As flows.
    /// Intercepts WebView2 downloads via DownloadStarting.
    /// </summary>
    public class HebrewBooksHandler
    {
        private static readonly string HbCacheDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KitveiHakodesh", "hebrewbooks-cache");

        private readonly WebBridge _bridge;
        private readonly WebView2 _webView;
        private readonly Control _owner;

        private HbDownloadInfo? _pendingDownload;
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

        private struct HbDownloadInfo { public string BookId; public string BookTitle; public string TabId; public string DestFolder; }
        private struct HbSaveAsInfo { public string BookId; public string BookTitle; }

        public HebrewBooksHandler(WebBridge bridge, WebView2 webView, Control owner)
        {
            _bridge = bridge;
            _webView = webView;
            _owner = owner;
        }

        // Called from AppViewer when navigation to hebrewbooks.org/message.aspx is detected,
        // meaning the requested book does not exist on the server.
        internal void NotifyBookNotFound()
        {
            string tabId = _pendingDownload.HasValue ? _pendingDownload.Value.TabId : null;
            _pendingDownload = null;
            _pendingSaveAs   = null;
            if (tabId != null)
                _bridge.PushEvent(new { @event = "hbPdfCancelled", tabId, notFound = true });
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

                // Cache miss — must re-download.
                _bridge.Reply(id, new { redownload = true });
                _pendingDownload = new HbDownloadInfo { BookId = bookId, BookTitle = bookTitle, TabId = tabId, DestFolder = localFolder };
                NavigateSafe("https://download.hebrewbooks.org/downloadhandler.ashx?req=" + bookId);
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
                string url         = root.GetProperty("url").GetString();
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

                // 3. Download required — check connectivity before navigating.
                if (!isOnline)
                {
                    _bridge.PushEvent(new { @event = "hbPdfCancelled", tabId, noInternet = true });
                    return;
                }

                Log("Navigating to: " + url);
                _pendingDownload = new HbDownloadInfo { BookId = bookId, BookTitle = bookTitle, TabId = tabId, DestFolder = localFolder };
                NavigateSafe(url);
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
                Log("OnDownloadStarting: uri=" + e.DownloadOperation.Uri + " pendingDownload=" + _pendingDownload.HasValue + " pendingSaveAs=" + _pendingSaveAs.HasValue);
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

                if (!_pendingDownload.HasValue) return;

                var info = _pendingDownload.Value;
                _pendingDownload = null;

                bool useLocalFolder = false;
                string destDir  = HbCacheDir;
                if (!string.IsNullOrWhiteSpace(info.DestFolder))
                {
                    // Verify the configured local folder is actually reachable before
                    // committing the download destination to it.  If the folder is on a
                    // removable drive that has been disconnected, or the path is otherwise
                    // inaccessible, fall back silently to the app cache so the download
                    // can still complete.
                    try
                    {
                        Directory.CreateDirectory(info.DestFolder);
                        useLocalFolder = true;
                        destDir = info.DestFolder;
                    }
                    catch (Exception ex)
                    {
                        Log("Local folder unavailable during download, falling back to cache: " + ex.Message);
                    }
                }

                string destFile = Path.Combine(destDir, info.BookId + ".pdf");
                if (!useLocalFolder) Directory.CreateDirectory(destDir);
                e.ResultFilePath = destFile;

                e.DownloadOperation.StateChanged += (s, _) =>
                {
                    try
                    {
                        var op = (CoreWebView2DownloadOperation)s;
                        if (op.State == CoreWebView2DownloadState.Completed)
                        {
                            // Only evict the app cache — never touch the user's local folder.
                            if (!useLocalFolder) EvictCache();
                            string resultUrl = useLocalFolder
                                ? RegisterLocalBookHost(destFile, info.BookId)
                                : CacheUrl(info.BookId);
                            _owner.Invoke(new Action(() =>
                            {
                                CloseDownloadDialogSafe();
                                _bridge.PushEvent(new { @event = "hbPdfReady", url = resultUrl, bookId = info.BookId, bookTitle = info.BookTitle, tabId = info.TabId });
                            }));
                        }
                        else if (op.State == CoreWebView2DownloadState.Interrupted)
                        {
                            _owner.Invoke(new Action(() =>
                            {
                                CloseDownloadDialogSafe();
                                _bridge.PushEvent(new { @event = "hbPdfCancelled", tabId = info.TabId });
                            }));
                        }
                    }
                    catch (Exception ex) { Log("StateChanged exception: " + ex.Message); }
                };
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

        private void CloseDownloadDialogSafe()
        {
            if (_owner.IsDisposed || _webView.IsDisposed) return;
            try
            {
                if (!_owner.IsDisposed && !_webView.IsDisposed && _webView.CoreWebView2 != null)
                    _webView.CoreWebView2.CloseDefaultDownloadDialog();
            }
            catch (Exception) { }
        }

        private static string GetCachePath(string bookId) =>
            Path.Combine(HbCacheDir, bookId + ".pdf");

        private static string CacheUrl(string bookId) =>
            "http://KitveiHakodesh-vue-app/hebrewbooks-cache/" + bookId + ".pdf";

        private static void EvictCache()
        {
            if (!Directory.Exists(HbCacheDir)) return;
            var files = new DirectoryInfo(HbCacheDir).GetFiles("*.pdf");
            if (files.Length <= 10) return;
            Array.Sort(files, (a, b) => a.LastAccessTimeUtc.CompareTo(b.LastAccessTimeUtc));
            for (int i = 0; i < files.Length - 10; i++) try { files[i].Delete(); } catch { }
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Length > 80 ? name.Substring(0, 80) : name;
        }
    }
}

