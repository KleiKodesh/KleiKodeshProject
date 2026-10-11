using KitveiHakodesh.Core.UserAnnotations;

namespace KitveiHakodeshService.SeforimDb;

/// <summary>
/// The user's highlights and notes live in <c>Settings\user_settings.db</c> NEXT TO the seforim
/// database (UserAnnotationStore.DeriveDatabasePath), so switching libraries would leave them
/// behind in the old folder. When the new library has no settings database yet, the old one is
/// copied over so the user keeps their annotations. An existing file at the destination is
/// never overwritten: that library already has its own history.
///
/// Runs inside the seforim-DB-path ops, BEFORE the service restarts onto the new database.
/// </summary>
internal static class UserSettingsCarryOver
{
    public static void CopyIfDestinationMissing(string? previousSeforimDbPath, string newSeforimDbPath, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(previousSeforimDbPath) || string.IsNullOrWhiteSpace(newSeforimDbPath)) return;
        try
        {
            string source = UserAnnotationStore.DeriveDatabasePath(previousSeforimDbPath);
            string destination = UserAnnotationStore.DeriveDatabasePath(newSeforimDbPath);
            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) return;
            if (!File.Exists(source) || File.Exists(destination)) return;

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
            logger.LogInformation("Copied user settings {Source} -> {Destination}", source, destination);
        }
        catch (Exception ex)
        {
            // Never block the library switch over this; the user keeps the old file in place.
            logger.LogWarning(ex, "Could not carry user settings over to the new library");
        }
    }
}
