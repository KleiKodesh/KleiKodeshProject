using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

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

        /// <summary>
        /// Synchronous disk-only check — no network, no async, no threading concerns.
        /// Reads %TEMP%\KleiKodeshSetup.exe's embedded ProductVersion and compares it
        /// against the installed version in the registry.
        ///
        /// Returns the version string (e.g. "v8.6.0") and arms RunPendingInstaller()
        /// when a newer installer is already downloaded and waiting.
        ///
        /// Side effects:
        ///   file > registry  → sets PendingInstallerPath, returns version
        ///   file <= registry → deletes the stale/already-installed file, returns null
        ///   no file          → returns null
        /// </summary>
        public static string GetReadyUpdateVersion()
        {
            try
            {
                // No registry = portable install, skip all update logic
                var registryVersion = GetCurrentVersionFromRegistry();
                if (string.IsNullOrEmpty(registryVersion)) return null;

                var fileVersion = DownloadManager.GetInstallerFileVersion();
                if (fileVersion == null) return null;

                if (CompareVersions(fileVersion, registryVersion) > 0)
                {
                    // Newer installer on disk — arm the launcher
                    DownloadManager.PendingInstallerPath =
                        Path.Combine(Path.GetTempPath(), "KleiKodeshSetup.exe");
                    return fileVersion;
                }
                else
                {
                    // File is same version or older — already installed or stale. Clean up.
                    DownloadManager.DeleteInstallerFile();
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateChecker] GetReadyUpdateVersion failed: {ex.Message}");
                return null;
            }
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

        /// <summary>
        /// Compares two version strings ("v" prefix optional).
        ///
        /// A version with MORE components always outranks one with fewer,
        /// regardless of the numbers: 0.2.3.4 > 1.2.3 and 0.2.3.4 > 12.345.456.
        /// This lets the versioning scheme move to four-part numbers and restart
        /// low without any installed three-part version blocking the update.
        /// Versions with the same component count compare numerically as usual.
        /// </summary>
        public static int CompareVersions(string githubVersion, string registryVersion)
        {
            var normalizedGithub = githubVersion?.TrimStart('v') ?? "";
            var normalizedRegistry = registryVersion?.TrimStart('v') ?? "";

            if (Version.TryParse(normalizedGithub, out var githubVer) &&
                Version.TryParse(normalizedRegistry, out var registryVer))
            {
                int lengthDiff = FieldCount(githubVer).CompareTo(FieldCount(registryVer));
                return lengthDiff != 0 ? lengthDiff : githubVer.CompareTo(registryVer);
            }

            return string.Compare(normalizedGithub, normalizedRegistry, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Number of components a parsed Version carries (2, 3, or 4).</summary>
        private static int FieldCount(Version v) =>
            v.Revision >= 0 ? 4 : v.Build >= 0 ? 3 : 2;

        /// <summary>
        /// Background-only task: hits the GitHub API and silently downloads a newer
        /// installer to %TEMP%\KleiKodeshSetup.exe if one is available.
        /// Never shows any UI. Never sets PendingInstallerPath.
        /// The downloaded file will be picked up by GetReadyUpdateVersion() on the
        /// next session's sync check.
        ///
        /// Also skips the download if %TEMP%\KleiKodeshSetup.exe already contains
        /// the latest version (e.g. a previous download succeeded but the user hasn't
        /// closed the app yet).
        /// </summary>
        public static async Task CheckForUpdateAsync()
        {
            try
            {
                var currentVersion = GetCurrentVersionFromRegistry();
                if (string.IsNullOrEmpty(currentVersion)) return;

                var release = await GetLatestReleaseAsync();
                if (release?.TagName == null || CompareVersions(release.TagName, currentVersion) <= 0)
                    return;

                // Check if the file already on disk is already this version — no need to re-download
                var existingFileVersion = DownloadManager.GetInstallerFileVersion();
                if (existingFileVersion != null)
                {
                    int cmp = CompareVersions(existingFileVersion, release.TagName);
                    if (cmp > 0)
                        return; // newer than the latest release (e.g. local dev build) — leave it alone

                    if (cmp == 0)
                    {
                        // Same version — but don't trust the version stamp alone: a download
                        // interrupted by process exit (before the .partial scheme) leaves a
                        // truncated exe whose version resource still reads, wedging the updater
                        // in an announce-but-never-install loop. Verify the byte size against
                        // the release asset; on mismatch fall through and re-download.
                        long? expectedSize = FindAssetSize(release, DownloadManager.GetInstallerAssetName(release.TagName));
                        long? actualSize   = DownloadManager.GetInstallerFileLength();

                        if (expectedSize == null || actualSize == expectedSize)
                        {
                            Debug.WriteLine($"[UpdateChecker] {release.TagName} already downloaded, skipping.");
                            return;
                        }

                        Debug.WriteLine(
                            $"[UpdateChecker] On-disk installer is {actualSize} bytes but the release asset " +
                            $"is {expectedSize} bytes — truncated download, fetching a fresh copy.");
                    }
                }

                await DownloadManager.DownloadAndScheduleInstallerAsync(release.TagName);
            }
            catch (UpdateCheckException ex)
            {
                Debug.WriteLine($"[UpdateChecker] Update check failed: {ex.Message} — {ex.InnerException?.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateChecker] Update check failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Byte size of the named asset in the release, or null if the release JSON
        /// carries no matching asset (older cached responses, renamed assets).
        /// </summary>
        private static long? FindAssetSize(GitHubRelease release, string assetName)
        {
            if (release?.Assets == null) return null;
            foreach (var asset in release.Assets)
            {
                if (string.Equals(asset.Name, assetName, StringComparison.OrdinalIgnoreCase) && asset.Size > 0)
                    return asset.Size;
            }
            return null;
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
    }
}