using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UpdateCheckerLib
{
    internal static class DownloadManager
    {
        public static string PendingInstallerPath { get; set; }

        private static readonly string InstallerTempPath =
            Path.Combine(Path.GetTempPath(), "KleiKodeshSetup.exe");

        // In-flight downloads write here and are renamed to InstallerTempPath only
        // after the byte count is validated. KleiKodeshSetup.exe therefore never
        // exists half-written: a download interrupted by process exit leaves only
        // a .partial file, which is ignored by the version check and cleaned up
        // by the next session's download / DeleteInstallerFile call.
        //
        // This matters because a truncated NSIS exe still reports its full
        // ProductVersion (the version resource lives in the small stub at the
        // start of the file), so a half-downloaded KleiKodeshSetup.exe used to
        // look like a complete update, get announced on every launch, and fail
        // its CRC check on every close — with the "already downloaded" check
        // preventing a re-download forever.
        private static readonly string InstallerPartialPath =
            Path.Combine(Path.GetTempPath(), "KleiKodeshSetup.exe.partial");

        /// <summary>
        /// Reads the ProductVersion embedded in %TEMP%\KleiKodeshSetup.exe (if it exists).
        /// Returns e.g. "v8.6.0" or null if the file is missing or has no version info.
        /// Pure sync — just a file stat + PE header read, no network.
        /// </summary>
        public static string GetInstallerFileVersion()
        {
            try
            {
                if (!File.Exists(InstallerTempPath)) return null;
                var version = FileVersionInfo.GetVersionInfo(InstallerTempPath).ProductVersion;
                return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Deletes %TEMP%\KleiKodeshSetup.exe (and any leftover .partial download)
        /// silently. Used to clean up after a successful install or when a
        /// stale/outdated installer is found on disk.
        /// </summary>
        public static void DeleteInstallerFile()
        {
            TryDeleteFile(InstallerTempPath);
            TryDeleteFile(InstallerPartialPath);
        }

        /// <summary>
        /// Byte size of %TEMP%\KleiKodeshSetup.exe, or null if it doesn't exist.
        /// Compared against the GitHub release asset size to detect files that
        /// pre-date the atomic .partial download scheme and were left truncated.
        /// </summary>
        public static long? GetInstallerFileLength()
        {
            try
            {
                var info = new FileInfo(InstallerTempPath);
                return info.Exists ? info.Length : (long?)null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// File name of the release asset for <paramref name="version"/>, matching
        /// this machine's installer variant — e.g. "KleiKodeshSetup-v8.7.2-x64.exe".
        /// </summary>
        internal static string GetInstallerAssetName(string version) =>
            $"KleiKodeshSetup-{version}{GetInstallerSuffix()}.exe";

        /// <summary>
        /// Returns the installer variant stored in the registry ("x64", "x86", or "AnyCPU").
        /// Falls back to "AnyCPU" if the value is missing (pre-variant installs).
        /// </summary>
        private static string GetInstallerVariantFromRegistry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\KleiKodesh"))
                {
                    var value = key?.GetValue("InstallerVariant")?.ToString();
                    if (value == "x64" || value == "x86") return value;
                }
            }
            catch { }
            return "AnyCPU";
        }

        /// <summary>
        /// Returns the URL suffix for the installer file name based on the stored variant.
        /// x64 → "-x64", x86 → "-x86", AnyCPU → "" (no suffix).
        /// </summary>
        private static string GetInstallerSuffix()
        {
            var variant = GetInstallerVariantFromRegistry();
            if (variant == "x64") return "-x64";
            if (variant == "x86") return "-x86";
            return "";
        }

        /// <summary>
        /// Downloads the installer for <paramref name="version"/> silently to
        /// <see cref="InstallerTempPath"/> (%TEMP%\KleiKodeshSetup.exe).
        /// Pure fire-and-forget: no UI, no side effects on program state.
        /// <see cref="UpdateChecker.GetReadyUpdateVersion"/> will pick up the file
        /// on the next session's sync disk check and arm <see cref="RunPendingInstaller"/>.
        ///
        /// <paramref name="downloadUrl"/> is the release asset URL resolved by
        /// <see cref="UpdateChecker.ResolveInstallerAsset"/> (installed variant with
        /// AnyCPU fallback); when null, the variant URL is constructed blindly
        /// (pre-assets behavior). When <paramref name="expectedSize"/> is positive,
        /// a download whose byte count differs is rejected — the .partial file is
        /// deleted and KleiKodeshSetup.exe is never produced.
        /// </summary>
        public static async Task DownloadAndScheduleInstallerAsync(
            string version, string downloadUrl = null, long expectedSize = 0)
        {
            string installerUrl = downloadUrl ??
                $"https://github.com/KleiKodesh/KleiKodeshProject/releases/download/{version}/{GetInstallerAssetName(version)}";

            // Cross-process mutex: prevents simultaneous downloads from VSTO + demo app.
            // If another process is already downloading, skip silently.
            bool createdNew;
            using (var mutex = new Mutex(initiallyOwned: false, "KleiKodesh-UpdateDownload-Mutex", out createdNew))
            {
                bool acquired = false;
                try
                {
                    acquired = mutex.WaitOne(0); // non-blocking — don't queue up
                    if (!acquired)
                    {
                        Debug.WriteLine("Update download already in progress in another process, skipping.");
                        return;
                    }

                    // Download silently in background without showing progress form.
                    // Written to .partial and renamed only after the byte count is
                    // validated, so KleiKodeshSetup.exe is never seen half-written —
                    // even if this process is killed mid-download.
                    await DownloadFileAsync(installerUrl, InstallerPartialPath, CancellationToken.None);

                    long downloadedLength = File.Exists(InstallerPartialPath)
                        ? new FileInfo(InstallerPartialPath).Length : 0;

                    if (downloadedLength == 0)
                        throw new UpdateException("הורדת הקובץ נכשלה — הקובץ ריק או חסר", installerUrl, attempts: 1);

                    // When the release JSON told us the asset's exact size, enforce it —
                    // a wrong-sized file must never be renamed into KleiKodeshSetup.exe.
                    if (expectedSize > 0 && downloadedLength != expectedSize)
                        throw new UpdateException(
                            $"הורדת הקובץ נכשלה — גודל שגוי ({downloadedLength}/{expectedSize} בתים)",
                            installerUrl, attempts: 1);

                    TryDeleteFile(InstallerTempPath);
                    File.Move(InstallerPartialPath, InstallerTempPath);

                    // Download complete. PendingInstallerPath is NOT set here —
                    // it is set by UpdateChecker.GetReadyUpdateVersion() on the next
                    // session when the sync disk check sees the file is newer than registry.
                }
                catch (OperationCanceledException) { TryDeleteFile(InstallerPartialPath); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Silent download failed: {ex.Message}");
                    TryDeleteFile(InstallerPartialPath);
                    // Fail silently - don't show error message to user
                }
                finally
                {
                    if (acquired)
                        mutex.ReleaseMutex();
                }
            }
        }

        /// <summary>
        /// Launches the installer at <see cref="PendingInstallerPath"/> (set by
        /// <see cref="UpdateChecker.GetReadyUpdateVersion"/>) and clears the path.
        /// Called on app/Word close. No-op if PendingInstallerPath is not set.
        ///
        /// The installer is launched unelevated — the NSIS wrapper and the WPF
        /// installer both have user-level manifests, so no UAC prompt appears here.
        /// (The WPF installer self-elevates just the DocumentLocator service
        /// registration step, from its own visible foreground window.)
        /// Verb="runas" must NOT be reintroduced: the verb itself forces a UAC
        /// consent prompt regardless of the target's manifest, and a declined or
        /// policy-denied prompt used to silently kill the update on every close.
        ///
        /// The installer is launched with "--silent": it auto-installs with no
        /// clicks and exits (fully headless in current installers; releases up to
        /// v8.7.2 show a small progress window). --silent is safe to pass because
        /// every installer generation supports it — it was the original
        /// auto-update flag. Do NOT pass newer arguments: the exe on disk is
        /// always an OLDER release than this code. --wait-for-pid specifically
        /// left pre-bound installers hidden FOREVER when the pid got recycled
        /// (verified live: three invisible installers accumulated across three
        /// app closes).
        /// </summary>
        public static void RunPendingInstaller()
        {
            if (string.IsNullOrEmpty(PendingInstallerPath))
                return;

            if (!File.Exists(PendingInstallerPath))
            {
                PendingInstallerPath = null;
                return;
            }

            // Word and the standalone app can close at the same moment and both
            // reach this point — two silent installers extracting concurrently
            // would corrupt each other (same NSIS temp dir, same install folder).
            // First launcher wins; if the running install fails, the file stays
            // on disk and the next session's close simply tries again.
            if (Process.GetProcessesByName("KleiKodeshSetup").Length > 0 ||
                Process.GetProcessesByName("KleiKodeshVstoInstallerWpf").Length > 0)
            {
                Debug.WriteLine("[UpdateChecker] An installer is already running, skipping launch.");
                PendingInstallerPath = null;
                return;
            }

            var pathToLaunch = PendingInstallerPath;
            PendingInstallerPath = null;
            try
            {
                LaunchInstaller(pathToLaunch);
            }
            catch (Win32Exception w32) when (w32.NativeErrorCode == 193)
            {
                // ERROR_BAD_EXE_FORMAT: the file on disk is corrupt (e.g. a truncated
                // download from a version that pre-dates the .partial scheme).
                // Delete it so the next session's background check downloads a fresh
                // copy instead of failing on every close forever.
                Debug.WriteLine("[UpdateChecker] Installer exe is corrupt, deleting so it re-downloads.");
                TryDeleteFile(pathToLaunch);
            }
            catch (Exception ex)
            {
                var details = ex is Win32Exception w32
                    ? $"{ex.Message}\n(Win32 error code: {w32.NativeErrorCode})"
                    : $"{ex.GetType().Name}: {ex.Message}";

                MessageBox.Show(
                    $"שגיאה בהפעלת המתקין:\n{details}\n\nניתן להפעיל את הקובץ ידנית:\n{pathToLaunch}",
                    "שגיאה - כלי קודש",
                    MessageBoxButtons.OK, MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1,
                    MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            }
        }

        private static void LaunchInstaller(string installerPath)
        {
            var psi = new ProcessStartInfo
            {
                FileName         = installerPath,
                Arguments        = "--silent",
                UseShellExecute  = true,
                WorkingDirectory = Path.GetDirectoryName(installerPath)
            };

            try
            {
                Process.Start(psi);
                // Process.Start with UseShellExecute=true may return null on success — that's fine.
            }
            catch (Win32Exception win32ex) when (win32ex.NativeErrorCode == 0)
            {
                // NativeErrorCode 0 = ERROR_SUCCESS: Windows threw despite a successful launch.
                // Treat this as success and do nothing.
            }
            catch (Win32Exception win32ex) when (win32ex.NativeErrorCode == 1223)
            {
                // ERROR_CANCELLED (1223): a UAC prompt was declined. Current installers
                // are user-level and never prompt, but an already-downloaded setup exe
                // from the era when the NSIS manifest said "admin" still elevates.
                // Respect the user's choice — the file stays for the next close.
                Debug.WriteLine("[UpdateChecker] User cancelled UAC prompt, skipping install.");
            }
        }

        static DownloadManager()
        {
            // Ensure TLS 1.2 is used for GitHub connections (required by NetFree and modern security standards).
            // This must be set globally before any HttpClient is created, as it affects all connections.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        }

        private static async Task DownloadFileAsync(
            string url, string filePath, CancellationToken token)
        {
            const int maxAttempts = 3;
            const int retryDelayMs = 2000;

            // Track the last HTTP status and inner exception for the final error message.
            HttpStatusCode? lastStatus = null;
            Exception lastException    = null;

            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                client.DefaultRequestHeaders.Add("User-Agent", "KleiKodesh-UpdateChecker");

                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    token.ThrowIfCancellationRequested();

                    if (attempt > 1)
                    {
                        Debug.WriteLine($"Retrying download... ({attempt}/{maxAttempts})");
                        await Task.Delay(retryDelayMs, token);
                    }

                    HttpResponseMessage response = null;
                    try
                    {
                        response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
                        lastStatus = response.StatusCode;

                        if (IsRetryableStatus(response.StatusCode) && attempt < maxAttempts)
                        {
                            Debug.WriteLine($"Download attempt {attempt} got {(int)response.StatusCode}, retrying...");
                            response.Dispose();
                            response = null;
                            continue;
                        }

                        response.EnsureSuccessStatusCode();
                        var totalBytes = response.Content.Headers.ContentLength ?? 0;
                        long totalRead = 0;

                        using (var input  = await response.Content.ReadAsStreamAsync())
                        using (var output = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            var buffer = new byte[8192];
                            int read;

                            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                            {
                                await output.WriteAsync(buffer, 0, read, token);
                                totalRead += read;

                                if (totalBytes > 0)
                                    Debug.WriteLine($"Download: {FormatBytes(totalRead)} / {FormatBytes(totalBytes)}");
                                else
                                    Debug.WriteLine($"Download: {FormatBytes(totalRead)}");
                            }
                        }

                        // A dropped connection can end the read loop without an exception.
                        // Anything shorter than Content-Length is a failed attempt, not a file.
                        if (totalBytes > 0 && totalRead != totalBytes)
                        {
                            Debug.WriteLine($"Download attempt {attempt} incomplete ({totalRead}/{totalBytes} bytes), retrying...");
                            lastException = new IOException($"ההורדה נקטעה ({totalRead}/{totalBytes} בתים)");
                            continue;
                        }

                        return; // success
                    }
                    catch (OperationCanceledException) { throw; } // let cancel propagate unchanged
                    catch (Exception ex) when (!(ex is UpdateException))
                    {
                        lastException = ex;
                        if (attempt >= maxAttempts)
                            break; // fall through to throw below
                        Debug.WriteLine($"Download attempt {attempt} threw: {ex.Message}, retrying...");
                    }
                    finally
                    {
                        response?.Dispose();
                    }
                }

                // All attempts exhausted — throw a structured exception with full context.
                var statusDesc = lastStatus.HasValue
                    ? $"קוד שגיאת שרת {(int)lastStatus.Value}"
                    : "שגיאת רשת";
                throw new UpdateException(
                    $"הורדת העדכון נכשלה לאחר {maxAttempts} ניסיונות ({statusDesc})",
                    url, maxAttempts, lastStatus, lastException);
            }
        }

        /// <summary>
        /// Returns true for HTTP status codes that are worth retrying.
        /// 404 can be a transient CDN/release-propagation glitch on GitHub.
        /// </summary>
        private static bool IsRetryableStatus(HttpStatusCode status) =>
            status == HttpStatusCode.NotFound ||           // 404
            status == HttpStatusCode.InternalServerError || // 500
            status == HttpStatusCode.BadGateway ||          // 502
            status == HttpStatusCode.ServiceUnavailable ||  // 503
            status == HttpStatusCode.GatewayTimeout;        // 504

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            decimal value = bytes;
            int i = 0;
            while (value >= 1024 && i < suffixes.Length - 1) { value /= 1024; i++; }
            return $"{value:n1} {suffixes[i]}";
        }
    }
}
