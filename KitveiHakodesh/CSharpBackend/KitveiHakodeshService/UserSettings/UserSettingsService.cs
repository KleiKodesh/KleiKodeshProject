using KitveiHakodesh.Core.UserAnnotations;

namespace KitveiHakodeshService.UserSettings;

/// <summary>
/// Transport wrapper over <see cref="UserAnnotationStore"/> — the user's highlights and notes,
/// and the only write path in the backend.
///
/// Core owns the database, its schema, the quote-aware '?' → @pN rewrite and the SQL guard that
/// is the whole boundary between frontend-supplied SQL and arbitrary SQLite. What stays here is
/// transport shaping only: MessagePack bind values normalized to what SQLite binds, and Core's
/// rows handed back as-is for the envelope to serialize.
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

    /// <summary>Runs a read query and returns the rows, column name -> value.</summary>
    public List<Dictionary<string, object?>> QueryRows(string sql, IReadOnlyList<object?> parameters)
    {
        if (_annotations is null) return [];
        EnsureInitialized();
        return _annotations.Query(sql, ToParameters(parameters));
    }

    /// <summary>Runs a write statement and returns the last inserted rowid.</summary>
    public long Execute(string sql, IReadOnlyList<object?> parameters)
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

    /// <summary>MessagePack hands integers back as whatever width fits (byte, int, ulong...);
    /// SQLite binds one integer type and one real type, and has no boolean.</summary>
    private static object?[] ToParameters(IReadOnlyList<object?> parameters)
    {
        var values = new object?[parameters.Count];
        for (int i = 0; i < parameters.Count; i++) values[i] = ToParameter(parameters[i]);
        return values;
    }

    private static object? ToParameter(object? value) => value switch
    {
        null => null,
        bool b => b ? 1L : 0L,
        string or byte[] => value,
        float or double => System.Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture),
        sbyte or byte or short or ushort or int or uint or long => System.Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture),
        ulong u => u <= long.MaxValue ? (long)u : (double)u,
        _ => value.ToString(),
    };
}
