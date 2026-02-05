using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Zayit.Viewer;

namespace Zayit.Services
{
    /// <summary>
    /// Hebrew Books Service - Handles ONLY Hebrew books specific operations
    /// Uses the same origin as PDF.js viewer to avoid cross-origin restrictions
    /// </summary>
    public class HebrewBooksService
    {
        private readonly WebView2 _webView;
        private readonly HttpClient _http = new HttpClient();
        private readonly HebrewBooksCacheManager _cache;

        public HebrewBooksService(WebView2 webView)
        {
            _webView = webView;
            _cache = new HebrewBooksCacheManager(GetHtmlPath());
        }

        /// <summary>
        /// Initialize Hebrew books service - no separate virtual host needed
        /// Hebrew books will use the same origin as PDF.js viewer
        /// </summary>
        public void Initialize()
        {
            try
            {
                var htmlPath = GetHtmlPath();
                var pdfJsWebPath = Path.Combine(htmlPath, "pdfjs", "web");
                var cacheDir = Path.Combine(pdfJsWebPath, "hebrewbookscache");

                // Ensure PDF.js web directory exists
                if (!Directory.Exists(pdfJsWebPath))
                {
                    Console.WriteLine($"[HebrewBooksService] ERROR: PDF.js web directory not found: {pdfJsWebPath}");
                    throw new DirectoryNotFoundException($"PDF.js web directory not found: {pdfJsWebPath}");
                }

                // Create Hebrew books cache directory within PDF.js web folder
                Directory.CreateDirectory(cacheDir);

                // Verify the cache directory was created successfully
                if (!Directory.Exists(cacheDir))
                {
                    Console.WriteLine($"[HebrewBooksService] ERROR: Failed to create Hebrew books cache directory: {cacheDir}");
                    throw new DirectoryNotFoundException($"Failed to create Hebrew books cache directory: {cacheDir}");
                }

                Console.WriteLine($"[HebrewBooksService] Hebrew books cache directory created/verified: {cacheDir}");
                Console.WriteLine($"[HebrewBooksService] Using same origin as PDF.js viewer to avoid cross-origin restrictions");
                Console.WriteLine($"[HebrewBooksService] Cache directory relative to PDF.js viewer: hebrewbookscache/");

                SetupDownloadHandler();

                // Log directory structure for debugging
                LogDirectoryStructure(pdfJsWebPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HebrewBooksService] Failed to initialize: {ex}");
                throw; // Re-throw to ensure initialization failures are noticed
            }
        }

        /// <summary>
        /// Log the PDF.js web directory structure for debugging
        /// </summary>
        private void LogDirectoryStructure(string pdfJsWebPath)
        {
            try
            {
                Console.WriteLine($"[HebrewBooksService] PDF.js web directory structure:");
                Console.WriteLine($"  Base: {pdfJsWebPath}");

                if (Directory.Exists(pdfJsWebPath))
                {
                    var subdirs = Directory.GetDirectories(pdfJsWebPath);
                    foreach (var subdir in subdirs)
                    {
                        var dirName = Path.GetFileName(subdir);
                        Console.WriteLine($"  - {dirName}/");
                    }

                    // Check for key files
                    var viewerHtml = Path.Combine(pdfJsWebPath, "viewer.html");
                    var viewerMjs = Path.Combine(pdfJsWebPath, "viewer.mjs");
                    Console.WriteLine($"  viewer.html exists: {File.Exists(viewerHtml)}");
                    Console.WriteLine($"  viewer.mjs exists: {File.Exists(viewerMjs)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HebrewBooksService] Error logging directory structure: {ex.Message}");
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
                        // Set download to our cache directory within PDF.js web folder
                        var cacheDir = GetCacheDirectory();

                        // Generate filename - use pending context if available, otherwise extract from URL
                        var fileName = GetExpectedFileName() ??
                                     GetFileNameFromUrl(e.DownloadOperation.Uri) ??
                                     e.DownloadOperation.ResultFilePath ??
                                     $"hebrewbook_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

                        var filePath = Path.Combine(cacheDir, fileName);
                        e.ResultFilePath = filePath;

                        Console.WriteLine($"[HebrewBooksService] Hebrew book download redirected to cache: {filePath}");

                        // Handle download completion - attach handler immediately
                        var downloadOperation = e.DownloadOperation;

                        // Create a handler that will be called when state changes
                        EventHandler<object> stateChangedHandler = null;
                        stateChangedHandler = (s, args) =>
                        {
                            Console.WriteLine($"[HebrewBooksService] Download state changed to: {downloadOperation.State}");

                            if (downloadOperation.State == CoreWebView2DownloadState.Completed)
                            {
                                Console.WriteLine($"[HebrewBooksService] Download completed, calling HandleDownloadCompleted");
                                HandleDownloadCompleted(filePath, fileName);

                                // Unsubscribe from the event to prevent memory leaks
                                downloadOperation.StateChanged -= stateChangedHandler;
                            }
                            else if (downloadOperation.State == CoreWebView2DownloadState.Interrupted)
                            {
                                Console.WriteLine($"[HebrewBooksService] Download was interrupted");
                                // Unsubscribe from the event
                                downloadOperation.StateChanged -= stateChangedHandler;
                            }
                        };

                        // Subscribe to state changes
                        downloadOperation.StateChanged += stateChangedHandler;
                        Console.WriteLine($"[HebrewBooksService] StateChanged handler attached for download");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HebrewBooksService] Error handling download: {ex}");
                }
            };
        }

        /// <summary>
        /// Flow 1: Prepare Hebrew book for viewing (cache if needed, no SaveAs dialog)
        /// </summary>
        public async Task<object> PrepareForViewing(string bookId, string title)
        {
            try
            {
                Console.WriteLine($"[HebrewBooksService] Preparing Hebrew book for viewing - ID: {bookId}, Title: {title}");

                // Initialize if not already done
                Initialize();

                // Check if book is already cached
                var fileName = SanitizeFileName($"{title}_{bookId}") + ".pdf";
                var cachedPath = GetCachedFilePath(fileName);

                Console.WriteLine($"[HebrewBooksService] Checking cache for viewing - fileName: {fileName}");
                Console.WriteLine($"[HebrewBooksService] Cache path: {cachedPath}");
                Console.WriteLine($"[HebrewBooksService] Cached file exists: {File.Exists(cachedPath)}");

                if (File.Exists(cachedPath))
                {
                    Console.WriteLine($"[HebrewBooksService] Book already cached for viewing: {fileName}");

                    // Return relative path from PDF.js viewer's perspective (viewer.html is in /pdfjs/web/)
                    var relativeUrl = $"hebrewbookscache/{fileName}";
                    Console.WriteLine($"[HebrewBooksService] Returning cached relative URL: {relativeUrl}");
                    return new { success = true, cached = true, fileName = fileName, url = relativeUrl };
                }

                // File not cached - need to download to cache
                Console.WriteLine($"[HebrewBooksService] File not cached, will download to cache for viewing");

                // Store context for download completion handler
                _pendingViewingContext = new ViewingContext
                {
                    BookId = bookId,
                    Title = title,
                    FileName = fileName
                };

                return new { success = true, cached = false };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HebrewBooksService] Error preparing for viewing: {ex}");
                return new { success = false, error = ex.Message };
            }
        }

        /// <summary>
        /// Flow 2: Download Hebrew book with SaveAs dialog (user chooses location)
        /// </summary>
        public async Task<object> PrepareForDownload(string bookId, string title)
        {
            try
            {
                Console.WriteLine($"[HebrewBooksService] Preparing Hebrew book for download - ID: {bookId}, Title: {title}");

                // Initialize if not already done
                Initialize();

                // Check if book is already cached
                var fileName = SanitizeFileName($"{title}_{bookId}") + ".pdf";
                var cachedPath = GetCachedFilePath(fileName);

                Console.WriteLine($"[HebrewBooksService] Checking cache for download - fileName: {fileName}");
                Console.WriteLine($"[HebrewBooksService] Cache path: {cachedPath}");
                Console.WriteLine($"[HebrewBooksService] Cached file exists: {File.Exists(cachedPath)}");

                if (File.Exists(cachedPath))
                {
                    Console.WriteLine($"[HebrewBooksService] Book already cached, showing SaveAs dialog");

                    // Show save dialog for cached file
                    return await ShowSaveDialogForCachedFile(cachedPath, title);
                }

                // File not cached - show SaveAs dialog first, then download
                Console.WriteLine($"[HebrewBooksService] File not cached, showing SaveAs dialog first");

                var filePath = await WebViewDialogHelper.ShowSaveFileDialogAsync(
                    _webView,
                    "PDF files (*.pdf)|*.pdf",
                    "Save Hebrew Book",
                    $"{title}.pdf"
                );

                if (string.IsNullOrEmpty(filePath))
                {
                    Console.WriteLine($"[HebrewBooksService] SaveAs dialog cancelled");
                    return new { success = false, cancelled = true };
                }

                // Store the target path for when download completes
                _pendingDownloadContext = new DownloadContext
                {
                    BookId = bookId,
                    Title = title,
                    TargetPath = filePath,
                    FileName = fileName
                };

                Console.WriteLine($"[HebrewBooksService] SaveAs dialog completed, target: {filePath}");
                return new { success = true, cached = false, targetPath = filePath };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HebrewBooksService] Error preparing for download: {ex}");
                return new { success = false, error = ex.Message };
            }
        }

        // Context classes for tracking pending operations
        private class ViewingContext
        {
            public string BookId { get; set; }
            public string Title { get; set; }
            public string FileName { get; set; }
        }

        private class DownloadContext
        {
            public string BookId { get; set; }
            public string Title { get; set; }
            public string TargetPath { get; set; }
            public string FileName { get; set; }
        }

        private ViewingContext _pendingViewingContext;
        private DownloadContext _pendingDownloadContext;

        /// <summary>
        /// Handle download completion
        /// </summary>
        private void HandleDownloadCompleted(string filePath, string fileName)
        {
            try
            {
                Console.WriteLine($"[HebrewBooksService] Download completed - filePath: {filePath}, fileName: {fileName}");

                // Verify file exists and get details
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"[HebrewBooksService] ERROR: Downloaded file does not exist at {filePath}");
                    return;
                }

                var fileInfo = new FileInfo(filePath);
                Console.WriteLine($"[HebrewBooksService] Downloaded file verified - size: {fileInfo.Length} bytes, created: {fileInfo.CreationTime}");

                // Verify the file is a valid PDF by checking header
                try
                {
                    var header = new byte[4];
                    using (var fs = File.OpenRead(filePath))
                    {
                        fs.Read(header, 0, 4);
                    }
                    var headerString = System.Text.Encoding.ASCII.GetString(header);
                    Console.WriteLine($"[HebrewBooksService] File header: {headerString} (should be '%PDF')");

                    if (!headerString.StartsWith("%PDF"))
                    {
                        Console.WriteLine($"[HebrewBooksService] WARNING: File does not appear to be a valid PDF");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HebrewBooksService] Error checking file header: {ex.Message}");
                }

                // Enforce cache file limit after adding new file
                _cache.EnforceFileLimit();

                // Close the default download dialog
                try
                {
                    _webView?.CoreWebView2?.CloseDefaultDownloadDialog();
                    Console.WriteLine($"[HebrewBooksService] Default download dialog closed");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HebrewBooksService] Error closing download dialog: {ex}");
                }

                // Handle based on the type of operation
                if (_pendingDownloadContext != null)
                {
                    // This was a download operation - copy to user's chosen location
                    Console.WriteLine($"[HebrewBooksService] Handling download completion - copying to: {_pendingDownloadContext.TargetPath}");
                    File.Copy(filePath, _pendingDownloadContext.TargetPath, true);
                    Console.WriteLine($"[HebrewBooksService] File copied to user location: {_pendingDownloadContext.TargetPath}");

                    // Notify Vue about download completion
                    NotifyDownloadComplete(_pendingDownloadContext.TargetPath, true);
                    _pendingDownloadContext = null;
                }
                else if (_pendingViewingContext != null)
                {
                    // This was a viewing operation - notify Vue with relative URL from PDF.js viewer's perspective
                    var relativeUrl = $"hebrewbookscache/{fileName}";
                    Console.WriteLine($"[HebrewBooksService] Handling viewing completion - relative URL: {relativeUrl}");

                    // Notify Vue about viewing readiness
                    NotifyViewingReady(_pendingViewingContext.FileName, relativeUrl, _pendingViewingContext.BookId, _pendingViewingContext.Title);
                    _pendingViewingContext = null;
                }
                else
                {
                    Console.WriteLine($"[HebrewBooksService] No pending context found - this might be an unexpected download");
                }
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
                var filePath = await WebViewDialogHelper.ShowSaveFileDialogAsync(
                    _webView,
                    "PDF files (*.pdf)|*.pdf",
                    "Save Hebrew Book",
                    $"{title}.pdf"
                );

                if (string.IsNullOrEmpty(filePath))
                {
                    return new { success = false, cancelled = true };
                }

                // Copy cached file to user's chosen location
                File.Copy(cachedPath, filePath, true);

                return new { success = true, filePath = filePath };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HebrewBooksService] Error in save dialog: {ex}");
                return new { success = false, error = ex.Message };
            }
        }

        /// <summary>
        /// Notify Vue about download completion (Flow 2: SaveAs download)
        /// </summary>
        private async void NotifyDownloadComplete(string filePath, bool success)
        {
            try
            {
                Console.WriteLine($"[HebrewBooksService] Notifying Vue about download completion - filePath: {filePath}, success: {success}");

                var script = $@"
                    console.log('[HebrewBooksService] Download completed notification - filePath: {filePath}, success: {success}');
                    // For download flow, we just log completion - no further action needed in Vue
                ";

                await _webView.ExecuteScriptAsync(script);
                Console.WriteLine($"[HebrewBooksService] Download completion notification sent");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HebrewBooksService] Error notifying download complete: {ex}");
            }
        }

        /// <summary>
        /// Notify Vue about viewing readiness (Flow 1: Cache and view)
        /// </summary>
        private async void NotifyViewingReady(string fileName, string relativeUrl, string bookId, string title)
        {
            try
            {
                Console.WriteLine($"[HebrewBooksService] Notifying Vue about viewing readiness - fileName: {fileName}, relativeUrl: {relativeUrl}");

                var script = $@"
                    console.log('[HebrewBooksService] Executing viewing ready notification for:', '{fileName}', '{relativeUrl}');
                    if (window.handleHebrewBookViewingReady) {{
                        console.log('[HebrewBooksService] Calling handleHebrewBookViewingReady');
                        window.handleHebrewBookViewingReady({{
                            fileName: '{fileName}',
                            url: '{relativeUrl}',
                            bookId: '{bookId}',
                            bookTitle: '{title}',
                            success: true
                        }});
                    }} else {{
                        console.error('[HebrewBooksService] handleHebrewBookViewingReady not found on window');
                        console.log('[HebrewBooksService] Available window properties:', Object.keys(window));
                    }}
                ";

                await _webView.ExecuteScriptAsync(script);
                Console.WriteLine($"[HebrewBooksService] Viewing ready notification script executed successfully");

                // Add a small delay and try again if the handler wasn't available
                await Task.Delay(500);

                var retryScript = $@"
                    if (window.handleHebrewBookViewingReady) {{
                        console.log('[HebrewBooksService] Retry: Calling handleHebrewBookViewingReady');
                        window.handleHebrewBookViewingReady({{
                            fileName: '{fileName}',
                            url: '{relativeUrl}',
                            bookId: '{bookId}',
                            bookTitle: '{title}',
                            success: true
                        }});
                    }} else {{
                        console.warn('[HebrewBooksService] Retry: handleHebrewBookViewingReady still not available');
                    }}
                ";

                await _webView.ExecuteScriptAsync(retryScript);
                Console.WriteLine($"[HebrewBooksService] Retry viewing ready notification script executed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HebrewBooksService] Error notifying viewing ready: {ex}");
            }
        }

        /// <summary>
        /// Get expected filename from pending context
        /// </summary>
        private string GetExpectedFileName()
        {
            if (_pendingViewingContext != null)
            {
                return _pendingViewingContext.FileName;
            }

            if (_pendingDownloadContext != null)
            {
                return _pendingDownloadContext.FileName;
            }

            return null;
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

        /// <summary>
        /// Get the Hebrew books cache directory path within PDF.js web folder
        /// </summary>
        private string GetCacheDirectory()
        {
            var htmlPath = GetHtmlPath();
            var pdfJsWebPath = Path.Combine(htmlPath, "pdfjs", "web");
            var cacheDir = Path.Combine(pdfJsWebPath, "hebrewbookscache");

            // Ensure cache directory exists
            Directory.CreateDirectory(cacheDir);

            return cacheDir;
        }

        /// <summary>
        /// Get the full path for a cached Hebrew book file
        /// </summary>
        private string GetCachedFilePath(string fileName)
        {
            return Path.Combine(GetCacheDirectory(), fileName);
        }

        private string GetHtmlPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var htmlPath = Path.Combine(baseDir, "zayit-vue-app");
            return Path.GetFullPath(htmlPath);
        }

        /// <summary>
        /// Handle Hebrew book tab closure (cleanup cache)
        /// </summary>
        public void HandleTabClosed(string fileName)
        {
            try
            {
                var filePath = Path.Combine(GetCacheDirectory(), fileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    Console.WriteLine($"[HebrewBooksService] Deleted Hebrew book from cache: {fileName}");
                }
                else
                {
                    Console.WriteLine($"[HebrewBooksService] Hebrew book file not found in cache: {fileName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HebrewBooksService] Error deleting Hebrew book from cache: {ex}");
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