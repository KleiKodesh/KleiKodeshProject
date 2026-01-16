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

        public static async Task DownloadAndScheduleInstallerAsync(string version)
        {
            var installerUrl = $"https://github.com/KleiKodesh/KleiKodeshProject/releases/download/{version}/KleiKodeshSetup-{version}.exe";
            var tempPath = Path.Combine(Path.GetTempPath(), $"KleiKodeshSetup-{version}.exe");

            try
            {
                var progressWindow = await CreateProgressWindowAsync(version);
                await DownloadFileAsync(installerUrl, tempPath, progressWindow, progressWindow.Cancellation.Token);

                if (progressWindow.IsCancelled)
                {
                    TryDeleteFile(tempPath);
                    return;
                }

                if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
                    throw new InvalidOperationException("הורדת הקובץ נכשלה");

                CloseProgressWindow(progressWindow);

                //if (ConfirmInstallation(version))
                PendingInstallerPath = tempPath;
                //else
                //    TryDeleteFile(tempPath);
            }
            catch (OperationCanceledException)
            {
                TryDeleteFile(tempPath);
            }
            catch (Exception ex)
            {
                ShowHebrewError($"שגיאה בהורדת העדכון: {ex.Message}");
            }
        }

        public static void RunPendingInstaller()
        {
            if (string.IsNullOrEmpty(PendingInstallerPath) || !File.Exists(PendingInstallerPath)) return;

            try
            {
                CreateInstallerLauncherScript(PendingInstallerPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create installer launcher: {ex.Message}");
            }
            finally
            {
                PendingInstallerPath = null;
            }
        }

        private static async Task<DownloadProgressWindow> CreateProgressWindowAsync(string version)
        {
            DownloadProgressWindow window = null;
            using (var created = new ManualResetEventSlim(false))
            {
                var thread = new Thread(() =>
                {
                    window = new DownloadProgressWindow();
                    window.SetVersion(version);
                    window.Show();
                    created.Set();
                    System.Windows.Threading.Dispatcher.Run();
                })
                { IsBackground = true };

                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                created.Wait();
            }

            return window;
        }

        private static void CloseProgressWindow(DownloadProgressWindow window)
        {
            using (var closed = new ManualResetEventSlim(false))
            {
                window?.Dispatcher.Invoke(() =>
                {
                    window.Close();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background);
                    closed.Set();
                });
                closed.Wait();
            }
        }

        private static async Task DownloadFileAsync(string url, string filePath, DownloadProgressWindow window, CancellationToken token)
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                client.DefaultRequestHeaders.Add("User-Agent", "KleiKodesh-UpdateChecker");

                window.SetIndeterminate("מתחבר לשרת...");

                using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength;

                    var progress = new Progress<(long read, long total)>(data =>
                    {
                        if (data.total > 0)
                        {
                            var pct = (int)((data.read * 100L) / data.total);
                            window.UpdateProgress(pct, $"הורדה: {FormatBytes(data.read)} מתוך {FormatBytes(data.total)}");
                        }
                        else
                            window.UpdateProgress(0, $"הורדה: {FormatBytes(data.read)}");
                    });

                    using (var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var buffer = new byte[8192];
                        var totalRead = 0L;
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                            totalRead += bytesRead;
                            ((IProgress<(long, long)>)progress).Report((totalRead, totalBytes ?? 0));
                        }
                    }
                }
            }
        }

        private static bool ConfirmInstallation(string version) =>
            MessageBox.Show(
                $"העדכון לגרסה {version} הורד בהצלחה!\n\nההתקנה תתבצע כאשר תסגור את התוכנה.",
                "עדכון מוכן להתקנה - כלי קודש",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign
            ) == DialogResult.OK;

        private static void ShowHebrewError(string message) =>
            MessageBox.Show(message, "שגיאה - כלי קודש", MessageBoxButtons.OK, MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        private static void CreateInstallerLauncherScript(string installerPath)
        {
            try
            {
                var scriptPath = Path.Combine(Path.GetTempPath(), $"KleiKodesh_Updater_{DateTime.Now.Ticks}.bat");

                // Script that waits for Word to fully close, then runs installer, then cleans up
                var script = $@"@echo off
REM Wait for Word to fully close (check for WINWORD.EXE process)
:waitForWord
tasklist /FI ""IMAGENAME eq WINWORD.EXE"" 2>NUL | find /I ""WINWORD.EXE"" >NUL
if ""%%ERRORLEVEL""==""0"" (
    timeout /t 2 /nobreak >NUL
    goto waitForWord
)

REM Additional delay to ensure all resources are released
timeout /t 1 /nobreak >NUL

REM Run the installer with silent flag and elevated privileges
if exist ""{installerPath}"" (
    powershell -Command ""Start-Process -FilePath '{installerPath}' -ArgumentList '--silent' -Verb RunAs -Wait""
)

REM Wait for installer to complete
:waitForInstaller
tasklist /FI ""IMAGENAME eq {Path.GetFileName(installerPath)}"" 2>NUL | find /I ""{Path.GetFileName(installerPath)}"" >NUL
if ""%%ERRORLEVEL""==""0"" (
    timeout /t 2 /nobreak >NUL
    goto waitForInstaller
)

REM Additional delay before cleanup
timeout /t 5 /nobreak >NUL

REM Clean up installer file
if exist ""{installerPath}"" del /f /q ""{installerPath}""

REM Self-delete this script
(goto) 2>nul & del ""%~f0""";

                File.WriteAllText(scriptPath, script);

                // Start the launcher script in background
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                Debug.WriteLine($"Installer launcher script created: {scriptPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create installer launcher script: {ex.Message}");
            }
        }

        private static string FormatBytes(long bytes)
        {
            var suffixes = new[] { "B", "KB", "MB", "GB" };
            var counter = 0;
            var number = (decimal)bytes;
            while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }
    }
}
