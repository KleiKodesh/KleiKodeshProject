#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace KitveiHakodesh.Core.LocalFiles;

/// <summary>
/// One level of a folder on disk, for UserSpace's tree: its subfolders and the files
/// the app can open (<see cref="LocalFileOpenRules.IsSupported"/>). One level only - the tree
/// asks again when the user unfolds a subfolder, so a huge drive costs nothing until opened.
/// </summary>
public static class LocalFolderListing
{
    public sealed class Listing
    {
        public List<string> Folders { get; } = new();
        public List<string> Files { get; } = new();
        /// <summary>Set when the folder itself could not be read (missing, no access).</summary>
        public string? Error { get; set; }
    }

    public static Listing List(string folder)
    {
        var result = new Listing();
        try
        {
            var dir = new DirectoryInfo(folder);
            if (!dir.Exists)
            {
                result.Error = "notFound";
                return result;
            }
            foreach (var entry in dir.EnumerateFileSystemInfos())
            {
                // Hidden and system entries ($Recycle.Bin, desktop.ini...) are not the user's books.
                if ((entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                if (entry is DirectoryInfo) result.Folders.Add(entry.Name);
                else if (LocalFileOpenRules.IsSupported(entry.Name, out _)) result.Files.Add(entry.Name);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            result.Error = ex.Message;
        }
        result.Folders.Sort(StringComparer.CurrentCultureIgnoreCase);
        result.Files.Sort(StringComparer.CurrentCultureIgnoreCase);
        return result;
    }
}
