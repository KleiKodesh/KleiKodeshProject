#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using KitveiHakodesh.Core.Settings;
using Microsoft.Win32;

namespace KitveiHakodesh.Core.LocalFiles.ToratEmet;

/// <summary>
/// Where the Torat Emet program keeps its books on this computer: the install's "Books" folder and the user's "MyBooks" folder.
/// Cheap by design (a few registry reads and directory probes, never a drive scan). Order of the sources, no duplicates:
///   1. the program's own records: Windows uninstall entries whose key or DisplayName names ToratEmet (InstallLocation, DisplayIcon,
///      UninstallString folder) and App Paths entries of bh3.exe / bh3_unicode.exe - the install root is the folder holding the program;
///   2. next to that install root: the sibling ToratEmetUserData\MyBooks (the program's own layout; its Config.txt names no user-data folder);
///   3. the program's default locations under the user's real Documents folder: ToratEmetInstall\Books and ToratEmetUserData\MyBooks.
/// </summary>
public static class ToratEmetFolders
{
    public const string InstallFolderName = "ToratEmetInstall";
    public const string UserDataFolderName = "ToratEmetUserData";

    /// <summary>The host-side facts the search needs; tests supply fakes.</summary>
    public sealed class Probe
    {
        /// <summary>The user's Documents folder (a redirected one too): the user's own via UserProfile, also when the service runs as SYSTEM.</summary>
        public Func<string> DocumentsFolder { get; set; } = () => TestDocuments ?? UserProfile.Documents;
        /// <summary>Folders the program's install records point at (install roots: the folder that holds bh3.exe and Books).</summary>
        public Func<IEnumerable<string>> RegisteredInstallRoots { get; set; } = () => TestDocuments != null ? Array.Empty<string>() : ReadRegisteredInstallRoots();

        /// <summary>KHS_TEST_DOCUMENTS_DIR: the hosted rig's fake Documents folder (and no registry records), so the rig never reads the real ones.</summary>
        private static string? TestDocuments
        {
            get { string? v = Environment.GetEnvironmentVariable("KHS_TEST_DOCUMENTS_DIR"); return string.IsNullOrWhiteSpace(v) ? null : v; }
        }
    }

    /// <summary>Existing book folders in the order above. MyBooks only when it holds at least one .txt file.</summary>
    public static List<string> Locate(Probe? probe = null, CancellationToken ct = default)
    {
        probe ??= new Probe();
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string path, bool needsTxt)
        {
            string full;
            try { full = Path.GetFullPath(path).TrimEnd('\\', '/'); } catch (Exception) { return; }
            if (!seen.Add(full)) return;
            if (!Directory.Exists(full)) return;
            if (needsTxt && !ContainsTxtFile(full, ct)) { seen.Remove(full); return; }
            found.Add(full);
        }

        var roots = new List<string>();
        try { foreach (var root in probe.RegisteredInstallRoots()) if (!string.IsNullOrWhiteSpace(root)) roots.Add(root); }
        catch (Exception) { /* records unreadable: the defaults still apply */ }
        foreach (var root in roots) Add(Path.Combine(root, "Books"), needsTxt: false);
        foreach (var root in roots)
        {
            string? parent = Path.GetDirectoryName(root.TrimEnd('\\', '/'));
            if (parent != null) Add(Path.Combine(parent, UserDataFolderName, "MyBooks"), needsTxt: true);
        }
        string documents = "";
        try { documents = probe.DocumentsFolder(); } catch (Exception) { }
        if (!string.IsNullOrWhiteSpace(documents))
        {
            Add(Path.Combine(documents, InstallFolderName, "Books"), needsTxt: false);
            Add(Path.Combine(documents, UserDataFolderName, "MyBooks"), needsTxt: true);
        }
        return found;
    }

    /// <summary>Hebrew name of the install's Books root: "Torat Emet".</summary>
    public const string BooksRootTitle = "תורת אמת";
    /// <summary>Hebrew name of the MyBooks root: "Torat Emet - personal books".</summary>
    public const string MyBooksRootTitle = "תורת אמת - ספרים אישיים";

    /// <summary>
    /// The Hebrew name to show for a folder that is one of the default roots <see cref="Locate"/> finds (wherever the program is
    /// installed); "" for any other folder, which keeps its own name.
    /// </summary>
    public static string RootTitle(string? folder, Probe? probe = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folder)) return "";
        string full;
        try { full = Path.GetFullPath(folder!).TrimEnd('\\', '/'); } catch (Exception) { return ""; }
        string name = Path.GetFileName(full);
        bool books = string.Equals(name, "Books", StringComparison.OrdinalIgnoreCase);
        bool mine = string.Equals(name, "MyBooks", StringComparison.OrdinalIgnoreCase);
        if (!books && !mine) return "";
        foreach (var found in Locate(probe, ct))
            if (string.Equals(found, full, StringComparison.OrdinalIgnoreCase)) return books ? BooksRootTitle : MyBooksRootTitle;
        return "";
    }

    /// <summary>True when the folder (any depth) holds a .txt file. Stops at the first one, with no cap on what it looks at (a tree
    /// with no .txt is walked whole); cancellable, and the host runs it off the startup path.</summary>
    public static bool ContainsTxtFile(string folder, CancellationToken ct = default)
    {
        var pending = new Queue<string>();
        pending.Enqueue(folder);
        while (pending.Count > 0)
        {
            try
            {
                foreach (var entry in new DirectoryInfo(pending.Dequeue()).EnumerateFileSystemInfos())
                {
                    ct.ThrowIfCancellationRequested();
                    if (entry is DirectoryInfo) pending.Enqueue(entry.FullName);
                    else if (entry.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
        }
        return false;
    }

    private static IEnumerable<string> ReadRegisteredInstallRoots()
    {
        var roots = new List<string>();
        // The user's records come from the bound user's hive (UserProfile.Hive): under a SYSTEM service the process's own HKCU is not theirs.
        foreach (string software in new[] { @"Software", @"Software\WOW6432Node" })
        {
            try { ReadRecords(UserProfile.Hive, software, roots); }
            catch (Exception) { /* a hive that cannot be read has no records for us */ }
        }
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                ReadRecords(baseKey, "Software", roots);
            }
            catch (Exception) { }
        }
        return roots;
    }

    private static void ReadRecords(RegistryKey baseKey, string software, List<string> roots)
    {
        ReadUninstallEntries(baseKey, software, roots);
        ReadAppPaths(baseKey, software, roots);
    }

    private static void ReadUninstallEntries(RegistryKey baseKey, string software, List<string> roots)
    {
        using var uninstall = baseKey.OpenSubKey(software + @"\Microsoft\Windows\CurrentVersion\Uninstall");
        if (uninstall == null) return;
        foreach (var name in uninstall.GetSubKeyNames())
        {
            using var entry = uninstall.OpenSubKey(name);
            if (entry == null) continue;
            string display = entry.GetValue("DisplayName") as string ?? "";
            bool ours = name.IndexOf("ToratEmet", StringComparison.OrdinalIgnoreCase) >= 0
                        || display.IndexOf("ToratEmet", StringComparison.OrdinalIgnoreCase) >= 0
                        || display.Contains(ToratEmetNames.SearchPhrase);
            if (!ours) continue;
            AddIfFolder(roots, entry.GetValue("InstallLocation") as string);
            AddIfFolder(roots, FolderOfCommand(entry.GetValue("DisplayIcon") as string));
            AddIfFolder(roots, FolderOfCommand(entry.GetValue("UninstallString") as string));
        }
    }

    private static void ReadAppPaths(RegistryKey baseKey, string software, List<string> roots)
    {
        foreach (var exe in new[] { "bh3.exe", "bh3_unicode.exe" })
        {
            using var key = baseKey.OpenSubKey(software + @"\Microsoft\Windows\CurrentVersion\App Paths\" + exe);
            AddIfFolder(roots, FolderOfCommand(key?.GetValue(null) as string));
        }
    }

    private static void AddIfFolder(List<string> roots, string? folder)
    {
        if (!string.IsNullOrWhiteSpace(folder)) roots.Add(folder!.Trim().Trim('"'));
    }

    /// <summary>The folder of a command line / icon spec (an exe path with an icon index, or a quoted exe path with arguments).</summary>
    internal static string? FolderOfCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        string text = command!.Trim();
        if (text.StartsWith("\"", StringComparison.Ordinal))
        {
            int close = text.IndexOf('"', 1);
            text = close > 0 ? text.Substring(1, close - 1) : text.Substring(1);
        }
        else
        {
            int comma = text.LastIndexOf(',');
            if (comma > 0 && int.TryParse(text.Substring(comma + 1), out _)) text = text.Substring(0, comma);
        }
        try { return Path.GetDirectoryName(text); } catch (Exception) { return null; }
    }
}
