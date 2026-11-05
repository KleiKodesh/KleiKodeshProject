#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KitveiHakodesh.Core.LocalFiles.ToratEmet;

namespace KitveiHakodesh.Core.UserSpace;

/// <summary>Result of <c>claimDefaultUserSpaceFolders</c>: computer folders UserSpace should add as roots now.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class DefaultUserSpaceFolders
{
    public List<string> Folders { get; set; } = new();
}

/// <summary>
/// Folders UserSpace has by default: the Torat Emet books (<see cref="ToratEmetFolders"/>) when they exist on this computer.
/// A folder is handed out ONCE: the first claim that finds it records it in the state file and returns it; the host adds it as a
/// root. Later claims never return it again, so a folder the user removed on purpose does not come back, and one the user added by
/// hand is not added twice (the host's root list ignores a duplicate; the folder is simply recorded as handed out).
/// State: plain text, one lower-cased full path per line, in <see cref="DefaultStateFile"/> (beside the other app data).
/// </summary>
public static class UserSpaceDefaultFolders
{
    public static string DefaultStateFile => Path.Combine(Common.AppFileLocator.DataRoot, "userspace-default-folders.txt");

    private static readonly object Gate = new();

    public static DefaultUserSpaceFolders Claim(string? stateFile = null, ToratEmetFolders.Probe? probe = null)
    {
        var result = new DefaultUserSpaceFolders();
        stateFile ??= DefaultStateFile;
        lock (Gate)
        {
            var located = ToratEmetFolders.Locate(probe);
            if (located.Count == 0) return result;
            var handedOut = ReadState(stateFile);
            var fresh = located.Where(f => !handedOut.Contains(f.ToLowerInvariant())).ToList();
            if (fresh.Count == 0) return result;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
                File.AppendAllLines(stateFile, fresh.Select(f => f.ToLowerInvariant()));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return result;   // state not saved: hand nothing out (it would come back after a removal)
            }
            result.Folders.AddRange(fresh);
        }
        return result;
    }

    private static HashSet<string> ReadState(string stateFile)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        try { if (File.Exists(stateFile)) foreach (var line in File.ReadAllLines(stateFile)) if (line.Length > 0) set.Add(line.Trim()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return set;
    }
}
