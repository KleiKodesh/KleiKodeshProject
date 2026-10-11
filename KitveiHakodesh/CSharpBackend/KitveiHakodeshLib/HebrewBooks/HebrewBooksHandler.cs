using KitveiHakodeshLib.Bridge;
using KitveiHakodeshLib.Settings;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace KitveiHakodeshLib.HebrewBooks
{
    /// <summary>
    /// The HebrewBooks flows that only the WebView2 host can do. Everything else about
    /// HebrewBooks - the catalog search, the download, the cache, the local-folder checks -
    /// is the KitveiHakodesh service's, and the page asks it directly.
    ///
    /// Save As navigates the WebView2 at the download endpoint, because DownloadStarting is
    /// what supplies the native Save dialog's file path. Reveal opens Explorer on the local
    /// file. Both need a window; neither touches data.
    /// </summary>
    public class HebrewBooksHandler
    {
        private readonly WebBridge _bridge;
        private readonly WebView2 _webView;
        private readonly Control _owner;

        private HbSaveAsInfo? _pendingSaveAs;

        private struct HbSaveAsInfo { public string BookId; public string BookTitle; }

        public HebrewBooksHandler(WebBridge bridge, WebView2 webView, Control owner)
        {
            _bridge = bridge;
            _webView = webView;
            _owner = owner;
        }

        // Called from AppViewer when navigation to hebrewbooks.org/message.aspx is detected,
        // meaning the requested book does not exist on the server. Only Save As can land here.
        internal void NotifyBookNotFound()
        {
            _pendingSaveAs = null;
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
                            dlg.Title    = HebrewBooksTexts.SaveAsDialogTitle;
                            dlg.Filter   = "PDF (*.pdf)|*.pdf";
                            dlg.FileName = suggestedName;
                            if (dlg.ShowDialog() == DialogResult.OK) dest = dlg.FileName;
                        }
                    }));

                    if (dest == null) { e.Cancel = true; return; }

                    e.ResultFilePath = dest;
                    return;
                }

                // Anything else reaching DownloadStarting is not ours: book downloads are the
                // service's (HttpClient), so the only WebView2 download left is the Save As above.
            }
            catch (Exception ex)
            {
                Log("OnDownloadStarting exception: " + ex.Message);
                // Cancel the download rather than leaving state inconsistent
                try { e.Cancel = true; } catch { }
            }
        }

        /// <summary>
        /// Opens Windows Explorer with {bookId}.pdf selected and highlighted -
        /// identical to VS Code's "Reveal in File Explorer" command (explorer.exe /select).
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
                    _bridge.Reply(id, new { error = HebrewBooksTexts.NoLocalFolderConfigured });
                    return;
                }

                string filePath = Path.Combine(localFolder, bookId + ".pdf");
                if (!File.Exists(filePath))
                {
                    _bridge.Reply(id, new { notFound = true });
                    return;
                }

                Process.Start("explorer.exe", "/select,\"" + filePath + "\"");
                _bridge.Reply(id, new { ok = true });
            }
            catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); }
        }

        /// <summary>A HebrewBooks id is a run of digits; anything else never reaches a URL or a path.</summary>
        private static bool IsValidBookId(string bookId) =>
            !string.IsNullOrEmpty(bookId) && Regex.IsMatch(bookId, @"^[0-9]+\z");

        private static void Log(string msg) => Debug.WriteLine("[HbHandler] " + msg);

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

        private static string MakeSafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Length > 80 ? name.Substring(0, 80) : name;
        }
    }
}
