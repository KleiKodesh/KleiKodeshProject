using Microsoft.Web.WebView2.WinForms;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Zayit.Viewer;

namespace Zayit.Services
{
    /// <summary>
    /// Global PDF Service - Handles general PDF operations (file picker, virtual URLs, temp files).
    /// Conversion is fully async via <see cref="WordToPdfConverter"/> — the UI is never blocked.
    /// </summary>
    public class PdfService
    {
        private readonly WebView2 _webView;
        private const string PDF_HOST = "zayitHost";

        public PdfService(WebView2 webView)
        {
            _webView = webView;
        }

        #region PDF Manager Initialization

        public bool InitializePdfManager()
        {
            try
            {
                if (_webView?.CoreWebView2 == null) return false;

                var htmlPath = GetHtmlPath();
                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    PDF_HOST,
                    htmlPath,
                    Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow
                );

                Console.WriteLine($"[PdfService] Virtual host mapping set: {PDF_HOST} -> {htmlPath}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfService] Failed to initialize PDF manager: {ex}");
                return false;
            }
        }

        public bool CheckPdfManagerReady()
        {
            return _webView?.CoreWebView2 != null;
        }

        #endregion

        #region File Picker

        /// <summary>
        /// Opens a file picker for PDFs or Word/HTML documents.
        /// Non-PDF files are converted asynchronously on a background thread before
        /// a virtual URL is returned — the UI stays responsive throughout.
        /// </summary>
        public async Task<object> OpenPdfOrWordFilePickerAsync()
        {
            try
            {
                var filter = "מסמכים (*.pdf;*.doc;*.docx;*.dot;*.dotx;*.docm;*.dotm;*.rtf;*.odt;*.txt;*.wps;*.xml;*.mht;*.mhtml;*.htm;*.html)|*.pdf;*.doc;*.docx;*.dot;*.dotx;*.docm;*.dotm;*.rtf;*.odt;*.txt;*.wps;*.xml;*.mht;*.mhtml;*.htm;*.html";
                var filePath = await WebViewDialogHelper.ShowOpenFileDialogAsync(
                    _webView,
                    filter,
                    "בחר קובץ PDF או Word"
                );

                if (string.IsNullOrEmpty(filePath))
                    return EmptyResult();

                string finalPath = filePath;

                if (!filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    finalPath = await PromptAndConvertAsync(filePath);

                    // User declined the conversion dialog
                    if (finalPath == null)
                        return EmptyResult();
                }

                var virtualUrl = CreateVirtualUrl(finalPath);
                return new
                {
                    fileName = Path.GetFileName(finalPath),
                    dataUrl = virtualUrl,
                    originalPath = finalPath
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfService] Error in file picker: {ex}");
                return EmptyResult();
            }
        }

        /// <summary>
        /// Shows a confirmation dialog, then converts the file to PDF on a background thread.
        /// Returns the PDF path, or null if the user declined.
        /// </summary>
        private async Task<string> PromptAndConvertAsync(string filePath)
        {
            bool isHtmlTxt = filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                             && WordToPdfConverter.TxtFileContainsHtml(filePath);

            string extraNote = isHtmlTxt ? "הקובץ מכיל תוכן HTML.\n" : string.Empty;
            var msg = $"{extraNote}כדי לפתוח קובץ ממין זה נדרש להמירו לקובץ PDF.\n\n" +
                      $"הקובץ המומר יישמר באותה תיקייה כמו קובץ המקור:\n" +
                      $"📁 {Path.GetDirectoryName(filePath)}\n" +
                      $"📄 {Path.GetFileNameWithoutExtension(filePath)}.pdf\n\n" +
                      $"האם אתה בטוח שברצונך להמיר את הקובץ?";

            var result = MessageBox.Show(
                msg,
                "המרת קובץ ל-PDF",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2,
                MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading
            );

            if (result != DialogResult.Yes)
                return null;

            string pdfPath = Path.ChangeExtension(filePath, ".pdf");

            // Both conversion paths are async — Word Interop runs on a background thread
            // so the UI remains responsive during the (potentially slow) conversion.
            return isHtmlTxt
                ? await WordToPdfConverter.ConvertHtmlToPdfAsync(filePath, pdfPath)
                : await WordToPdfConverter.ConvertWordToPdfAsync(filePath, pdfPath);
        }

        private static object EmptyResult() =>
            new { fileName = (string)null, dataUrl = (string)null, originalPath = (string)null };

        #endregion

        #region Virtual URL / Temp Files

        public string CreateVirtualUrl(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return null;

                var htmlPath = GetHtmlPath();
                var tempDir = Path.Combine(htmlPath, "temp");
                Directory.CreateDirectory(tempDir);

                var uniqueName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(filePath);
                var tempPath = Path.Combine(tempDir, uniqueName);

                File.Copy(filePath, tempPath, true);

                var virtualUrl = $"https://{PDF_HOST}/temp/{uniqueName}";
                Console.WriteLine($"[PdfService] Created virtual URL: {filePath} -> {virtualUrl}");

                return virtualUrl;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfService] Error creating virtual URL: {ex}");
                return null;
            }
        }

        public string RecreateVirtualUrlFromPath(string originalPath)
        {
            try
            {
                if (!File.Exists(originalPath))
                {
                    Console.WriteLine($"[PdfService] Original file not found: {originalPath}");
                    return null;
                }

                return CreateVirtualUrl(originalPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfService] Error recreating virtual URL: {ex}");
                return null;
            }
        }

        public void CleanupTempFile(string fileName)
        {
            try
            {
                var htmlPath = GetHtmlPath();
                var tempPath = Path.Combine(htmlPath, "temp", fileName);
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                    Console.WriteLine($"[PdfService] Cleaned up temp file: {fileName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfService] Error cleaning up temp file: {ex}");
            }
        }

        public object GetTempFileStats()
        {
            try
            {
                var htmlPath = GetHtmlPath();
                var tempDir = Path.Combine(htmlPath, "temp");

                if (!Directory.Exists(tempDir))
                    return new { fileCount = 0, totalSize = 0 };

                var files = Directory.GetFiles(tempDir);
                long totalSize = 0;

                foreach (var file in files)
                    totalSize += new FileInfo(file).Length;

                return new { fileCount = files.Length, totalSize = totalSize };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfService] Error getting temp file stats: {ex}");
                return new { fileCount = 0, totalSize = 0 };
            }
        }

        public void ClearTempFiles()
        {
            try
            {
                var htmlPath = GetHtmlPath();
                var tempDir = Path.Combine(htmlPath, "temp");

                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                    Directory.CreateDirectory(tempDir);
                    Console.WriteLine("[PdfService] All temp files cleared");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfService] Error clearing temp files: {ex}");
            }
        }

        private string GetHtmlPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            var standardPath = Path.Combine(baseDir, "zayit-vue-app");
            if (Directory.Exists(standardPath))
                return Path.GetFullPath(standardPath);

            var assemblyPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var clickOncePath = Path.Combine(assemblyPath, "zayit-vue-app");
            if (Directory.Exists(clickOncePath))
                return Path.GetFullPath(clickOncePath);

            Console.WriteLine($"[PdfService] WARNING: zayit-vue-app folder not found! Tried:");
            Console.WriteLine($"  - {standardPath}");
            Console.WriteLine($"  - {clickOncePath}");
            return Path.GetFullPath(standardPath);
        }

        #endregion
    }
}