using Microsoft.Web.WebView2.WinForms;
using System;
using System.Linq;
using System.Threading.Tasks;
using Zayit.Viewer;

namespace Zayit.Services
{
    public class ServiceProvider
    {
        private readonly DbService _db;
        private readonly HebrewBooksService _hebrewBooks;
        private readonly PdfService _pdf;
        private readonly BloomSearchService _bloomSearch;
        private readonly WebView2 _webView;
        private Action _popOutAction;

        public ServiceProvider(WebView2 webView, DbQueries db)
        {
            try
            {
                Console.WriteLine("[ServiceProvider] Initializing services...");

                _webView = webView;
                _db = new DbService(db);
                Console.WriteLine("[ServiceProvider] DbService initialized");

                _hebrewBooks = new HebrewBooksService(webView);
                Console.WriteLine("[ServiceProvider] HebrewBooksService initialized");

                _pdf = new PdfService(webView);
                Console.WriteLine("[ServiceProvider] PdfService initialized");

                // Initialize services
                _pdf.InitializePdfManager();
                Console.WriteLine("[ServiceProvider] PDF manager initialized");

                _hebrewBooks.Initialize();
                Console.WriteLine("[ServiceProvider] Hebrew books service initialized");

                _bloomSearch = new BloomSearchService();
                Console.WriteLine("[ServiceProvider] BloomSearchService created");

                // Set up search streaming callbacks
                _bloomSearch.SetSearchCallbacks(
                    onBatch: (searchId, results) => SendSearchBatch(searchId, results),
                    onComplete: (searchId) => SendSearchComplete(searchId),
                    onCancelled: (searchId) => SendSearchCancelled(searchId),
                    onError: (searchId, error) => SendSearchError(searchId, error)
                );
                Console.WriteLine("[ServiceProvider] BloomSearch callbacks configured");

                _bloomSearch.Initialize();
                Console.WriteLine("[ServiceProvider] BloomSearchService initialized");

                Console.WriteLine("[ServiceProvider] All services initialized successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServiceProvider] ERROR during initialization: {ex}");
                throw;
            }
        }

        public void SetPopOutToggleAction(Action action) => _popOutAction = action;

        // Database Operations - Core Vue Communication Pipeline
        public object GetConnectionTypes(string q) => _db.GetConnectionTypes(q);
        public object GetTree(string cq, string bq) => _db.GetTree(cq, bq);
        public object GetToc(int bookId, string q) => _db.GetToc(bookId, q);
        public int GetTotalLines(int bookId, string q) => _db.GetTotalLines(bookId, q);
        public object GetLineId(int bookId, int idx, string q) => _db.GetLineId(bookId, idx, q);
        public object GetLineContent(int bookId, int idx, string q) => _db.GetLineContent(bookId, idx, q);
        public object GetLineRange(int bookId, int s, int e, string q) => _db.GetLineRange(bookId, s, e, q);
        public object GetLinks(int lineId, string tabId, int bookId, string q, object[] p = null)
        {
            try
            {
                Console.WriteLine($"[ServiceProvider] GetLinks called: lineId={lineId}, tabId={tabId}, bookId={bookId}, params={p?.Length ?? 0}");
                var result = _db.GetLinks(lineId, tabId, bookId, q, p);
                Console.WriteLine($"[ServiceProvider] GetLinks result type: {result?.GetType().Name ?? "null"}");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServiceProvider] GetLinks error: {ex.Message}");
                throw;
            }
        }
        public object SearchLines(int bookId, string term, string q) => _db.SearchLines(bookId, term, q);
        public object DiagnoseDatabaseContent() => _db.DiagnoseDatabaseContent();

        // Generic query execution for custom queries
        public object ExecuteQuery(string q, object[] p = null) => _db.ExecuteQuery(q, p);

        // TOC-based Line Loading
        public object GetLineIdsByTocEntry(int tocEntryId, string q) => _db.GetLineIdsByTocEntry(tocEntryId, q);
        public object GetLinesByIds(int bookId, object lineIds, string q) => _db.GetLinesByIds(bookId, lineIds, q);
        public object GetLineIndexFromLineId(int lineId, string q) => _db.GetLineIndexFromLineId(lineId, q);

        // PDF Operations - File Dialog & Virtual Host Mapping
        public object OpenPdfFilePicker() => _pdf.OpenPdfOrWordFilePickerAsync();
        public string CreateVirtualUrl(string path) => _pdf.CreateVirtualUrl(path);
        public string RecreateVirtualUrlFromPath(string path) => _pdf.RecreateVirtualUrlFromPath(path);
        public string LoadPdfFromPath(string path) => _pdf.CreateVirtualUrl(path); // Alias for CreateVirtualUrl
        public bool CheckPdfManagerReady() => _pdf.CheckPdfManagerReady();
        public bool IsPdfManagerReady() => _pdf.CheckPdfManagerReady(); // Alias for CheckPdfManagerReady
        public bool InitializePdfManager() => _pdf.InitializePdfManager();
        public void CleanupTempFile(string fileName) => _pdf.CleanupTempFile(fileName);
        public object GetTempFileStats() => _pdf.GetTempFileStats();
        public void ClearTempFiles() => _pdf.ClearTempFiles();

        // Hebrew Books Operations - Two distinct flows

        // Flow 1: Prepare book for viewing (cache if needed, no SaveAs dialog)
        public object PrepareHebrewBookForViewing(string bookId, string title) =>
            _hebrewBooks.PrepareForViewing(bookId, title).GetAwaiter().GetResult();

        // Flow 2: Download book with SaveAs dialog (user chooses location)
        public object PrepareHebrewBookForDownload(string bookId, string title) =>
            _hebrewBooks.PrepareForDownload(bookId, title).GetAwaiter().GetResult();

        // Check if Hebrew book file exists in cache
        public object CheckHebrewBookInCache(string bookId, string title) =>
            _hebrewBooks.CheckFileInCache(bookId, title);

        public object GetHebrewBooksCacheStats() => _hebrewBooks.GetCacheStats();
        public void ClearHebrewBooksCache() => _hebrewBooks.ClearCache();
        public void HandleHebrewBookTabClosed(string name) => _hebrewBooks.HandleTabClosed(name);

        // Bloom Search Operations
        public bool IsBloomSearchReady() => _bloomSearch.IsReady();
        public object GetBloomIndexingProgress() => _bloomSearch.GetIndexingProgress();

        // Start a new search and return search ID
        public string BloomSearchStart(string query)
        {
            return _bloomSearch.StartSearch(query);
        }

        // Cancel an ongoing search
        public void BloomSearchCancel(string searchId)
        {
            _bloomSearch.CancelSearch(searchId);
        }

        // Send search batch to Vue
        private void SendSearchBatch(string searchId, System.Collections.Generic.List<BloomSearchEngineLib.SearchResultItem> results)
        {
            try
            {
                var message = new
                {
                    type = "searchBatch",
                    searchId = searchId,
                    results = results.Select(r => new
                    {
                        lineId = r.LineId,
                        bookId = r.BookId,
                        bookTitle = r.BookTitle,
                        tocText = r.TocText,
                        score = r.Score,
                        proximityScore = r.ProximityScore,
                        snippet = r.Snippet
                    }).ToArray()
                };

                var json = System.Text.Json.JsonSerializer.Serialize(message);

                // Must invoke on UI thread
                if (_webView.InvokeRequired)
                {
                    _webView.Invoke(new Action(() =>
                    {
                        _webView.CoreWebView2.PostWebMessageAsString(json);
                    }));
                }
                else
                {
                    _webView.CoreWebView2.PostWebMessageAsString(json);
                }

                Console.WriteLine($"[ServiceProvider] Sent batch for {searchId}: {results.Count} results");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServiceProvider] Error sending search batch: {ex}");
            }
        }

        // Send search complete to Vue
        private void SendSearchComplete(string searchId)
        {
            try
            {
                var message = new
                {
                    type = "searchComplete",
                    searchId = searchId
                };

                var json = System.Text.Json.JsonSerializer.Serialize(message);

                // Must invoke on UI thread
                if (_webView.InvokeRequired)
                {
                    _webView.Invoke(new Action(() =>
                    {
                        _webView.CoreWebView2.PostWebMessageAsString(json);
                    }));
                }
                else
                {
                    _webView.CoreWebView2.PostWebMessageAsString(json);
                }

                Console.WriteLine($"[ServiceProvider] Sent complete for {searchId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServiceProvider] Error sending search complete: {ex}");
            }
        }

        // Send search cancelled to Vue
        private void SendSearchCancelled(string searchId)
        {
            try
            {
                var message = new
                {
                    type = "searchCancelled",
                    searchId = searchId
                };

                var json = System.Text.Json.JsonSerializer.Serialize(message);

                // Must invoke on UI thread
                if (_webView.InvokeRequired)
                {
                    _webView.Invoke(new Action(() =>
                    {
                        _webView.CoreWebView2.PostWebMessageAsString(json);
                    }));
                }
                else
                {
                    _webView.CoreWebView2.PostWebMessageAsString(json);
                }

                Console.WriteLine($"[ServiceProvider] Sent cancelled for {searchId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServiceProvider] Error sending search cancelled: {ex}");
            }
        }

        // Send search error to Vue
        private void SendSearchError(string searchId, string error)
        {
            try
            {
                var message = new
                {
                    type = "searchError",
                    searchId = searchId,
                    error = error
                };

                var json = System.Text.Json.JsonSerializer.Serialize(message);

                // Must invoke on UI thread
                if (_webView.InvokeRequired)
                {
                    _webView.Invoke(new Action(() =>
                    {
                        _webView.CoreWebView2.PostWebMessageAsString(json);
                    }));
                }
                else
                {
                    _webView.CoreWebView2.PostWebMessageAsString(json);
                }

                Console.WriteLine($"[ServiceProvider] Sent error for {searchId}: {error}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServiceProvider] Error sending search error: {ex}");
            }
        }

        // Popout functionality
        public void TogglePopOut() => _popOutAction?.Invoke();

        // Database Configuration Operations
        public async Task<object> OpenDatabaseFilePicker()
        {
            try
            {
                return await OpenDatabaseFilePickerAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServiceProvider] Database file picker failed: {ex}");
                return new { filePath = (string)null, fileName = (string)null };
            }
        }

        private async Task<object> OpenDatabaseFilePickerAsync()
        {
            var filePath = await WebViewDialogHelper.ShowOpenFileDialogAsync(
                _webView,
                "SQLite Database files (*.db)|*.db|All files (*.*)|*.*",
                "Select Database File"
            );

            if (string.IsNullOrEmpty(filePath))
            {
                return new { filePath = (string)null, fileName = (string)null };
            }

            var fileName = System.IO.Path.GetFileName(filePath);
            Console.WriteLine($"[ServiceProvider] Database file selected: {fileName} -> {filePath}");

            return new
            {
                filePath = filePath,
                fileName = fileName
            };
        }

        public bool SetDatabasePath(string path)
        {
            try
            {
                Console.WriteLine($"[ServiceProvider] Setting database path: {path}");

                // If path is empty or null, clear the database path (revert to default)
                if (string.IsNullOrWhiteSpace(path))
                {
                    Console.WriteLine("[ServiceProvider] Empty path provided, clearing database path");
                    return ClearDatabasePath();
                }

                // Use the validation method from DbQueries
                if (!DbQueries.ValidateDatabasePath(path))
                {
                    Console.WriteLine($"[ServiceProvider] Invalid database path: {path}");
                    return false;
                }

                // Set the database path using DbQueries method
                DbQueries.SetDatabasePath(path);
                Console.WriteLine($"[ServiceProvider] Database path set successfully: {path}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServiceProvider] Failed to set database path: {ex}");
                return false;
            }
        }

        public string GetCurrentDatabasePath()
        {
            try
            {
                return DbQueries.CurrentDbPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServiceProvider] Failed to get current database path: {ex}");
                return "";
            }
        }

        public bool ClearDatabasePath()
        {
            try
            {
                Console.WriteLine("[ServiceProvider] Clearing database path (reverting to default)");
                DbQueries.ClearDatabasePath();
                Console.WriteLine("[ServiceProvider] Database path cleared successfully");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServiceProvider] Failed to clear database path: {ex}");
                return false;
            }
        }

        public bool ValidateDatabasePath(string path)
        {
            try
            {
                Console.WriteLine($"[ServiceProvider] Validating database path: {path}");
                bool isValid = DbQueries.ValidateDatabasePath(path);
                Console.WriteLine($"[ServiceProvider] Database path validation result: {isValid}");
                return isValid;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServiceProvider] Failed to validate database path: {ex}");
                return false;
            }
        }

        public bool IsDatabaseAvailable()
        {
            try
            {
                bool isAvailable = DbQueries.IsDatabaseAvailable();
                Console.WriteLine($"[ServiceProvider] Database available: {isAvailable}");
                return isAvailable;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServiceProvider] Failed to check database availability: {ex}");
                return false;
            }
        }

        public void OpenUrlInBrowser(string url)
        {
            try
            {
                Console.WriteLine($"[ServiceProvider] Opening URL in browser: {url}");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServiceProvider] Failed to open URL in browser: {ex}");
            }
        }

        public void ReloadPage()
        {
            try
            {
                Console.WriteLine("[ServiceProvider] Reloading page");

                // Dispose and reinitialize database connection
                DbQueries.DisposeConnection();
                Console.WriteLine("[ServiceProvider] Database connection disposed");

                // Reinitialize BloomSearchService to trigger indexing if needed
                _bloomSearch.Initialize();
                Console.WriteLine("[ServiceProvider] BloomSearchService reinitialized");

                // Reload the WebView2 page
                if (_webView.InvokeRequired)
                {
                    _webView.Invoke(new Action(() =>
                    {
                        _webView.CoreWebView2.Reload();
                    }));
                }
                else
                {
                    _webView.CoreWebView2.Reload();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServiceProvider] Failed to reload page: {ex}");
            }
        }

        /// <summary>
        /// Extract book ID from Hebrew books URL for legacy compatibility
        /// </summary>
        private string ExtractBookIdFromUrl(string url)
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
                        return Uri.UnescapeDataString(keyValue[1]);
                    }
                }
                return "unknown";
            }
            catch
            {
                return "unknown";
            }
        }
    }
}