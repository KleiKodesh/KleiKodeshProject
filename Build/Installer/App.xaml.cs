using System;
using System.Reflection;
using System.Windows;

namespace KleiKodeshVstoInstallerWpf
{
    public partial class App : Application
    {
        // Wire up assembly resolver before anything else loads
        static App()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbeddedAssembly;
        }

        private static Assembly ResolveEmbeddedAssembly(object sender, ResolveEventArgs args)
        {
            string shortName = new AssemblyName(args.Name).Name + ".dll";
            var asm = Assembly.GetExecutingAssembly();

            string resourceName = Array.Find(
                asm.GetManifestResourceNames(),
                r => r.EndsWith(shortName, StringComparison.OrdinalIgnoreCase));

            if (resourceName == null) return null;

            using (var stream = asm.GetManifestResourceStream(resourceName))
            {
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                return Assembly.Load(bytes);
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // When built with -p:ForceCleanInstall=true, the installer always launches
            // in repair mode — wipes existing files/registry then installs fresh.
            // The "התקן" button on LandingPage therefore behaves exactly like "תיקון".
#if FORCE_CLEAN_INSTALL
            bool repairMode  = true;
#else
            bool repairMode  = false;
#endif
            int  waitForPid  = 0;

            foreach (string arg in e.Args)
            {
                // --silent / --install are accepted and intentionally IGNORED. The
                // auto-updater still passes --silent (it must — the exe on disk predates
                // this code and older builds only understand that flag), but an update now
                // shows the normal wizard. See the note above the window construction below
                // for why a headless install is not viable here.

                if (arg.Equals("--repair", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("/repair",  StringComparison.OrdinalIgnoreCase))
                    repairMode = true;
            }

            // --payload <path>: explicit payload-archive location. Passed by
            // AdminHelper.RelaunchAsAdmin so the elevated instance can still find the
            // archive the NSIS wrapper staged — an elevated process resolves %TEMP% to
            // a different profile, and only the exe crosses the UAC boundary, not the
            // sibling .pkg. Without it, ניקוי עמוק fails with "Payload archive not found".
            for (int i = 0; i < e.Args.Length - 1; i++)
            {
                if (e.Args[i].Equals(Helpers.PayloadArchive.PathSwitch, StringComparison.OrdinalIgnoreCase))
                {
                    Helpers.AddinInstaller.PayloadPathOverride = e.Args[i + 1];
                    break;
                }
            }

            // --wait-for-pid <PID>: hide until the given process exits, then show normally.
            // Used by the auto-updater: installer is launched from Word's shutdown event
            // while Word is still alive, then waits for Word to fully exit before showing UI.
            for (int i = 0; i < e.Args.Length - 1; i++)
            {
                if (e.Args[i].Equals("--wait-for-pid", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(e.Args[i + 1], out waitForPid);
                    break;
                }
            }

            if (waitForPid > 0)
            {
                // Wait on a background thread — don't block the UI thread.
                // The wait is bounded: PIDs are recycled, so if the id now belongs to
                // some unrelated long-lived process the wizard must not stay hidden
                // forever. 30s covers even a slow Word/app teardown.
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var proc = System.Diagnostics.Process.GetProcessById(waitForPid);
                        proc.WaitForExit(30_000);
                    }
                    catch { /* process already gone */ }

                    // Now show the window on the UI thread
                    Dispatcher.Invoke(() => MainWindow?.Show());
                });
            }

            // "--silent" no longer suppresses anything: an auto-update shows the normal
            // wizard and the user drives it, exactly as if they had run the installer.
            //
            // Two reasons it cannot be silent, and they compound:
            //
            //   1. The install overwrites files that Word (VSTO) and כתבי הקודש hold open,
            //      so BOTH must be closed first. The only place to enforce that usefully is
            //      the התקן click: LandingPage waits for them, and
            //      EnsureWordClosed()/EnsureKitveiHakodeshClosed() can tell the user what to
            //      close and let them retry. A headless installer can only wait blindly and
            //      give up, which is how a partial extraction over locked files happens.
            //   2. Registering the elevated index service raises a UAC prompt. Raised from a
            //      hidden process it had no visible parent and appeared minutes after the
            //      user closed Word, so it read as a dialog from nowhere and got dismissed.
            //
            // The flag is still accepted, and the updater still passes it, because the exe
            // on disk is always an OLDER release than the code launching it — DownloadManager
            // must not pass arguments older builds don't understand. So the meaning changed
            // here, in the installer, rather than by adding a flag old builds would ignore.
            //
            // Net effect: --silent takes the same path as a manual launch. Do not
            // reintroduce a headless branch here.

            MainWindow mainWindow = new MainWindow();
            if (repairMode)
                mainWindow.NavigateToRepairOnLoad();

            // If waiting for a pid, start hidden — the background task above will show it
            if (waitForPid > 0)
                mainWindow.Visibility = System.Windows.Visibility.Hidden;
            else
                mainWindow.Show();
        }
    }
}
