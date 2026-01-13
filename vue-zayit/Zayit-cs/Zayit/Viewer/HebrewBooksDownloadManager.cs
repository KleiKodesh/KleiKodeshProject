using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace Zayit.Viewer
{
    public class HebrewBooksDownloadManager
    {
        private readonly CoreWebView2 _webView;
        private readonly string _cacheDirectory;
        private const int MAX_CACHE_SIZE = 10;
        private HebrewBookDownloadInfo _pendingDownload;

        public HebrewBooksDownloadManager(CoreWebView2 webView)
        {
            _webView = webView;
            // Download to hebrewbookscache subfolder within PDF.js web directory
            _cacheDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Html", "pdfjs", "web", "hebrewbookscache");
            // Ensure cache directory exists
            Directory.CreateDirectory(_cacheDirectory);
            Console.WriteLine($"[HebrewBooks] Cache directory: {_cacheDirectory}");
        }

        public async Task PrepareHebrewBookDownload(string bookId, string title, string action)
        {
            try
            {
                Console.WriteLine($"[HebrewBooks] Preparing {action} for {bookId}: {title}");

                // Only use cache for view action
                if (action == "view")
                {
                    // Check if file exists in cache with title_id pattern
                    string sanitizedTitle = SanitizeFileName(title);
                    string fileName = $"{sanitizedTitle}_{bookId}";
                    string pdfFilePath = Path.Combine(_cacheDirectory, $"{fileName}.pdf");
                    if (File.Exists(pdfFilePath))
                    {
                        Console.WriteLine($"[HebrewBooks] Found in cache: {pdfFilePath}");
                        // Send download complete signal - Vue will construct URL using fileName
                        await SendDownloadComplete(bookId, fileName);
                        return;
                    }
                    else
                    {
                        Console.WriteLine($"[HebrewBooks] Not found in cache, will download: {pdfFilePath}");
                    }
                }

                // Show save dialog for download action
                if (action == "download")
                {
                    Console.WriteLine($"[HebrewBooks] Showing save dialog for download");
                    using (var dialog = new SaveFileDialog())
                    {
                        dialog.Filter = "PDF Files (*.pdf)|*.pdf";
                        dialog.FileName = $"{title}.pdf";
                        dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                        
                        if (dialog.ShowDialog() != DialogResult.OK)
                        {
                            Console.WriteLine($"[HebrewBooks] Save dialog cancelled");
                            await SendDownloadComplete(bookId, null, true);
                            return;
                        }

                        Console.WriteLine($"[HebrewBooks] User selected save path: {dialog.FileName}");
                        _pendingDownload = new HebrewBookDownloadInfo
                        {
                            BookId = bookId,
                            Title = title,
                            Action = action,
                            SavePath = dialog.FileName
                        };
                    }
                }
                else
                {
                    _pendingDownload = new HebrewBookDownloadInfo
                    {
                        BookId = bookId,
                        Title = title,
                        Action = action
                    };
                }

                Console.WriteLine($"[HebrewBooks] Sending ready signal for {action}");
                await SendReady(bookId, action);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HebrewBooks] Error in PrepareHebrewBookDownload: {ex}");
                // Always send an error response
                await SendDownloadComplete(bookId, null, false);
            }
        }

        public void HandleDownloadStarting(CoreWebView2DownloadStartingEventArgs e)
        {
            if (_pendingDownload == null || !e.DownloadOperation.Uri.Contains("hebrewbooks.org"))
                return;

            Console.WriteLine($"[HebrewBooks] Handling download for {_pendingDownload.Title}");

            // Capture values before clearing _pendingDownload
            var bookId = _pendingDownload.BookId;
            var title = _pendingDownload.Title;
            var action = _pendingDownload.Action;
            var savePath = _pendingDownload.SavePath;

            if (action == "view")
            {
                // Save to cache with title_id pattern
                string sanitizedTitle = SanitizeFileName(title);
                string fileName = $"{sanitizedTitle}_{bookId}";
                string pdfFilePath = Path.Combine(_cacheDirectory, $"{fileName}.pdf");
                e.ResultFilePath = pdfFilePath;

                e.DownloadOperation.StateChanged += async (sender, args) =>
                {
                    if (sender is CoreWebView2DownloadOperation download && download.State == CoreWebView2DownloadState.Completed)
                    {
                        try
                        {
                            Console.WriteLine($"[HebrewBooks] Download completed to cache: {pdfFilePath}");
                            
                            // Close the download dialog for view action (to show PDF immediately)
                            _webView.CloseDefaultDownloadDialog();
                            
                            // Simple cache management - just delete oldest files if we have too many
                            ManageCache();
                            
                            // Send download complete signal - Vue will construct URL using fileName
                            await SendDownloadComplete(bookId, fileName);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[HebrewBooks] Error processing downloaded file: {ex.Message}");
                            await SendDownloadComplete(bookId, null, false);
                        }
                    }
                };
            }
            else
            {
                // Save to user-selected path
                e.ResultFilePath = savePath;
                Console.WriteLine($"[HebrewBooks] Download will be saved to: {savePath}");
                
                e.DownloadOperation.StateChanged += async (sender, args) =>
                {
                    if (sender is CoreWebView2DownloadOperation download)
                    {
                        Console.WriteLine($"[HebrewBooks] Download state changed to: {download.State}");
                        if (download.State == CoreWebView2DownloadState.Completed)
                        {
                            Console.WriteLine($"[HebrewBooks] Download completed successfully to: {download.ResultFilePath}");
                            await SendDownloadComplete(bookId, download.ResultFilePath, true);
                        }
                        else if (download.State == CoreWebView2DownloadState.Interrupted)
                        {
                            Console.WriteLine($"[HebrewBooks] Download was interrupted");
                            await SendDownloadComplete(bookId, null, true);
                        }
                    }
                };
            }

            _pendingDownload = null;
        }

        private async Task SendBlob(string bookId, string title, string base64)
        {
            _webView.CloseDefaultDownloadDialog();
            Console.WriteLine($"[HebrewBooks] SendBlob called - bookId: {bookId}, title: {title}, hasBase64: {base64 != null}");
            string js = $"window.receiveHebrewBookBlob({JsonSerializer.Serialize(bookId)}, {JsonSerializer.Serialize(title)}, {JsonSerializer.Serialize(base64)});";
            Console.WriteLine($"[HebrewBooks] Executing JS: {js.Substring(0, Math.Min(100, js.Length))}...");
            await _webView.ExecuteScriptAsync(js);
            Console.WriteLine($"[HebrewBooks] SendBlob completed for {bookId}");
        }

        private async Task SendReady(string bookId, string action)
        {
            string js = $"window.receiveHebrewBookDownloadReady({JsonSerializer.Serialize(bookId)}, {JsonSerializer.Serialize(action)});";
            await _webView.ExecuteScriptAsync(js);
        }

        private async Task SendDownloadComplete(string bookId, string fileName)
        {
            string js = $"window.receiveHebrewBookDownloadComplete({JsonSerializer.Serialize(bookId)}, {JsonSerializer.Serialize(fileName)});";
            await _webView.ExecuteScriptAsync(js);
        }

        private async Task SendDownloadComplete(string bookId, string filePath, bool isDownload)
        {
            string js = $"window.receiveHebrewBookDownloadComplete({JsonSerializer.Serialize(bookId)}, {JsonSerializer.Serialize(filePath)});";
            await _webView.ExecuteScriptAsync(js);
        }

        private void ManageCache()
        {
            try
            {
                // Manage all PDF files in cache directory
                var cacheFiles = Directory.GetFiles(_cacheDirectory, "*.pdf")
                    .Select(f => new FileInfo(f))
                    .OrderBy(f => f.LastAccessTime)
                    .ToArray();

                // Delete oldest files if we have more than MAX_CACHE_SIZE
                while (cacheFiles.Length > MAX_CACHE_SIZE)
                {
                    cacheFiles[0].Delete();
                    Console.WriteLine($"[HebrewBooks] Deleted old cache file: {cacheFiles[0].Name}");
                    cacheFiles = cacheFiles.Skip(1).ToArray();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HebrewBooks] Error managing cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Sanitize filename by removing invalid characters and limiting length
        /// </summary>
        private string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "unknown";

            // Remove invalid filename characters
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string sanitized = new string(fileName.Where(c => !invalidChars.Contains(c)).ToArray());
            
            // Replace spaces with underscores for better URL compatibility
            sanitized = sanitized.Replace(' ', '_');
            
            // Limit length to avoid filesystem issues
            if (sanitized.Length > 100)
                sanitized = sanitized.Substring(0, 100);
            
            // Ensure we have a valid filename
            if (string.IsNullOrEmpty(sanitized))
                sanitized = "unknown";
                
            return sanitized;
        }
    }

    public class HebrewBookDownloadInfo
    {
        public string BookId { get; set; }
        public string Title { get; set; }
        public string Action { get; set; }
        public string SavePath { get; set; }
    }
}