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
        private Action _popOutToggleAction;

        public ZayitViewerCommands(WebView2 webView)
        {
            this._webView = webView;
            _dbCommands = new ZayitViewerDbCommands(webView);
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
            _hebrewBooksDownloadManager = new HebrewBooksDownloadManager(coreWebView);
        }

        /// <summary>
        /// Check if the viewer is hosted in a UserControl
        /// </summary>
        private async void CheckHostingMode()
        {
            try
            {
                bool isInUserControl = _webView.Parent is UserControl;
                string js = $"window.setHostingMode && window.setHostingMode({isInUserControl.ToString().ToLower()});";
                await _webView.ExecuteScriptAsync(js);
                Debug.WriteLine($"Hosting mode sent: isInUserControl={isInUserControl}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CheckHostingMode error: {ex}");
            }
        }

        /// <summary>
        /// Open PDF file picker dialog (DEPRECATED - Vue now uses browser file picker)
        /// </summary>
        private async void OpenPdfFilePicker()
        {
            try
            {
                string filePath = null;
                string fileName = null;
                string base64 = null;

                // Ensure dialog runs on UI thread
                if (_webView.InvokeRequired)
                {
                    _webView.Invoke(new Action(() => OpenPdfFilePickerOnUIThread(out filePath, out fileName, out base64)));
                }
                else
                {
                    OpenPdfFilePickerOnUIThread(out filePath, out fileName, out base64);
                }

                string filePathJson = JsonSerializer.Serialize(filePath);
                string fileNameJson = JsonSerializer.Serialize(fileName);
                string base64Json = JsonSerializer.Serialize(base64);

                string js = $"window.receivePdfFilePath({filePathJson}, {fileNameJson}, {base64Json});";
                await _webView.ExecuteScriptAsync(js);

                Debug.WriteLine($"PDF result sent: filePath={filePath}, fileName={fileName}, hasBase64={base64 != null}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OpenPdfFilePicker error: {ex}");
            }
        }

        /// <summary>
        /// Helper method to run file picker on UI thread
        /// </summary>
        private void OpenPdfFilePickerOnUIThread(out string filePath, out string fileName, out string base64)
        {
            filePath = null;
            fileName = null;
            base64 = null;

            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "PDF Files (*.pdf)|*.pdf";
                dlg.Title = "בחר קובץ PDF";

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    filePath = dlg.FileName;
                    fileName = Path.GetFileName(filePath);

                    try
                    {
                        byte[] bytes = File.ReadAllBytes(filePath);
                        base64 = Convert.ToBase64String(bytes);
                    }
                    catch (Exception fileEx)
                    {
                        Debug.WriteLine($"Failed to read PDF: {fileEx}");
                    }
                }
            }
        }

        /// <summary>
        /// Load PDF using WebView2 virtual host mapping (DEPRECATED - Vue now uses browser file picker)
        /// </summary>
        private async void LoadPdfFromPath(string filePath)
        {
            try
            {
                Debug.WriteLine($"LoadPdfFromPath called: {filePath}");

                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    try
                    {
                        // Get the folder containing the PDF
                        string pdfFolder = Path.GetDirectoryName(filePath);
                        string pdfFileName = Path.GetFileName(filePath);
                        
                        Debug.WriteLine($"PDF folder: {pdfFolder}");
                        Debug.WriteLine($"PDF filename: {pdfFileName}");

                        // Ensure WebView2 is initialized
                        if (_webView.CoreWebView2 == null)
                        {
                            Debug.WriteLine("WebView2 not initialized, waiting...");
                            await _webView.EnsureCoreWebView2Async();
                        }

                        // Clear any existing virtual host mapping for pdf.local
                        try
                        {
                            _webView.CoreWebView2.ClearVirtualHostNameToFolderMapping("pdf.local");
                        }
                        catch (Exception clearEx)
                        {
                            Debug.WriteLine($"Note: Could not clear existing mapping: {clearEx.Message}");
                        }

                        // Map the PDF's folder to a virtual host
                        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                            "pdf.local",
                            pdfFolder,
                            Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow
                        );

                        Debug.WriteLine($"Mapped pdf.local to folder: {pdfFolder}");

                        // Create the virtual URL for the PDF
                        string virtualUrl = $"https://pdf.local/{Uri.EscapeDataString(pdfFileName)}";
                        
                        Debug.WriteLine($"Virtual PDF URL: {virtualUrl}");

                        // Send the virtual URL to JavaScript
                        string filePathJson = JsonSerializer.Serialize(filePath);
                        string virtualUrlJson = JsonSerializer.Serialize(virtualUrl);
                        string js = $"window.receivePdfVirtualUrl({filePathJson}, {virtualUrlJson});";
                        
                        await _webView.ExecuteScriptAsync(js);

                        Debug.WriteLine($"PDF virtual URL sent: filePath={filePath}, virtualUrl={virtualUrl}");
                    }
                    catch (Exception fileEx)
                    {
                        Debug.WriteLine($"Failed to map PDF folder: {fileEx.Message}");
                        
                        // Fallback to base64 for compatibility
                        await LoadPdfAsBase64Fallback(filePath);
                    }
                }
                else
                {
                    Debug.WriteLine($"PDF file not found: {filePath}");
                    
                    // Send null result
                    string filePathJson = JsonSerializer.Serialize(filePath);
                    string js = $"window.receivePdfVirtualUrl({filePathJson}, null);";
                    await _webView.ExecuteScriptAsync(js);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadPdfFromPath error: {ex}");

                // Send null result on error
                string filePathJson = JsonSerializer.Serialize(filePath);
                string js = $"window.receivePdfVirtualUrl({filePathJson}, null);";
                await _webView.ExecuteScriptAsync(js);
            }
        }

        /// <summary>
        /// Fallback method for base64 loading when virtual mapping fails
        /// </summary>
        private async Task LoadPdfAsBase64Fallback(string filePath)
        {
            try
            {
                Debug.WriteLine($"Using base64 fallback for: {filePath}");
                
                // Read PDF file and convert to base64
                byte[] pdfBytes = File.ReadAllBytes(filePath);
                string base64 = Convert.ToBase64String(pdfBytes);
                
                Debug.WriteLine($"PDF loaded as base64, size: {pdfBytes.Length} bytes");

                // Send base64 data URL to JavaScript
                string filePathJson = JsonSerializer.Serialize(filePath);
                string dataUrlJson = JsonSerializer.Serialize(base64);
                string js = $"window.receivePdfDataUrl({filePathJson}, {dataUrlJson});";
                
                await _webView.ExecuteScriptAsync(js);
                
                Debug.WriteLine($"PDF base64 fallback sent: filePath={filePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Base64 fallback failed: {ex}");
            }
        }

        /// <summary>
        /// Prepare Hebrew book for download or viewing
        /// </summary>
        private async void PrepareHebrewBookDownload(string bookId, string title, string action)
        {
            if (_hebrewBooksDownloadManager != null)
            {
                await _hebrewBooksDownloadManager.PrepareHebrewBookDownload(bookId, title, action);
            }
            else
            {
                Console.WriteLine("[HebrewBooks] Download manager not initialized");
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