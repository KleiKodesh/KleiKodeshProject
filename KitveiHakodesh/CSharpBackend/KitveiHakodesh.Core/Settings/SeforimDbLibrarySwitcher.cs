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
    /// completes it on its next run.
    ///
    /// A marker younger than <see cref="InterruptedAfter"/> is an update (or a plain write
    /// transaction) IN PROGRESS, not an interrupted one — SQLite creates and deletes a
    /// -journal around every write — so it does not count.</summary>
    public static bool IsUpdateInterrupted(string? path) =>
        path is not null && (IsStaleMarker(path + "-journal") || IsStaleMarker(path + ".applying"));

    /// <summary>How long a -journal / .applying marker must sit untouched before it means an
    /// update died rather than one running right now.</summary>
    private static readonly TimeSpan InterruptedAfter = TimeSpan.FromSeconds(60);

    private static bool IsStaleMarker(string markerPath)
    {
        try
        {
            var info = new FileInfo(markerPath);
            return info.Exists && DateTime.UtcNow - info.LastWriteTimeUtc > InterruptedAfter;
        }
        catch (IOException) { return false; }          // vanished / locked mid-check: not stale
        catch (UnauthorizedAccessException) { return false; }
    }
}
