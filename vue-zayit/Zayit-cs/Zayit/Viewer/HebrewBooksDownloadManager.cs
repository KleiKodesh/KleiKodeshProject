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
            // Download directly to PDF.js web directory so files are immediately accessible
            _cacheDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Html", "pdfjs", "web");
            // Ensure cache directory exists
            Directory.CreateDirectory(_cacheDirectory);
            Console.WriteLine($"[HebrewBooks] Cache directory (PDF.js web): {_cacheDirectory}");
        }

        public async Task PrepareHebrewBookDownload(string bookId, string title, string action)
        {
            Console.WriteLine($"[HebrewBooks] Preparing {action} for {bookId}: {title}");

            // Only use cache for view action
            if (action == "view")
            {
                // Check if file exists in PDF.js web directory with hebrewbooks prefix
                string pdfFilePath = Path.Combine(_cacheDirectory, $"hebrewbooks-{bookId}.pdf");
                if (File.Exists(pdfFilePath))
                {
                    Console.WriteLine($"[HebrewBooks] Found in PDF.js web directory: {pdfFilePath}");
                    // Send download complete signal - Vue will construct URL using bookId
                    await SendDownloadComplete(bookId);
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
                        await SendDownloadComplete(bookId, null);
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
                // Save to PDF.js web directory with hebrewbooks prefix
                string pdfFilePath = Path.Combine(_cacheDirectory, $"hebrewbooks-{bookId}.pdf");
                e.ResultFilePath = pdfFilePath;

                e.DownloadOperation.StateChanged += async (sender, args) =>
                {
                    if (sender is CoreWebView2DownloadOperation download && download.State == CoreWebView2DownloadState.Completed)
                    {
                        try
                        {
                            Console.WriteLine($"[HebrewBooks] Download completed to PDF.js web directory: {pdfFilePath}");
                            
                            // Close the download dialog for view action (to show PDF immediately)
                            _webView.CloseDefaultDownloadDialog();
                            
                            // Simple cache management - just delete oldest files if we have too many
                            ManageCache();
                            
                            // Send download complete signal - Vue will construct URL using bookId
                            await SendDownloadComplete(bookId);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[HebrewBooks] Error processing downloaded file: {ex.Message}");
                            await SendDownloadComplete(bookId, null);
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
                            await SendDownloadComplete(bookId, download.ResultFilePath);
                        }
                        else if (download.State == CoreWebView2DownloadState.Interrupted)
                        {
                            Console.WriteLine($"[HebrewBooks] Download was interrupted");
                            await SendDownloadComplete(bookId, null);
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

        private async Task SendDownloadComplete(string bookId)
        {
            string js = $"window.receiveHebrewBookDownloadComplete({JsonSerializer.Serialize(bookId)}, true);";
            await _webView.ExecuteScriptAsync(js);
        }

        private async Task SendDownloadComplete(string bookId, string filePath)
        {
            string js = $"window.receiveHebrewBookDownloadComplete({JsonSerializer.Serialize(bookId)}, {JsonSerializer.Serialize(filePath)});";
            await _webView.ExecuteScriptAsync(js);
        }

        private void ManageCache()
        {
            try
            {
                // Only manage Hebrew book PDF files (files with "hebrewbooks-" prefix)
                var hebrewBookFiles = Directory.GetFiles(_cacheDirectory, "hebrewbooks-*.pdf")
                    .Select(f => new FileInfo(f))
                    .OrderBy(f => f.LastAccessTime)
                    .ToArray();

                // Delete oldest Hebrew book files if we have more than MAX_CACHE_SIZE
                while (hebrewBookFiles.Length > MAX_CACHE_SIZE)
                {
                    hebrewBookFiles[0].Delete();
                    Console.WriteLine($"[HebrewBooks] Deleted old cache file: {hebrewBookFiles[0].Name}");
                    hebrewBookFiles = hebrewBookFiles.Skip(1).ToArray();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HebrewBooks] Error managing cache: {ex.Message}");
            }
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