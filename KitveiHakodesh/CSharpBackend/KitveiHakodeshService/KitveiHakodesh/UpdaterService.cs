using KitveiHakodesh.Core.Settings;
using KitveiHakodeshService.Hosting;

namespace KitveiHakodeshService.KitveiHakodesh;

/// <summary>
/// Self-update, owned by the service (the hosts no longer check or download anything).
///
/// Once a week: ask GitHub for the latest release; when it is newer than the installed
/// version, download its installer (or reuse it when that exact file is already here). It is
/// NOT launched here: the app asks the service whether an update is ready (getUpdateStatus),
/// asks the user, and on "install now" the service starts the installer as the logged-on user
/// on their desktop (installUpdate). The installer then asks to close Word and the app.
///
/// Only the INSTALLED Windows service does this. A console instance (dev, a portable copy,
/// one spawned per host) never updates: the installer installs into the user's profile and
/// registers the Word add-in, which a copy someone merely unzipped must never trigger.
///
/// The service is demand-start and may not run for days, so "weekly" is a stamp on disk,
/// not a timer: every start (and every few hours while running) compares it with now.
/// </summary>
internal sealed class UpdaterService(Updater updater, ILogger<UpdaterService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(7);
    private static readonly TimeSpan WakeInterval = TimeSpan.FromHours(6);
    /// <summary>Lets the service settle (index builds, first requests) before any network work.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);

    private static string StampPath => Path.Combine(Updater.DownloadFolder, "last-check.txt");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!WindowsServiceMode.IsWindowsService) return;

        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (IsDue()) await RunPassAsync(stoppingToken);
            try { await Task.Delay(WakeInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task RunPassAsync(CancellationToken ct)
    {
        // The shared "turn off automatic updates" switch (the Word add-in and the settings page
        // write the same value). Not stamped: turning it back on checks at the next wake.
        if (AppSettingsRegistry.GetTurnOffUpdates())
        {
            logger.LogInformation("Update check skipped: automatic updates are turned off");
            return;
        }

        try
        {
            // Download only. Installing is the user's decision: the app asks the service whether
            // an update is ready (getUpdateStatus), asks the user, and only then installUpdate
            // launches the installer.
            string? installer = await updater.FindOrDownloadNewerInstallerAsync(ct);
            if (installer is not null)
                logger.LogInformation("Update ready to install: {Installer}", installer);
            // Stamped only once the pass fully succeeded; a failed one retries at the next wake.
            WriteStamp();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Offline, GitHub down, a partial download: not stamped, so the next wake retries.
            logger.LogWarning(ex, "Update check failed");
        }
    }

    private static bool IsDue()
    {
        try
        {
            if (!File.Exists(StampPath)) return true;
            string text = File.ReadAllText(StampPath).Trim();
            return !DateTime.TryParse(text, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime last)
                || DateTime.UtcNow - last >= CheckInterval;
        }
        catch { return true; }
    }

    private static void WriteStamp()
    {
        try
        {
            Directory.CreateDirectory(Updater.DownloadFolder);
            File.WriteAllText(StampPath, DateTime.UtcNow.ToString("o"));
        }
        catch { /* a missing stamp only means checking again sooner */ }
    }
}
