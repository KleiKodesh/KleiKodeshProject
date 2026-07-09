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
        /// Deletes %TEMP%\KleiKodeshSetup.exe silently. Used to clean up after a
        /// successful install or when a stale/outdated installer is found on disk.
        /// </summary>
        public static void DeleteInstallerFile() => TryDeleteFile(InstallerTempPath);

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
        /// </summary>
        public static async Task DownloadAndScheduleInstallerAsync(string version)
        {
            string suffix       = GetInstallerSuffix();
            string installerUrl = $"https://github.com/KleiKodesh/KleiKodeshProject/releases/download/{version}/KleiKodeshSetup-{version}{suffix}.exe";

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

                    // Download silently in background without showing progress form
                    await DownloadFileAsync(installerUrl, InstallerTempPath, CancellationToken.None);

                    if (!File.Exists(InstallerTempPath) || new FileInfo(InstallerTempPath).Length == 0)
                        throw new UpdateException("הורדת הקובץ נכשלה — הקובץ ריק או חסר", installerUrl, attempts: 1);

                    // Download complete. PendingInstallerPath is NOT set here —
                    // it is set by UpdateChecker.GetReadyUpdateVersion() on the next
                    // session when the sync disk check sees the file is newer than registry.
                }
                catch (OperationCanceledException) { TryDeleteFile(InstallerTempPath); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Silent download failed: {ex.Message}");
                    TryDeleteFile(InstallerTempPath);
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
        /// Uses Verb="runas" to hand off to the Windows AIS service so the process
        /// survives Word/app shutdown. No UAC prompt — NSIS has RequestExecutionLevel=user.
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

            var pathToLaunch = PendingInstallerPath;
            PendingInstallerPath = null;
            try
            {
                LaunchInstaller(pathToLaunch);
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

        // UseShellExecute=true with Verb="runas" hands off to the Windows AIS system service,
        // which runs outside Word's process tree. Without runas, ShellExecuteEx runs in-process
        // and gets killed when Word shuts down before Process.Start returns.
        // The NSIS wrapper has RequestExecutionLevel=user so no UAC prompt appears.
        private static void LaunchInstaller(string installerPath)
        {
            var psi = new ProcessStartInfo
            {
                FileName         = installerPath,
                UseShellExecute  = true,
                Verb             = "runas",
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
                // ERROR_CANCELLED (1223): user clicked "No" on the UAC prompt.
                // Respect the user's choice — don't retry, don't show an error.
                Debug.WriteLine("[UpdateChecker] User cancelled UAC prompt, skipping install.");
            }
            catch
            {
                // Any other failure: retry without "runas".
                // runas is only used to escape Word's process tree via the AIS service —
                // the NSIS wrapper has RequestExecutionLevel=user so no elevation is needed.
                // If the retry also throws, let the exception propagate to RunPendingInstaller's
                // catch block which will show the error with the path for manual launch.
                var fallback = new ProcessStartInfo
                {
                    FileName         = installerPath,
                    UseShellExecute  = true,
                    WorkingDirectory = Path.GetDirectoryName(installerPath)
                };
                Process.Start(fallback);
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

                        using (var input  = await response.Content.ReadAsStreamAsync())
                        using (var output = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            var buffer     = new byte[8192];
                            long totalRead = 0;
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
