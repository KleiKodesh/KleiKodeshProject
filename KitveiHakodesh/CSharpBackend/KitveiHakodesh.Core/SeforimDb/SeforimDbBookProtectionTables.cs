using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Data.Sqlite;

namespace KitveiHakodesh.Core.SeforimDb
{
    public sealed partial class SeforimDbQueries
    {
        // Otzaria schema 7+: book_protection(bookId, level) and banner(id, text) +
        // book_banner(bookId, bannerId). Older DBs (schema 6, Zayit) have none of them -
        // then every book is unprotected and has no banner. Protected/credited books are
        // few, so each table is read whole, once, with the catalog (and dropped with it when
        // the file changes - ForgetShapeIfFileChanged clears the catalog cache). Each read first
        // checks its table AND the columns it uses, so a DB whose tables have another shape is
        // treated like one without them.
        private const string SelectBookProtectionLevels = "SELECT bookId, level FROM book_protection";
        private const string SelectBookBanners =
            "SELECT bb.bookId, b.title, n.text FROM book_banner bb" +
            " JOIN banner n ON n.id = bb.bannerId JOIN book b ON b.id = bb.bookId";

        /// <summary>Fills <see cref="BookRow.Protection"/> and <see cref="BookRow.PublisherBanner"/>
        /// on catalog rows. Leaves both null on a DB without the tables.</summary>
        private static void AttachBookProtection(SqliteConnection conn, List<BookRow> books)
        {
            var levels = new Dictionary<int, int>();
            if (HasColumns(conn, "book_protection", "bookId", "level"))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = SelectBookProtectionLevels;
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    if (!r.IsDBNull(0) && !r.IsDBNull(1)) levels[r.GetInt32(0)] = r.GetInt32(1);
            }

            var banners = new Dictionary<int, string>();
            if (HasColumns(conn, "book_banner", "bookId", "bannerId") && HasColumns(conn, "banner", "id", "text"))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = SelectBookBanners;
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    if (!r.IsDBNull(0) && !r.IsDBNull(1) && !r.IsDBNull(2))
                        banners[r.GetInt32(0)] = ExpandBannerTitle(r.GetString(2), r.GetString(1));
            }

            if (levels.Count == 0 && banners.Count == 0) return;
            foreach (var book in books)
            {
                if (levels.TryGetValue(book.Id, out int level) && level > 0)
                    book.Protection = BookProtection.ForLevel(level);
                if (banners.TryGetValue(book.Id, out string? banner) && banner.Trim().Length > 0)
                    book.PublisherBanner = banner;
            }
        }

        private static bool HasColumns(SqliteConnection conn, string table, params string[] columns)
        {
            if (!TableExists(conn, table)) return false;
            foreach (string column in columns)
                if (!SeforimDbSchema.ColumnExists(conn, table, column)) return false;
            return true;
        }

        /// <summary>
        /// Otzaria's expandBannerTitle (lib/book_protection/utils/banner_title.dart): <c>{title}</c>
        /// becomes the book title - percent-encoded inside a link target <c>[label](target)</c>,
        /// as-is everywhere else.
        /// </summary>
        internal static string ExpandBannerTitle(string text, string title)
        {
            const string Placeholder = "{title}";
            if (!text.Contains(Placeholder, StringComparison.Ordinal)) return text;
            string encoded = PercentEncodeTitle(title);
            string linked = System.Text.RegularExpressions.Regex.Replace(
                text, @"\[([^\]\n]*)\]\(([^)\n]*)\)",
                m => "[" + m.Groups[1].Value + "](" + m.Groups[2].Value.Replace(Placeholder, encoded, StringComparison.Ordinal) + ")");
            return linked.Replace(Placeholder, title, StringComparison.Ordinal);
        }

        // Java's URLEncoder.encode(title, UTF_8) with space as %20 - what Otzaria matches.
        private static string PercentEncodeTitle(string title)
        {
            var sb = new StringBuilder();
            foreach (byte b in Encoding.UTF8.GetBytes(title))
            {
                bool kept = (b >= 'a' && b <= 'z') || (b >= 'A' && b <= 'Z') || (b >= '0' && b <= '9')
                    || b == '.' || b == '-' || b == '*' || b == '_';
                if (kept) sb.Append((char)b);
                else sb.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
