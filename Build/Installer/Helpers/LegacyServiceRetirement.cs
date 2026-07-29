using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace KleiKodeshVstoInstallerWpf.Helpers
{
    /// <summary>
    /// Removes Windows services that this product used to ship but no longer does.
    ///
    /// TRANSITIONAL — this whole class exists for the DocumentLocatorSvc →
    /// KitveiHakodeshService migration and is meant to be deleted, not maintained. Once no
    /// supported upgrade path can still have a retired service registered (keep the entry
    /// for several releases so machines that skip versions are still cleaned up), remove
    /// the file and its call in InstallRunner. Nothing else depends on it.
    ///
    /// Why this exists separately from <see cref="DocumentLocatorHelper"/>:
    /// that helper delegates registration to the service exe itself
    /// (<c>DocumentLocator.Service.exe --install</c>). That approach cannot retire a
    /// service, because on the very update that drops a service the exe is no longer
    /// in the package — there is nothing left to run <c>--uninstall</c>. Worse, the
    /// copy already on disk gets deleted by the extract/clean step, so any teardown
    /// that shells out to it is racing its own removal.
    ///
    /// So retirement talks to the SCM directly, from inside the installer process —
    /// no dependency on the retired binary existing at all.
    ///
    /// ── Elevation ─────────────────────────────────────────────────────────────────
    ///
    /// This usually runs NON-elevated, and deregistering a service needs admin rights,
    /// so retirement is opportunistic: it happens when the process already has the
    /// rights (the repair flow), and is skipped otherwise.
    ///
    /// It must stay that way. The installer is <c>asInvoker</c> because the install is
    /// per-user — VSTO registers under HKCU and the WebView2 webcache dir needs runtime
    /// write access as the real user — and elevating this process would make
    /// %LOCALAPPDATA% and HKCU resolve against the approving admin, sending the whole
    /// install to the wrong profile. So do NOT add a UAC prompt or a <c>runas</c> child
    /// here; see the notes in app.manifest and Build/nsis/KleiKodeshWrapper.nsi.
    /// Prompting for *cleanup* would also be poor behaviour in its own right — it buys
    /// the user nothing.
    ///
    /// Skipping is safe because of what the retired service is: registered
    /// <c>SERVICE_DEMAND_START</c> and started only when a client opens its named pipe.
    /// Once the release that drops it stops opening that pipe, the registration is
    /// inert — it never runs, holds no file lock, uses no memory, and does not appear
    /// at boot. A stale SCM entry is the entire remaining cost.
    ///
    /// ── Ordering ──────────────────────────────────────────────────────────────────
    ///
    /// Run BEFORE extraction. While the old release is still installed the service can
    /// be started by its client, and a running service holds a lock on its exe that
    /// would fail the extract step's overwrite.
    ///
    /// Everything here is best-effort and non-throwing. Cleanup of the old world must
    /// never block installation of the new one.
    /// </summary>
    public static class LegacyServiceRetirement
    {
        /// <summary>
        /// Services this product no longer ships. Add an entry here when a service is
        /// dropped; leave it in place for several releases so machines that skip
        /// versions still get cleaned up on whatever update they land on.
        ///
        /// Keep the exe name in sync with the service's real ImagePath — it is used to
        /// distinguish "our" registration from an unrelated service that happens to
        /// share the name, so we never delete something that isn't ours.
        /// </summary>
        private static readonly RetiredService[] Retired =
        {
            // DocumentLocatorSvc — superseded by KitveiHakodeshService, which performs
            // the same path indexing in-process and needs no Windows service at all.
            // Retained here (not deleted) so users updating from any older build get
            // the stale registration cleaned up.
            new RetiredService("DocumentLocatorSvc", "DocumentLocator.Service.exe"),
        };

        private sealed class RetiredService
        {
            public readonly string ServiceName;
            public readonly string ExeName;

            public RetiredService(string serviceName, string exeName)
            {
                ServiceName = serviceName;
                ExeName     = exeName;
            }
        }

        // ── Win32 SCM ─────────────────────────────────────────────────────────────

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenSCManager(
            string lpMachineName, string lpDatabaseName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenService(
            IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool DeleteService(IntPtr hService);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr hSCObject);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool ControlService(
            IntPtr hService, uint dwControl, ref SERVICE_STATUS lpServiceStatus);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool QueryServiceStatus(
            IntPtr hService, ref SERVICE_STATUS lpServiceStatus);

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_STATUS
        {
            public uint dwServiceType;
            public uint dwCurrentState;
            public uint dwControlsAccepted;
            public uint dwWin32ExitCode;
            public uint dwServiceSpecificExitCode;
            public uint dwCheckPoint;
            public uint dwWaitHint;
        }

        private const uint SC_MANAGER_CONNECT   = 0x0001;
        private const uint SERVICE_STOP         = 0x0020;
        private const uint SERVICE_QUERY_STATUS = 0x0004;
        private const uint DELETE               = 0x00010000;

        private const uint SERVICE_CONTROL_STOP = 0x00000001;
        private const uint SERVICE_STOPPED      = 0x00000001;
        private const uint SERVICE_STOP_PENDING = 0x00000003;

        private const int ERROR_SERVICE_NOT_ACTIVE     = 1062;
        private const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;

        private const int StopTimeoutMs = 10_000;
        private const int StopPollMs    = 250;

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Stops and deregisters every service in <see cref="Retired"/> that is still
        /// registered on this machine, then removes its leftover files from the
        /// install folder.
        ///
        /// Call before extraction. Safe to call on every install: services that were
        /// never registered, or were already cleaned up by a previous run, cost one
        /// cheap registry probe each and are skipped.
        ///
        /// Deregisters only when this process already has admin rights; otherwise stops
        /// the service (which needs no elevation) and leaves the registration for a
        /// later elevated run. Never prompts — see the elevation notes on this class.
        ///
        /// Never throws.
        /// </summary>
        public static async Task RetireAllAsync(IProgress<string> status = null)
        {
            foreach (var svc in Retired)
            {
                try
                {
                    if (!IsRegistered(svc.ServiceName)) continue;

                    // Stopping needs no elevation — the service grants authenticated
                    // users pipe access, and a cooperative shutdown lets it flush its
                    // index and release its exe lock. Worth doing even when we cannot
                    // deregister, because it is the file lock that breaks extraction.
                    await SendCooperativeShutdownAsync(svc.ServiceName).ConfigureAwait(false);

                    if (!AdminHelper.IsElevated) continue; // inert; a later elevated run gets it

                    status?.Report("מסיר שירות ישן...");

                    // Resolve the exe location before deregistering — afterwards the
                    // ImagePath is gone and we would lose track of what to clean up.
                    string exePath = GetImagePathIfUnderInstallDir(
                        svc.ServiceName, svc.ExeName, AddinInstaller.InstallPath);

                    bool removed = await Task.Run(() => StopAndDelete(svc.ServiceName))
                        .ConfigureAwait(false);

                    // Only delete binaries once the registration is confirmed gone —
                    // otherwise we would strand a registered service pointing at a
                    // missing exe, which is strictly worse than leaving both in place.
                    if (removed)
                        RemoveLeftoverFiles(svc.ExeName, exePath);
                }
                catch
                {
                    // Best-effort: never block the update on cleanup of the old version.
                }
            }
        }

        /// <summary>
        /// Whether this installer variant can host KitveiHakodeshService, the
        /// successor to DocumentLocatorSvc.
        ///
        /// That service is published as a native x64 AOT binary — there is no x86 or
        /// architecture-neutral build of it, and there will not be one. So the x86 and
        /// AnyCPU installer variants must never deploy or register it: on x86 the
        /// binary cannot load at all, and AnyCPU makes no guarantee about the host
        /// bitness it would end up running under.
        ///
        /// Retirement of the old service is deliberately NOT gated on this. Dropping a
        /// stale DocumentLocatorSvc is correct on every variant, including the ones
        /// that get no replacement — leaving a dead service registered would be worse
        /// than having no indexing service.
        /// </summary>
        public static bool SupportsKitveiHakodeshService =>
            string.Equals(AddinInstaller.InstallerVariant, "x64", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Returns true if any retired service is still registered — useful for
        /// deciding whether this install needs to run elevated at all.
        /// Reads HKLM only; requires no elevation.
        /// </summary>
        public static bool AnyStillRegistered()
        {
            foreach (var svc in Retired)
            {
                try
                {
                    if (IsRegistered(svc.ServiceName)) return true;
                }
                catch { /* treat as absent */ }
            }
            return false;
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Returns true if <paramref name="serviceName"/> exists in the SCM registry.
        /// </summary>
        private static bool IsRegistered(string serviceName)
        {
            using (var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\" + serviceName))
                return key != null;
        }

        /// <summary>
        /// Reads the registered ImagePath so leftover-file cleanup targets the exe the
        /// SCM actually pointed at, rather than assuming the current install folder.
        /// Returns null when unavailable.
        /// </summary>
        private static string GetImagePath(string serviceName)
        {
            using (var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\" + serviceName))
            {
                if (key == null) return null;
                string path = (key.GetValue("ImagePath") as string ?? "").Trim().Trim('"');
                return path.Length == 0 ? null : path;
            }
        }

        /// <summary>
        /// Sends the DocumentLocator-style <c>{"type":"shutdown"}</c> frame if the
        /// service still exposes its named pipe. Unknown/absent pipes are ignored.
        ///
        /// This reuses <see cref="DocumentLocatorHelper.SendShutdownAsync"/> for the
        /// one service whose protocol we know, rather than duplicating frame I/O.
        /// Future retired services can add their own branch here, or simply rely on
        /// the SCM stop below if they have no cooperative shutdown channel.
        /// </summary>
        private static async Task SendCooperativeShutdownAsync(string serviceName)
        {
            if (string.Equals(serviceName, "DocumentLocatorSvc", StringComparison.OrdinalIgnoreCase))
                await DocumentLocatorHelper.SendShutdownAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Issues an SCM stop (waiting for the transition to complete) and then
        /// deletes the registration. Both steps tolerate the service having already
        /// stopped or already been removed.
        ///
        /// Returns true once the registration is actually gone.
        ///
        /// Note on DeleteService semantics — verified against a real registration:
        /// it returns TRUE while merely *marking* the service for deletion, and the
        /// registry key survives until the last open handle to that service closes.
        /// So its return value proves nothing about removal, and the confirming probe
        /// must run after CloseServiceHandle — never inside the using/try that still
        /// holds the handle. Getting this wrong makes retirement look like it failed
        /// (and, worse, invites a pointless retry loop).
        /// </summary>
        private static bool StopAndDelete(string serviceName)
        {
            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero) return false; // SCM unavailable

            try
            {
                IntPtr svc = OpenService(
                    scm, serviceName, SERVICE_STOP | SERVICE_QUERY_STATUS | DELETE);

                if (svc == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    // Already gone counts as success; anything else (typically
                    // ERROR_ACCESS_DENIED when not elevated) is a real failure.
                    return err == ERROR_SERVICE_DOES_NOT_EXIST;
                }

                try
                {
                    StopAndWait(svc);
                    DeleteService(svc); // return value is not proof — see remarks above
                }
                finally { CloseServiceHandle(svc); }
            }
            finally { CloseServiceHandle(scm); }

            // Handles are closed, so any deferred deletion has now taken effect.
            return !IsRegistered(serviceName);
        }

        /// <summary>
        /// Sends SERVICE_CONTROL_STOP and polls until the service reports STOPPED or
        /// <see cref="StopTimeoutMs"/> elapses.
        ///
        /// DeleteService succeeds even on a still-running service (the removal is
        /// deferred until it exits), but then the exe stays locked and extraction
        /// fails — so waiting here is what actually protects the install.
        /// </summary>
        private static void StopAndWait(IntPtr svc)
        {
            var st = new SERVICE_STATUS();

            if (!ControlService(svc, SERVICE_CONTROL_STOP, ref st))
            {
                int err = Marshal.GetLastWin32Error();
                // Already stopped or already gone — nothing to wait for.
                if (err == ERROR_SERVICE_NOT_ACTIVE || err == ERROR_SERVICE_DOES_NOT_EXIST)
                    return;
                // Any other failure: fall through and still attempt deletion.
            }

            int waited = 0;
            while (waited < StopTimeoutMs)
            {
                var cur = new SERVICE_STATUS();
                if (!QueryServiceStatus(svc, ref cur)) return;
                if (cur.dwCurrentState == SERVICE_STOPPED) return;
                if (cur.dwCurrentState != SERVICE_STOP_PENDING) return; // not going to stop

                Thread.Sleep(StopPollMs);
                waited += StopPollMs;
            }
        }

        /// <summary>
        /// Deletes the retired service's exe (and its .pdb) from the install folder so
        /// the next run's <c>IsRegistered</c>/exe-exists probes agree, and so a stale
        /// binary cannot be launched by anything left pointing at it.
        ///
        /// Uses the registered ImagePath when it still resolves inside our install
        /// folder; otherwise falls back to the expected install-folder location. Files
        /// outside the install folder are never touched.
        /// </summary>
        private static void RemoveLeftoverFiles(string exeName, string registeredExePath)
        {
            string installDir = AddinInstaller.InstallPath;

            foreach (string candidate in new[]
            {
                registeredExePath,
                Path.Combine(installDir, exeName),
            })
            {
                if (string.IsNullOrEmpty(candidate)) continue;
                TryDelete(candidate);
                TryDelete(Path.ChangeExtension(candidate, ".pdb"));
            }
        }

        /// <summary>
        /// Returns the service's registered ImagePath only when it names the expected
        /// exe and lives under <paramref name="installDir"/>. Guards against deleting
        /// an unrelated binary if the registration was hijacked or hand-edited.
        /// </summary>
        private static string GetImagePathIfUnderInstallDir(
            string serviceName, string exeName, string installDir)
        {
            string imagePath = GetImagePath(serviceName);
            if (string.IsNullOrEmpty(imagePath)) return null;

            // An ImagePath can carry arguments; keep only the executable itself.
            if (!string.Equals(
                    Path.GetFileName(imagePath), exeName, StringComparison.OrdinalIgnoreCase))
                return null;

            try
            {
                string full = Path.GetFullPath(imagePath);
                string root = Path.GetFullPath(installDir)
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return full;
            }
            catch { /* malformed path — ignore */ }

            return null;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // Still locked, or access denied — harmless leftover.
            }
        }
    }
}
