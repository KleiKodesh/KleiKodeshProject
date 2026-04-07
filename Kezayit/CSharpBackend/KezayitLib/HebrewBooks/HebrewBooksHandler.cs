using KezayitLib.Bridge;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace KezayitLib.HebrewBooks
{
    /// <summary>
    /// Handles HebrewBooks PDF restore, download-to-cache, and Save As flows.
    /// Intercepts WebView2 downloads via DownloadStarting.
    /// </summary>
    public class HebrewBooksHandler
    {
        private static readonly string HbCacheDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kezayit", "cache", "hebrewbooks");

        private readonly WebBridge _bridge;
        private readonly WebView2 _webView;
        private readonly Control _owner;

        private HbDownloadInfo? _pendingDownload;
        private HbSaveAsInfo? _pendingSaveAs;

        private struct HbDownloadInfo { public string BookId; public string BookTitle; public string TabId; }
        private struct HbSaveAsInfo { public string BookId; public string BookTitle; }

        public HebrewBooksHandler(WebBridge bridge, WebView2 webView, Control owner)
        {
            _bridge = bridge;
            _webView = webView;
            _owner = owner;
        }

        public void HandleRestoreHbPdf(JsonElement root, string id)
        {
            string bookId    = root.GetProperty("bookId").GetString();
            string bookTitle = root.GetProperty("bookTitle").GetString();
            string tabId     = root.GetProperty("tabId").GetString();
            string cached    = GetCachePath(bookId, bookTitle);

            if (File.Exists(cached)) { _bridge.Reply(id, new { url = CacheUrl(cached) }); return; }

            // Cache miss — must re-download through the browser (direct HTTP is blocked by HebrewBooks)
            _bridge.Reply(id, new { redownload = true });
            _pendingDownload = new HbDownloadInfo { BookId = bookId, BookTitle = bookTitle, TabId = tabId };
            NavigateSafe("https://download.hebrewbooks.org/downloadhandler.ashx?req=" + bookId);
        }

        public void HandleTriggerHbDownload(JsonElement root, string id)
        {
            _bridge.Reply(id, new { ok = true });
            string bookId    = root.GetProperty("bookId").GetString();
            string bookTitle = root.GetProperty("bookTitle").GetString();
            string url       = root.GetProperty("url").GetString();
            string tabId     = root.GetProperty("tabId").GetString();

            string cached = GetCachePath(bookId, bookTitle);
            if (File.Exists(cached)) { _bridge.PushEvent(new { @event = "hbPdfReady", url = CacheUrl(cached), bookId, bookTitle, tabId }); return; }

            Log("Navigating to: " + url);
            _pendingDownload = new HbDownloadInfo { BookId = bookId, BookTitle = bookTitle, TabId = tabId };
            NavigateSafe(url);
        }

        public void HandleTriggerHbSaveAs(JsonElement root, string id)
        {
            _bridge.Reply(id, new { ok = true });
            string bookId    = root.GetProperty("bookId").GetString();
            string bookTitle = root.GetProperty("bookTitle").GetString();
            string url       = root.GetProperty("url").GetString();

            _pendingSaveAs = new HbSaveAsInfo { BookId = bookId, BookTitle = bookTitle };
            NavigateSafe(url);
        }

        public void OnDownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs e)
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

                if (dest == null)
                {
                    e.Cancel = true;
                    return;
                }

                e.ResultFilePath = dest;
                // Do NOT set e.Handled — let the browser show its download progress UI
                return;
            }

            if (!_pendingDownload.HasValue) return;

            var info = _pendingDownload.Value;
            _pendingDownload = null;

            string cacheDest = GetCachePath(info.BookId, info.BookTitle);
            Directory.CreateDirectory(HbCacheDir);
            e.ResultFilePath = cacheDest;
            // Do NOT set e.Handled — let the browser show its download dialog so the user sees progress

            e.DownloadOperation.StateChanged += (s, _) =>
            {
                var op = (CoreWebView2DownloadOperation)s;
                if (op.State == CoreWebView2DownloadState.Completed)
                {
                    EvictCache();
                    _owner.Invoke(new Action(() =>
                    {
                        CloseDownloadDialogSafe();
                        _bridge.PushEvent(new { @event = "hbPdfReady", url = CacheUrl(cacheDest), bookId = info.BookId, bookTitle = info.BookTitle, tabId = info.TabId });
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
            };
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

        private static string GetCachePath(string bookId, string bookTitle)
        {
            string safe = MakeSafeFileName(bookTitle + "-" + bookId);
            return Path.Combine(HbCacheDir, safe + ".pdf");
        }

        private static string CacheUrl(string path) =>
            "http://kezayit-vue-app/cache/hebrewbooks/" + Path.GetFileName(path);

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
