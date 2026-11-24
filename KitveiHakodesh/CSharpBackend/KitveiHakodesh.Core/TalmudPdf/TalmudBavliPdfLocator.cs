using System;
using System.Collections.Generic;
using System.IO;
using KitveiHakodesh.Core.Common;
using KitveiHakodesh.Core.Settings;

namespace KitveiHakodesh.Core.TalmudPdf
{
    /// <summary>
    /// Otzaria's per-tractate Talmud Bavli PDFs: a folder named after the Talmud Bavli category,
    /// beside seforim.db (fallback: the same folder one level up, the library root), holding
    /// "tractate title.pdf". A folder whose ".version" reads "installing" is skipped.
    /// </summary>
    public static class TalmudBavliPdfLocator
    {
        /// <summary>The category and folder name (Hebrew "Talmud Bavli").</summary>
        private const string TalmudBavliName = "תלמוד בבלי";

        /// <summary>Second-level category prefix (Hebrew "Seder ").</summary>
        private const string SederPrefix = "סדר ";

        /// <summary>Full path of the tractate's PDF, or null when the book is not a tractate
        /// (e.g. a commentary) or no PDF is on disk.</summary>
        public static string? TalmudPdfPathOf(int bookId)
        {
            string? seforimDbPath = SeforimDbPathResolver.Resolve().Path;
            if (string.IsNullOrWhiteSpace(seforimDbPath) || !File.Exists(seforimDbPath)) return null;

            string? title = null;
            var categories = new List<string>();   // root first
            using (var connection = SqliteConnectionFactory.OpenCorpusRead(seforimDbPath!))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    WITH RECURSIVE chain(id, parentId, title, depth) AS (
                        SELECT c.id, c.parentId, c.title, 0 FROM category c
                        JOIN book b ON b.categoryId = c.id WHERE b.id = @b
                        UNION ALL
                        SELECT c.id, c.parentId, c.title, chain.depth + 1 FROM category c
                        JOIN chain ON c.id = chain.parentId WHERE chain.depth < 50
                    )
                    SELECT (SELECT title FROM book WHERE id = @b), title FROM chain ORDER BY depth DESC";
                command.Parameters.AddWithValue("@b", bookId);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (title == null && !reader.IsDBNull(0)) title = reader.GetString(0);
                    categories.Add(reader.IsDBNull(1) ? "" : reader.GetString(1).Trim());
                }
            }

            if (string.IsNullOrWhiteSpace(title) || categories.Count == 0) return null;
            if (!categories[0].StartsWith(TalmudBavliName, StringComparison.Ordinal)) return null;
            if (categories.Count > 1 && !categories[1].StartsWith(SederPrefix, StringComparison.Ordinal)) return null;

            string fileName = title!.Trim() + ".pdf";
            if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;

            foreach (string folder in CandidateFolders(seforimDbPath!))
            {
                try
                {
                    if (!Directory.Exists(folder) || IsInstalling(folder)) continue;
                    string path = Path.Combine(folder, fileName);
                    if (File.Exists(path)) return path;
                }
                catch (Exception) { /* unreadable folder - try the next one */ }
            }
            return null;
        }

        private static IEnumerable<string> CandidateFolders(string seforimDbPath)
        {
            string? dbFolder = Path.GetDirectoryName(seforimDbPath);
            if (dbFolder == null) yield break;
            yield return Path.Combine(dbFolder, TalmudBavliName);
            string? libraryRoot = Path.GetDirectoryName(dbFolder);
            if (!string.IsNullOrEmpty(libraryRoot)) yield return Path.Combine(libraryRoot, TalmudBavliName);
        }

        private static bool IsInstalling(string folder)
        {
            string versionFile = Path.Combine(folder, ".version");
            return File.Exists(versionFile)
                && string.Equals(File.ReadAllText(versionFile).Trim(), "installing", StringComparison.OrdinalIgnoreCase);
        }
    }
}
