using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.SefroimDb;

/// <summary>
/// Read-only access to the user's seforim.db (the large Torah-library content DB).
///
/// The DB is NOT bundled — it's the user's file. In dev the path comes from the
/// DB_PATH env var, which the Vite plugin forwards when it spawns this service
/// (matching the dev-sqlite worker). Production path resolution (Zayit/Otzaria +
/// registry) arrives with the later C# migration.
///
/// This file holds the connection/path plumbing; the query methods live in
/// Sqlite.Queries.cs and the SQL strings in Sqlite.Strings.cs (SeforimSql).
/// </summary>
public sealed partial class SeforimDbService(ILogger<SeforimDbService> logger)
{
    private readonly string? _dbPath = SeforimDbLocator.Resolve();

    public bool HasDb => !string.IsNullOrWhiteSpace(_dbPath) && File.Exists(_dbPath);

    private SqliteConnection Open()
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ConnectionString;
        var conn = new SqliteConnection(cs);
        conn.Open();
        return conn;
    }

    /// <summary>True if <paramref name="table"/> has a column named <paramref name="column"/>.</summary>
    private static bool ColumnExists(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (string.Equals(r.GetString(1), column, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// Client-triggered warm-up — called by an app when IT loads (never at service boot;
    /// boot stays idle by design). Pays the one-time cold costs in the background so the
    /// user's first real query doesn't: loads the SQLite native library, opens the first
    /// pooled connection to the seforim DB, fills the static catalog cache, and JITs the
    /// hot read paths with a tiny book+lines query. Fire-and-forget; best-effort.
    /// </summary>
    public void Warmup()
    {
        if (!HasDb) return;
        _ = Task.Run(() =>
        {
            try
            {
                GetAllCategories();
                GetAllBooks();
                GetBookById(1);
                GetLinesPaged(1, 1, 0);
                logger.LogInformation("seforim DB warm-up complete (client-triggered)");
            }
            catch { /* best-effort — the first real query will warm instead */ }
        });
    }

    /// <summary>Runs a query body guarded by DB availability + error logging.</summary>
    private void Run(Action action, string op)
    {
        if (!HasDb)
        {
            logger.LogWarning("seforim DB not available (DB_PATH={Path})", _dbPath ?? "<unset>");
            return;
        }
        try { action(); }
        catch (Exception ex) { logger.LogError(ex, "{Op} failed", op); }
    }
}
