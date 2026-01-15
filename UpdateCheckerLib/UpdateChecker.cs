using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UpdateCheckerLib
{
    public static class UpdateChecker
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private const string REGISTRY_KEY = @"SOFTWARE\KleiKodesh";
        private const string API_URL = "https://api.github.com/repos/KleiKodesh/KleiKodeshProject/releases/latest";

        static UpdateChecker()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            httpClient.DefaultRequestHeaders.Add("User-Agent", "KleiKodesh-UpdateChecker");
        }

        public static void RunPendingInstaller() => DownloadManager.RunPendingInstaller();

        public static string GetCurrentVersionFromRegistry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY))
                    return key?.GetValue("Version")?.ToString();
            }
            catch { return null; }
        }

        public static async Task<GitHubRelease> GetLatestReleaseAsync()
        {
            try
            {
                var response = await httpClient.GetStringAsync(API_URL);
                return JsonSerializer.Deserialize<GitHubRelease>(response);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update check failed: {ex.Message}");
                return null;
            }
        }

        public static int CompareVersions(string githubVersion, string registryVersion)
        {
            var normalizedGithub = githubVersion?.TrimStart('v') ?? "";
            var normalizedRegistry = registryVersion?.TrimStart('v') ?? "";

            return Version.TryParse(normalizedGithub, out var githubVer) &&
                   Version.TryParse(normalizedRegistry, out var registryVer)
                ? githubVer.CompareTo(registryVer)
                : string.Compare(normalizedGithub, normalizedRegistry, StringComparison.OrdinalIgnoreCase);
        }

        public static async Task CheckAndPromptForUpdateAsync(Action closeApplicationAction = null)
        {
            try
            {
                var currentVersion = GetCurrentVersionFromRegistry();
                if (string.IsNullOrEmpty(currentVersion)) return;

                var release = await GetLatestReleaseAsync();
                if (release?.TagName == null || CompareVersions(release.TagName, currentVersion) <= 0) return;

                var result = ShowHebrewMessageBox(
                    $"גרסה חדשה זמינה: {release.TagName}\nהגרסה הנוכחית שלך: {currentVersion}\n\nהאם ברצונך להוריד ולהתקין את הגרסה החדשה?",
                    "עדכון זמין - כלי קודש",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (result == DialogResult.Yes)
                    await DownloadManager.DownloadAndScheduleInstallerAsync(release.TagName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update check failed: {ex.Message}");
            }
        }

        public static async Task<bool> IsUpdateAvailableAsync()
        {
            try
            {
                var currentVersion = GetCurrentVersionFromRegistry();
                if (string.IsNullOrEmpty(currentVersion)) return false;

                var release = await GetLatestReleaseAsync();
                return release?.TagName != null && CompareVersions(release.TagName, currentVersion) > 0;
            }
            catch { return false; }
        }

        private static DialogResult ShowHebrewMessageBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) =>
            MessageBox.Show(text, caption, buttons, icon, MessageBoxDefaultButton.Button1,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
    }
}