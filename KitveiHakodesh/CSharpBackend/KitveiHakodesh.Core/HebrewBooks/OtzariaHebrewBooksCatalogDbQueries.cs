using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using KitveiHakodesh.Core.Common;

namespace KitveiHakodesh.Core.HebrewBooks
{
    /// <summary>
    /// Reads Otzaria's own map of which HebrewBooks scans are editions of which Otzaria book:
    /// otzar-HB_catalog.db, shipped by Otzaria beside its seforim.db.
    ///
    /// Schema (Otzaria's, read-only to us):
    ///   otzaria_hebrew_books(hb_id, otzaria_id, otzaria_title, is_best, confidence)
    ///     — otzaria_id is seforim.db book.id; confidence is 'certain' or 'likely';
    ///       exactly one row per book has is_best = 1. Indexed on otzaria_id.
    ///   hebrew_books(id_book, title, author, printing_place, printing_year, pub_date, pages, tags)
    ///
    /// The ids are Otzaria's seforim.db ids. Zayit's seforim.db numbers its books differently,
    /// so <see cref="LocateBeside"/> answers only for an Otzaria-shaped database.
    /// </summary>
    public sealed class OtzariaHebrewBooksCatalogDbQueries
    {
        public const string FileName = "otzar-HB_catalog.db";

        // Best edition first, then certain matches before likely ones; title keeps the rest stable.
        private const string EditionsOfBookSql =
            "SELECT o.hb_id, h.title, h.author, h.printing_place, h.printing_year, h.pages, o.is_best, o.confidence " +
            "FROM otzaria_hebrew_books o JOIN hebrew_books h ON h.id_book = o.hb_id " +
            "WHERE o.otzaria_id = @bookId " +
            "ORDER BY o.is_best DESC, CASE o.confidence WHEN 'certain' THEN 0 ELSE 1 END, h.title";

        // Zayit's schema has no version_line table; Otzaria's does (steering: detect by structure).
        private const string IsOtzariaShapedSql =
            "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'version_line'";

        private readonly string _databasePath;

        private OtzariaHebrewBooksCatalogDbQueries(string databasePath) => _databasePath = databasePath;

        public string DatabasePath => _databasePath;

        /// <summary>
        /// The map that belongs to <paramref name="seforimDbPath"/>, or null when there is none to
        /// use: no otzar-HB_catalog.db beside it, or the database is Zayit's, whose book ids the
        /// map does not describe. Opens seforim.db once to check its shape — callers keep the
        /// answer per path rather than asking again.
        /// </summary>
        public static OtzariaHebrewBooksCatalogDbQueries? LocateBeside(string? seforimDbPath)
        {
            if (string.IsNullOrWhiteSpace(seforimDbPath) || !File.Exists(seforimDbPath)) return null;

            string? folder = Path.GetDirectoryName(seforimDbPath);
            if (string.IsNullOrEmpty(folder)) return null;

            string catalogPath = Path.Combine(folder, FileName);
            if (!File.Exists(catalogPath)) return null;

            using (var seforim = SqliteConnectionFactory.OpenCorpusProbe(seforimDbPath))
            using (var command = seforim.CreateCommand())
            {
                command.CommandText = IsOtzariaShapedSql;
                if (command.ExecuteScalar() == null) return null;
            }

            return new OtzariaHebrewBooksCatalogDbQueries(catalogPath);
        }

        /// <summary>The HebrewBooks scans of one Otzaria book, best first. Empty when it has none.</summary>
        public List<HebrewBooksEdition> EditionsOf(int otzariaBookId)
        {
            var editions = new List<HebrewBooksEdition>();

            using var connection = SqliteConnectionFactory.OpenCorpusRead(_databasePath);
            using var command = connection.CreateCommand();
            command.CommandText = EditionsOfBookSql;
            command.Parameters.AddWithValue("@bookId", otzariaBookId);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                editions.Add(new HebrewBooksEdition
                {
                    Id = reader.GetInt32(0),
                    Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Author = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    PrintingPlace = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    PrintingYear = reader.IsDBNull(4) ? "" : Convert.ToString(reader.GetValue(4), System.Globalization.CultureInfo.InvariantCulture) ?? "",
                    Pages = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
                    IsBest = !reader.IsDBNull(6) && reader.GetInt64(6) != 0,
                    IsCertain = !reader.IsDBNull(7) && string.Equals(reader.GetString(7), "certain", StringComparison.Ordinal),
                });
            }
            return editions;
        }
    }
}
