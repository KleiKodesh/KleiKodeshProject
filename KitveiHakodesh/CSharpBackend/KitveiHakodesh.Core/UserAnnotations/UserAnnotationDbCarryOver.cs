using System;
using System.IO;

namespace KitveiHakodesh.Core.UserAnnotations;

/// <summary>
/// The user's highlights and notes live in <c>Settings\user_settings.db</c> NEXT TO the seforim
/// database (<see cref="UserAnnotationStore.DeriveDatabasePath"/>), so switching libraries would
/// leave them behind in the old folder. When the new library has no settings database yet, the
/// old one is copied over so the user keeps their annotations. An existing file at the
/// destination is never overwritten: that library already has its own history.
/// </summary>
public static class UserAnnotationDbCarryOver
{
    /// <summary>Copies the annotations database from the previous library to the new one when
    /// the new one has none. Returns the (source, destination) pair when a copy was made, null
    /// when there was nothing to do. Throws on an IO failure — the caller decides whether that
    /// blocks the switch (it should not).</summary>
    public static (string Source, string Destination)? CopyIfDestinationMissing(
        string? previousSeforimDbPath, string? newSeforimDbPath)
    {
        if (string.IsNullOrWhiteSpace(previousSeforimDbPath) || string.IsNullOrWhiteSpace(newSeforimDbPath)) return null;

        string source = UserAnnotationStore.DeriveDatabasePath(previousSeforimDbPath!);
        string destination = UserAnnotationStore.DeriveDatabasePath(newSeforimDbPath!);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) return null;
        if (!File.Exists(source) || File.Exists(destination)) return null;

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination);
        return (source, destination);
    }
}
