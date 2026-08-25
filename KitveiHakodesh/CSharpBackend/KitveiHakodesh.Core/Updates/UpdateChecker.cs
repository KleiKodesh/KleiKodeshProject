using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace KitveiHakodesh.Core.Updates
{
    /// <summary>
    /// Decides whether a newer release exists and which file to fetch for it.
    ///
    /// WHY THIS IS IN Updates/ AND NOT Common/. Everything in Common is reusable because it
    /// knows nothing about this app. This knows the repository, the installer's file name and
    /// the registry key the installer stamps — that is app knowledge, so it sits beside
    /// Settings/ rather than pretending to be general-purpose.
    ///
    /// IT NEVER UPDATES THE RUNNING EXECUTABLE. It downloads the full installer and lets that
    /// install into the per-user location and register the Word add-in. Which is why
    /// <see cref="IsInstalled"/> gates everything: doing that from a portable copy the user
    /// unzipped would install software they never asked to install.
    /// </summary>
    public static class UpdateChecker
    {
        private const string RegistryKeyPath = @"SOFTWARE\KleiKodesh";
        private const string LatestReleaseApiUrl =
            "https://api.github.com/repos/KleiKodesh/KleiKodeshProject/releases/latest";

        /// <summary>GitHub rejects requests with no User-Agent.</summary>
        private const string UserAgent = "KleiKodesh-UpdateChecker";

        private const int MaxAttempts = 3;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
#if !NET5_0_OR_GREATER
            // .NET Framework defaults to a protocol set GitHub no longer accepts, and this is
            // process-wide rather than per-client. The modern runtime negotiates TLS itself.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
#endif
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            return client;
        }

        /// <summary>
        /// Whether the installer has run on this machine for this user.
        ///
        /// The proof is the version the installer stamps into the registry as its last step. A
        /// portable copy has no such stamp, so it never checks for, downloads, or launches an
        /// update — see the class remarks for why that is the whole point rather than a caution.
        /// </summary>
        public static bool IsInstalled() => !string.IsNullOrEmpty(InstalledVersion());

        /// <summary>The installed version, or null when this is not an installed copy.</summary>
        public static string? InstalledVersion()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                return key?.GetValue("Version")?.ToString();
            }
            catch (Exception)
            {
                return null;   // no key, or no permission — either way, not an installed copy
            }
        }

        /// <summary>
        /// Records the installed version as seen, so the stamp tracks what each host has run.
        ///
        /// It returns nothing on purpose. This used to hand back the version on the first run
        /// after an update so a host could show a one-time "updated" notice — which existed
        /// because updates installed silently and the user had no other way to know. Updates now
        /// run the installer visibly and the user drives it, so announcing it afterwards tells
        /// them something they just watched happen.
        ///
        /// If a first-run-after-update hook is ever wanted again, do the comparison HERE rather
        /// than reading the stamp from a host: it is shared between Word and the standalone app,
        /// so only whichever host runs first would ever see it.
        /// </summary>
        public static void RecordInstalledVersionAsSeen()
        {
            string? current = InstalledVersion();
            if (string.IsNullOrEmpty(current)) return;

            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
                key?.SetValue("LastSeenVersion", current);
            }
            catch (Exception)
            {
                // Tracking only. This runs during startup, and no update stamp is worth
                // failing a launch over.
            }
        }

        /// <summary>
        /// Which installer variant this machine was installed with — "x64", "x86" or "AnyCPU".
        /// Older installs predate the value and are treated as AnyCPU, which is the release
        /// that has always existed.
        /// </summary>
        public static string InstallerVariant()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                string? value = key?.GetValue("InstallerVariant")?.ToString();
                if (value == "x64" || value == "x86") return value;
            }
            catch (Exception) { /* fall through to the default */ }

            return "AnyCPU";
        }

        /// <summary>
        /// Compares two version strings; a leading "v" is optional on either.
        ///
        /// A version with MORE components outranks one with fewer, whatever the numbers:
        /// 0.2.3.4 beats 1.2.3, and beats 12.345.456. That is deliberate, so the numbering
        /// scheme can move to four parts and restart low without every installed three-part
        /// version blocking the update. Equal component counts compare numerically as usual.
        /// </summary>
        public static int CompareVersions(string? candidate, string? installed)
        {
            string left = candidate?.TrimStart('v') ?? "";
            string right = installed?.TrimStart('v') ?? "";

            if (Version.TryParse(left, out Version? leftVersion)
                && Version.TryParse(right, out Version? rightVersion))
            {
                int byComponentCount = ComponentCount(leftVersion).CompareTo(ComponentCount(rightVersion));
                return byComponentCount != 0 ? byComponentCount : leftVersion.CompareTo(rightVersion);
            }

            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static int ComponentCount(Version version) =>
            version.Revision >= 0 ? 4 : version.Build >= 0 ? 3 : 2;

        /// <summary>
        /// The latest release from GitHub.
        ///
        /// Retries the failures worth retrying — including 404, which from GitHub is often CDN
        /// propagation rather than a missing release — and throws
        /// <see cref="UpdateCheckFailedException"/> when it runs out of attempts. The caller
        /// decides whether a failed check is worth telling anyone about; most callers do this on
        /// startup and should say nothing.
        /// </summary>
        public static async Task<GithubRelease> LatestReleaseAsync(CancellationToken cancellationToken = default)
        {
            Exception? lastFailure = null;

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                try
                {
                    string json = await Http.GetStringAsync(LatestReleaseApiUrl).ConfigureAwait(false);
                    GithubRelease? release = JsonSerializer.Deserialize(json, GithubJsonContext.Default.GithubRelease);
                    if (release != null) return release;

                    lastFailure = new InvalidOperationException("the release response was empty");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (HttpRequestException ex) when (IsWorthRetrying(ex) && attempt < MaxAttempts)
                {
                    lastFailure = ex;
                    await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                catch (Exception ex)
                {
                    lastFailure = ex;
                    break;   // not retryable — stop rather than waiting twice for the same answer
                }
            }

            throw new UpdateCheckFailedException(LatestReleaseApiUrl, lastFailure);
        }

        /// <summary>
        /// The asset to download: the variant matching this machine's install when the release
        /// ships it, otherwise the unsuffixed AnyCPU one — a release may publish only that.
        /// Null when the release carries no usable asset, which the caller answers by
        /// constructing the URL by convention instead.
        /// </summary>
        public static GithubReleaseAsset? ResolveInstallerAsset(GithubRelease? release)
        {
            if (release?.Assets == null || string.IsNullOrEmpty(release.TagName)) return null;

            return FindAsset(release, InstallerAssetName(release.TagName))
                ?? FindAsset(release, "KleiKodeshSetup-" + release.TagName + ".exe");
        }

        /// <summary>The release asset file name for a version and this machine's variant —
        /// e.g. "KleiKodeshSetup-v8.7.2-x64.exe".</summary>
        public static string InstallerAssetName(string version) =>
            "KleiKodeshSetup-" + version + VariantSuffix() + ".exe";

        private static string VariantSuffix()
        {
            string variant = InstallerVariant();
            if (variant == "x64") return "-x64";
            if (variant == "x86") return "-x86";
            return "";
        }

        /// <summary>An asset is only usable if it has a size and a URL — a renamed or
        /// still-uploading asset has one or neither, and downloading it would produce a file
        /// nothing can verify.</summary>
        private static GithubReleaseAsset? FindAsset(GithubRelease release, string assetName)
        {
            foreach (GithubReleaseAsset asset in release.Assets)
            {
                if (string.Equals(asset.Name, assetName, StringComparison.OrdinalIgnoreCase)
                    && asset.Size > 0
                    && !string.IsNullOrEmpty(asset.BrowserDownloadUrl))
                    return asset;
            }
            return null;
        }

        /// <summary>
        /// Status codes worth a second attempt. 404 is in here on purpose: GitHub serves one
        /// while a fresh release propagates through its CDN.
        /// </summary>
        private static bool IsWorthRetrying(HttpRequestException exception)
        {
            string message = exception.Message;
            return message.IndexOf("404", StringComparison.Ordinal) >= 0
                || message.IndexOf("500", StringComparison.Ordinal) >= 0
                || message.IndexOf("502", StringComparison.Ordinal) >= 0
                || message.IndexOf("503", StringComparison.Ordinal) >= 0
                || message.IndexOf("504", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// True when the failure means there is no route to GitHub at all — offline, or a
        /// content filter or proxy refusing the request.
        ///
        /// Worth telling apart from a real error: these are ordinary conditions on a filtered
        /// network, and a startup update check should pass over them in silence rather than
        /// reporting that something went wrong. The 418 is a content filter's own block
        /// response, not a joke.
        /// </summary>
        public static bool IndicatesNoConnectivity(Exception? exception)
        {
            if (exception == null) return false;

            if (exception is WebException webException)
            {
                switch (webException.Status)
                {
                    case WebExceptionStatus.NameResolutionFailure:
                    case WebExceptionStatus.ConnectFailure:
                    case WebExceptionStatus.Timeout:
                    case WebExceptionStatus.SendFailure:
                    case WebExceptionStatus.ReceiveFailure:
                        return true;
                }
                return false;
            }

            if (exception is HttpRequestException httpException)
            {
                string message = httpException.Message;
                // Any 4xx other than 404 (retried above as a GitHub propagation glitch) means
                // something between us and GitHub said no.
                if (message.IndexOf("418", StringComparison.Ordinal) >= 0    // content-filter block
                    || message.IndexOf("407", StringComparison.Ordinal) >= 0 // proxy auth required
                    || message.IndexOf("403", StringComparison.Ordinal) >= 0 // firewall or proxy rule
                    || message.IndexOf("400", StringComparison.Ordinal) >= 0 // proxy rejected the request
                    || message.IndexOf("451", StringComparison.Ordinal) >= 0) // blocked for legal reasons
                    return true;

                return IndicatesNoConnectivity(httpException.InnerException);
            }

            return IndicatesNoConnectivity(exception.InnerException);
        }
    }

    /// <summary>
    /// The release check itself failed — not "no update available", which is an ordinary result.
    ///
    /// Carries the URL and the underlying failure as STRUCTURED data. The version this replaces
    /// built a Hebrew sentence for the user inside the exception; Core supplies the facts and
    /// the host writes the words. Pass <see cref="Exception.InnerException"/> to
    /// <see cref="UpdateChecker.IndicatesNoConnectivity"/> to decide whether it is worth
    /// mentioning at all.
    /// </summary>
    public sealed class UpdateCheckFailedException : Exception
    {
        public string Url { get; }

        public UpdateCheckFailedException(string url, Exception? inner)
            : base("the update check failed: " + url, inner)
        {
            Url = url;
        }
    }
}
