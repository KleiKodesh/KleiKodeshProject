using Microsoft.Web.WebView2.WinForms;
using System;
using System.Threading.Tasks;
using Zayit.Viewer;

namespace Zayit.Services
{
    public class ServiceProvider
    {
        private readonly DbService _db;
        private readonly HebrewBooksService _hebrewBooks;
        private readonly PdfService _pdf;
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

        // PDF Operations - File Dialog & Virtual Host Mapping
        public object OpenPdfFilePicker() => _pdf.OpenPdfFilePicker();
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

        // Popout functionality
        public void TogglePopOut() => _popOutAction?.Invoke();

        // Database Configuration Operations
        public object OpenDatabaseFilePicker()
        {
            try
            {
                return OpenDatabaseFilePickerAsync().GetAwaiter().GetResult();
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