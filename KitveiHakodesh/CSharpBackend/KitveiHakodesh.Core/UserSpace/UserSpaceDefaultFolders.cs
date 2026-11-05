#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using KitveiHakodesh.Core.LocalFiles.ToratEmet;

namespace KitveiHakodesh.Core.UserSpace;

/// <summary>Result of <c>claimDefaultUserSpaceFolders</c>: computer folders UserSpace should add as roots now.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class DefaultUserSpaceFolders
{
    public List<string> Folders { get; set; } = new();
}

/// <summary>Result of <c>userSpaceRootTitles</c>: the Hebrew name of each asked folder that is a default root, "" for the others (parallel).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class UserSpaceRootTitles
{
    public List<string> Titles { get; set; } = new();
}

/// <summary>
/// Folders UserSpace has by default: the Torat Emet books (<see cref="ToratEmetFolders"/>) when they exist on this computer.
/// This only FINDS them; it keeps no state. Which of them the host has already added is the host's to remember, in the SAME store
/// as the user's roots (the frontend's app-user-space database), so every reset that clears the roots also clears that memory and
/// the defaults come back, while a folder the user removes by hand stays removed until such a reset.
/// </summary>
public static class UserSpaceDefaultFolders
{
    public static DefaultUserSpaceFolders Find(ToratEmetFolders.Probe? probe = null, CancellationToken ct = default)
    {
        var result = new DefaultUserSpaceFolders();
        result.Folders.AddRange(ToratEmetFolders.Locate(probe, ct));
        return result;
    }

    /// <summary>The names to show for the user's computer roots (parallel to <paramref name="folders"/>): the Hebrew name of a default root, else "".</summary>
    public static UserSpaceRootTitles Titles(IEnumerable<string> folders, ToratEmetFolders.Probe? probe = null, CancellationToken ct = default)
    {
        var result = new UserSpaceRootTitles();
        foreach (var folder in folders) result.Titles.Add(ToratEmetFolders.RootTitle(folder, probe, ct));
        return result;
    }
}
