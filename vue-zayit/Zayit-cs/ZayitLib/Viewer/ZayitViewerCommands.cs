using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Zayit.Viewer
{
    internal class ZayitViewerCommands
    {
        readonly WebView2 _webView;
        private HebrewBooksDownloadManager _hebrewBooksDownloadManager;
        private ZayitViewerDbCommands _dbCommands;
        private CSharpPdfManager _pdfManager;
        private Action _popOutToggleAction;

        public ZayitViewerCommands(WebView2 webView)
        {
            this._webView = webView;
            _dbCommands = new ZayitViewerDbCommands(webView);
            _pdfManager = new CSharpPdfManager(webView);
        }

        /// <summary>
        /// Set the popout toggle action - called by ZayitViewerHost when popout functionality is available
        /// </summary>
        public void SetPopOutToggleAction(Action popOutToggleAction)
        {
            _popOutToggleAction = popOutToggleAction;
        }

        // Database commands delegation
        public ZayitViewerDbCommands DbCommands => _dbCommands;

        // Database command delegation methods for reflection-based dispatching
        private async void GetTree() => _dbCommands.GetTree();
        private async void GetToc(int bookId) => _dbCommands.GetToc(bookId);
        private async void GetLinks(int lineId, string tabId, int bookId) => _dbCommands.GetLinks(lineId, tabId, bookId);
        private async void GetTotalLines(int bookId) => _dbCommands.GetTotalLines(bookId);
        private async void GetLineContent(int bookId, int lineIndex) => _dbCommands.GetLineContent(bookId, lineIndex);
        private async void GetLineId(int bookId, int lineIndex) => _dbCommands.GetLineId(bookId, lineIndex);
        private async void GetLineRange(int bookId, int start, int end) => _dbCommands.GetLineRange(bookId, start, end);
        private async void SearchLines(int bookId, string searchTerm) => _dbCommands.SearchLines(bookId, searchTerm);

        public void InitializeHebrewBooksDownloadManager(CoreWebView2 coreWebView)
        {
            _hebrewBooksDownloadManager = new HebrewBooksDownloadManager(coreWebView, _webView);
        }

        /// <summary>
        /// Initialize the PDF manager for virtual host mapping
        /// </summary>
        public async Task InitializePdfManager()
        {
            await _pdfManager.Initialize();
        }

        /// <summary>
        /// Show C# file dialog and create virtual URL (integrates with existing bridge system)
        /// </summary>
        private async void OpenPdfFilePicker()
        {
            try
            {
                // Use ConfigureAwait(false) to avoid deadlocks and ensure proper async handling
                var (fileName, virtualUrl, originalPath) = await _pdfManager.ShowFileDialogAndCreateUrl().ConfigureAwait(false);
                
                if (!string.IsNullOrEmpty(virtualUrl))
                {
                    // Use existing bridge pattern - call receivePdfFilePath with all 3 parameters
                    // virtualUrl for current session viewing, originalPath for persistence
                    // Properly escape strings for JavaScript
                    string escapedVirtualUrl = EscapeJavaScriptString(virtualUrl);
                    string escapedFileName = EscapeJavaScriptString(fileName);
                    string escapedOriginalPath = EscapeJavaScriptString(originalPath);
                    
                    string js = $"window.receivePdfFilePath && window.receivePdfFilePath('{escapedVirtualUrl}', '{escapedFileName}', '{escapedOriginalPath}');";
                    
                    // Ensure we're on the UI thread for WebView operations
                    if (_webView.InvokeRequired)
                    {
                        _webView.BeginInvoke(new Action(async () => {
                            await _webView.ExecuteScriptAsync(js);
                        }));
                    }
                    else
                    {
                        await _webView.ExecuteScriptAsync(js);
                    }
                    
                    Console.WriteLine($"[PdfManager] Sent to bridge: {fileName} -> {virtualUrl} (original: {originalPath})");
                }
                else
                {
                    // No file selected
                    string js = "window.receivePdfFilePath && window.receivePdfFilePath(null, null, null);";
                    
                    if (_webView.InvokeRequired)
                    {
                        _webView.BeginInvoke(new Action(async () => {
                            await _webView.ExecuteScriptAsync(js);
                        }));
                    }
                    else
                    {
                        await _webView.ExecuteScriptAsync(js);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfManager] Error in file picker: {ex}");
                
                // Send error via bridge
                string js = "window.receivePdfFilePath && window.receivePdfFilePath(null, null, null);";
                
                if (_webView.InvokeRequired)
                {
                    _webView.BeginInvoke(new Action(async () => {
                        await _webView.ExecuteScriptAsync(js);
                    }));
                }
                else
                {
                    await _webView.ExecuteScriptAsync(js);
                }
            }
        }

        /// <summary>
        /// Escape string for safe use in JavaScript
        /// </summary>
        private string EscapeJavaScriptString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
                
            return input
                .Replace("\\", "\\\\")  // Escape backslashes first
                .Replace("'", "\\'")    // Escape single quotes
                .Replace("\"", "\\\"")  // Escape double quotes
                .Replace("\n", "\\n")   // Escape newlines
                .Replace("\r", "\\r")   // Escape carriage returns
                .Replace("\t", "\\t");  // Escape tabs
        }

        /// <summary>
        /// Check if PDF manager is ready for operations
        /// </summary>
        private async void CheckPdfManagerReady()
        {
            try
            {
                bool isReady = _pdfManager != null && await _pdfManager.IsInitialized();
                
                // Send readiness status to Vue
                string js = $"window.receivePdfManagerReady && window.receivePdfManagerReady({isReady.ToString().ToLower()});";
                await _webView.ExecuteScriptAsync(js);
                
                Console.WriteLine($"[PdfManager] PDF manager ready status: {isReady}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfManager] Error checking PDF manager readiness: {ex}");
                
                // Send not ready status
                string js = "window.receivePdfManagerReady && window.receivePdfManagerReady(false);";
                await _webView.ExecuteScriptAsync(js);
            }
        }

        /// <summary>
        /// Recreate virtual URL from stored file path (for session persistence)
        /// </summary>
        private async void RecreateVirtualUrlFromPath(string originalPath)
        {
            try
            {
                string virtualUrl = await _pdfManager.RecreateVirtualUrlFromPath(originalPath);
                
                // Send recreated virtual URL back to Vue
                // Properly escape strings for JavaScript
                string escapedOriginalPath = EscapeJavaScriptString(originalPath);
                string escapedVirtualUrl = virtualUrl != null ? EscapeJavaScriptString(virtualUrl) : "null";
                
                string js = $"window.receivePdfVirtualUrl && window.receivePdfVirtualUrl('{escapedOriginalPath}', {(virtualUrl != null ? $"'{escapedVirtualUrl}'" : "null")});";
                await _webView.ExecuteScriptAsync(js);
                
                Console.WriteLine($"[PdfManager] Recreated virtual URL for: {originalPath} -> {virtualUrl}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfManager] Error recreating virtual URL for {originalPath}: {ex}");
                
                // Send error response
                string escapedOriginalPath = EscapeJavaScriptString(originalPath);
                string js = $"window.receivePdfVirtualUrl && window.receivePdfVirtualUrl('{escapedOriginalPath}', null);";
                await _webView.ExecuteScriptAsync(js);
            }
        }



        /// <summary>
        /// Prepare Hebrew book for download or viewing
        /// </summary>
        private async void PrepareHebrewBookDownload(string bookId, string title, string action)
        {
            try
            {
                Console.WriteLine($"[HebrewBooks] PrepareHebrewBookDownload called - bookId: {bookId}, title: {title}, action: {action}");
                
                if (_hebrewBooksDownloadManager != null)
                {
                    await _hebrewBooksDownloadManager.PrepareHebrewBookDownload(bookId, title, action);
                }
                else
                {
                    Console.WriteLine("[HebrewBooks] Download manager not initialized");
                    // Send error response following bridge pattern
                    string js = $"window.receiveHebrewBookDownloadReady && window.receiveHebrewBookDownloadReady({JsonSerializer.Serialize(bookId)}, {JsonSerializer.Serialize(action)});";
                    await _webView.ExecuteScriptAsync(js);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HebrewBooks] Error in PrepareHebrewBookDownload: {ex}");
                // Send error response following bridge pattern
                string js = $"window.receiveHebrewBookDownloadComplete && window.receiveHebrewBookDownloadComplete({JsonSerializer.Serialize(bookId)}, null);";
                await _webView.ExecuteScriptAsync(js);
            }
        }

        /// <summary>
        /// Toggle popout mode for the task pane
        /// </summary>
        private async void TogglePopOut()
        {
            try
            {
                if (_popOutToggleAction != null)
                {
                    _popOutToggleAction.Invoke();
                    Debug.WriteLine("TogglePopOut: Successfully invoked popout toggle action");
                }
                else
                {
                    Debug.WriteLine("TogglePopOut: No popout toggle action available");
                    // Send message back to Vue indicating popout is not available
                    string js = "window.postMessage(JSON.stringify({type: 'popout-unavailable', message: 'Popout not available in this context'}), '*');";
                    await _webView.ExecuteScriptAsync(js);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TogglePopOut error: {ex}");
            }
        }

        /// <summary>
        /// Handle Hebrew book download capture
        /// </summary>
        public void HandleDownloadStarting(CoreWebView2DownloadStartingEventArgs e)
        {
            if (_hebrewBooksDownloadManager != null)
            {
                _hebrewBooksDownloadManager.HandleDownloadStarting(e);
            }
            else
            {
                Console.WriteLine("[HebrewBooks] Download manager not initialized");
            }
        }
    }
}