using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Threading.Tasks;

namespace Zayit.Viewer
{
    /// <summary>
    /// HEBREW BOOKS FUNCTIONALITY ONLY
    /// 
    /// This class handles ONLY Hebrew Books operations:
    /// - Downloading Hebrew books from hebrewbooks.org
    /// - Caching Hebrew books in PDF.js web directory
    /// - Managing Hebrew book download states
    /// 
    /// Files are saved with book title as filename (sanitized)
    /// </summary>
    public class HebrewBooksCommands
    {
        private readonly WebView2 _webView;
        private HebrewBooksDownloadManager _downloadManager;

        public HebrewBooksCommands(WebView2 webView)
        {
            _webView = webView;
        }

        /// <summary>
        /// Initialize Hebrew Books download manager
        /// ONLY for hebrewbooks.org downloads
        /// </summary>
        public void Initialize(CoreWebView2 coreWebView)
        {
            _downloadManager = new HebrewBooksDownloadManager(coreWebView, _webView);
            Console.WriteLine("[HebrewBooks] Download manager initialized");
        }

        /// <summary>
        /// Prepare Hebrew book for download or viewing
        /// This is for books from hebrewbooks.org website ONLY
        /// </summary>
        public async Task PrepareHebrewBookDownload(string bookId, string title, string action)
        {
            if (_downloadManager != null)
            {
                await _downloadManager.PrepareHebrewBookDownload(bookId, title, action);
            }
            else
            {
                Console.WriteLine("[HebrewBooks] Download manager not initialized");
            }
        }

        /// <summary>
        /// Handle Hebrew book download capture
        /// This intercepts downloads from hebrewbooks.org ONLY
        /// </summary>
        public void HandleDownloadStarting(CoreWebView2DownloadStartingEventArgs e)
        {
            if (_downloadManager != null)
            {
                _downloadManager.HandleDownloadStarting(e);
            }
            else
            {
                Console.WriteLine("[HebrewBooks] Download manager not initialized");
            }
        }

        /// <summary>
        /// Check if a download is a Hebrew book download
        /// Hebrew books come from hebrewbooks.org domain
        /// </summary>
        public bool IsHebrewBookDownload(string uri)
        {
            return uri != null && uri.Contains("hebrewbooks.org");
        }
    }
}