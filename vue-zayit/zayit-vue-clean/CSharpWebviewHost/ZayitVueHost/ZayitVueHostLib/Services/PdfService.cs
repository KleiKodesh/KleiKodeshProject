using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Zayit.Services
{
    public static class PdfService
    {
        private const string PDF_HOST = "zayitHost";
        private const int CacheMax = 10;
        private static string _htmlPath;

        public static void Initialize(CoreWebView2 core, string htmlPath)
        {
            _htmlPath = htmlPath;
            core.SetVirtualHostNameToFolderMapping(PDF_HOST, htmlPath, CoreWebView2HostResourceAccessKind.Allow);
        }

        /// <summary>Opens a file picker for PDF/Word/HTML docs. Converts non-PDF to PDF. Returns { fileName, url, originalPath }.</summary>
        public static async Task<object> OpenFileAsync(WebView2 webView)
        {
            const string filter =
                "Documents (*.pdf;*.doc;*.docx;*.rtf;*.odt;*.txt;*.htm;*.html)|*.pdf;*.doc;*.docx;*.rtf;*.odt;*.txt;*.htm;*.html";

            string filePath = await ShowOpenDialogAsync(webView, filter, "Select file");
            if (string.IsNullOrEmpty(filePath)) return Empty();

            if (!filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                bool isHtml = filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                              && WordToPdfConverter.TxtFileContainsHtml(filePath);
                filePath = isHtml
                    ? await WordToPdfConverter.ConvertHtmlToPdfAsync(webView, filePath, CachePath(filePath))
                    : await WordToPdfConverter.ConvertWordToPdfAsync(webView, filePath, CachePath(filePath));
                if (filePath == null) return Empty();
            }

            EnforceCacheLimit();
            return new { fileName = Path.GetFileName(filePath), url = CreateUrl(filePath), originalPath = filePath };
        }

        public static string RecreateUrl(string originalPath)
            => File.Exists(originalPath) ? CreateUrl(originalPath) : null;

        public static void CleanupTemp(string fileName)
        {
            try
            {
                var path = Path.Combine(_htmlPath, "temp", fileName);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) { Console.WriteLine($"[PdfService] CleanupTemp: {ex.Message}"); }
        }

        // ── Internals ─────────────────────────────────────────────────────────

        private static string CreateUrl(string filePath)
        {
            var tempDir = Path.Combine(_htmlPath, "temp");
            Directory.CreateDirectory(tempDir);
            var name = Guid.NewGuid() + "_" + Path.GetFileName(filePath);
            File.Copy(filePath, Path.Combine(tempDir, name), true);
            return $"https://{PDF_HOST}/temp/{name}";
        }

        private static string CachePath(string source)
        {
            var dir = Path.Combine(_htmlPath, "pdfconversioncache");
            Directory.CreateDirectory(dir);
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var hash = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(source.ToLowerInvariant())))
                    .Replace("-", "").Substring(0, 16);
                return Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(source)}_{hash}.pdf");
            }
        }

        private static void EnforceCacheLimit()
        {
            var dir = Path.Combine(_htmlPath, "pdfconversioncache");
            if (!Directory.Exists(dir)) return;
            var files = new System.IO.DirectoryInfo(dir).GetFiles("*.pdf");
            if (files.Length <= CacheMax) return;
            Array.Sort(files, (a, b) => a.LastAccessTime.CompareTo(b.LastAccessTime));
            for (int i = 0; i < files.Length - CacheMax; i++)
                try { files[i].Delete(); } catch { }
        }

        private static object Empty() => new { fileName = (string)null, url = (string)null, originalPath = (string)null };

        internal static Task<string> ShowOpenDialogAsync(WebView2 webView, string filter, string title)
        {
            var tcs = new TaskCompletionSource<string>();
            void Show()
            {
                using var dlg = new OpenFileDialog { Filter = filter, Title = title };
                tcs.SetResult(dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null);
            }
            if (webView.InvokeRequired) webView.Invoke((Action)Show); else Show();
            return tcs.Task;
        }

        internal static Task<string> ShowSaveDialogAsync(WebView2 webView, string filter, string title, string defaultName = null)
        {
            var tcs = new TaskCompletionSource<string>();
            void Show()
            {
                using var dlg = new SaveFileDialog { Filter = filter, Title = title, FileName = defaultName ?? "" };
                tcs.SetResult(dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null);
            }
            if (webView.InvokeRequired) webView.Invoke((Action)Show); else Show();
            return tcs.Task;
        }
    }
}
