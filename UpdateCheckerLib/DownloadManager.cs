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
        private const string REGISTRY_KEY   = @"SOFTWARE\KleiKodesh";
        private const string REGISTRY_VALUE = "PendingInstallerPath";

        /// <summary>
        /// Persisted to the registry so it survives AppDomain boundaries
        /// (VSTO can load assemblies in a separate AppDomain from the shutdown callback).
        /// </summary>
        public static string PendingInstallerPath
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY))
                        return key?.GetValue(REGISTRY_VALUE)?.ToString();
                }
                catch { return null; }
            }
            private set
            {
                try
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(REGISTRY_KEY))
                    {
                        if (value == null)
                            key?.DeleteValue(REGISTRY_VALUE, throwOnMissingValue: false);
                        else
                            key?.SetValue(REGISTRY_VALUE, value);
                    }
                }
                catch { }
            }
        }

        public static async Task DownloadAndScheduleInstallerAsync(string version)
        {
            string installerUrl = $"https://github.com/KleiKodesh/KleiKodeshProject/releases/download/{version}/KleiKodeshSetup-{version}.exe";
            string tempPath = Path.Combine(Path.GetTempPath(), $"KleiKodeshSetup.exe"); // use same path each time

            DownloadProgressForm form = null;

            try
            {
                form = DownloadProgressForm.ShowModeless(version);

                await DownloadFileAsync(
                    installerUrl,
                    tempPath,
                    form,
                    form.Cancellation.Token);

                if (form.IsCancelled)
                {
                    TryDeleteFile(tempPath);
                    return;
                }

                if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
                    throw new InvalidOperationException("הורדת הקובץ נכשלה");

                PendingInstallerPath = tempPath;
            }
            catch (OperationCanceledException)
            {
                TryDeleteFile(tempPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"שגיאה בהורדת העדכון:\n{ex.Message}",
                    "שגיאה - כלי קודש",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1,
                    MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            }
            finally
            {
                form?.SafeClose();
            }
        }

        public static void RunPendingInstaller()
        {
            if (string.IsNullOrEmpty(PendingInstallerPath) ||
                !File.Exists(PendingInstallerPath))
                return;

            try
            {
                LaunchInstaller(PendingInstallerPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Installer launch failed: " + ex);
            }
            finally
            {
                PendingInstallerPath = null;
            }
        }

        private static async Task DownloadFileAsync(
            string url,
            string filePath,
            DownloadProgressForm form,
            CancellationToken token)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                client.DefaultRequestHeaders.Add(
                    "User-Agent", "KleiKodesh-UpdateChecker");

                form.SetIndeterminate("מתחבר לשרת...");

                using (var response =
                    await client.GetAsync(url,
                        HttpCompletionOption.ResponseHeadersRead, token))
                {
                    response.EnsureSuccessStatusCode();

                    var totalBytes =
                        response.Content.Headers.ContentLength ?? 0;

                    using (var input =
                        await response.Content.ReadAsStreamAsync())
                    using (var output =
                        new FileStream(filePath,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None,
                            8192,
                            true))
                    {
                        var buffer = new byte[8192];
                        long totalRead = 0;
                        int read;

                        while ((read =
                            await input.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                        {
                            await output.WriteAsync(buffer, 0, read, token);

                            totalRead += read;

                            if (totalBytes > 0)
                            {
                                var pct = (int)(totalRead * 100 / totalBytes);
                                form.UpdateProgress(
                                    pct,
                                    $"הורדה: {FormatBytes(totalRead)} מתוך {FormatBytes(totalBytes)}");
                            }
                            else
                            {
                                form.UpdateProgress(
                                    0,
                                    $"הורדה: {FormatBytes(totalRead)}");
                            }
                        }
                    }
                }
            }
        }

        // Launch with ShellExecute so Windows handles UAC correctly per the
        // manifest's RequestExecutionLevel (NSIS wrapper is user-level, no elevation needed).
        // No --silent: the installer shows its normal landing page so the user clicks through.
        private static void LaunchInstaller(string installerPath)
        {
            var psi = new ProcessStartInfo
            {
                FileName         = installerPath,
                UseShellExecute  = true,
                WorkingDirectory = Path.GetDirectoryName(installerPath)
            };

            var p = Process.Start(psi);
            if (p == null)
                throw new InvalidOperationException("Failed to start installer process");
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            decimal value = bytes;
            int i = 0;

            while (value >= 1024 && i < suffixes.Length - 1)
            {
                value /= 1024;
                i++;
            }

            return $"{value:n1} {suffixes[i]}";
        }
    }
}
