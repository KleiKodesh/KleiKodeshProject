using Microsoft.Win32;

namespace KitveiHakodeshService.UserSettings;

/// <summary>
/// Reads/writes the SAME Windows registry values the net4.8 KitveiHakodeshLib writes via VB
/// <c>Interaction.GetSetting/SaveSetting</c>. Those helpers store under
/// <c>HKCU\Software\VB and VBA Program Settings\{app}\{section}</c> with the key as the value
/// name — this mirrors that layout byte-for-byte so the hosted app and this service (hence dev)
/// share one source of truth. See KitveiHakodeshLib/Settings/AppSettings.cs for the canonical
/// (app, section, key) tuples; do not diverge from them or the setting forks.
///
/// SeforimDbLocator already does this for the DB path; this is the general helper for the rest.
/// </summary>
public static class AppSettingsRegistry
{
    private static string SubKey(string app, string section) =>
        $@"Software\VB and VBA Program Settings\{app}\{section}";

    public static string Get(string app, string section, string key, string fallback)
    {
        if (!OperatingSystem.IsWindows()) return fallback;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(SubKey(app, section));
            return k?.GetValue(key) as string ?? fallback;
        }
        catch { return fallback; }
    }

    public static void Set(string app, string section, string key, string value)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var k = Registry.CurrentUser.CreateSubKey(SubKey(app, section));
        k.SetValue(key, value ?? "");
    }

    // ── Named settings shared with KitveiHakodeshLib.Settings.AppSettings ──────────

    /// <summary>HebrewBooks local download folder — same key as AppSettings.Load/SaveHbLocalFolder:
    /// HKCU\Software\VB and VBA Program Settings\KitveiHakodesh\HebrewBooks\LocalFolder.</summary>
    public static string GetHbLocalFolder() => Get("KitveiHakodesh", "HebrewBooks", "LocalFolder", "");
    public static void SetHbLocalFolder(string path) => Set("KitveiHakodesh", "HebrewBooks", "LocalFolder", path);
}
