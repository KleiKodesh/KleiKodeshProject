using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using KitveiHakodesh.Core.Common;

namespace KitveiHakodesh.Core.HebrewBooks
{
    /// <summary>
    /// Searches the bundled HebrewBooks catalog — a listing of what exists upstream on
    /// hebrewbooks.org, not of what is on this machine. Downloading a listed book is
    /// <see cref="HebrewBooksDownloader"/>'s job; these two are split because searching a
    /// local file and fetching over the network are not the same work.
    ///
    /// Schema: hebrewBooks(id, title, author, placeOfPublication, year, pageCount, categories)
    /// plus a one-row _metadata table the updater stamps. Opened read-only.
    ///
    /// The one statement is built per query from the caller's word count, so it stays inline
    /// rather than in a strings file (project rule 5).
    /// </summary>
    public sealed class HebrewBooksCatalogDbQueries
    {
        public const int DefaultSearchLimit = 200;

        private const string SelectColumns =
            "SELECT id, title, author, placeOfPublication, year, pageCount, categories FROM hebrewBooks";

        /// <summary>
        /// Title, author and categories concatenated and lowercased, so one LIKE per word
        /// filters inside SQLite and non-matching rows are never materialised. Lowercasing
        /// matters for the Latin part only — SQLite's LIKE is case-insensitive for ASCII and
        /// leaves Hebrew alone, which has no case to fold.
        /// </summary>
        private const string SearchExpression =
            "lower(coalesce(title,'') || ' ' || coalesce(author,'') || ' ' || coalesce(categories,''))";

        private const string MaxIdSql = "SELECT COALESCE(MAX(id), 0) FROM hebrewBooks";

        private const string ReadMetadataSql = "SELECT value FROM _metadata WHERE key = @key";

        private readonly string _databasePath;

        public HebrewBooksCatalogDbQueries(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("databasePath is required", nameof(databasePath));

            _databasePath = databasePath;
        }

        /// <summary>
        /// Finds the bundled catalog wherever this host keeps it, or null if it is not
        /// installed. Probes both layouts in use — Core's own Resources folder and the
        /// service's HebrewBooks folder.
        /// </summary>
        public static string? Locate() =>
            AppFileLocator.FindFile(Path.Combine("Resources", "HebrewBooksCatalog.db"))
            ?? AppFileLocator.FindFile(Path.Combine("HebrewBooks", "HebrewBooksCatalog.db"));

        public string DatabasePath => _databasePath;

        /// <summary>False when no catalog is installed. <see cref="Search"/> returns empty
        /// rather than throwing in that case; the catalog is optional data, not a broken app.</summary>
        public bool IsAvailable => File.Exists(_databasePath);

        /// <summary>
        /// Books whose title, author or categories contain EVERY word of the query.
        /// Sorted by title, capped at <paramref name="limit"/>.
        ///
        /// When <paramref name="localFolder"/> is given, each result is stamped with whether
        /// {id}.pdf is already sitting there — one <c>File.Exists</c> per row, which is why
        /// the limit exists.
        /// </summary>
        public List<HebrewBook> Search(string query, string? localFolder = null, int limit = DefaultSearchLimit)
        {
            var books = new List<HebrewBook>();
            if (!IsAvailable || string.IsNullOrWhiteSpace(query)) return books;

            string[] words = NormalizeForSearch(query)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return books;

            if (limit <= 0) limit = DefaultSearchLimit;

            var where = new StringBuilder();
            for (int i = 0; i < words.Length; i++)
            {
                if (i > 0) where.Append(" AND ");
                where.Append(SearchExpression).Append(" LIKE @w").Append(i);
            }

            // limit is an int the caller cannot make into SQL, so it is concatenated;
            // every value that came from text is a parameter.
            string sql = SelectColumns + " WHERE " + where + " ORDER BY title LIMIT " + limit;

            bool stampLocalFiles = !string.IsNullOrWhiteSpace(localFolder);

            using var connection = SqliteConnectionFactory.OpenCorpusRead(_databasePath);
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            for (int i = 0; i < words.Length; i++)
                command.Parameters.AddWithValue("@w" + i, "%" + words[i] + "%");

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var book = new HebrewBook
                {
                    Id = reader.GetInt32(0),
                    Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Author = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    PrintingPlace = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    PrintingYear = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Pages = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
                    Categories = reader.IsDBNull(6) ? "" : reader.GetString(6),
                };

                if (stampLocalFiles)
                {
                    try { book.HasLocalFile = File.Exists(Path.Combine(localFolder!, book.Id + ".pdf")); }
                    catch (Exception) { /* disconnected drive or permission — leave it false */ }
                }

                books.Add(book);
            }

            return books;
        }

        /// <summary>
        /// Lowercases and strips Hebrew vowel points (U+05B0-U+05C2) so a pointed query still
        /// matches the catalog's unpointed titles.
        ///
        /// DELIBERATELY NOT SHARED. The book viewer normalizes a wider range
        /// (U+0591-U+05C7, cantillation included) for a different job; folding the two into one
        /// helper would silently change what this search matches. Two normalizers, two purposes.
        /// </summary>
        private static string NormalizeForSearch(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            text = text.ToLowerInvariant();

            var normalized = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c >= 'ְ' && c <= 'ׂ') continue;
                normalized.Append(c);
            }

            return normalized.ToString().Trim();
        }

        /// <summary>The highest book id in the catalog, or 0 when it is empty. The updater
        /// resumes its walk from here.</summary>
        public int MaxBookId()
        {
            if (!IsAvailable) return 0;

            using var connection = SqliteConnectionFactory.OpenCorpusRead(_databasePath);
            using var command = connection.CreateCommand();
            command.CommandText = MaxIdSql;
            object? value = command.ExecuteScalar();
            return value == null || value is DBNull ? 0 : Convert.ToInt32(value);
        }

        /// <summary>Reads a value from the catalog's _metadata table, or null if absent.</summary>
        public string? ReadMetadata(string key)
        {
            if (!IsAvailable) return null;

            using var connection = SqliteConnectionFactory.OpenCorpusRead(_databasePath);
            using var command = connection.CreateCommand();
            command.CommandText = ReadMetadataSql;
            command.Parameters.AddWithValue("@key", key);
            object? value = command.ExecuteScalar();
            return value == null || value is DBNull ? null : Convert.ToString(value);
        }
    }
}
