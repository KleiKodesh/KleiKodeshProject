using System.Globalization;
using System.Text;
using System.Text.Json;
using KitveiHakodeshService.Ipc;
using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.UserSettings;

/// <summary>
/// Read+write access to the per-user settings DB (highlights &amp; notes). This is
/// the first WRITE path in the service. It mirrors the hosted C# bridge exactly:
/// the frontend sends parameterized SQL (positional '?'), which is executed against
/// user_settings.db. The DB lives beside the seforim DB at {dbDir}\Settings\user_settings.db
/// (DB_PATH env) and its schema is created on first use.
/// </summary>
public sealed class UserSettingsService
{
    private readonly string? _dbPath = ResolvePath(SeforimDb.SeforimDbLocator.Resolve());
    private readonly object _initLock = new();
    private bool _initialized;

    private static string? ResolvePath(string? seforimDbPath)
    {
        if (string.IsNullOrWhiteSpace(seforimDbPath)) return null;
        string? dir = Path.GetDirectoryName(Path.GetFullPath(seforimDbPath));
        return dir is null ? null : Path.Combine(dir, "Settings", "user_settings.db");
    }

    private SqliteConnection Open()
    {
        EnsureInitialized();
        var cs = new SqliteConnectionStringBuilder { DataSource = _dbPath }.ConnectionString;
        var conn = new SqliteConnection(cs);
        conn.Open();
        return conn;
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_dbPath!)!);
            var cs = new SqliteConnectionStringBuilder { DataSource = _dbPath }.ConnectionString;
            using var conn = new SqliteConnection(cs);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS user_highlights (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  bookId INTEGER NOT NULL, lineId INTEGER NOT NULL,
                  startOffset INTEGER NOT NULL, endOffset INTEGER NOT NULL,
                  colorArgb INTEGER NOT NULL, createdAt INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_user_highlights_book_line ON user_highlights (bookId, lineId);
                CREATE TABLE IF NOT EXISTS user_notes (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  bookId INTEGER NOT NULL, lineId INTEGER NOT NULL,
                  startOffset INTEGER NOT NULL, endOffset INTEGER NOT NULL,
                  note TEXT NOT NULL, quote TEXT NOT NULL,
                  createdAt INTEGER NOT NULL, updatedAt INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_user_notes_book_line ON user_notes (bookId, lineId);";
            cmd.ExecuteNonQuery();
            _initialized = true;
        }
    }

    public bool Available => !string.IsNullOrWhiteSpace(_dbPath);

    /// <summary>Runs a read query and returns the rows as a raw JSON array string.</summary>
    public string QueryRowsJson(string sql, JsonElement[] parameters)
    {
        if (!Available) return "[]";
        GuardSql(sql, write: false);
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = RewritePlaceholders(sql);
        Bind(cmd, parameters);
        using var reader = cmd.ExecuteReader();

        var sb = new StringBuilder("[");
        bool firstRow = true;
        while (reader.Read())
        {
            if (!firstRow) sb.Append(',');
            firstRow = false;
            sb.Append('{');
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(JsonSerializer.Serialize(reader.GetName(i), RpcJsonContext.Default.String)).Append(':');
                if (reader.IsDBNull(i)) { sb.Append("null"); continue; }
                var t = reader.GetFieldType(i);
                if (t == typeof(long) || t == typeof(int))
                    sb.Append(reader.GetInt64(i).ToString(CultureInfo.InvariantCulture));
                else if (t == typeof(double) || t == typeof(float))
                    sb.Append(reader.GetDouble(i).ToString(CultureInfo.InvariantCulture));
                else
                    sb.Append(JsonSerializer.Serialize(reader.GetValue(i)?.ToString() ?? "", RpcJsonContext.Default.String));
            }
            sb.Append('}');
        }
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>Runs a write statement and returns the last inserted rowid.</summary>
    public long Execute(string sql, JsonElement[] parameters)
    {
        if (!Available) return 0;
        GuardSql(sql, write: true);
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = RewritePlaceholders(sql);
        Bind(cmd, parameters);
        cmd.ExecuteNonQuery();

        using var idCmd = conn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        var result = idCmd.ExecuteScalar();
        return result is long l ? l : 0;
    }

    // The two settings tables. Nothing else in the DB is reachable through this op, and the DB
    // itself is the only file reachable at all - see GuardSql.
    private static readonly string[] AllowedTables = ["user_highlights", "user_notes"];

    // Statement verbs, split read vs write so the read op cannot mutate and the write op cannot
    // be used as an exfiltration channel for a SELECT the frontend was not given.
    private static readonly string[] ReadVerbs = ["select"];
    private static readonly string[] WriteVerbs = ["insert", "update", "delete"];

    // Anything that can reach OUTSIDE user_settings.db, change its shape, or run a second
    // statement. ATTACH is the sharp one: without it in this list, "edit my highlights" is
    // "create and write an arbitrary SQLite file anywhere the service can write".
    private static readonly string[] ForbiddenWords =
    [
        "attach", "detach", "pragma", "vacuum", "alter", "drop", "create", "reindex",
        "load_extension", "readfile", "writefile",
        "sqlite_master", "sqlite_schema", "sqlite_temp_master", "sqlite_dbpage", "sqlite_stat1",
    ];

    /// <summary>
    /// The frontend supplies SQL text, so this is the whole boundary between a compromised page
    /// (or a leaked bearer token) and arbitrary SQLite. It is deliberately a FILTER, not a parser:
    /// one statement, one allowed verb, only the two settings tables, and none of the words that
    /// can escape the file. Anything it cannot understand is rejected rather than passed through.
    /// </summary>
    private static void GuardSql(string sql, bool write)
    {
        if (string.IsNullOrWhiteSpace(sql)) throw new InvalidOperationException("empty statement");

        string bare = StripLiteralsAndComments(sql);

        // No batching: a second statement would never be seen by the verb check below.
        if (bare.TrimEnd().TrimEnd(';').Contains(';'))
            throw new InvalidOperationException("only one statement is allowed");

        var tokens = Words(bare);
        if (tokens.Count == 0) throw new InvalidOperationException("empty statement");

        string verb = tokens[0];
        string[] allowedVerbs = write ? WriteVerbs : ReadVerbs;
        if (Array.IndexOf(allowedVerbs, verb) < 0)
            throw new InvalidOperationException($"statement type '{verb}' is not allowed here");

        foreach (string w in tokens)
            if (Array.IndexOf(ForbiddenWords, w) >= 0)
                throw new InvalidOperationException($"'{w}' is not allowed in a settings statement");

        // Every table position - what follows FROM / JOIN / INTO / UPDATE - must be one of ours.
        for (int i = 0; i < tokens.Count - 1; i++)
        {
            if (tokens[i] is not ("from" or "join" or "into" or "update")) continue;
            if (Array.IndexOf(AllowedTables, tokens[i + 1]) < 0)
                throw new InvalidOperationException($"table '{tokens[i + 1]}' is not a settings table");
        }
    }

    /// <summary>Blanks out string literals and comments so the guard reads structure only - a
    /// note whose TEXT happens to contain the word "attach" must not be rejected.</summary>
    private static string StripLiteralsAndComments(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        char quote = '\0';
        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                sb.Append(' ');
                continue;
            }
            if (c is '\'' or '"' or '`' or '[') { quote = c == '[' ? ']' : c; sb.Append(' '); continue; }
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n') i++;
                sb.Append('\n');
                continue;
            }
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/')) i++;
                i++;
                sb.Append(' ');
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Lower-cased identifier/keyword runs. Hand-rolled rather than a Regex so nothing
    /// here depends on Reflection.Emit under native AOT.</summary>
    private static List<string> Words(string s)
    {
        var words = new List<string>();
        var cur = new StringBuilder();
        foreach (char c in s)
        {
            if (char.IsLetterOrDigit(c) || c == '_') cur.Append(char.ToLowerInvariant(c));
            else if (cur.Length > 0) { words.Add(cur.ToString()); cur.Clear(); }
        }
        if (cur.Length > 0) words.Add(cur.ToString());
        return words;
    }

    // Rewrite positional '?' to @p0..@pN (Microsoft.Data.Sqlite binds by name),
    // matching the hosted DbAccess conversion. Ignores '?' inside quoted strings.
    private static string RewritePlaceholders(string sql)
    {
        var sb = new StringBuilder(sql.Length + 16);
        int idx = 0;
        char quote = '\0';
        foreach (char c in sql)
        {
            if (quote != '\0')
            {
                sb.Append(c);
                if (c == quote) quote = '\0';
            }
            else if (c is '\'' or '"')
            {
                quote = c;
                sb.Append(c);
            }
            else if (c == '?')
            {
                sb.Append("@p").Append(idx++);
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static void Bind(SqliteCommand cmd, JsonElement[] parameters)
    {
        for (int i = 0; i < parameters.Length; i++)
            cmd.Parameters.AddWithValue("@p" + i, ToParam(parameters[i]));
    }

    private static object ToParam(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? (object)DBNull.Value,
        JsonValueKind.Number => e.TryGetInt64(out long l) ? l : e.GetDouble(),
        JsonValueKind.True => 1L,
        JsonValueKind.False => 0L,
        JsonValueKind.Null or JsonValueKind.Undefined => DBNull.Value,
        _ => e.GetRawText(),
    };
}
