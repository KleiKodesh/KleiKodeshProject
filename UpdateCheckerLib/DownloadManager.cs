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

                // Confirm the write landed in the registry
                var logPath = Path.Combine(Path.GetTempPath(), "KleiKodesh-update.log");
                try { File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss.fff} Download complete, PendingInstallerPath set to '{PendingInstallerPath}'\r\n"); } catch { }
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
            var logPath = Path.Combine(Path.GetTempPath(), "KleiKodesh-update.log");
            void Log(string msg)
            {
                try { File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss.fff} {msg}\r\n"); } catch { }
            }

            Log("RunPendingInstaller called");
            Log($"PendingInstallerPath = '{PendingInstallerPath}'");

            if (string.IsNullOrEmpty(PendingInstallerPath))
            {
                Log("SKIP: path is null/empty");
                return;
            }

            if (!File.Exists(PendingInstallerPath))
            {
                Log($"SKIP: file does not exist at '{PendingInstallerPath}'");
                PendingInstallerPath = null;
                return;
            }

            Log($"File exists, size = {new FileInfo(PendingInstallerPath).Length} bytes");

            // Clear the registry entry BEFORE launching — if Word's process is killed
            // mid-launch the finally block may never run, leaving a stale entry.
            var pathToLaunch = PendingInstallerPath;
            PendingInstallerPath = null;
            Log("PendingInstallerPath cleared");

            try
            {
                LaunchInstaller(pathToLaunch);
                Log("LaunchInstaller succeeded");
            }
            catch (Exception ex)
            {
                Log($"LaunchInstaller FAILED: {ex}");
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
        // Uses a bat script launched via cmd.exe so the installer runs in a process
        // completely detached from Word — Word's shutdown kills all its threads before
        // Process.Start can return, so we must hand off to an external process.
        private static void LaunchInstaller(string installerPath)
        {
            var scriptPath = Path.Combine(Path.GetTempPath(), $"KleiKodesh_Updater_{DateTime.Now.Ticks}.bat");

            var script = $@"@echo off
REM Wait for Word to fully close
:waitForWord
tasklist /FI ""IMAGENAME eq WINWORD.EXE"" 2>NUL | find /I ""WINWORD.EXE"" >NUL
if ""%ERRORLEVEL%""==""0"" (
    timeout /t 2 /nobreak >NUL
    goto waitForWord
)

REM Small extra delay to ensure file handles are released
timeout /t 2 /nobreak >NUL

REM Run the installer
if exist ""{installerPath}"" (
    start """" ""{installerPath}""
)

REM Self-delete
(goto) 2>nul & del ""%~f0""";

            File.WriteAllText(scriptPath, script);

            Process.Start(new ProcessStartInfo
            {
                FileName        = "cmd.exe",
                Arguments       = $"/c \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow  = true,
                WindowStyle     = ProcessWindowStyle.Hidden
            });
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
