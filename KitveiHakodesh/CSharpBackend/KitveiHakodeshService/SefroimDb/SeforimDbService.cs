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
