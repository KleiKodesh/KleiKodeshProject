using Dapper;
using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;

namespace KitveiHakodeshService.Seforim;

/// <summary>
/// Opens and queries the seforim SQLite database.
///
/// Uses a pool of read-only connections so concurrent HTTP requests execute
/// truly in parallel rather than serialising on a single shared connection.
/// Pool size of 8 covers the maximum number of queries the frontend fires
/// concurrently (line chunks, TOC, metadata, prefetch).
///
/// Mirrors the pool strategy in KitveiHakodeshLib's DbAccess.cs.
/// </summary>
public sealed class SeforimDbManager : IDisposable
{
    private const int PoolSize = 8;

    private SqliteConnection[]? _pool;
    private readonly object _lock = new();
    private int _nextSlot = 0;
    private bool _disposed = false;
    private string? _dbPath;

    public bool IsReady => _pool != null && !_disposed;

    /// <summary>
    /// Opens the database at the given path and initialises the connection pool.
    /// Safe to call multiple times — replaces the pool if a path change occurs.
    /// Does nothing if the path is empty or the file does not exist.
    /// </summary>
    public void Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        lock (_lock)
        {
            // Dispose previous pool if switching databases.
            DisposePool();

            _dbPath = path;
            _pool = new SqliteConnection[PoolSize];
            for (int i = 0; i < PoolSize; i++)
                _pool[i] = OpenConnection(path);
        }
    }

    private static SqliteConnection OpenConnection(string path)
    {
        // Mode=ReadOnly prevents accidental writes; Cache=Shared allows the OS
        // page cache to be shared across connections to the same file.
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        var conn = new SqliteConnection(connectionString);
        conn.Open();

        // 8 MB page cache per connection.
        try { conn.Execute("PRAGMA cache_size = -8192"); } catch { /* non-fatal */ }
        // 256 MB memory-mapped I/O — shared across connections to the same file.
        try { conn.Execute("PRAGMA mmap_size = 268435456"); } catch { /* non-fatal */ }

        return conn;
    }

    /// <summary>
    /// Executes a parameterised SELECT and returns all rows as dictionaries.
    /// Accepts positional ? params (frontend convention) and converts them to
    /// Dapper-style named params (@p0, @p1, ...) internally.
    /// </summary>
    public IEnumerable<IDictionary<string, object?>> Query(string sql, object?[] parameters)
    {
        SqliteConnection conn;
        lock (_lock)
        {
            if (_pool == null) throw new InvalidOperationException("Database not open.");
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

    private void DisposePool()
    {
        if (_pool == null) return;
        foreach (var conn in _pool)
        {
            try { conn.Close(); conn.Dispose(); } catch { /* best effort */ }
        }
        _pool = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lock) { DisposePool(); }
    }
}
