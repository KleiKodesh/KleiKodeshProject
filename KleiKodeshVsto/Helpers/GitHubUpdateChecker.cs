using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KleiKodesh.Helpers
{
    public class GitHubUpdateChecker
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private const string REGISTRY_KEY = @"SOFTWARE\KleiKodesh";

        static GitHubUpdateChecker() => httpClient.DefaultRequestHeaders.Add("User-Agent", "KleiKodesh-UpdateChecker");

        /// <summary>
        /// Checks for updates and prompts the user in Hebrew if a new version is available.
        /// If user confirms, downloads and runs the installer directly.
        /// </summary>
        /// <returns>Task that completes when update check and user interaction is finished</returns>
        public async Task CheckAndPromptForUpdateAsync()
        {
            try
            {
                var updateInfo = await GetUpdateInfoAsync();

                if (updateInfo.HasUpdate)
                {
                    var hebrewMessage = $"גרסה חדשה זמינה: {updateInfo.LatestVersion}\n" +
                                      $"הגרסה הנוכחית שלך: {updateInfo.CurrentVersion}\n\n" +
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
                        // Construct installer download URL
                        var installerUrl = $"https://github.com/KleiKodesh/KleiKodeshProject/releases/download/{updateInfo.LatestVersion}/KleiKodeshInstaller.exe";

                        // Download to temp and run
                        var tempPath = Path.Combine(Path.GetTempPath(), $"KleiKodeshInstaller_{updateInfo.LatestVersion}.exe");

                        using (var client = new HttpClient())
                        {
                            client.DefaultRequestHeaders.Add("User-Agent", "KleiKodesh-UpdateChecker");
                            var fileBytes = await client.GetByteArrayAsync(installerUrl);
                            File.WriteAllBytes(tempPath, fileBytes);
                        }

                        // Run installer with admin privileges
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = tempPath,
                            UseShellExecute = true,
                            Verb = "runas"
                        });

                        // Close Word
                        Globals.ThisAddIn.Application.Quit();
                    }
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

                var response = await httpClient.GetStringAsync("https://api.github.com/repos/KleiKodesh/KleiKodeshProject/releases/latest");
                var release = JsonConvert.DeserializeObject<GitHubRelease>(response);

                if (release?.TagName == null) return false;

                return CompareVersions(release.TagName, currentVersion) > 0;
            }
            catch { return false; }
        }

        public async Task<UpdateInfo> GetUpdateInfoAsync()
        {
            try
            {
                var currentVersion = GetCurrentVersionFromRegistry();
                if (string.IsNullOrEmpty(currentVersion))
                {
                    return new UpdateInfo { HasUpdate = false, Message = "No current version found in registry" };
                }

                var response = await httpClient.GetStringAsync("https://api.github.com/repos/KleiKodesh/KleiKodeshProject/releases/latest");
                var release = JsonConvert.DeserializeObject<GitHubRelease>(response);

                if (release?.TagName == null)
                {
                    return new UpdateInfo { HasUpdate = false, Message = "Unable to fetch latest version from GitHub" };
                }

                var comparison = CompareVersions(release.TagName, currentVersion);

                return new UpdateInfo
                {
                    HasUpdate = comparison > 0,
                    CurrentVersion = currentVersion,
                    LatestVersion = release.TagName,
                    DownloadUrl = release.HtmlUrl,
                    Message = comparison > 0
                        ? $"Update available: {currentVersion} → {release.TagName}"
                        : $"Current version {currentVersion} is up to date"
                };
            }
            catch (Exception ex)
            {
                return new UpdateInfo { HasUpdate = false, Message = $"Error checking for updates: {ex.Message}" };
            }
        }

        private string GetCurrentVersionFromRegistry()
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

        private int CompareVersions(string githubVersion, string registryVersion)
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
    }

    public class GitHubRelease
    {
        [JsonProperty("tag_name")] public string TagName { get; set; } = string.Empty;
        [JsonProperty("html_url")] public string HtmlUrl { get; set; } = string.Empty;
    }

    public class UpdateInfo
    {
        public bool HasUpdate { get; set; }
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
