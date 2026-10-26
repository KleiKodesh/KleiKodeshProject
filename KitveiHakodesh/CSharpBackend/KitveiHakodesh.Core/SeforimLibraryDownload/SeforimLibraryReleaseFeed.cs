using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>One file attached to a release.</summary>
    /// <param name="Sha256">Lower-case hex, or null when GitHub reported no digest.</param>
    public sealed record SeforimLibraryReleaseAsset(string Name, long Size, string DownloadUrl, string? Sha256);

    /// <summary>One published release of Otzaria's SeforimLibrary, newest first in a listing.</summary>
    public sealed record SeforimLibraryRelease(string Tag, IReadOnlyList<SeforimLibraryReleaseAsset> Assets)
    {
        public SeforimLibraryReleaseAsset? FindAsset(string name) =>
            Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));
    }

    /// <summary>
    /// Reads Otzaria's SeforimLibrary releases from the GitHub API — the same releases the
    /// Otzaria app downloads its library from (empty_library_bloc.dart, findLatestRelease).
    ///
    /// The database asset is named by its schema: seforim-schema6.db.zst. Otzaria's newer
    /// releases may add a schema7 file the reader here does not know, so the search is for the
    /// newest release that still carries a schema-6 file, not simply "latest".
    /// </summary>
    public sealed class SeforimLibraryReleaseFeed
    {
        /// <summary>The schema this app reads (SeforimDbSchema). Bump with the reader, never ahead of it.</summary>
        public const int SupportedSchemaVersion = 6;

        public static readonly string DatabaseAssetName = $"seforim-schema{SupportedSchemaVersion}.db.zst";

        private const string ReleasesApiUrl =
            "https://api.github.com/repos/Otzaria/SeforimLibrary/releases?per_page=30";

        private readonly HttpClient _http;

        public SeforimLibraryReleaseFeed(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        /// <summary>Releases newest first. Drafts and pre-releases are left out.</summary>
        public async Task<IReadOnlyList<SeforimLibraryRelease>> ListAsync(CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApiUrl);
            // GitHub rejects requests with no User-Agent; the host's shared client sends one.
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var releases = await JsonSerializer.DeserializeAsync(
                body, SeforimLibraryReleaseJsonContext.Default.ListGithubRelease, cancellationToken).ConfigureAwait(false);

            return (releases ?? new List<GithubRelease>())
                .Where(r => !r.Draft && !r.Prerelease && !string.IsNullOrEmpty(r.TagName))
                .Select(ToRelease)
                .ToList();
        }

        /// <summary>The newest release carrying a database this app can read, or null.</summary>
        public async Task<(SeforimLibraryRelease Release, SeforimLibraryReleaseAsset Database)?> FindNewestDatabaseAsync(
            CancellationToken cancellationToken)
        {
            foreach (var release in await ListAsync(cancellationToken).ConfigureAwait(false))
            {
                var asset = release.FindAsset(DatabaseAssetName);
                if (asset != null && asset.Size > 0) return (release, asset);
            }
            return null;
        }

        private static SeforimLibraryRelease ToRelease(GithubRelease release) =>
            new SeforimLibraryRelease(
                release.TagName,
                release.Assets
                    .Where(a => !string.IsNullOrEmpty(a.Name) && !string.IsNullOrEmpty(a.BrowserDownloadUrl))
                    .Select(a => new SeforimLibraryReleaseAsset(a.Name, a.Size, a.BrowserDownloadUrl, Sha256Of(a.Digest)))
                    .ToList());

        /// <summary>GitHub reports "sha256:&lt;hex&gt;"; anything else is treated as no digest.</summary>
        private static string? Sha256Of(string? digest)
        {
            const string prefix = "sha256:";
            return digest != null && digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? digest.Substring(prefix.Length).ToLowerInvariant()
                : null;
        }
    }

    /// <summary>A release as the GitHub API reports it (somebody else's JSON).</summary>
    internal sealed class GithubRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("assets")] public List<GithubReleaseAsset> Assets { get; set; } = new();
    }

    internal sealed class GithubReleaseAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
        [JsonPropertyName("digest")] public string? Digest { get; set; }
    }

    [JsonSerializable(typeof(List<GithubRelease>))]
    internal sealed partial class SeforimLibraryReleaseJsonContext : JsonSerializerContext;
}
