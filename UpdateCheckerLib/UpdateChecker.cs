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
            const int maxAttempts = 3;
            const int retryDelayMs = 2000;

            Exception lastException = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var response = await httpClient.GetStringAsync(API_URL);
                    return JsonSerializer.Deserialize<GitHubRelease>(response);
                }
                catch (HttpRequestException ex) when (IsRetryable(ex) && attempt < maxAttempts)
                {
                    lastException = ex;
                    Debug.WriteLine($"Update check attempt {attempt} failed ({ex.Message}), retrying in {retryDelayMs}ms...");
                    await Task.Delay(retryDelayMs);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    break; // non-retryable — stop immediately
                }
            }

            Debug.WriteLine($"Update check failed after all attempts: {lastException?.Message}");
            // Rethrow so callers can decide whether to show a message.
            if (lastException != null)
                throw new UpdateCheckException("לא ניתן לבדוק עדכונים", API_URL, lastException);
            return null;
        }

        public static int CompareVersions(string githubVersion, string registryVersion)
        {
            var normalizedGithub = githubVersion?.TrimStart('v') ?? "";
            var normalizedRegistry = registryVersion?.TrimStart('v') ?? "";

            var result =  Version.TryParse(normalizedGithub, out var githubVer) &&
                   Version.TryParse(normalizedRegistry, out var registryVer)
                ? githubVer.CompareTo(registryVer)
                : string.Compare(normalizedGithub, normalizedRegistry, StringComparison.OrdinalIgnoreCase);
            
            return result;
        }

        /// <summary>
        /// Checks for an update and downloads it silently if one is available.
        /// Returns the new version tag (e.g. "v3.1.0") when a download succeeded,
        /// or null when there is nothing to do (no update, no internet, download failed).
        /// Does NOT show any UI — callers are responsible for marshalling back to the UI
        /// thread and showing the notification dialog.
        /// </summary>
        public static async Task<string> CheckForUpdateAsync()
        {
            try
            {
                var currentVersion = GetCurrentVersionFromRegistry();
                if (string.IsNullOrEmpty(currentVersion)) return null;

                var release = await GetLatestReleaseAsync();
                if (release?.TagName == null || CompareVersions(release.TagName, currentVersion) <= 0)
                    return null;

                // Download silently in background
                await DownloadManager.DownloadAndScheduleInstallerAsync(release.TagName);

                // Return the version tag only if the download succeeded
                return string.IsNullOrEmpty(DownloadManager.PendingInstallerPath) ? null : release.TagName;
            }
            catch (UpdateCheckException ex)
            {
                Debug.WriteLine($"Update check failed: {ex.Message} — {ex.InnerException?.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update check failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Legacy helper kept for call sites that need the old fire-and-forget behaviour.
        /// Prefer <see cref="CheckForUpdateAsync"/> so callers can marshal the dialog
        /// to the UI thread themselves.
        /// </summary>
        public static async Task CheckAndPromptForUpdateAsync(Action closeApplicationAction = null)
        {
            var newVersion = await CheckForUpdateAsync();
            if (newVersion == null) return;

            // We are still on the Task.Run threadpool thread here.
            // Show the MessageBox on a dedicated STA thread so it has a proper message pump.
            ShowOnStaThread(() =>
                ShowHebrewMessageBox(
                    $"עדכון זמין לגרסה {newVersion}.\nהעדכון יותקן אוטומטית עם סגירת וורד.",
                    "עדכון זמין - כלי קודש",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                )
            );
        }

        /// <summary>
        /// Runs <paramref name="action"/> on a fresh STA thread so that WinForms
        /// MessageBox (and any other COM/UI calls) have a proper message pump.
        /// Blocks the calling thread until the STA thread finishes.
        /// </summary>
        private static void ShowOnStaThread(Action action)
        {
            var thread = new System.Threading.Thread(() => action());
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();
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

        /// <summary>
        /// Returns true for HTTP errors that are worth retrying (404, 5xx, network issues).
        /// 404 from GitHub can be a transient CDN/propagation glitch.
        /// </summary>
        private static bool IsRetryable(HttpRequestException ex)
        {
            var msg = ex.Message;
            // HttpRequestException message contains the status code text for .NET Framework
            return msg.Contains("404") ||
                   msg.Contains("500") ||
                   msg.Contains("502") ||
                   msg.Contains("503") ||
                   msg.Contains("504");
        }

        /// <summary>
        /// Returns true when the exception indicates there is no internet connectivity
        /// or that the request was blocked by a content filter / proxy (e.g. NetFree returns 418),
        /// so the update check should be cancelled silently without showing an error.
        /// </summary>
        private static bool IsNoConnectivityException(Exception ex)
        {
            if (ex == null) return false;

            // WebException wraps most no-internet failures on .NET Framework
            if (ex is WebException we)
            {
                return we.Status == WebExceptionStatus.NameResolutionFailure ||
                       we.Status == WebExceptionStatus.ConnectFailure       ||
                       we.Status == WebExceptionStatus.Timeout              ||
                       we.Status == WebExceptionStatus.SendFailure          ||
                       we.Status == WebExceptionStatus.ReceiveFailure;
            }

            // HttpRequestException: check for proxy/content-filter blocks (e.g. NetFree → 418).
            // Any 4xx response that isn't 404 (which is retried as a transient GitHub glitch)
            // means the request was blocked or rejected by a proxy — treat as silent failure.
            if (ex is HttpRequestException hre)
            {
                var msg = hre.Message;
                if (msg.Contains("418") ||   // NetFree "I'm a teapot" content filter block
                    msg.Contains("407") ||   // Proxy authentication required
                    msg.Contains("403") ||   // Forbidden (firewall / proxy rule)
                    msg.Contains("400") ||   // Bad request from proxy
                    msg.Contains("451"))     // Unavailable for legal reasons
                    return true;

                // Also recurse into inner for wrapped WebExceptions
                if (hre.InnerException != null)
                    return IsNoConnectivityException(hre.InnerException);
            }

            return false;
        }

        private static DialogResult ShowHebrewMessageBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) =>
            MessageBox.Show(text, caption, buttons, icon, MessageBoxDefaultButton.Button1,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
    }
}