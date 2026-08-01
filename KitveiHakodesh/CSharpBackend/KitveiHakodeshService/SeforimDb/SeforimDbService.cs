using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.SeforimDb;

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

    // Otzaria's personal-books database, when the user has one. Resolved lazily on first
    // use rather than at construction: most users have no Otzaria install, and boot stays
    // idle by design — a File.Exists probe per candidate path is not worth paying there.
    //
    // Unlike the seforim DB (only ever replaced while the service is down), this file is
    // LIVE: Otzaria creates it the moment the user adds their first personal book and
    // rewrites it as books come and go — all while this service may be running. So a
    // null resolution is re-probed (throttled) instead of cached forever, and catalog
    // consumers re-check <see cref="UserBooksChangeStamp"/> before trusting their caches.
    private string? _userBooksDbPath;
    private bool _userBooksResolved;
    private long _userBooksNextProbeTicks;

    /// <summary>
    /// Whether Otzaria's personal-books database is present. False is the ordinary case
    /// and means every query keeps taking the library path exactly as before.
    /// </summary>
    public bool HasUserBooksDb => UserBooksDbPath is not null;

    private string? UserBooksDbPath
    {
        get
        {
            if (_userBooksResolved && _userBooksDbPath is not null) return _userBooksDbPath;

            // Not found (yet): re-probe at most every 5s, so the DB Otzaria creates
            // mid-run is picked up without paying candidate File.Exists on every query.
            long now = Environment.TickCount64;
            if (_userBooksResolved && now < _userBooksNextProbeTicks) return null;
            _userBooksNextProbeTicks = now + 5_000;

            _userBooksDbPath = UserBooksDbLocator.Resolve(_dbPath);
            _userBooksResolved = true;
            if (_userBooksDbPath is not null)
                logger.LogInformation("personal-books DB found at {Path}", _userBooksDbPath);
            return _userBooksDbPath;
        }
    }

    /// <summary>
    /// A value that changes whenever the personal-books database changes on disk
    /// (0 = absent). Catalog caches key on it: same stamp → caches stay valid; the
    /// library needs no such stamp because it never changes while the service runs.
    /// </summary>
    private long UserBooksChangeStamp
    {
        get
        {
            string? path = UserBooksDbPath;
            if (path is null) return 0;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return 0;
                // Ticks XOR length: either a rewrite or a size change flips the stamp.
                long stamp = info.LastWriteTimeUtc.Ticks ^ info.Length;
                // While Otzaria has the DB open, writes land in the WAL sidecar first
                // and the main file's mtime can stay put until checkpoint — fold the
                // WAL in so those changes flip the stamp too.
                var wal = new FileInfo(path + "-wal");
                if (wal.Exists) stamp ^= wal.LastWriteTimeUtc.Ticks ^ (wal.Length << 1);
                return stamp;
            }
            catch { return 0; }
        }
    }

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

    /// <summary>
    /// Opens the database holding <paramref name="corpus"/>. Both files carry the same
    /// table names, so callers run the SAME SQL against whichever connection comes back —
    /// that is what keeps the library path unchanged instead of rewriting its queries.
    ///
    /// Throws when the personal-books database is asked for but absent; callers must gate
    /// on <see cref="HasUserBooksDb"/> (or simply never produce a personal-book id, which
    /// is automatic when the file does not exist).
    /// </summary>
    private SqliteConnection Open(Corpus corpus)
    {
        if (corpus == Corpus.Library) return Open();

        string path = UserBooksDbPath
            ?? throw new InvalidOperationException(
                "personal-books database requested but none is present");

        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
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

    /// <summary>True if the DB has a table named <paramref name="table"/>. Whole tables
    /// (not just columns) differ across seforim-DB schema versions — e.g. link_anchor
    /// only exists from SeforimLibrary schema v2 on.</summary>
    private static bool TableExists(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @t LIMIT 1";
        cmd.Parameters.AddWithValue("@t", table);
        return cmd.ExecuteScalar() != null;
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

    /// <summary>
    /// Corpus-gated <see cref="Run(Action, string)"/>. A missing LIBRARY is a warning
    /// (the service is useless without it); a missing personal-books DB is the ordinary
    /// case and exits silently — a personal-book id reaching here with no DB behind it
    /// can only mean the file was deleted mid-session, and empty results are the correct
    /// answer for rows that no longer exist.
    /// </summary>
    private void Run(Corpus corpus, Action action, string op)
    {
        if (corpus == Corpus.Library)
        {
            Run(action, op);
            return;
        }
        if (!HasUserBooksDb) return;
        try { action(); }
        catch (Exception ex) { logger.LogError(ex, "{Op} failed (user books)", op); }
    }
}
