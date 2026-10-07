using KitveiHakodesh.Core.Settings;

namespace KitveiHakodeshService.SeforimDb;

/// <summary>
/// Thin adapter over <see cref="SeforimDbPathResolver"/>, which owns WHERE the seforim database
/// lives and the registry value it is persisted in (shared with the hosted app, the VSTO add-in
/// and the DemoApp, so all of them read one setting).
///
/// It exists only to keep the service's string-shaped call sites working. Core answers with a
/// <see cref="SeforimDbPathResolver.SeforimDbLocation"/> that distinguishes "found here" from
/// "found nothing" and never invents a path; this flattens that to a nullable string, which is
/// what the service's callers already treat it as — null meaning "no database configured".
///
/// Two deliberate behaviour changes came with the move to Core, both stricter:
///   - a saved registry path is used only when the file is actually THERE, so a moved or
///     deleted database no longer yields a stale path that fails later somewhere less obvious
///   - when nothing is found, the answer is null rather than a default path known not to exist
/// The DB_PATH environment override is gone with them: Core reads no environment variables, and
/// a developer changes the setting exactly like a user does.
/// </summary>
public static class SeforimDbLocator
{
    /// <summary>The registry key holding the user's DB choice. Public so the DB change
    /// watcher can subscribe to it — the HOSTED app writes this value directly (not via
    /// the service RPC), so a switch can happen while the service is running.</summary>
    public const string RegistryKeyPath = SeforimDbPathResolver.RegistryKeyPath;

    /// <summary>The database to use right now, or null when none was found. Unlike the old
    /// version, a returned path is one that EXISTS.</summary>
    public static string? Resolve() => SeforimDbPathResolver.Resolve().Path;

    /// <summary>True when the user's own choice is what <see cref="Resolve"/> returns.</summary>
    public static bool IsCustom() => SeforimDbPathResolver.HasUserChoice();

    /// <summary>The first install location worth probing, used when there is nothing to resolve
    /// and the caller still has to name a place — the file picker's starting folder, say.</summary>
    public static string ResolveDefaultDbPath() => SeforimDbPathResolver.DefaultLocations()[0];

    public static string? LoadRegistryPath() => SeforimDbPathResolver.GetSavedPath();

    public static void SaveRegistryPath(string path) => SeforimDbPathResolver.SavePath(path);

    public static void ClearRegistryPath() => SeforimDbPathResolver.ClearSavedPath();
}
