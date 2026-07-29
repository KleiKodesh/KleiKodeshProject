using Microsoft.Win32;

namespace KitveiHakodeshService.SeforimDb;

/// <summary>
/// Single source of truth for WHERE the seforim database lives.
///
/// Persistence is the SAME registry value KitveiHakodeshLib uses
/// (AppSettings.Load/SaveDbPath → VB Interaction Get/SaveSetting("KitveiHakodesh",
/// "Database", "Path")), i.e. HKCU\Software\VB and VBA Program Settings\
/// KitveiHakodesh\Database\Path — so the hosted app, the DemoApp and this service
/// all read and write one setting.
///
/// Resolution order:
///   1. the registry value, when set and non-empty (the user's explicit choice)
///   2. the DB_PATH environment variable (dev override forwarded by the Vite plugin)
///   3. the default probe — Zayit then Otzaria (same order as AppSettings.ResolveDefaultDbPath)
/// </summary>
public static class SeforimDbLocator
{
    /// <summary>The registry key holding the user's DB choice. Public so the DB change
    /// watcher can subscribe to it — the HOSTED app writes this value directly (not via
    /// the service RPC), so a switch can happen while the service is running.</summary>
    public const string RegistryKeyPath = @"Software\VB and VBA Program Settings\KitveiHakodesh\Database";

    private const string RegistryKey = RegistryKeyPath;
    private const string RegistryValue = "Path";

    /// <summary>The path the service should use right now (may not exist on disk).</summary>
    public static string Resolve()
    {
        string? reg = LoadRegistryPath();
        if (!string.IsNullOrWhiteSpace(reg)) return reg;

        string? env = Environment.GetEnvironmentVariable("DB_PATH");
        if (!string.IsNullOrWhiteSpace(env)) return env;

        return ResolveDefaultDbPath();
    }

    /// <summary>True when the registry (user-set) value is what Resolve() returns.</summary>
    public static bool IsCustom() => !string.IsNullOrWhiteSpace(LoadRegistryPath());

    /// <summary>Default probe — identical to KitveiHakodeshLib AppSettings.ResolveDefaultDbPath:
    /// Zayit first, then Otzaria; Zayit path as the fallback even if neither exists.</summary>
    public static string ResolveDefaultDbPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string zayit = Path.Combine(appData, "io.github.kdroidfilter.seforimapp", "databases", "seforim.db");
        string otzaria = Path.Combine(appData, "otzaria", "books", "seforim.db");
        if (File.Exists(zayit)) return zayit;
        if (File.Exists(otzaria)) return otzaria;
        return zayit;
    }

    public static string? LoadRegistryPath()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
            return key?.GetValue(RegistryValue) as string;
        }
        catch { return null; }
    }

    public static void SaveRegistryPath(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var key = Registry.CurrentUser.CreateSubKey(RegistryKey);
        key.SetValue(RegistryValue, path);
    }

    public static void ClearRegistryPath()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);
            key?.DeleteValue(RegistryValue, throwOnMissingValue: false);
        }
        catch { /* nothing to clear */ }
    }
}
