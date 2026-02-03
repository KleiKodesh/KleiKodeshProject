using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Zayit.Services
{
    /// <summary>
    /// Hebrew Books Service - Handles ONLY Hebrew books specific operations
    /// Does NOT handle general PDF operations - that's in PdfService
    /// </summary>
    public class HebrewBooksService
    {
        private readonly WebView2 _webView;
        private readonly HttpClient _http = new HttpClient();
        private readonly HebrewBooksCacheManager _cache;
        private const string HEBREW_BOOKS_HOST = "zayitHost";
        private static bool _virtualHostMappingSet = false;

        public HebrewBooksService(WebView2 webView)
        {
            _webView = webView;
            _cache = new HebrewBooksCacheManager(GetHtmlPath());
            
            // Set up download event handler
            if (_webView?.CoreWebView2 != null)
            {
                SetupDownloadHandler();
            }
        }

        /// <summary>
        /// Initialize Hebrew books service and set up virtual host mapping (static/global)
        /// </summary>
        public void Initialize()
        {
            if (_virtualHostMappingSet || _webView?.CoreWebView2 == null) return;

            try
            {
                var htmlPath = GetHtmlPath();
                var cacheDir = Path.Combine(htmlPath, "pdfjs", "web", "hebrewbookscache");
                Directory.CreateDirectory(cacheDir);

                // Set up static virtual host mapping for all Hebrew books
                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    HEBREW_BOOKS_HOST,
                    htmlPath,
                    CoreWebView2HostResourceAccessKind.Allow
                );

                _virtualHostMappingSet = true;
                Console.WriteLine($"[HebrewBooksService] Static virtual host mapping set: {HEBREW_BOOKS_HOST} -> {htmlPath}");
                
                SetupDownloadHandler();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HebrewBooksService] Failed to initialize: {ex}");
            }
        }

        /// <summary>
        /// Set up download event handler to capture Hebrew book downloads
        /// </summary>
        private void SetupDownloadHandler()
        {
            if (_webView?.CoreWebView2 == null) return;

            _webView.CoreWebView2.DownloadStarting += (sender, e) =>
            {
                try
                {
                    Console.WriteLine($"[HebrewBooksService] Download starting: {e.DownloadOperation.Uri}");
                    
                    // Check if this is a Hebrew books download
                    if (e.DownloadOperation.Uri.Contains("hebrewbooks.org") || 
                        e.DownloadOperation.Uri.Contains("download.hebrewbooks.org"))
                    {
                        // Set download to our cache directory
                        var htmlPath = GetHtmlPath();
                        var cacheDir = Path.Combine(htmlPath, "pdfjs", "web", "hebrewbookscache");
                        Directory.CreateDirectory(cacheDir);
                        
                        // Generate filename from URL or use suggested name
                        var fileName = GetFileNameFromUrl(e.DownloadOperation.Uri) ?? 
                                     e.DownloadOperation.ResultFilePath ?? 
                                     $"hebrewbook_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                        
                        var filePath = Path.Combine(cacheDir, fileName);
                        e.ResultFilePath = filePath;
                        
                        Console.WriteLine($"[HebrewBooksService] Hebrew book download redirected to: {filePath}");
                        
                        // Handle download completion
                        e.DownloadOperation.StateChanged += (s, args) =>
                        {
                            if (e.DownloadOperation.State == CoreWebView2DownloadState.Completed)
                            {
                                HandleDownloadCompleted(filePath, fileName);
                            }
                        };
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HebrewBooksService] Error handling download: {ex}");
                }
            };
        }

        /// <summary>
        /// Prepare Hebrew book for download or viewing
        /// </summary>
        public async Task<object> PrepareDownload(string bookId, string title, string action)
        {
            try
            {
                Console.WriteLine($"[HebrewBooksService] Preparing Hebrew book - ID: {bookId}, Title: {title}, Action: {action}");
                
                // Initialize if not already done
                Initialize();
                
                // Check if book is already cached
                var fileName = SanitizeFileName($"{title}_{bookId}") + ".pdf";
                var htmlPath = GetHtmlPath();
                var cacheDir = Path.Combine(htmlPath, "pdfjs", "web", "hebrewbookscache");
                var cachedPath = Path.Combine(cacheDir, fileName);
                
                if (File.Exists(cachedPath))
                {
                    Console.WriteLine($"[HebrewBooksService] Book already cached: {fileName}");
                    
                    if (action == "view")
                    {
                        // Return virtual URL for immediate viewing
                        var virtualUrl = $"https://{HEBREW_BOOKS_HOST}/pdfjs/web/hebrewbookscache/{fileName}";
                        return new { success = true, cached = true, fileName = fileName, url = virtualUrl };
                    }
                    else if (action == "download")
                    {
                        // Show save dialog for cached file
                        return await ShowSaveDialogForCachedFile(cachedPath, title);
                    }
                }
                
                // File not cached - need to download
                if (action == "download")
                {
                    // Show save dialog first
                    using (var dialog = new SaveFileDialog())
                    {
                        dialog.Filter = "PDF files (*.pdf)|*.pdf";
                        dialog.FileName = $"{title}.pdf";
                        dialog.Title = "Save Hebrew Book";
                        
                        if (dialog.ShowDialog() != DialogResult.OK)
                        {
                            return new { success = false, cancelled = true };
                        }
                        
                        // Store the target path for when download completes
                        _pendingDownloadPath = dialog.FileName;
                        return new { success = true, cached = false, targetPath = dialog.FileName };
                    }
                }
                
                // For viewing, just return success - download will be handled by browser
                return new { success = true, cached = false };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HebrewBooksService] Error preparing download: {ex}");
                return new { success = false, error = ex.Message };
            }
        }

        private string _pendingDownloadPath;

        /// <summary>
        /// Handle download completion
        /// </summary>
        private void HandleDownloadCompleted(string filePath, string fileName)
        {
            try
            {
                Console.WriteLine($"[HebrewBooksService] Download completed: {fileName}");
                
                // Close the default download dialog
                _webView?.CoreWebView2?.CloseDefaultDownloadDialog();
                
                // If this was a user download (not cache), copy to user's chosen location
                if (!string.IsNullOrEmpty(_pendingDownloadPath))
                {
                    File.Copy(filePath, _pendingDownloadPath, true);
                    Console.WriteLine($"[HebrewBooksService] File copied to user location: {_pendingDownloadPath}");
                    _pendingDownloadPath = null;
                }
                
                // Create virtual URL for viewing
                var virtualUrl = $"https://{HEBREW_BOOKS_HOST}/pdfjs/web/hebrewbookscache/{fileName}";
                
                // Notify Vue about completion
                NotifyDownloadComplete(fileName, virtualUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HebrewBooksService] Error handling download completion: {ex}");
            }
        }

        /// <summary>
        /// Show save dialog for already cached file
        /// </summary>
        private async Task<object> ShowSaveDialogForCachedFile(string cachedPath, string title)
        {
            try
            {
                using (var dialog = new SaveFileDialog())
                {
                    dialog.Filter = "PDF files (*.pdf)|*.pdf";
                    dialog.FileName = $"{title}.pdf";
                    dialog.Title = "Save Hebrew Book";
                    
                    if (dialog.ShowDialog() != DialogResult.OK)
                    {
                        return new { success = false, cancelled = true };
                    }
                    
                    // Copy cached file to user's chosen location
                    File.Copy(cachedPath, dialog.FileName, true);
                    
                    return new { success = true, filePath = dialog.FileName };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HebrewBooksService] Error in save dialog: {ex}");
                return new { success = false, error = ex.Message };
            }
        }

        /// <summary>
        /// Notify Vue about download completion
        /// </summary>
        private async void NotifyDownloadComplete(string fileName, string virtualUrl)
        {
            try
            {
                var script = $@"
                    if (window.handleHebrewBookDownloadComplete) {{
                        window.handleHebrewBookDownloadComplete({{
                            fileName: '{fileName}',
                            url: '{virtualUrl}',
                            success: true
                        }});
                    }}
                ";
                
                await _webView.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HebrewBooksService] Error notifying download complete: {ex}");
            }
        }

        /// <summary>
        /// Extract filename from Hebrew books URL
        /// </summary>
        private string GetFileNameFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                var query = uri.Query;
                if (query.StartsWith("?"))
                    query = query.Substring(1);
                
                var pairs = query.Split('&');
                foreach (var pair in pairs)
                {
                    var keyValue = pair.Split('=');
                    if (keyValue.Length == 2 && keyValue[0] == "req")
                    {
                        var req = Uri.UnescapeDataString(keyValue[1]);
                        return $"hebrewbook_{req}.pdf";
                    }
                }
                
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Sanitize filename for safe file system usage
        /// </summary>
        private string SanitizeFileName(string fileName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            foreach (var c in invalid)
            {
                fileName = fileName.Replace(c, '_');
            }
            return fileName;
        }

        private string GetHtmlPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var htmlPath = Path.Combine(baseDir, "Html");
            
            if (!Directory.Exists(htmlPath))
            {
                htmlPath = Path.Combine(baseDir, "..", "..", "..", "zayit-vue", "dist");
                if (!Directory.Exists(htmlPath))
                {
                    htmlPath = Path.Combine(baseDir, "dist");
                }
            }
            
            return Path.GetFullPath(htmlPath);
        }

        /// <summary>
        /// Handle Hebrew book tab closure (cleanup cache)
        /// </summary>
        public void HandleTabClosed(string fileName)
        {
            try
            {
                _cache.UnregisterActive(fileName);
                Console.WriteLine($"[HebrewBooksService] Hebrew book tab closed: {fileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HebrewBooksService] Error handling tab closure: {ex}");
            }
        }

        /// <summary>
        /// Get Hebrew books cache statistics
        /// </summary>
        public object GetCacheStats()
        {
            return _cache.GetStats();
        }

        /// <summary>
        /// Clear Hebrew books cache
        /// </summary>
        public void ClearCache()
        {
            try
            {
                _cache.ClearAll();
                Console.WriteLine("[HebrewBooksService] Hebrew books cache cleared");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HebrewBooksService] Error clearing cache: {ex}");
            }
        }
    }
}