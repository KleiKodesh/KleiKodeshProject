using Microsoft.Web.WebView2.WinForms;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Zayit.Viewer;

namespace Zayit.Services
{
    /// <summary>
    /// Global PDF Service - Handles all general PDF operations (file picker, virtual URLs, temp files)
    /// Does NOT handle Hebrew books specific functionality - that's in HebrewBooksService
    /// </summary>
    public class PdfService
    {
        private readonly WebView2 _webView;
        private const string PDF_HOST = "zayitHost";

        public PdfService(WebView2 webView)
        {
            _webView = webView;
        }

        /// <summary>
        /// Initialize PDF manager and set up virtual host mapping
        /// </summary>
        public bool InitializePdfManager()
        {
            try
            {
                if (_webView?.CoreWebView2 == null) return false;

                // Set up virtual host mapping for PDF files
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

        /// <summary>
        /// Open file picker dialog and create virtual URL for selected PDF
        /// Returns result object with fileName, dataUrl (virtual URL), and originalPath for persistence
        /// </summary>
        public object OpenPdfFilePicker()
        {
            try
            {
                return OpenPdfFilePickerAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfService] Error in file picker: {ex}");
                return new { fileName = (string)null, dataUrl = (string)null, originalPath = (string)null };
            }
        }

        private async Task<object> OpenPdfFilePickerAsync()
        {
            var filePath = await WebViewDialogHelper.ShowOpenFileDialogAsync(
                _webView,
                "PDF files (*.pdf)|*.pdf",
                "Select PDF File"
            );

            if (string.IsNullOrEmpty(filePath))
            {
                return new { fileName = (string)null, dataUrl = (string)null, originalPath = (string)null };
            }

            // Create virtual URL for the selected file
            var virtualUrl = CreateVirtualUrl(filePath);
            var fileName = Path.GetFileName(filePath);

            Console.WriteLine($"[PdfService] File selected: {fileName} -> {virtualUrl}");

            return new { 
                fileName = fileName, 
                dataUrl = virtualUrl, 
                originalPath = filePath 
            };
        }

        /// <summary>
        /// Create virtual URL from file path by copying to temp directory
        /// </summary>
        public string CreateVirtualUrl(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return null;

                var htmlPath = GetHtmlPath();
                var tempDir = Path.Combine(htmlPath, "temp");
                Directory.CreateDirectory(tempDir);

                // Create unique filename to avoid conflicts
                var uniqueName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(filePath);
                var tempPath = Path.Combine(tempDir, uniqueName);
                
                // Copy file to temp location for virtual URL access
                File.Copy(filePath, tempPath, true);
                
                // Return virtual URL
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

        /// <summary>
        /// Recreate virtual URL from stored file path (for session persistence)
        /// </summary>
        public string RecreateVirtualUrlFromPath(string originalPath)
        {
            try
            {
                if (!File.Exists(originalPath))
                {
                    Console.WriteLine($"[PdfService] Original file not found: {originalPath}");
                    return null;
                }

                // Create new virtual URL for the existing file
                return CreateVirtualUrl(originalPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfService] Error recreating virtual URL: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Check if PDF manager is ready
        /// </summary>
        public bool CheckPdfManagerReady()
        {
            return _webView?.CoreWebView2 != null;
        }

        /// <summary>
        /// Clean up temp file
        /// </summary>
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

        /// <summary>
        /// Get temp file statistics
        /// </summary>
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
                {
                    totalSize += new FileInfo(file).Length;
                }

                return new { fileCount = files.Length, totalSize = totalSize };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfService] Error getting temp file stats: {ex}");
                return new { fileCount = 0, totalSize = 0 };
            }
        }

        /// <summary>
        /// Clear all temp files
        /// </summary>
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
            // Get Html path - handle both regular and ClickOnce deployments
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            
            // 1. Standard deployment: zayit-vue-app folder in base directory
            var standardPath = Path.Combine(baseDir, "zayit-vue-app");
            if (Directory.Exists(standardPath))
            {
                return Path.GetFullPath(standardPath);
            }
            
            // 2. ClickOnce deployment: Check assembly location
            var assemblyPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var clickOncePath = Path.Combine(assemblyPath, "zayit-vue-app");
            if (Directory.Exists(clickOncePath))
            {
                return Path.GetFullPath(clickOncePath);
            }
            
            // 3. Fallback: Return standard path even if it doesn't exist
            Console.WriteLine($"[PdfService] WARNING: zayit-vue-app folder not found! Tried:");
            Console.WriteLine($"  - {standardPath}");
            Console.WriteLine($"  - {clickOncePath}");
            return Path.GetFullPath(standardPath);
        }
    }
}