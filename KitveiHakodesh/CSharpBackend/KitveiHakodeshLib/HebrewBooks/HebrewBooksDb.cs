using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using Dapper;

namespace KitveiHakodeshLib.HebrewBooks
{
    /// <summary>
    /// Hebrew book database model for JSON serialization back to the Vue frontend.
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
    /// Internal Dapper row type — matches the hebrewBooks table columns exactly.
    /// Named to match the SQLite column names so Dapper maps them without dynamic.
    /// </summary>
    internal class HbRow
    {
        public int id { get; set; }
        public string title { get; set; }
        public string author { get; set; }
        public string placeOfPublication { get; set; }
        public string year { get; set; }
        public int? pageCount { get; set; }
        public string categories { get; set; }

        public HebrewBookInfo ToInfo()
        {
            return new HebrewBookInfo
            {
                Id = id,
                Title = title ?? "",
                Author = author ?? "",
                PrintingPlace = placeOfPublication ?? "",
                PrintingYear = year ?? "",
                Pages = pageCount,
                Categories = categories ?? "",
            };
        }
    }

    /// <summary>
    /// Provides search and retrieval operations on the Hebrew Books SQLite database.
    /// The database is deployed as a content file (Resources/HebrewBooks.db) and opened read-only.
    /// A single shared connection is maintained for the lifetime of the app.
    /// </summary>
    public class HebrewBooksDb
    {
        private static readonly Lazy<HebrewBooksDb> _instance =
            new Lazy<HebrewBooksDb>(() => new HebrewBooksDb());

        public static HebrewBooksDb Instance => _instance.Value;

        private readonly string _dbPath;
        private SQLiteConnection _connection;
        private bool _initialized = false;

        private HebrewBooksDb()
        {
            // BaseDirectory ends with a backslash, so use it directly.
            _dbPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Resources",
                "HebrewBooks.db");
            Log("DB path resolved to: " + _dbPath);
        }

        public void Initialize()
        {
            if (_initialized) return;

            lock (this)
            {
                if (_initialized) return;

                Log("Initializing — looking for DB at: " + _dbPath);
                Log("File exists: " + File.Exists(_dbPath));

                if (!File.Exists(_dbPath))
                {
                    // List what IS in the Resources folder to help diagnose path problems
                    string resourcesDir = Path.GetDirectoryName(_dbPath);
                    if (Directory.Exists(resourcesDir))
                    {
                        Log("Resources folder exists. Contents: " + string.Join(", ", Directory.GetFiles(resourcesDir)));
                    }
                    else
                    {
                        Log("Resources folder does not exist: " + resourcesDir);
                        // Show parent folder contents
                        string parent = Path.GetDirectoryName(resourcesDir);
                        if (Directory.Exists(parent))
                            Log("Parent folder contents: " + string.Join(", ", Directory.GetFiles(parent)));
                    }
                    return;
                }

                try
                {
                    string cs = new SQLiteConnectionStringBuilder
                    {
                        DataSource = _dbPath,
                        Version = 3,
                        DefaultTimeout = 5,
                        ReadOnly = true,
                    }.ConnectionString;

                    _connection = new SQLiteConnection(cs);
                    _connection.Open();
                    Log("Opened Hebrew Books database: " + _dbPath);

                    // Verify the table exists and log the row count
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM hebrewBooks";
                        var count = cmd.ExecuteScalar();
                        Log("hebrewBooks row count: " + count);
                    }

                    _initialized = true;
                }
                catch (Exception ex)
                {
                    Log("Failed to initialize Hebrew Books database: " + ex.Message);
                }
            }
        }

        public bool IsInitialized
        {
            get { return _initialized && _connection != null; }
        }

        private const string SelectColumns = @"
            SELECT id, title, author, placeOfPublication, year, pageCount, categories
            FROM hebrewBooks";

        /// <summary>
        /// Search for books whose title, author, or categories contain all of the query words.
        /// Matching mirrors the in-memory logic used by the Vue CSV path:
        ///   - text is normalised (diacritics stripped, lowercased)
        ///   - every query word must appear as a whole-word match OR as a substring of a word
        /// Returns results sorted by title.
        /// </summary>
        public List<HebrewBookInfo> Search(string query)
        {
            Log("Search called. IsInitialized=" + IsInitialized + " query='" + query + "'");
            if (!IsInitialized) return new List<HebrewBookInfo>();
            if (string.IsNullOrWhiteSpace(query)) return new List<HebrewBookInfo>();

            string[] words = NormalizeText(query)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            Log("Normalized words: [" + string.Join(", ", words) + "]");
            if (words.Length == 0) return new List<HebrewBookInfo>();

            try
            {
                List<HbRow> rows;
                lock (_connection)
                {
                    rows = _connection.Query<HbRow>(SelectColumns + " ORDER BY title").ToList();
                }

                Log("Total rows fetched for search: " + rows.Count);

                var results = new List<HebrewBookInfo>();
                foreach (HbRow row in rows)
                {
                    string searchText = NormalizeText(row.title) + " "
                                      + NormalizeText(row.author) + " "
                                      + NormalizeText(row.categories);

                    string[] searchWords = searchText
                        .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    bool allMatch = true;
                    foreach (string qw in words)
                    {
                        bool found = false;
                        foreach (string sw in searchWords)
                        {
                            if (sw == qw || sw.Contains(qw)) { found = true; break; }
                        }
                        if (!found) { allMatch = false; break; }
                    }

                    if (allMatch)
                        results.Add(row.ToInfo());
                }

                Log("Search results: " + results.Count);
                return results;
            }
            catch (Exception ex)
            {
                Log("Error searching Hebrew Books: " + ex.Message);
                return new List<HebrewBookInfo>();
            }
        }

        /// <summary>
        /// Return every book in the database, sorted by title.
        /// Used for the initial catalog load in the hosted app.
        /// </summary>
        public List<HebrewBookInfo> GetAllBooks()
        {
            Log("GetAllBooks called. IsInitialized=" + IsInitialized);
            if (!IsInitialized) return new List<HebrewBookInfo>();

            try
            {
                List<HbRow> rows;
                lock (_connection)
                {
                    rows = _connection.Query<HbRow>(SelectColumns + " ORDER BY title").ToList();
                }

                Log("GetAllBooks returned " + rows.Count + " rows");

                var results = new List<HebrewBookInfo>(rows.Count);
                foreach (HbRow row in rows)
                    results.Add(row.ToInfo());

                return results;
            }
            catch (Exception ex)
            {
                Log("Error fetching all Hebrew books: " + ex.Message);
                return new List<HebrewBookInfo>();
            }
        }

        /// <summary>
        /// Normalise Hebrew text for search comparison.
        /// Strips nikudot (vowel points) and lowercases — mirrors the Vue normalizeText util.
        /// </summary>
        private static string NormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            text = text.ToLowerInvariant();

            // Strip Hebrew nikudot (U+05B0–U+05C2 range used for vowel points and cantillation)
            var sb = new System.Text.StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c >= '\u05B0' && c <= '\u05C2') continue;
                sb.Append(c);
            }

            return sb.ToString().Trim();
        }

        private static void Log(string msg) =>
            System.Diagnostics.Debug.WriteLine("[HebrewBooksDb] " + msg);

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
