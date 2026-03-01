using Microsoft.Web.WebView2.WinForms;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Zayit.Viewer;

namespace Zayit.Services
{
    /// <summary>
    /// Global PDF Service - Handles general PDF operations (file picker, virtual URLs, temp files).
    /// Non-PDF files are converted via a background task; the UI shows a progress dialog throughout.
    /// Converted PDFs are cached on disk (LRU, max 10 entries) to avoid redundant conversions.
    /// </summary>
    public class PdfService
    {
        private readonly WebView2 _webView;
        private const string PDF_HOST = "zayitHost";
        private const int CacheMax = 10;

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

        public bool CheckPdfManagerReady() => _webView?.CoreWebView2 != null;

        #endregion

        #region File Picker

        /// <summary>
        /// Opens a file picker for PDFs or Word/HTML documents.
        /// Non-PDF files are converted asynchronously on a background thread.
        /// A non-blocking progress dialog is shown during conversion.
        /// Results are cached (LRU-10) in the AppDomain to skip repeated conversions.
        /// </summary>
        public async Task<object> OpenPdfOrWordFilePickerAsync()
        {
            try
            {
                var filter =
                    "מסמכים (*.pdf;*.doc;*.docx;*.dot;*.dotx;*.docm;*.dotm;*.rtf;*.odt;*.txt;*.wps;*.xml;*.mht;*.mhtml;*.htm;*.html)" +
                    "|*.pdf;*.doc;*.docx;*.dot;*.dotx;*.docm;*.dotm;*.rtf;*.odt;*.txt;*.wps;*.xml;*.mht;*.mhtml;*.htm;*.html";

                var filePath = await WebViewDialogHelper.ShowOpenFileDialogAsync(
                    _webView, filter, "בחר קובץ PDF או Word");

                if (string.IsNullOrEmpty(filePath))
                    return EmptyResult();

                string finalPath = filePath;

                if (!filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    finalPath = await ConvertWithProgressAsync(filePath);

                    // Conversion failed or was cancelled
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
        /// Returns a cached PDF path if available; otherwise converts the file on a
        /// background thread while showing a WinForms progress dialog with a marquee bar.
        /// </summary>
        private async Task<string> ConvertWithProgressAsync(string filePath)
        {
            var cacheDir = GetConversionCacheDir();

            // ── Check disk cache ─────────────────────────────────────────────────
            var cachedPdf = FindInCache(cacheDir, filePath);
            if (cachedPdf != null && File.Exists(cachedPdf))
            {
                Console.WriteLine($"[PdfService] Cache hit for: {filePath}");
                File.SetLastAccessTime(cachedPdf, DateTime.Now); // Update LRU
                return cachedPdf;
            }

            Console.WriteLine($"[PdfService] Starting conversion: {filePath}");

            string pdfPath = null;
            Exception conversionError = null;

            bool isHtmlTxt = filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                             && WordToPdfConverter.TxtFileContainsHtml(filePath);

            // Create output path with hash + original filename
            var outputFileName = GetCacheFileName(filePath);
            var outputPath = Path.Combine(cacheDir, outputFileName);

            Console.WriteLine($"[PdfService] Output path: {outputPath}");

            try
            {
                pdfPath = isHtmlTxt
                    ? await WordToPdfConverter.ConvertHtmlToPdfAsync(_webView, filePath, outputPath)
                    : await WordToPdfConverter.ConvertWordToPdfAsync(_webView, filePath, outputPath);

                Console.WriteLine($"[PdfService] Conversion returned: {pdfPath}");
                Console.WriteLine($"[PdfService] File exists: {File.Exists(pdfPath)}");
            }
            catch (Exception ex)
            {
                conversionError = ex;
            }

            if (conversionError != null)
            {
                Console.WriteLine($"[PdfService] Conversion failed: {conversionError}");
                MessageBox.Show(
                    $"המרת הקובץ נכשלה:\n{conversionError.Message}",
                    "שגיאה בהמרה",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1,
                    MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
                return null;
            }

            if (pdfPath != null)
            {
                // ── Enforce LRU cache limit ──────────────────────────────────────
                EnforceCacheLimit(cacheDir);
                Console.WriteLine($"[PdfService] Cached conversion: {filePath} -> {pdfPath}");
            }

            return pdfPath;
        }

        /// <summary>Builds a borderless RTL progress dialog with a marquee bar.</summary>
        private static Form BuildProgressForm(string fileName)
        {
            var form = new Form
            {
                Text = "ממיר קובץ…",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                Size = new System.Drawing.Size(360, 120),
                MinimizeBox = false,
                MaximizeBox = false,
                ControlBox = false,
                RightToLeft = RightToLeft.Yes,
                RightToLeftLayout = true,
                TopMost = true
            };

            var label = new Label
            {
                Text = $"ממיר את הקובץ \"{fileName}\" ל-PDF…",
                Dock = DockStyle.Top,
                Height = 36,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Padding = new Padding(8, 8, 8, 0)
            };

            var bar = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                Dock = DockStyle.Bottom,
                Height = 20,
                MarqueeAnimationSpeed = 30
            };

            form.Controls.Add(label);
            form.Controls.Add(bar);
            return form;
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
                var tempPath = Path.Combine(GetHtmlPath(), "temp", fileName);
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
                var tempDir = Path.Combine(GetHtmlPath(), "temp");
                if (!Directory.Exists(tempDir))
                    return new { fileCount = 0, totalSize = 0 };

                var files = Directory.GetFiles(tempDir);
                long totalSize = 0;
                foreach (var file in files)
                    totalSize += new FileInfo(file).Length;

                return new { fileCount = files.Length, totalSize };
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
                var tempDir = Path.Combine(GetHtmlPath(), "temp");
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

        private string GetConversionCacheDir()
        {
            var htmlPath = GetHtmlPath();
            var cacheDir = Path.Combine(htmlPath, "pdfconversioncache");
            Directory.CreateDirectory(cacheDir);
            return cacheDir;
        }

        private string GetCacheFileName(string sourceFilePath)
        {
            // Create hash from full path
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sourceFilePath.ToLowerInvariant()));
                var hash = BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 16);
                var originalName = Path.GetFileNameWithoutExtension(sourceFilePath);
                return $"{originalName}_{hash}.pdf";
            }
        }

        private string FindInCache(string cacheDir, string sourceFilePath)
        {
            try
            {
                var expectedFileName = GetCacheFileName(sourceFilePath);
                var cachedPath = Path.Combine(cacheDir, expectedFileName);

                if (File.Exists(cachedPath))
                    return cachedPath;

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfService] Error finding in cache: {ex}");
                return null;
            }
        }

        private void EnforceCacheLimit(string cacheDir)
        {
            try
            {
                var files = Directory.GetFiles(cacheDir, "*.pdf");

                if (files.Length <= CacheMax)
                    return;

                // Sort by last access time (oldest first)
                var fileInfos = files.Select(f => new FileInfo(f))
                    .OrderBy(fi => fi.LastAccessTime)
                    .ToArray();

                // Delete oldest files until we're at the limit
                int toDelete = fileInfos.Length - CacheMax;
                for (int i = 0; i < toDelete; i++)
                {
                    fileInfos[i].Delete();
                    Console.WriteLine($"[PdfService] LRU evicted: {fileInfos[i].Name}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfService] Error enforcing cache limit: {ex}");
            }
        }

        #endregion
    }
}