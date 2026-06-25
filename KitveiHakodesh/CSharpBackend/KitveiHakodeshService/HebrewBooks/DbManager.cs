using Dapper;
using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.HebrewBooks;

/// <summary>
/// Opens and queries HebrewBooks.db — the local HebrewBooks catalogue.
/// The database is bundled with the service (CopyToOutputDirectory = PreserveNewest).
/// Single read-only connection is sufficient for catalogue search (low concurrency).
/// </summary>
public sealed class HebrewBooksDbManager : IDisposable
{
    private readonly SqliteConnection _connection;
    private bool _disposed = false;

    public HebrewBooksDbManager()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "HebrewBooks", "HebrewBooks.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();

        _connection = new SqliteConnection(connectionString);
        _connection.Open();
    }

    /// <summary>
    /// Searches the HebrewBooks catalogue by title or author keyword.
    /// Returns rows as dictionaries so the frontend can access them by column name.
    /// </summary>
    public IEnumerable<IDictionary<string, object?>> Search(string query)
    {
        // The exact SQL depends on the HebrewBooks.db schema — this will be updated
        // once the schema is confirmed. The pattern mirrors hbSearch in KitveiHakodeshLib.
        const string Sql = @"
            SELECT id, title, author, pages, year
            FROM books
            WHERE title LIKE @query OR author LIKE @query
            ORDER BY title
            LIMIT 200";

        return _connection.Query(Sql, new { query = "%" + query + "%" })
                          .Cast<IDictionary<string, object?>>()
                          .ToList();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _connection.Close(); _connection.Dispose(); } catch { /* best effort */ }
    }
}
