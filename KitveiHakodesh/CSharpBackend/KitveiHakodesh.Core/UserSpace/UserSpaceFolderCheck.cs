using KitveiHakodesh.Core.LocalFiles;

namespace KitveiHakodesh.Core.UserSpace;

/// <summary>Result of checking a folder the user wants as a UserSpace root
/// (checkUserSpaceFolder wire shape).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class UserSpaceFolderCheckResult
{
    /// <summary>True when the file index covers the folder, so its files can be searched.</summary>
    public bool Covered { get; set; }
    /// <summary>"" when covered; else "notFound", "notIndexedDrive", "excluded" or "systemFolder".</summary>
    public string Reason { get; set; } = "";
}

/// <summary>
/// Whether the local file index covers a folder: it exists, sits on a drive the index crawls
/// (NTFS), is not under one of the user's excluded folders, and has no segment the crawler
/// skips (system folders). Same rules the index itself applies, read from the same places.
/// </summary>
public static class UserSpaceFolderCheck
{
    public static UserSpaceFolderCheckResult Check(string folder, IList<string> excludedFolders)
    {
        string full;
        try
        {
            if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathRooted(folder)) return NotCovered("notFound");
            full = Path.GetFullPath(folder).TrimEnd('\\', '/');
            if (!Directory.Exists(full)) return NotCovered("notFound");
        }
        catch
        {
            return NotCovered("notFound");
        }

        string root = Path.GetPathRoot(full) ?? "";
        bool onIndexedDrive = false;
        foreach (var drive in LocalFileIndexMftCrawler.GetNtfsDrives())
            if (string.Equals(drive.Name.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            { onIndexedDrive = true; break; }
        if (!onIndexedDrive) return NotCovered("notIndexedDrive");

        if (LocalFileIndexExcludedFolders.IsExcluded(full, excludedFolders)) return NotCovered("excluded");

        // Every segment below the drive root, the folder's own name included.
        foreach (var segment in full.Substring(root.Length).Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
            if (LocalFileIndexMftCrawler.IsSkippedDirectoryName(segment)) return NotCovered("systemFolder");

        return new UserSpaceFolderCheckResult { Covered = true };
    }

    private static UserSpaceFolderCheckResult NotCovered(string reason) => new() { Reason = reason };
}
