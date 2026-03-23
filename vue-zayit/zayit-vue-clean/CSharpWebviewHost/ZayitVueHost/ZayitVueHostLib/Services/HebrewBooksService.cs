using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Zayit.Services
{
    public static class HebrewBooksService
    {
        private static readonly HttpClient _http = new HttpClient();
        private const int CacheMax = 10;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Returns cached relative URL if available, otherwise signals Vue to trigger download.</summary>
        public static async Task<object> PrepareForViewingAsync(WebView2 webView, string bookId, string title)
        {
            var fileName = SafeName(title, bookId) + ".pdf";
            var cached = CachedPath(webView, fileName);

            if (File.Exists(cached))
                return new { success = true, cached = true, fileName, url = $"hebrewbookscache/{fileName}" };

            // Not cached — attach one-shot download handler then let Vue trigger the download
            AttachDownloadHandler(webView, fileName, isViewing: true);
            return new { success = true, cached = false };
        }

        /// <summary>Shows SaveAs dialog then downloads (or copies from cache) to user's chosen path.</summary>
        public static async Task<object> PrepareForDownloadAsync(WebView2 webView, string bookId, string title)
        {
            var fileName = SafeName(title, bookId) + ".pdf";
            var cached = CachedPath(webView, fileName);

            var savePath = await PdfService.ShowSaveDialogAsync(webView, "PDF files (*.pdf)|*.pdf", "Save Hebrew Book", $"{title}.pdf");
            if (string.IsNullOrEmpty(savePath))
                return new { success = false, cancelled = true };

            if (File.Exists(cached))
            {
                File.Copy(cached, savePath, true);
                return new { success = true, filePath = savePath };
            }

            AttachDownloadHandler(webView, fileName, isViewing: false, savePath: savePath);
            return new { success = true, cached = false, targetPath = savePath };
        }

        public static object CheckCache(string bookId, string title)
        {
            // Can't resolve path without webView here — return a flag; viewer resolves path
            return new { bookId, title, note = "use prepareHebrewBook to check" };
        }

        // ── Download handler ──────────────────────────────────────────────────

        private static void AttachDownloadHandler(WebView2 webView, string fileName, bool isViewing, string savePath = null)
        {
            EventHandler<CoreWebView2DownloadStartingEventArgs> handler = null;
            handler = (sender, e) =>
            {
                if (!e.DownloadOperation.Uri.Contains("hebrewbooks.org")) return;

                webView.CoreWebView2.DownloadStarting -= handler; // one-shot

                var cacheDir = EnsureCacheDir(webView);
                var cachePath = Path.Combine(cacheDir, fileName);
                e.ResultFilePath = cachePath;
                e.Handled = true;

                EventHandler<object> stateHandler = null;
                stateHandler = (s2, _) =>
                {
                    if (e.DownloadOperation.State != CoreWebView2DownloadState.Completed) return;
                    e.DownloadOperation.StateChanged -= stateHandler;

                    EnforceCacheLimit(cacheDir);
                    webView.CoreWebView2.CloseDefaultDownloadDialog();

                    if (!isViewing && savePath != null)
                        File.Copy(cachePath, savePath, true);
                    else
                        _ = NotifyViewingReadyAsync(webView, fileName);
                };
                e.DownloadOperation.StateChanged += stateHandler;
            };

            webView.CoreWebView2.DownloadStarting += handler;
        }

        private static async Task NotifyViewingReadyAsync(WebView2 webView, string fileName)
        {
            var url = $"hebrewbookscache/{fileName}";
            var script = $"window.handleHebrewBookViewingReady&&window.handleHebrewBookViewingReady({{fileName:{Esc(fileName)},url:{Esc(url)},success:true}})";
            try { await webView.ExecuteScriptAsync(script); } catch { }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string EnsureCacheDir(WebView2 webView)
        {
            // Cache lives inside pdfjs/web so it's same-origin with the viewer
            var htmlPath = ResolveHtmlPath();
            var dir = Path.Combine(htmlPath, "pdfjs", "web", "hebrewbookscache");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string CachedPath(WebView2 webView, string fileName)
            => Path.Combine(ResolveHtmlPath(), "pdfjs", "web", "hebrewbookscache", fileName);

        private static void EnforceCacheLimit(string dir)
        {
            var files = new DirectoryInfo(dir).GetFiles("*.pdf");
            if (files.Length <= CacheMax) return;
            Array.Sort(files, (a, b) => a.LastAccessTime.CompareTo(b.LastAccessTime));
            for (int i = 0; i < files.Length - CacheMax; i++)
                try { files[i].Delete(); } catch { }
        }

        private static string SafeName(string title, string bookId)
        {
            var name = $"{title}_{bookId}";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        private static string Esc(string s) => System.Text.Json.JsonSerializer.Serialize(s);

        private static string ResolveHtmlPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var p = Path.Combine(baseDir, "zayit-vue-app");
            if (Directory.Exists(p)) return p;
            return Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "zayit-vue-app");
        }
    }
}
