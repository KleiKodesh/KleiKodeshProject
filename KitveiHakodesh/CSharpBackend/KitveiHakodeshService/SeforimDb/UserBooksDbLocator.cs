using Microsoft.Win32;

namespace KitveiHakodeshService.SeforimDb;

/// <summary>
/// Single source of truth for WHERE Otzaria's personal-books database lives.
///
/// Otzaria keeps user-added books in a SEPARATE file (`user_books.db`) that reuses the
/// seforim schema and the same table names, so the very same SQL runs against either
/// database — that is what lets this service route a query to one of them instead of
/// merging the two (a UNION over both corpora is provably wrong: their id spaces both
/// start at 1, so `WHERE id = 5` would match a library sefer AND a personal book).
///
/// The file is OPTIONAL. Most users have no Otzaria install at all, and Otzaria itself
/// creates the file lazily — only once the first personal book is added. Absence is the
/// normal case and must stay free: no probing at boot, no empty file created here.
///
/// Otzaria's own path resolution has several branches, so a single hard-coded path would
/// miss real users. Resolution order (first existing file wins):
///   1. the registry value, when set and non-empty (the user's explicit choice)
///   2. the USER_BOOKS_DB_PATH environment variable (dev override)
///   3. `%APPDATA%\otzaria\databases` — Otzaria's per-user default
///   4. `%ProgramData%\otzaria\databases` — Otzaria's SYSTEM-WIDE install mode
///   5. a `databases` folder beside the seforim library — travels with a moved library
/// </summary>
public static class UserBooksDbLocator
{
    /// <summary>The file name Otzaria gives the personal-books database.</summary>
    public const string DatabaseFileName = "user_books.db";

    /// <summary>The registry key holding an explicit user override. Sits beside the
    /// seforim DB path so both live under one KitveiHakodesh key.</summary>
    public const string RegistryKeyPath = @"Software\VB and VBA Program Settings\KitveiHakodesh\Database";

    private const string RegistryValue = "UserBooksPath";

    /// <summary>
    /// The personal-books database to use right now, or null when there is none.
    ///
    /// Unlike <see cref="SeforimDbLocator.Resolve"/> this returns null rather than a
    /// best-guess path: the seforim DB is required and its absence is an error worth
    /// surfacing, whereas a missing personal-books DB is the ordinary case.
    ///
    /// <paramref name="seforimDbPath"/> is the resolved seforim database, used only to
    /// find the sibling `databases` folder of candidate 4.
    /// </summary>
    public static string? Resolve(string? seforimDbPath)
    {
        foreach (string candidate in Candidates(seforimDbPath))
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// The paths <see cref="Resolve"/> probes, in order. Exposed so diagnostics can show
    /// where the service looked when a user expects their personal books and sees none.
    /// </summary>
    public static IEnumerable<string> Candidates(string? seforimDbPath)
    {
        string? registry = LoadRegistryPath();
        if (!string.IsNullOrWhiteSpace(registry)) yield return registry;

        string? environment = Environment.GetEnvironmentVariable("USER_BOOKS_DB_PATH");
        if (!string.IsNullOrWhiteSpace(environment)) yield return environment;

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
            yield return Path.Combine(appData, "otzaria", "databases", DatabaseFileName);

        // Otzaria's system-wide install mode (system_install.marker) roots its data in
        // ProgramData\otzaria — independent of which seforim DB THIS app was pointed at,
        // so this candidate must not be derived from seforimDbPath: the user may run
        // KitveiHakodesh against a Zayit library while Otzaria keeps personal books here.
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrWhiteSpace(programData))
            yield return Path.Combine(programData, "otzaria", "databases", DatabaseFileName);

        // Otzaria's newer default puts `databases` beside the library folder, so that
        // moving the library to another drive takes the personal books along with it.
        if (!string.IsNullOrWhiteSpace(seforimDbPath))
        {
            string? libraryFolder = Path.GetDirectoryName(seforimDbPath);
            string? libraryRoot = libraryFolder is null ? null : Path.GetDirectoryName(libraryFolder);
            if (!string.IsNullOrWhiteSpace(libraryRoot))
                yield return Path.Combine(libraryRoot, "databases", DatabaseFileName);
        }
    }

    public static string? LoadRegistryPath()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            return key?.GetValue(RegistryValue) as string;
        }
        catch { return null; }
    }

    public static void SaveRegistryPath(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
        key.SetValue(RegistryValue, path);
    }

    public static void ClearRegistryPath()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);
            key?.DeleteValue(RegistryValue, throwOnMissingValue: false);
        }
        catch { /* nothing to clear */ }
    }
}
