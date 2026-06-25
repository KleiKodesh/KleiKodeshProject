using Dapper;
using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;

namespace KitveiHakodeshService.Dictionary;

/// <summary>
/// Opens and queries KitveiHakodesh_dictionary.db.
/// The database is bundled with the service (CopyToOutputDirectory = PreserveNewest).
/// Uses a small read-only connection pool for concurrent dictionary lookups.
/// </summary>
public sealed class DictionaryDbManager : IDisposable
{
    private const int PoolSize = 4;

    private readonly SqliteConnection[] _pool;
    private readonly object _lock = new();
    private int _nextSlot = 0;
    private bool _disposed = false;

    public DictionaryDbManager()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Dictionary", "KitveiHakodesh_dictionary.db");
        _pool = new SqliteConnection[PoolSize];
        for (int i = 0; i < PoolSize; i++)
            _pool[i] = OpenConnection(path);
    }

    private static SqliteConnection OpenConnection(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        var conn = new SqliteConnection(connectionString);
        conn.Open();
        try { conn.Execute("PRAGMA cache_size = -4096"); } catch { /* non-fatal */ }
        return conn;
    }

    /// <summary>
    /// Executes a parameterised SELECT against the dictionary database.
    /// Accepts positional ? params and converts them to named Dapper params.
    /// </summary>
    public IEnumerable<IDictionary<string, object?>> Query(string sql, object?[] parameters)
    {
        SqliteConnection conn;
        lock (_lock)
        {
            conn = _pool[_nextSlot];
            _nextSlot = (_nextSlot + 1) % PoolSize;
        }

        int index = 0;
        string namedSql = Regex.Replace(sql, @"\?", _ => "@p" + index++);

        var dp = new DynamicParameters();
        for (int i = 0; i < parameters.Length; i++)
            dp.Add("@p" + i, parameters[i]);

        return conn.Query(namedSql, dp)
                   .Cast<IDictionary<string, object?>>()
                   .ToList();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var conn in _pool)
        {
            try { conn.Close(); conn.Dispose(); } catch { /* best effort */ }
        }
    }
}
