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
