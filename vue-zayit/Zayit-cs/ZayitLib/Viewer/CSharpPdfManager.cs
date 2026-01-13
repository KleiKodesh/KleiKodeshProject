using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Zayit.Viewer
{
    /// <summary>
    /// C# PDF manager using WebView2 virtual host mapping
    /// Creates secure HTTPS URLs that work perfectly with PDF.js viewer
    /// Maps directly to original file locations - no copying needed
    /// Uses unique virtual hosts to avoid mapping conflicts
    /// </summary>
    public class CSharpPdfManager
    {
        private readonly WebView2 _webView;
        private readonly ConcurrentDictionary<string, string> _virtualMappings = new ConcurrentDictionary<string, string>();
        private readonly string _virtualHost = "app.local";
        private bool _isInitialized = false;

        public CSharpPdfManager(WebView2 webView)
        {
            _webView = webView;
        }

        /// <summary>
        /// Initialize the virtual host mapping system
        /// </summary>
        public async Task Initialize()
        {
            if (_isInitialized) return;

            await _webView.EnsureCoreWebView2Async();
            _isInitialized = true;
            
            Console.WriteLine($"[CSharpPdf] Virtual host mapping system initialized for {_virtualHost}");
        }

        /// <summary>
        /// Check if the PDF manager is initialized and ready
        /// </summary>
        public async Task<bool> IsInitialized()
        {
            return _isInitialized && _webView?.CoreWebView2 != null;
        }

        /// <summary>
        /// Create virtual HTTPS URL for PDF file at its original location
        /// Uses unique virtual host for each file to avoid mapping conflicts
        /// </summary>
        public async Task<string> CreateObjectURLFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"PDF file not found: {filePath}");

            // Get directory and filename
            string directory = Path.GetDirectoryName(filePath);
            string fileName = Path.GetFileName(filePath);
            
            // Create unique virtual host for this file
            string uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
            string uniqueVirtualHost = $"{uniqueId}.{_virtualHost}";
            
            // Create virtual URL
            string virtualUrl = $"https://{uniqueVirtualHost}/{fileName}";

            // Map this unique virtual host to the file's directory
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                uniqueVirtualHost,
                directory,
                CoreWebView2HostResourceAccessKind.Allow
            );

            // Store mapping for cleanup
            _virtualMappings[uniqueId] = directory;
            
            Console.WriteLine($"[CSharpPdf] Mapped {directory} -> https://{uniqueVirtualHost}/");
            Console.WriteLine($"[CSharpPdf] Created virtual URL: {virtualUrl}");
            
            return virtualUrl;
        }

        /// <summary>
        /// C# equivalent of URL.createObjectURL(file) - for byte array data
        /// Creates temp file and maps its directory
        /// </summary>
        public async Task<string> CreateObjectURL(byte[] pdfData)
        {
            // Create temp file for the PDF data
            string tempDir = Path.Combine(Path.GetTempPath(), "ZayitPdfViewer");
            Directory.CreateDirectory(tempDir);
            
            string fileName = $"{Guid.NewGuid().ToString("N")}.pdf";
            string tempFilePath = Path.Combine(tempDir, fileName);
            
            await WriteAllBytesAsync(tempFilePath, pdfData);
            
            // Now map this temp file like any other file
            return await CreateObjectURLFromFile(tempFilePath);
        }

        /// <summary>
        /// Show C# file dialog and create virtual URL
        /// </summary>
        public async Task<(string fileName, string virtualUrl, string originalPath)> ShowFileDialogAndCreateUrl()
        {
            // Use the WebViewDialogHelper for robust dialog handling
            string selectedFile = await WebViewDialogHelper.ShowOpenFileDialogAsync(
                _webView, 
                "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*", 
                "Select PDF File"
            );

            if (string.IsNullOrEmpty(selectedFile))
                return (null, null, null);

            string fileName = Path.GetFileName(selectedFile);

            // Create virtual URL from selected file at its original location
            string virtualUrl = await CreateObjectURLFromFile(selectedFile);
            return (fileName, virtualUrl, selectedFile); // Return original path too
        }

        /// <summary>
        /// Open PDF using virtual HTTPS URL
        /// </summary>
        public async Task OpenPdfWithVirtualUrl(string fileName, string virtualUrl)
        {
            // Send virtual URL to Vue/JavaScript (same interface as blob URL)
            var response = new
            {
                type = "virtualPdfUrl",
                fileName = fileName,
                fileUrl = virtualUrl,  // Use fileUrl to match Vue interface
                success = true
            };

            string json = JsonSerializer.Serialize(response);
            _webView.CoreWebView2.PostWebMessageAsString(json);

            Console.WriteLine($"[CSharpPdf] Sent virtual URL to Vue: {fileName} -> {virtualUrl}");
        }

        /// <summary>
        /// C# equivalent of URL.revokeObjectURL()
        /// Cleans up virtual host mappings (mappings persist but we track them)
        /// </summary>
        public void RevokeObjectURL(string virtualUrl)
        {
            if (virtualUrl.Contains($".{_virtualHost}/"))
            {
                // Extract unique ID from virtual host
                string virtualHost = virtualUrl.Split('/')[2]; // Get host part
                string uniqueId = virtualHost.Split('.')[0]; // Get ID before .app.local
                
                if (_virtualMappings.TryRemove(uniqueId, out string directory))
                {
                    Console.WriteLine($"[CSharpPdf] Revoked virtual URL: {virtualUrl}");
                    Console.WriteLine($"[CSharpPdf] Note: Virtual host mapping persists but is no longer tracked");
                }
            }
        }

        /// <summary>
        /// Clean up all virtual mappings tracking
        /// Note: Virtual host mappings persist in WebView2 but we stop tracking them
        /// </summary>
        public void Cleanup()
        {
            _virtualMappings.Clear();
            Console.WriteLine("[CSharpPdf] Cleaned up virtual mapping tracking");
        }

        /// <summary>
        /// Recreate virtual URL from original file path (for session persistence)
        /// </summary>
        public async Task<string> RecreateVirtualUrlFromPath(string originalPath)
        {
            if (!File.Exists(originalPath))
                throw new FileNotFoundException($"PDF file not found: {originalPath}");

            // Create virtual URL from the stored file path
            return await CreateObjectURLFromFile(originalPath);
        }

        /// <summary>
        /// Get the virtual host name used for PDF URLs
        /// </summary>
        public string VirtualHost => _virtualHost;

        /// <summary>
        /// Helper method for async file writing (not available in .NET Framework 4.7.2)
        /// </summary>
        private async Task WriteAllBytesAsync(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
            {
                await stream.WriteAsync(bytes, 0, bytes.Length);
            }
        }
    }
}