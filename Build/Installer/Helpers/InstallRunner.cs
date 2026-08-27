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
        /// <summary>
        /// Returns true when the DocumentLocator service ended up registered — or was
        /// not part of the package, which is equally fine. False means the package
        /// ships the service but registration failed (typically a declined UAC
        /// prompt): the add-in itself installed, only file search is missing, so the
        /// caller decides how to surface it rather than the install throwing.
        /// </summary>
        public static async Task<bool> RunAsync(IProgress<double> progress, IProgress<string> status)
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
            //
            // Currently a no-op: nothing is on the retirement list. DocumentLocatorSvc
            // used to be, which was wrong — EnsureServiceInstalledAsync below installs
            // that same service, so the two steps fought and left it registered but
            // unstartable. Do not re-add it without also dropping the exe from the
            // package and the install call below.
            //
            // Note this installer is asInvoker, NOT elevated (per-user install — see
            // app.manifest), so retirement can only deregister on the repair flow that
            // happens to already be elevated. It never prompts.
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
            bool serviceRegistered = await DocumentLocatorHelper.EnsureServiceInstalledAsync();

            // Trigger a background reindex of the file-system search service
            // so it reflects any new files from this install. Fire-and-forget —
            // the service acks immediately and rebuilds without blocking us.
            _ = DocumentLocatorHelper.EnsureServiceRunningAndReindexAsync();

            // EnsureServiceInstalledAsync returns false both for "registration failed"
            // and for "the package doesn't ship the service at all". Only the former
            // is a problem worth telling the user about.
            return serviceRegistered || !DocumentLocatorHelper.IsServiceDeployed;
        }
    }
}
