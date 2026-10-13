using System;
using System.IO;
using KitveiHakodesh.Core.UserAnnotations;

namespace KitveiHakodesh.Core.Settings;

/// <summary>
/// The rules for pointing the app at a different seforim database: normalize what the user
/// typed, refuse an empty or missing path, carry the user's annotations over to the new
/// library, then persist the choice in the shared registry value. Also answers whether the
/// current database is mid-update by another app. What a host does AFTER a switch (the service
/// restarts itself) is the host's.
/// </summary>
public static class SeforimDbLibrarySwitcher
{
    public enum SwitchOutcome
    {
        Switched,
        EmptyPath,
        FileNotFound,
    }

    /// <summary>Trims whitespace and surrounding quotes (a pasted "C:\...\seforim.db").</summary>
    public static string NormalizePath(string? rawPath) => (rawPath ?? "").Trim().Trim('"');

    /// <summary>
    /// Validates <paramref name="path"/>, carries annotations over from the current library and
    /// saves the choice. <paramref name="onCarryOverFailed"/> receives an annotation-copy failure,
    /// which never blocks the switch (the user keeps the old file in place);
    /// <paramref name="onCarriedOver"/> reports a copy that was made.
    /// </summary>
    public static SwitchOutcome SwitchTo(
        string path,
        Action<string, string>? onCarriedOver = null,
        Action<Exception>? onCarryOverFailed = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return SwitchOutcome.EmptyPath;
        if (!File.Exists(path)) return SwitchOutcome.FileNotFound;

        try
        {
            var copied = UserAnnotationDbCarryOver.CopyIfDestinationMissing(SeforimDbPathResolver.Resolve().Path, path);
            if (copied is { } c) onCarriedOver?.Invoke(c.Source, c.Destination);
        }
        catch (Exception ex)
        {
            onCarryOverFailed?.Invoke(ex);
        }

        SeforimDbPathResolver.SavePath(path);
        return SwitchOutcome.Switched;
    }

    /// <summary>True when another app's update of <paramref name="path"/> was interrupted (a
    /// rollback journal or Otzaria's .applying marker is present). Detected only, never
    /// repaired: deleting the journal would corrupt the database; Otzaria rolls it back or
    /// completes it on its next run.</summary>
    public static bool IsUpdateInterrupted(string? path) =>
        path is not null && (File.Exists(path + "-journal") || File.Exists(path + ".applying"));
}
