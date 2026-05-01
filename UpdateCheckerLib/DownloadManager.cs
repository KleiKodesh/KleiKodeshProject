using Microsoft.Win32;
using System;
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
        public static string PendingInstallerPath { get; private set; }

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

        public static async Task DownloadAndScheduleInstallerAsync(string version)
        {
            string suffix       = GetInstallerSuffix();
            string installerUrl = $"https://github.com/KleiKodesh/KleiKodeshProject/releases/download/{version}/KleiKodeshSetup-{version}{suffix}.exe";
            string tempPath     = Path.Combine(Path.GetTempPath(), "KleiKodeshSetup.exe");

            DownloadProgressForm form = null;
            try
            {
                form = DownloadProgressForm.ShowModeless(version);

                await DownloadFileAsync(installerUrl, tempPath, form, form.Cancellation.Token);

                if (form.IsCancelled) { TryDeleteFile(tempPath); return; }

                if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
                    throw new InvalidOperationException("הורדת הקובץ נכשלה");

                PendingInstallerPath = tempPath;
            }
            catch (OperationCanceledException) { TryDeleteFile(tempPath); }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"שגיאה בהורדת העדכון:\n{ex.Message}",
                    "שגיאה - כלי קודש",
                    MessageBoxButtons.OK, MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1,
                    MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            }
            finally { form?.SafeClose(); }
        }

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
                MessageBox.Show(
                    $"שגיאה בהפעלת המתקין:\n{ex.Message}",
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

            var p = Process.Start(psi);
            if (p == null)
                throw new InvalidOperationException("Failed to start installer process");
        }

        private static async Task DownloadFileAsync(
            string url, string filePath, DownloadProgressForm form, CancellationToken token)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                client.DefaultRequestHeaders.Add("User-Agent", "KleiKodesh-UpdateChecker");
                form.SetIndeterminate("מתחבר לשרת...");

                using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? 0;

                    using (var input  = await response.Content.ReadAsStreamAsync())
                    using (var output = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var buffer    = new byte[8192];
                        long totalRead = 0;
                        int read;

                        while ((read = await input.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                        {
                            await output.WriteAsync(buffer, 0, read, token);
                            totalRead += read;

                            if (totalBytes > 0)
                                form.UpdateProgress((int)(totalRead * 100 / totalBytes),
                                    $"הורדה: {FormatBytes(totalRead)} מתוך {FormatBytes(totalBytes)}");
                            else
                                form.UpdateProgress(0, $"הורדה: {FormatBytes(totalRead)}");
                        }
                    }
                }
            }
        }

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
