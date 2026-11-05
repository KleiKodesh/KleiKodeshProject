#nullable enable
using System;
using System.Collections.Generic;

namespace KitveiHakodesh.Core.LocalFiles.ToratEmet;

/// <summary>Result of <c>displayLocalPaths</c>: parallel to the asked paths, "" = show the stored value unchanged.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LocalPathDisplayResult
{
    public List<string> Titles { get; set; } = new();
    public List<string> Paths { get; set; } = new();
}

/// <summary>
/// How a recent file or folder is shown (home history, address bar recents): the Hebrew title and the path line, the same names the
/// file search and the user-space page show. Under an install's Books the path starts with the Books root name and carries the
/// Hebrew folder names; under ToratEmetUserData\MyBooks it starts with the MyBooks root name and the rest stays as on disk.
/// Display only: the stored entry is never rewritten. "" for a path that is not Torat Emet.
/// </summary>
public static class LocalPathDisplay
{
    public static LocalPathDisplayResult Resolve(IReadOnlyList<string> paths, IReadOnlyList<bool> isFolder)
    {
        var result = new LocalPathDisplayResult();
        for (int i = 0; i < paths.Count; i++)
        {
            var (title, path) = One(paths[i], i < isFolder.Count && isFolder[i]);
            result.Titles.Add(title);
            result.Paths.Add(path);
        }
        return result;
    }

    public static (string Title, string Path) One(string? fullPath, bool isFolder)
    {
        if (!ToratEmetNames.IsToratEmetPath(fullPath)) return ("", "");
        var segs = fullPath!.Replace('/', (char)92).Split(new[] { (char)92 }, StringSplitOptions.RemoveEmptyEntries);
        // Same rule as ToratEmetNames.SegmentsUnderInstallBooks: the install is the first ToratEmet folder that is not UserData.
        bool userData = false;
        string root = "";
        int rootAt = -1;
        int install = -1;
        for (int i = 0; i < segs.Length && install < 0; i++)
            if (segs[i].IndexOf("ToratEmet", StringComparison.OrdinalIgnoreCase) >= 0
                && segs[i].IndexOf("UserData", StringComparison.OrdinalIgnoreCase) < 0) install = i;
        if (install >= 0)
            for (int i = install + 1; i < segs.Length && rootAt < 0; i++)
                if (string.Equals(segs[i], "Books", StringComparison.OrdinalIgnoreCase)) { rootAt = i; root = ToratEmetFolders.BooksRootTitle; }
        if (rootAt < 0)
            for (int i = 0; i + 1 < segs.Length && rootAt < 0; i++)
                if (segs[i].IndexOf("ToratEmet", StringComparison.OrdinalIgnoreCase) >= 0
                    && segs[i].IndexOf("UserData", StringComparison.OrdinalIgnoreCase) >= 0
                    && string.Equals(segs[i + 1], "MyBooks", StringComparison.OrdinalIgnoreCase)) { rootAt = i + 1; root = ToratEmetFolders.MyBooksRootTitle; userData = true; }
        if (rootAt < 0) return ("", "");
        int restLen = segs.Length - rootAt - 1;
        if (restLen == 0) return (isFolder ? root : "", root);
        var shown = new string[restLen];
        string? bookTitle = isFolder || userData ? null : ToratEmetNames.FileTitle(fullPath);
        for (int i = 0; i < restLen; i++)
        {
            string raw = segs[rootAt + 1 + i];
            shown[i] = !userData && ToratEmetNames.Data.Value.FolderTitles.TryGetValue(ToratEmetNames.FolderToken(raw), out var t) && (isFolder || i < restLen - 1) ? t : raw;
        }
        if (bookTitle != null) shown[restLen - 1] = bookTitle;   // the path ends with the item as shown, like a folder does
        string line = root + "\\" + string.Join("\\", shown);
        return (userData ? "" : ToratEmetNames.Title(fullPath, isFolder) ?? "", line);
    }
}
