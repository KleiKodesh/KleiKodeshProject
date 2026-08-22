using System;
using System.IO;
using System.Threading.Tasks;

namespace KleiKodeshVstoInstallerWpf.Helpers
{
    /// <summary>
    /// The install sequence itself, independent of any UI. Driven by InstallPage, which is
    /// the only caller — auto-updates included, since --silent no longer has a headless
    /// path. Assumes Word and כתבי הקודש are already closed; LandingPage enforces that.
    /// Throws on failure; callers decide how to surface or swallow errors.
    /// </summary>
    public static class InstallRunner
    {
        public static async Task RunAsync(IProgress<double> progress, IProgress<string> status)
        {
            // NOTE: this assumes Word and כתבי הקודש are already closed. Enforcing that is
            // LandingPage's job — its constructor waits for them and the התקן click calls
            // EnsureWordClosed()/EnsureKitveiHakodeshClosed(), which can actually tell the
            // user what to close. Do not re-add a wait here; there is no useful way to
            // stall from this far down, and duplicating the check just hides where the real
            // gate is.
            if (!Directory.Exists(AddinInstaller.InstallPath))
                Directory.CreateDirectory(AddinInstaller.InstallPath);

            // Retire services this product no longer ships, before extraction — a
            // registered service holds a lock on its exe that would fail the extract.
            // No-op (one registry probe) once a machine has been cleaned up, so this is
            // safe on every install. Runs elevated like the rest of the installer, so it
            // needs no prompting of its own — see LegacyServiceRetirement.
            await LegacyServiceRetirement.RetireAllAsync(status);

            // Send pipe shutdown to DocumentLocator service immediately so the
            // 1 500 ms exit window runs in the background while we extract other
            // files. AddinInstaller will wait for the remainder of that window
            // (if any) before it tries to overwrite DocumentLocator.Service.exe.
            // Harmless once DocumentLocator is retired: the pipe simply isn't there.
            _ = DocumentLocatorHelper.SendShutdownAsync();

            status?.Report("מחלץ קבצים...");
            await AddinInstaller.ExtractAsync(progress);

            status?.Report("רושם תוסף...");
            await AddinInstaller.RegisterAddInAsync(progress);

            status?.Report("שומר גרסה...");
            AddinInstaller.SaveVersion();

            status?.Report("יוצר קיצור דרך...");
            AddinInstaller.CreateKitveiHakodeshShortcut();

            // Claim the kitveihakodeshapp:// scheme so deep links copied from the app
            // open in it. Unconditional, unlike the "Open with" preference on
            // ComponentSettingsPage: that one claims other applications' file types,
            // this is the app opening its own links.
            ShellRegistrationHelper.RegisterProtocol();

            // Register (or re-register) the DocumentLocator Windows Service.
            // Normally a no-op on updates (already registered at the same path);
            // when registration IS needed it surfaces one UAC prompt.
            status?.Report("מתקין שירות אינדקס...");
            await DocumentLocatorHelper.EnsureServiceInstalledAsync();

            // Trigger a background reindex of the file-system search service
            // so it reflects any new files from this install. Fire-and-forget —
            // the service acks immediately and rebuilds without blocking us.
            _ = DocumentLocatorHelper.EnsureServiceRunningAndReindexAsync();
        }
    }
}
