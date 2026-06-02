using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using Dapper;

namespace KitveiHakodeshLib.HebrewBooks
{
    /// <summary>
    /// Hebrew book database model for JSON serialization.
    /// </summary>
    public class HebrewBookInfo
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("author")]
        public string Author { get; set; }

        [JsonPropertyName("printingPlace")]
        public string PrintingPlace { get; set; }

        [JsonPropertyName("printingYear")]
        public string PrintingYear { get; set; }

        [JsonPropertyName("pages")]
        public int? Pages { get; set; }

        [JsonPropertyName("categories")]
        public string Categories { get; set; }
    }

    /// <summary>
    /// Provides search and retrieval operations on the Hebrew Books SQLite database.
    /// Maintains a single shared connection for the lifetime of the app.
    /// </summary>
    public class HebrewBooksDb
    {
        private static readonly Lazy<HebrewBooksDb> _instance = new Lazy<HebrewBooksDb>(() => new HebrewBooksDb());
        public static HebrewBooksDb Instance => _instance.Value;

        private readonly string _dbPath;
        private SQLiteConnection _connection;
        private bool _initialized = false;

        private HebrewBooksDb()
        {
            // Database lives in the bin folder alongside the Vue dist
            string binPath = AppDomain.CurrentDomain.BaseDirectory;
            _dbPath = Path.Combine(binPath, "KitveiHakodesh", "HebrewBooks.db");
        }

        public void Initialize()
        {
            if (_initialized) return;

            lock (this)
            {
                if (_initialized) return;

                if (!File.Exists(_dbPath))
                {
                    Log($"Hebrew Books database not found at {_dbPath}");
                    return;
                }

                try
                {
                    var connectionString = new SQLiteConnectionStringBuilder
                    {
                        DataSource = _dbPath,
                        Version = 3,
                        DefaultTimeout = 5,
                        ReadOnly = true,
                    }.ConnectionString;

                    _connection = new SQLiteConnection(connectionString);
                    _connection.Open();
                    Log($"Opened Hebrew Books database: {_dbPath}");
                    _initialized = true;
                }
                catch (Exception ex)
                {
                    Log($"Failed to initialize Hebrew Books database: {ex.Message}");
                }
            }
        }

        public bool IsInitialized => _initialized && _connection != null;

        /// <summary>
        /// Search the Hebrew Books database by query term.
        /// Matches query words against title, author, and categories using normalized text.
        /// Returns results sorted by title.
        /// </summary>
        public List<HebrewBookInfo> Search(string query)
        {
            if (!IsInitialized) return new List<HebrewBookInfo>();

            if (string.IsNullOrWhiteSpace(query)) return new List<HebrewBookInfo>();

            // Parse query into words
            var words = NormalizeText(query)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 0)
                .ToList();

            if (words.Count == 0) return new List<HebrewBookInfo>();

            try
            {
                // Build WHERE clause that matches all words
                // Each word must be found in (normalized title + author + categories)
                var results = new List<HebrewBookInfo>();

                lock (_connection)
                {
                    var sql = @"
                        SELECT 
                            id, 
                            title, 
                            author, 
                            placeOfPublication, 
                            printingYear, 
                            pageCount, 
                            categories
                        FROM hebrewBooks
                        ORDER BY title
                    ";

                    var books = _connection.Query<dynamic>(sql).ToList();

                    foreach (var book in books)
                    {
                        string id = book.id?.ToString() ?? "";
                        string title = book.title ?? "";
                        string author = book.author ?? "";
                        string categories = book.categories ?? "";

                        // Normalize search text
                        string searchText = $"{NormalizeText(title)} {NormalizeText(author)} {NormalizeText(categories)}";
                        var searchWords = searchText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                        // Check if all query words are found in search text
                        if (words.All(qw => searchWords.Any(sw => sw == qw || sw.Contains(qw))))
                        {
                            results.Add(new HebrewBookInfo
                            {
                                Id = int.TryParse(id, out int bookId) ? bookId : 0,
                                Title = title,
                                Author = author,
                                PrintingPlace = book.placeOfPublication ?? "",
                                PrintingYear = book.printingYear ?? "",
                                Pages = book.pageCount != null ? Convert.ToInt32(book.pageCount) : (int?)null,
                                Categories = categories,
                            });
                        }
                    }
                }

                return results;
            }
            catch (Exception ex)
            {
                Log($"Error searching Hebrew Books: {ex.Message}");
                return new List<HebrewBookInfo>();
            }
        }

        /// <summary>
        /// Get all books in the database (used for initial full catalog load).
        /// Returns all books sorted by title.
        /// </summary>
        public List<HebrewBookInfo> GetAllBooks()
        {
            if (!IsInitialized) return new List<HebrewBookInfo>();

            try
            {
                var results = new List<HebrewBookInfo>();

                lock (_connection)
                {
                    var sql = @"
                        SELECT 
                            id, 
                            title, 
                            author, 
                            placeOfPublication, 
                            printingYear, 
                            pageCount, 
                            categories
                        FROM hebrewBooks
                        ORDER BY title
                    ";

                    var books = _connection.Query<dynamic>(sql).ToList();

                    foreach (var book in books)
                    {
                        string id = book.id?.ToString() ?? "";
                        results.Add(new HebrewBookInfo
                        {
                            Id = int.TryParse(id, out int bookId) ? bookId : 0,
                            Title = book.title ?? "",
                            Author = book.author ?? "",
                            PrintingPlace = book.placeOfPublication ?? "",
                            PrintingYear = book.printingYear ?? "",
                            Pages = book.pageCount != null ? Convert.ToInt32(book.pageCount) : (int?)null,
                            Categories = book.categories ?? "",
                        });
                    }
                }

                return results;
            }
            catch (Exception ex)
            {
                Log($"Error fetching all Hebrew books: {ex.Message}");
                return new List<HebrewBookInfo>();
            }
        }

        /// <summary>
        /// Get a single book by ID.
        /// </summary>
        public HebrewBookInfo GetBookById(int bookId)
        {
            if (!IsInitialized) return null;

            try
            {
                lock (_connection)
                {
                    var sql = @"
                        SELECT 
                            id, 
                            title, 
                            author, 
                            placeOfPublication, 
                            printingYear, 
                            pageCount, 
                            categories
                        FROM hebrewBooks
                        WHERE id = @id
                    ";

                    var book = _connection.QueryFirstOrDefault<dynamic>(sql, new { id = bookId });

                    if (book == null) return null;

                    return new HebrewBookInfo
                    {
                        Id = bookId,
                        Title = book.title ?? "",
                        Author = book.author ?? "",
                        PrintingPlace = book.placeOfPublication ?? "",
                        PrintingYear = book.printingYear ?? "",
                        Pages = book.pageCount != null ? Convert.ToInt32(book.pageCount) : (int?)null,
                        Categories = book.categories ?? "",
                    };
                }
            }
            catch (Exception ex)
            {
                Log($"Error fetching book {bookId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get the total count of books in the database.
        /// </summary>
        public int GetBookCount()
        {
            if (!IsInitialized) return 0;

            try
            {
                lock (_connection)
                {
                    var sql = "SELECT COUNT(*) FROM hebrewBooks";
                    return _connection.QueryFirstOrDefault<int>(sql);
                }
            }
            catch (Exception ex)
            {
                Log($"Error getting book count: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Normalize Hebrew text for search matching.
        /// Strips diacritics and converts to lowercase for comparison.
        /// </summary>
        private static string NormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            // Lowercase
            text = text.ToLowerInvariant();

            // Simple normalization: strip common Hebrew diacritics
            // Removing vowels (Nikudot) for matching variants
            char[] toRemove = new[] { '\u05B0', '\u05B1', '\u05B2', '\u05B3', '\u05B4', '\u05B5', '\u05B6', '\u05B7', '\u05B8', '\u05B9', '\u05BC', '\u05BD', '\u05BF', '\u05C1', '\u05C2' };
            foreach (char c in toRemove)
            {
                text = text.Replace(c.ToString(), "");
            }

            return text.Trim();
        }

        private static void Log(string msg) => System.Diagnostics.Debug.WriteLine("[HebrewBooksDb] " + msg);

        public void Dispose()
        {
            if (_connection != null)
            {
                _connection.Dispose();
                _connection = null;
                _initialized = false;
            }
        }
    }
}
