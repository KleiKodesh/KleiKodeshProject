using System.Text;
using KitveiHakodeshService.Ipc;
using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.HebrewBooks;

/// <summary>
/// HebrewBooks catalog search over the bundled SQLite catalog
/// (HebrewBooks/HebrewBooksCatalog.db, copied to output as content).
///
/// Reproduces the net4.8 KitveiHakodeshLib HebrewBooksDb.Search contract exactly:
/// per-word LIKE against lower(title|author|categories), ORDER BY title, LIMIT n,
/// nikud stripped, and hasLocalFile stamped when a local folder is given. Uses
/// Microsoft.Data.Sqlite (the service's only SQLite library).
/// </summary>
public sealed class HebrewBooksService(ILogger<HebrewBooksService> logger)
{
    private static readonly string DbPath =
        Path.Combine(AppContext.BaseDirectory, "HebrewBooks", "HebrewBooksCatalog.db");

    public const int DefaultLimit = 200;

    public HbSearchResult Search(string query, string? localFolder, int limit)
    {
        var result = new HbSearchResult();
        if (string.IsNullOrWhiteSpace(query)) return result;

        if (!File.Exists(DbPath))
        {
            logger.LogWarning("HebrewBooks catalog not found at {Path}", DbPath);
            return result;
        }

        string[] words = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return result;
        if (limit <= 0) limit = DefaultLimit;

        // Filter in SQLite: one AND'd LIKE per word against a concatenated, lowercased
        // search column so no non-matching rows are materialised.
        const string searchExpr =
            "lower(coalesce(title,'') || ' ' || coalesce(author,'') || ' ' || coalesce(categories,''))";

        var where = new StringBuilder();
        for (int i = 0; i < words.Length; i++)
        {
            if (i > 0) where.Append(" AND ");
            where.Append(searchExpr).Append(" LIKE @w").Append(i);
        }

        string sql =
            "SELECT id, title, author, placeOfPublication, year, pageCount, categories " +
            "FROM hebrewBooks WHERE " + where + " ORDER BY title LIMIT " + limit;

        bool checkLocal = !string.IsNullOrWhiteSpace(localFolder);

        try
        {
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = DbPath,
                Mode = SqliteOpenMode.ReadOnly,
            }.ConnectionString;

            using var conn = new SqliteConnection(cs);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            for (int i = 0; i < words.Length; i++)
                cmd.Parameters.AddWithValue("@w" + i, "%" + words[i] + "%");

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var book = new HebrewBook
                {
                    Id = reader.GetInt32(0),
                    Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Author = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    PrintingPlace = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    PrintingYear = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Pages = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    Categories = reader.IsDBNull(6) ? "" : reader.GetString(6),
                };

                if (checkLocal)
                {
                    try { book.HasLocalFile = File.Exists(Path.Combine(localFolder!, book.Id + ".pdf")); }
                    catch { /* disconnected drive / permission — leave false */ }
                }

                result.Books.Add(book);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "HebrewBooks search failed");
        }

        return result;
    }

    /// <summary>Lowercase + strip Hebrew nikud (U+05B0–U+05C2), matching the Vue/C# normalizer.</summary>
    private static string Normalize(string text)
    {
        text = text.ToLowerInvariant();
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c >= 'ְ' && c <= 'ׂ') continue; // strip nikud
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }
}
