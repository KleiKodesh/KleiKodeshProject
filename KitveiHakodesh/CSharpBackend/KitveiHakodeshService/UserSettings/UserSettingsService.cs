using System.Globalization;
using System.Text;
using System.Text.Json;
using KitveiHakodesh.Core.UserAnnotations;
using KitveiHakodeshService.Ipc;

namespace KitveiHakodeshService.UserSettings;

/// <summary>
/// Transport wrapper over <see cref="UserAnnotationStore"/> — the user's highlights and notes,
/// and the only write path in the backend.
///
/// Core owns the database, its schema, the quote-aware '?' → @pN rewrite and the SQL guard that
/// is the whole boundary between frontend-supplied SQL and arbitrary SQLite. What stays here is
/// transport shaping only: JsonElement arguments in, a raw JSON array string out, because that
/// is the wire contract the frontend already speaks (RawRowsResult.RowsJson).
///
/// The class and the file on disk still say "settings" while Core says "annotations" — the
/// database name is frozen (existing installs hold real user highlights under
/// user_settings.db); see the remarks on UserAnnotationStore.
/// </summary>
public sealed class UserSettingsService
{
    private readonly UserAnnotationStore? _annotations;
    private readonly object _initLock = new();
    private bool _initialized;

    public UserSettingsService()
    {
        string? seforimDbPath = SeforimDb.SeforimDbLocator.Resolve();
        if (!string.IsNullOrWhiteSpace(seforimDbPath))
            _annotations = new UserAnnotationStore(UserAnnotationStore.DeriveDatabasePath(seforimDbPath!));
    }

    /// <summary>False when no seforim library is configured, so there is nowhere for the
    /// annotations to live yet. Reads answer empty and writes do nothing.</summary>
    public bool Available => _annotations is not null;

    /// <summary>Runs a read query and returns the rows as a raw JSON array string.</summary>
    public string QueryRowsJson(string sql, JsonElement[] parameters)
    {
        if (_annotations is null) return "[]";
        EnsureInitialized();
        return ToJsonArray(_annotations.Query(sql, ToParameters(parameters)));
    }

    /// <summary>Runs a write statement and returns the last inserted rowid.</summary>
    public long Execute(string sql, JsonElement[] parameters)
    {
        if (_annotations is null) return 0;
        EnsureInitialized();
        return _annotations.Execute(sql, ToParameters(parameters));
    }

    /// <summary>Creates the schema on first use. Core's EnsureCreated is idempotent; the lock
    /// keeps two concurrent RPCs from racing into it on a cold database.</summary>
    private void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            _annotations!.EnsureCreated();
            _initialized = true;
        }
    }

    private static object?[] ToParameters(JsonElement[] parameters)
    {
        var values = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++) values[i] = ToParameter(parameters[i]);
        return values;
    }

    private static object? ToParameter(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out long l) ? l : element.GetDouble(),
        JsonValueKind.True => 1L,
        JsonValueKind.False => 0L,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => element.GetRawText(),
    };

    /// <summary>Renders Core's rows as the JSON array string the frontend expects. Numbers stay
    /// unquoted and invariant-formatted; everything else goes out as a JSON string.</summary>
    private static string ToJsonArray(List<Dictionary<string, object?>> rows)
    {
        var json = new StringBuilder("[");
        bool firstRow = true;

        foreach (var row in rows)
        {
            if (!firstRow) json.Append(',');
            firstRow = false;

            json.Append('{');
            bool firstColumn = true;
            foreach (var column in row)
            {
                if (!firstColumn) json.Append(',');
                firstColumn = false;

                json.Append(JsonSerializer.Serialize(column.Key, RpcJsonContext.Default.String)).Append(':');
                switch (column.Value)
                {
                    case null:
                        json.Append("null");
                        break;
                    case long or int:
                        json.Append(Convert.ToInt64(column.Value, CultureInfo.InvariantCulture)
                                           .ToString(CultureInfo.InvariantCulture));
                        break;
                    case double or float:
                        json.Append(Convert.ToDouble(column.Value, CultureInfo.InvariantCulture)
                                           .ToString(CultureInfo.InvariantCulture));
                        break;
                    default:
                        json.Append(JsonSerializer.Serialize(
                            column.Value.ToString() ?? "", RpcJsonContext.Default.String));
                        break;
                }
            }
            json.Append('}');
        }

        return json.Append(']').ToString();
    }
}
