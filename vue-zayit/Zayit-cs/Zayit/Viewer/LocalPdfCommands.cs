using Microsoft.Web.WebView2.WinForms;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Zayit.Viewer
{
    /// <summary>
    /// LOCAL PDF FUNCTIONALITY ONLY
    /// 
    /// This class handles ONLY local PDF file operations:
    /// - Opening PDF files from user's computer via file dialog
    /// - Replacing PDF.js built-in file dialog with C# dialog
    /// - Session persistence for local PDF files
    /// - Virtual host mapping for local PDF access
    /// 
    /// DO NOT CONFUSE WITH HebrewBooksCommands:
    /// - LocalPdfCommands = Opens local PDF files from user's computer
    /// - HebrewBooksCommands = Downloads from hebrewbooks.org website
    /// 
    /// Files are saved with "local-{guid}.pdf" naming pattern
    /// </summary>
    public class LocalPdfCommands
    {
        private readonly WebView2 _webView;
        private LocalPdfManager _pdfManager;

        public LocalPdfCommands(WebView2 webView)
        {
            _webView = webView;
        }

        /// <summary>
        /// Initialize local PDF manager
        /// ONLY for local PDF files from user's computer, NOT Hebrew books
        /// </summary>
        public async Task Initialize(string htmlPath)
        {
            _pdfManager = new LocalPdfManager(_webView, htmlPath);
            await _pdfManager.Initialize();
            Console.WriteLine("[LocalPDF] Manager initialized");
        }

        /// <summary>
        /// Open local PDF file dialog
        /// This replaces PDF.js built-in file dialog with C# dialog
        /// For LOCAL files only, NOT Hebrew books from website
        /// </summary>
        public async Task OpenLocalPdfDialog()
        {
            if (_pdfManager != null)
            {
                await _pdfManager.OpenLocalPdfDialog();
            }
            else
            {
                Console.WriteLine("[LocalPDF] Manager not initialized");
            }
        }

        /// <summary>
        /// Open specific local PDF file by path
        /// For LOCAL files only, NOT Hebrew books
        /// </summary>
        public async Task OpenPdfFile(string filePath)
        {
            if (_pdfManager != null)
            {
                await _pdfManager.OpenPdfFile(filePath);
            }
            else
            {
                Console.WriteLine("[LocalPDF] Manager not initialized");
            }
        }

        /// <summary>
        /// Restore last opened local PDF session
        /// For LOCAL files only, NOT Hebrew books
        /// </summary>
        public async Task RestoreLastSession()
        {
            if (_pdfManager != null)
            {
                await _pdfManager.RestoreLastSession();
            }
            else
            {
                Console.WriteLine("[LocalPDF] Manager not initialized");
            }
        }

        /// <summary>
        /// Clean up old cached local PDF files
        /// For LOCAL files only, NOT Hebrew books
        /// </summary>
        public void CleanupCache()
        {
            if (_pdfManager != null)
            {
                _pdfManager.CleanupCache();
            }
            else
            {
                Console.WriteLine("[LocalPDF] Manager not initialized");
            }
        }

        /// <summary>
        /// Check if a file is a local PDF cache file
        /// Local PDFs use "local-{guid}.pdf" pattern
        /// </summary>
        public bool IsLocalPdfCacheFile(string fileName)
        {
            return fileName != null && fileName.StartsWith("local-") && fileName.EndsWith(".pdf");
        }
    }
}