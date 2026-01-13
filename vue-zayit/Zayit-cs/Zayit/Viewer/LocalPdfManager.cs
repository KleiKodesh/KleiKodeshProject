using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Zayit.Viewer
{
    /// <summary>
    /// Manages local PDF file operations with persistence and streaming support
    /// Replaces PDF.js built-in file dialog with C# implementation
    /// </summary>
    public class LocalPdfManager
    {
        private readonly WebView2 _webView;
        private readonly string _pdfJsWebDirectory;
        private readonly string _sessionFile;

        public LocalPdfManager(WebView2 webView, string htmlPath)
        {
            _webView = webView;
            _pdfJsWebDirectory = Path.Combine(htmlPath, "pdfjs", "web");
            _sessionFile = Path.Combine(_pdfJsWebDirectory, "local-pdf-session.json");
            
            // Ensure directory exists
            Directory.CreateDirectory(_pdfJsWebDirectory);
        }

        /// <summary>
        /// Initialize the local PDF manager
        /// </summary>
        public async Task Initialize()
        {
            await RestoreLastSession();
        }

        /// <summary>
        /// Show C# file dialog and open selected PDF
        /// </summary>
        public async Task OpenLocalPdfDialog()
        {
            try
            {
                string selectedFile = null;
                string fileName = null;

                // Show file dialog on UI thread
                if (_webView.InvokeRequired)
                {
                    _webView.Invoke(new Action(() => {
                        selectedFile = ShowFileDialog();
                        if (!string.IsNullOrEmpty(selectedFile))
                        {
                            fileName = Path.GetFileName(selectedFile);
                        }
                    }));
                }
                else
                {
                    selectedFile = ShowFileDialog();
                    if (!string.IsNullOrEmpty(selectedFile))
                    {
                        fileName = Path.GetFileName(selectedFile);
                    }
                }

                // Send response to Vue in expected format
                var response = new
                {
                    type = "pdfFilePicker",
                    filePath = selectedFile,
                    fileName = fileName,
                    success = !string.IsNullOrEmpty(selectedFile)
                };

                string json = JsonSerializer.Serialize(response);
                _webView.CoreWebView2.PostWebMessageAsString(json);

                // Let Vue handle opening the PDF - don't open it here
                Console.WriteLine($"[LocalPDF] File dialog completed, sent response to Vue: {fileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LocalPDF] Error opening file dialog: {ex}");
                
                // Send error response to Vue
                var errorResponse = new
                {
                    type = "pdfFilePicker",
                    filePath = (string)null,
                    fileName = (string)null,
                    success = false,
                    error = ex.Message
                };

                string json = JsonSerializer.Serialize(errorResponse);
                _webView.CoreWebView2.PostWebMessageAsString(json);
            }
        }

        /// <summary>
        /// Show the file dialog and return selected file path
        /// </summary>
        private string ShowFileDialog()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*";
                dialog.Title = "Open PDF File";
                dialog.CheckFileExists = true;
                dialog.CheckPathExists = true;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    return dialog.FileName;
                }
            }
            return null;
        }

        /// <summary>
        /// Open PDF file using virtual host (same performance as PDF.js native)
        /// </summary>
        public async Task OpenPdfFile(string originalPath)
        {
            try
            {
                if (!File.Exists(originalPath))
                {
                    Console.WriteLine($"[LocalPDF] File not found: {originalPath}");
                    return;
                }

                string fileName = Path.GetFileName(originalPath);
                string targetPath = Path.Combine(_pdfJsWebDirectory, $"local-{Guid.NewGuid():N}.pdf");

                // Copy file to PDF.js web directory for virtual host access
                File.Copy(originalPath, targetPath, true);
                Console.WriteLine($"[LocalPDF] Copied to: {targetPath}");

                // Create virtual host URL
                string virtualFileName = Path.GetFileName(targetPath);
                string fileUrl = $"https://zayitHost/pdfjs/web/{virtualFileName}";

                // Open in PDF.js - exactly like native file dialog
                string js = $@"
                    console.log('[LocalPDF] Opening PDF:', '{fileUrl}');
                    PDFViewerApplication.open({{
                        url: '{fileUrl}',
                        originalUrl: '{fileName.Replace("'", "\\'")}' 
                    }}).then(() => {{
                        console.log('[LocalPDF] PDF opened successfully');
                    }}).catch((error) => {{
                        console.error('[LocalPDF] Error opening PDF:', error);
                    }});
                ";

                await _webView.CoreWebView2.ExecuteScriptAsync(js);

                // Save session for persistence
                await SaveSessionInfo(originalPath, fileName, virtualFileName);

                Console.WriteLine($"[LocalPDF] Opened: {fileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LocalPDF] Error opening PDF: {ex}");
            }
        }

        /// <summary>
        /// Save session information for persistence
        /// </summary>
        private async Task SaveSessionInfo(string originalPath, string fileName, string virtualFileName)
        {
            try
            {
                var session = new LocalPdfSession
                {
                    OriginalPath = originalPath,
                    FileName = fileName,
                    VirtualFileName = virtualFileName,
                    LastOpened = DateTime.Now
                };

                string json = JsonSerializer.Serialize(session, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });

                File.WriteAllText(_sessionFile, json);
                Console.WriteLine($"[LocalPDF] Session saved: {fileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LocalPDF] Error saving session: {ex}");
            }
        }

        /// <summary>
        /// Restore last opened PDF on startup
        /// </summary>
        public async Task RestoreLastSession()
        {
            try
            {
                if (!File.Exists(_sessionFile))
                {
                    Console.WriteLine("[LocalPDF] No session file found");
                    return;
                }

                string json = File.ReadAllText(_sessionFile);
                var session = JsonSerializer.Deserialize<LocalPdfSession>(json);

                if (session == null)
                {
                    Console.WriteLine("[LocalPDF] Invalid session data");
                    return;
                }

                // Check if virtual file still exists
                string virtualPath = Path.Combine(_pdfJsWebDirectory, session.VirtualFileName);
                if (!File.Exists(virtualPath))
                {
                    // Try to restore from original path
                    if (File.Exists(session.OriginalPath))
                    {
                        Console.WriteLine($"[LocalPDF] Restoring from original: {session.OriginalPath}");
                        await OpenPdfFile(session.OriginalPath);
                        return;
                    }
                    else
                    {
                        Console.WriteLine($"[LocalPDF] Original file no longer exists: {session.OriginalPath}");
                        return;
                    }
                }

                // Restore using existing virtual file
                string fileUrl = $"https://zayitHost/pdfjs/web/{session.VirtualFileName}";
                
                string js = $@"
                    // Wait for PDF.js to be ready before restoring
                    function restoreSession() {{
                        if (typeof PDFViewerApplication === 'undefined' || !PDFViewerApplication.initialized) {{
                            setTimeout(restoreSession, 100);
                            return;
                        }}
                        
                        console.log('[LocalPDF] Restoring session:', '{session.FileName}');
                        PDFViewerApplication.open({{
                            url: '{fileUrl}',
                            originalUrl: '{session.FileName.Replace("'", "\\'")}'
                        }}).then(() => {{
                            console.log('[LocalPDF] Session restored successfully');
                        }}).catch((error) => {{
                            console.error('[LocalPDF] Error restoring session:', error);
                        }});
                    }}
                    
                    restoreSession();
                ";

                await _webView.CoreWebView2.ExecuteScriptAsync(js);
                Console.WriteLine($"[LocalPDF] Session restored: {session.FileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LocalPDF] Error restoring session: {ex}");
            }
        }

        /// <summary>
        /// Clean up old cached PDF files (keep last 10)
        /// </summary>
        public void CleanupCache()
        {
            try
            {
                var localPdfFiles = Directory.GetFiles(_pdfJsWebDirectory, "local-*.pdf");
                
                if (localPdfFiles.Length <= 10)
                    return;

                // Sort by creation time and delete oldest
                Array.Sort(localPdfFiles, (x, y) => 
                    File.GetCreationTime(x).CompareTo(File.GetCreationTime(y)));

                for (int i = 0; i < localPdfFiles.Length - 10; i++)
                {
                    try
                    {
                        File.Delete(localPdfFiles[i]);
                        Console.WriteLine($"[LocalPDF] Cleaned up: {Path.GetFileName(localPdfFiles[i])}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[LocalPDF] Error deleting {localPdfFiles[i]}: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LocalPDF] Error during cleanup: {ex}");
            }
        }
    }

    /// <summary>
    /// Session data for PDF persistence
    /// </summary>
    public class LocalPdfSession
    {
        public string OriginalPath { get; set; }
        public string FileName { get; set; }
        public string VirtualFileName { get; set; }
        public DateTime LastOpened { get; set; }
    }
}