using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using CoreHebrewBooks = KitveiHakodesh.Core.HebrewBooks;

namespace KitveiHakodeshLib.HebrewBooks
{
    /// <summary>
    /// A catalog row in the shape the page expects.
    ///
    /// Kept as its own type rather than serializing Core's HebrewBook directly: the bridge
    /// serializes with no naming policy, so Core's PascalCase properties would reach the page
    /// as Id/Title/HasLocalFile and every field would read back undefined. The
    /// JsonPropertyName attributes below are that wire contract.
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

        [JsonPropertyName("hasLocalFile")]
        public bool HasLocalFile { get; set; }
    }

    /// <summary>
    /// The hosted app's HebrewBooks catalog search.
    ///
    /// The catalog, its SQL and the per-word LIKE search are KitveiHakodesh.Core's
    /// (<see cref="CoreHebrewBooks.HebrewBooksCatalogDbQueries"/>) — the same code the dev
    /// service runs, so a query cannot rank differently between the two apps. This project used
    /// to carry its own copy of that search, with its own Dapper row type and its own
    /// concatenated-column LIKE builder.
    ///
    /// What stays here is the singleton the WebView2 handlers resolve against, and projecting
    /// rows onto the wire shape above.
    /// </summary>
    public class HebrewBooksDb
    {
        private static readonly Lazy<HebrewBooksDb> _instance =
            new Lazy<HebrewBooksDb>(() => new HebrewBooksDb());

        public static HebrewBooksDb Instance => _instance.Value;

        /// <summary>Default result cap for a catalog search — Core's, so both apps agree.</summary>
        public const int DefaultSearchResultLimit =
            CoreHebrewBooks.HebrewBooksCatalogDbQueries.DefaultSearchLimit;

        private CoreHebrewBooks.HebrewBooksCatalogDbQueries _catalog;

        public bool IsInitialized => _catalog != null;

        /// <summary>
        /// Locates the bundled catalog. Idempotent, and non-fatal when the catalog is not
        /// installed: searches then return empty, which is a STATE rather than a failure.
        /// </summary>
        public void Initialize()
        {
            if (_catalog != null) return;

            string path = CoreHebrewBooks.HebrewBooksCatalogDbQueries.Locate();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                System.Diagnostics.Debug.WriteLine("[HebrewBooksDb] Catalog not found — search returns empty.");
                return;
            }

            try { _catalog = new CoreHebrewBooks.HebrewBooksCatalogDbQueries(path); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[HebrewBooksDb] Failed to open catalog: " + ex.Message);
            }
        }

        /// <summary>
        /// Catalog search: every query word must appear somewhere in title, author or
        /// categories. <paramref name="localFolder"/> stamps which results are already
        /// downloaded.
        /// </summary>
        public List<HebrewBookInfo> Search(
            string query, string localFolder = null, int limit = DefaultSearchResultLimit)
        {
            var results = new List<HebrewBookInfo>();
            if (_catalog == null) return results;

            foreach (var book in _catalog.Search(query, localFolder, limit))
            {
                results.Add(new HebrewBookInfo
                {
                    Id = book.Id,
                    Title = book.Title,
                    Author = book.Author,
                    PrintingPlace = book.PrintingPlace,
                    PrintingYear = book.PrintingYear,
                    Pages = book.Pages,
                    Categories = book.Categories,
                    HasLocalFile = book.HasLocalFile,
                });
            }
            return results;
        }

        /// <summary>Nothing is held open — Core opens a connection per search.</summary>
        public void Dispose() => _catalog = null;
    }
}
