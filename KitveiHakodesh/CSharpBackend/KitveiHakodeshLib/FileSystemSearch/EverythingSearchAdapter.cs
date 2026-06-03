using EverythingSearchClient;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace KitveiHakodeshLib.FileSystemSearch
{
    /// <summary>
    /// Adapter wrapping EverythingSearchClient.SearchClient to provide the same API
    /// as the old custom SearchExecutor, including launch-or-install behaviour in EnsureReady.
    ///
    /// Three independent operations:
    ///   IsReady()     — non-blocking check: is Everything running and answering IPC?
    ///   EnsureReady() — locates and launches Everything if not running; offers to install
    ///                   it if not found; blocks until IPC is ready.
    ///   Search()      — executes a query and returns matching file paths.
    /// </summary>
    public class EverythingSearchAdapter
    {
        private const string DocumentExtensionFilter =
            "ext:pdf;doc;docx;dot;dotx;dotm;docm;rtf;odt;wps;xps;htm;html;mht;mhtml;txt";

        private const int PollIntervalMs  = 50;
        private const int IpcTimeoutMs    = 10_000;  // wait up to 10s for IPC window to appear
        private const int DbLoadTimeoutMs = 60_000;  // wait up to 60s for DB index to finish loading

        private readonly SearchClient _client = new SearchClient();

        /// <summary>
        /// When true, every Search() call is restricted to document file types.
        /// </summary>
        public bool FilterToDocumentTypes { get; set; } = false;

        // ── 1. IsReady ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true if Everything is running AND its index has finished loading.
        /// Never blocks, never launches anything.
        /// </summary>
        public bool IsReady()
        {
            try
            {
                return SearchClient.IsEverythingReady();
            }
            catch
            {
                return false;
            }
        }

        // ── 2. EnsureReady ────────────────────────────────────────────────────

        /// <summary>
        /// Ensures Everything is running and its IPC is ready.
        /// If not running, locates and launches Everything.exe.
        /// If not installed, offers the user a chance to install it.
        /// Blocks until IPC becomes ready or the launch timeout elapses.
        /// Call on a background thread only — may show a MessageBox on the UI thread.
        /// </summary>
        public void EnsureReady(CancellationToken cancellationToken = default)
        {
            if (IsReady())
                return;

            string exePath = FindExe();

            if (!string.IsNullOrEmpty(exePath))
            {
                Launch(exePath);
            }
            // If FindExe returned empty the user was already shown the install dialog.

            WaitUntilReady(cancellationToken);
        }

        // ── 3. Search ─────────────────────────────────────────────────────────

        /// <summary>
        /// Executes a search and returns up to <paramref name="max"/> results.
        /// Assumes EnsureReady() has already been called.
        /// </summary>
        public List<EverythingSearchResult> Search(
            string query,
            int max,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string filteredQuery = FilterToDocumentTypes
                ? (string.IsNullOrWhiteSpace(query)
                    ? DocumentExtensionFilter
                    : query + " " + DocumentExtensionFilter)
                : query;

            Result result = _client.Search(
                filteredQuery,
                SearchClient.SearchFlags.None,
                (uint)max,
                0,
                SearchClient.BehaviorWhenBusy.WaitOrError);

            var list = new List<EverythingSearchResult>((int)result.NumItems);
            foreach (Result.Item item in result.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                list.Add(new EverythingSearchResult(item.Name, item.Path));
            }
            return list;
        }

        // ── Launch helpers ────────────────────────────────────────────────────

        private static void Launch(string exePath)
        {
            // -startup: starts minimised to tray, same as normal autostart behaviour.
            Process.Start(new ProcessStartInfo
            {
                FileName        = exePath,
                Arguments       = "-startup",
                UseShellExecute = false,
            });
        }

        private static string FindExe()
        {
            string fromRegistry = ReadAppPathsRegistry();
            if (fromRegistry != null)
                return fromRegistry;

            string fromPath = FindOnPath("Everything.exe");
            if (fromPath != null)
                return fromPath;

            // Not found — offer to install.
            var result = MessageBox.Show(
                "התוכנה Everything אינה מותקנת.\n\nהאם ברצונך להתקין אותה כעת?",
                "Everything לא מותקן",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

            if (result == DialogResult.Yes)
            {
                string installerPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "Resources",
                    "Everything-setup.zip");

                if (File.Exists(installerPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName        = installerPath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    bool hasInternet = System.Net.NetworkInformation.NetworkInterface
                        .GetIsNetworkAvailable();

                    if (hasInternet)
                    {
                        var downloadResult = MessageBox.Show(
                            "קובץ ההתקנה לא נמצא.\n\nהאם לפתוח את דף ההורדה של Everything?",
                            "קובץ התקנה חסר",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question,
                            MessageBoxDefaultButton.Button1,
                            MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

                        if (downloadResult == DialogResult.Yes)
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName        = "https://www.voidtools.com/he-il/downloads/",
                                UseShellExecute = true
                            });
                        }
                    }
                    else
                    {
                        MessageBox.Show(
                            "קובץ ההתקנה לא נמצא ואין חיבור לאינטרנט.",
                            "שגיאה",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error,
                            MessageBoxDefaultButton.Button1,
                            MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                    }
                }
            }

            return string.Empty;
        }

        private static string ReadAppPathsRegistry()
        {
            const string keyPath = @"SOFTWARE\voidtools\Everything";

            // Check both hives — 64-bit installs write to the native hive,
            // 32-bit installs write to WOW6432Node.
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (RegistryKey key  = hklm.OpenSubKey(keyPath))
                {
                    string installDir = key != null ? key.GetValue("InstallLocation") as string : null;
                    if (!string.IsNullOrEmpty(installDir))
                    {
                        string exePath = Path.Combine(installDir, "Everything.exe");
                        if (File.Exists(exePath))
                            return exePath;
                    }
                }
            }

            return null;
        }

        private static string FindOnPath(string fileName)
        {
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                string candidate = Path.Combine(dir.Trim(), fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        private static void WaitUntilReady(CancellationToken cancellationToken)
        {
            // Phase 1: wait for the IPC window to appear (Everything process started).
            int elapsed = 0;
            while (elapsed < IpcTimeoutMs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (SearchClient.IsEverythingAvailable())
                    break;

                Thread.Sleep(PollIntervalMs);
                elapsed += PollIntervalMs;
            }

            // Phase 2: IPC is up — wait for the DB index to finish loading.
            elapsed = 0;
            while (elapsed < DbLoadTimeoutMs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (SearchClient.IsEverythingReady())
                    return;

                Thread.Sleep(PollIntervalMs);
                elapsed += PollIntervalMs;
            }
        }
    }

    /// <summary>
    /// Result DTO — FileName and Path, matching what Vue expects.
    /// </summary>
    public sealed class EverythingSearchResult
    {
        public string FileName { get; }
        public string Path     { get; }

        public EverythingSearchResult(string fileName, string path)
        {
            FileName = fileName;
            Path     = path;
        }
    }
}
