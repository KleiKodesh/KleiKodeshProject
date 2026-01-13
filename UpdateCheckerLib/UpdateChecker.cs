using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UpdateCheckerLib
{
    public class UpdateChecker
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private const string REGISTRY_KEY = @"SOFTWARE\KleiKodesh";
        private const string API_URL = "https://api.github.com/repos/KleiKodesh/KleiKodeshProject/releases/latest";

        // Static property to store pending installer path
        public static string PendingInstallerPath { get; private set; }

        static UpdateChecker()
        {
            // Force TLS 1.2 or higher for GitHub API compatibility
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            httpClient.DefaultRequestHeaders.Add("User-Agent", "KleiKodesh-UpdateChecker");
        }

        /// <summary>
        /// Runs any pending installer that was deferred during update process
        /// Call this during application shutdown
        /// </summary>
        public static void RunPendingInstaller()
        {
            if (!string.IsNullOrEmpty(PendingInstallerPath) && File.Exists(PendingInstallerPath))
            {
                try
                {
                    // Create cleanup script to delete installer after completion
                    // This is only for deferred updates, not manual installations
                    var cleanupScript = CreateCleanupScript(PendingInstallerPath);

                    // Run installer in silent mode for automatic updates
                    var installerProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = PendingInstallerPath,
                        Arguments = "--silent",
                        UseShellExecute = true,
                        Verb = "runas"
                    });

                    // Run cleanup script to delete installer after it completes
                    // Only for automatic updates - manual installs should leave the file
                    if (!string.IsNullOrEmpty(cleanupScript))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/c \"{cleanupScript}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        });
                    }
                }
                catch (Exception ex)
                {
                    // Log error but don't show UI during shutdown
                    System.Diagnostics.Debug.WriteLine($"Failed to run pending installer: {ex.Message}");
                }
                finally
                {
                    PendingInstallerPath = null;
                }
            }
        }

        /// <summary>
        /// Creates a cleanup script that waits for installer to finish then deletes it
        /// This is only used for automatic deferred updates, not manual installations
        /// </summary>
        private static string CreateCleanupScript(string installerPath)
        {
            try
            {
                var installerName = Path.GetFileNameWithoutExtension(installerPath);
                var scriptPath = Path.Combine(Path.GetTempPath(), $"cleanup_{installerName}_{DateTime.Now.Ticks}.bat");

                var scriptContent = $@"@echo off
REM Cleanup script for automatic update - deletes installer after completion
REM This only runs for deferred updates, not manual installations

REM Wait for installer process to finish
:wait
tasklist /FI ""IMAGENAME eq {Path.GetFileName(installerPath)}"" 2>NUL | find /I ""{Path.GetFileName(installerPath)}"" >NUL
if ""%%ERRORLEVEL""==""0"" (
    timeout /t 2 /nobreak >NUL
    goto wait
)

REM Wait a bit more to ensure installer is completely done
timeout /t 5 /nobreak >NUL

REM Delete the installer file (only for automatic updates)
if exist ""{installerPath}"" (
    del /f /q ""{installerPath}""
)

REM Self-delete this script (this command deletes the currently running batch file)
(goto) 2>nul & del ""%~f0""
";

                File.WriteAllText(scriptPath, scriptContent);
                return scriptPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create cleanup script: {ex.Message}");
                return null;
            }
        }

        public string GetCurrentVersionFromRegistry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY))
                {
                    return key?.GetValue("Version")?.ToString();
                }
            }
            catch { return null; }
        }

        public async Task<GitHubRelease> GetLatestReleaseAsync()
        {
            try
            {
                string response = await httpClient.GetStringAsync(API_URL);
                return JsonSerializer.Deserialize<GitHubRelease>(response);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("404"))
            {
                throw new InvalidOperationException($"GitHub repository not found. Please verify the repository exists at: {API_URL.Replace("/releases/latest", "")}", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to fetch latest release from GitHub: {ex.Message}", ex);
            }
        }

        public int CompareVersions(string githubVersion, string registryVersion)
        {
            // Normalize versions by removing 'v' prefix if present
            var normalizedGithub = githubVersion?.TrimStart('v') ?? "";
            var normalizedRegistry = registryVersion?.TrimStart('v') ?? "";

            // Try to parse as semantic versions (e.g., "1.0.31")
            if (Version.TryParse(normalizedGithub, out var githubVer) &&
                Version.TryParse(normalizedRegistry, out var registryVer))
            {
                return githubVer.CompareTo(registryVer);
            }

            // Fallback to string comparison if not valid semantic versions
            return string.Compare(normalizedGithub, normalizedRegistry, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks for updates and prompts the user in Hebrew if a new version is available.
        /// If user confirms, downloads the installer and schedules it to run on application shutdown.
        /// </summary>
        /// <param name="closeApplicationAction">Action to close the current application (optional)</param>
        /// <returns>Task that completes when update check and user interaction is finished</returns>
        public async Task CheckAndPromptForUpdateAsync(Action closeApplicationAction = null)
        {
            try
            {
                var currentVersion = GetCurrentVersionFromRegistry();
                if (string.IsNullOrEmpty(currentVersion))
                    return;

                var release = await GetLatestReleaseAsync();
                if (release?.TagName == null)
                    return;

                var comparison = CompareVersions(release.TagName, currentVersion);
                if (comparison <= 0)
                    return; // No update available

                var hebrewMessage = $"גרסה חדשה זמינה: {release.TagName}\n" +
                                  $"הגרסה הנוכחית שלך: {currentVersion}\n\n" +
                                  "האם ברצונך להוריד ולהתקין את הגרסה החדשה?";

                var result = MessageBox.Show(
                    hebrewMessage,
                    "עדכון זמין - כלי קודש",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1,
                    MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign
                );

                if (result == DialogResult.Yes)
                {
                    await DownloadAndRunInstallerAsync(release.TagName);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"שגיאה בעדכון: {ex.Message}",
                    "שגיאה - כלי קודש",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1,
                    MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign
                );
            }
        }

        public async Task<bool> IsUpdateAvailableAsync()
        {
            try
            {
                var currentVersion = GetCurrentVersionFromRegistry();
                if (string.IsNullOrEmpty(currentVersion)) return false;

                var release = await GetLatestReleaseAsync();
                if (release?.TagName == null) return false;

                return CompareVersions(release.TagName, currentVersion) > 0;
            }
            catch (InvalidOperationException)
            {
                // Repository doesn't exist or other API error
                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task DownloadAndRunInstallerAsync(string version)
        {
            DownloadProgressWindow progressWindow = null;
            Thread staThread = null;

            try
            {
                // Construct installer download URL - matches build script output filename
                var installerUrl = $"https://github.com/KleiKodesh/KleiKodeshProject/releases/download/{version}/KleiKodeshSetup-{version}.exe";
                var tempPath = Path.Combine(Path.GetTempPath(), $"KleiKodeshSetup-{version}.exe");

                // Create WPF progress window on STA thread
                var windowCreated = new ManualResetEventSlim(false);

                staThread = new Thread(() =>
                {
                    progressWindow = new DownloadProgressWindow();
                    progressWindow.SetVersion(version);
                    progressWindow.Show();

                    windowCreated.Set();

                    // Start WPF message pump
                    System.Windows.Threading.Dispatcher.Run();
                });

                staThread.SetApartmentState(ApartmentState.STA);
                staThread.Start();

                // Wait for window to be created
                windowCreated.Wait();

                // Use HttpClient with proper async/await and IProgress
                await DownloadFileAsync(installerUrl, tempPath, progressWindow, progressWindow.Cancellation.Token);

                // Check if user cancelled during download
                if (progressWindow.IsCancelled)
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                    return;
                }

                // Verify file was downloaded successfully
                if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
                {
                    throw new InvalidOperationException("הורדת הקובץ נכשלה");
                }

                // Close progress window and wait for it to actually close
                var windowClosed = new ManualResetEventSlim(false);
                progressWindow?.Dispatcher.Invoke(() =>
                {
                    progressWindow.Close();
                    windowClosed.Set();
                });
                windowClosed.Wait();

                // Store installer for execution on app shutdown
                PendingInstallerPath = tempPath;

                // Ask user if they want to proceed with deferred installation
                var notificationMessage = $"העדכון לגרסה {version} הורד בהצלחה!\n\n" +
                                        "ההתקנה תתבצע כאשר תסגור את התוכנה.";

                var result = MessageBox.Show(
                    notificationMessage,
                    "עדכון מוכן להתקנה - כלי קודש",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1,
                    MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign
                );

                if (result == DialogResult.Cancel)
                {
                    // User cancelled - clear pending installer and delete file
                    PendingInstallerPath = null;
                    MessageBox.Show("ההתקנה בוטלה!");
                    if (File.Exists(tempPath))
                    {
                        try
                        {
                            File.Delete(tempPath);
                        }
                        catch
                        {
                            // Ignore deletion errors
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // User cancelled - clean up and return silently
                var tempPath = Path.Combine(Path.GetTempPath(), $"KleiKodeshSetup-{version}.exe");
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"שגיאה בהורדת העדכון: {ex.Message}",
                    "שגיאה - כלי קודש",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1,
                    MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign
                );
            }
            finally
            {
                // Clean up WPF window and thread
                if (progressWindow != null)
                {
                    progressWindow.Dispatcher.Invoke(() =>
                    {
                        progressWindow.Close();
                        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background);
                    });
                }
            }
        }

        private async Task DownloadFileAsync(string url, string filePath, DownloadProgressWindow progressWindow, CancellationToken cancellationToken)
        {
            using (var client = new HttpClient())
            {
                // Ensure TLS 1.2+ for this client as well
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
                client.Timeout = TimeSpan.FromMinutes(5);
                client.DefaultRequestHeaders.Add("User-Agent", "KleiKodesh-UpdateChecker");

                // Start with indeterminate progress
                progressWindow.SetIndeterminate("מתחבר לשרת...");

                using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength;
                    var canReportProgress = totalBytes.HasValue;

                    // Create IProgress for reporting progress
                    var progress = new Progress<(long bytesRead, long totalBytes)>(data =>
                    {
                        if (canReportProgress)
                        {
                            var progressPercentage = (int)((data.bytesRead * 100L) / data.totalBytes);
                            var status = $"הורדה: {FormatBytes(data.bytesRead)} מתוך {FormatBytes(data.totalBytes)}";
                            progressWindow.UpdateProgress(progressPercentage, status);
                        }
                        else
                        {
                            var status = $"הורדה: {FormatBytes(data.bytesRead)}";
                            progressWindow.UpdateProgress(0, status);
                        }
                    });

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var totalBytesRead = 0L;
                        var buffer = new byte[8192];
                        var isMoreToRead = true;
                        var lastReportedPercentage = -1;

                        do
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                            if (bytesRead == 0)
                            {
                                isMoreToRead = false;
                                continue;
                            }

                            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                            totalBytesRead += bytesRead;

                            if (canReportProgress)
                            {
                                var progressPercentage = (int)((totalBytesRead * 100L) / totalBytes.Value);

                                // Only update every 5% or at completion
                                //if (progressPercentage % 5 == 0 && progressPercentage != lastReportedPercentage || progressPercentage >= 100)
                                //{
                                lastReportedPercentage = progressPercentage;
                                ((IProgress<(long, long)>)progress).Report((totalBytesRead, totalBytes.Value));
                                //}
                            }
                            else
                            {
                                // Unknown size - update every MB
                                if (totalBytesRead % (1024 * 1024) == 0 || totalBytesRead < 1024 * 1024)
                                {
                                    ((IProgress<(long, long)>)progress).Report((totalBytesRead, 0));
                                }
                            }
                        }
                        while (isMoreToRead);
                    }
                }
            }
        }

        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }
    }

    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("published_at")]
        public DateTime PublishedAt { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;
    }
}