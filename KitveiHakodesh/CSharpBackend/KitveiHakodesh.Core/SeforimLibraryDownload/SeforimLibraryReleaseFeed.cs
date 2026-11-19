using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
    /// The whole library as one release publishes it: a zstd stream, either one file
    /// (seforim-schemaN.db.zst) or split into parts (.part-000, .part-001, ...) that are
    /// concatenated in order. A single file is a list of one part.
    /// </summary>
    /// <param name="Version">The release's library version (v38-... -> 38).</param>
    /// <param name="SchemaVersion">The database schema, from the file name.</param>
    /// <param name="Sha256">The whole stream's sha256 when published (split manifest or single file), else null.</param>
    /// <param name="Parts">In concatenation order. Each carries its own sha256 when known.</param>
    public sealed record SeforimLibraryDatabaseArchive(
        string ReleaseTag, int Version, int SchemaVersion, string ArchiveName, long Size, string? Sha256,
        IReadOnlyList<SeforimLibraryReleaseAsset> Parts);

    /// <summary>
    /// Reads Otzaria's SeforimLibrary releases from the GitHub API - the same releases the
    /// Otzaria app downloads its library from (otzaria_library_updater, library_release.dart).
    ///
    /// The library is whatever the newest stable release carries, in whatever schema: the
    /// reader detects a database's shape by its tables (SeforimDbSchema), so a newer schema is
    /// taken as is. GitHub caps an asset at 2 GiB, so a large library comes split into parts
    /// with a manifest (split_asset.dart) - both forms are handled.
    /// </summary>
    public sealed class SeforimLibraryReleaseFeed
    {
        private const string ReleasesApiUrl =
            "https://api.github.com/repos/Otzaria/SeforimLibrary/releases?per_page=30";

        private static readonly Regex SingleArchiveName =
            new Regex(@"^seforim-schema(\d+)\.db\.zst$", RegexOptions.CultureInvariant);

        private static readonly Regex SplitManifestName =
            new Regex(@"^seforim-schema(\d+)\.db\.zst\.manifest\.json$", RegexOptions.CultureInvariant);

        private static readonly Regex PartName =
            new Regex(@"^seforim-schema(\d+)\.db\.zst\.part-(\d+)$", RegexOptions.CultureInvariant);

        private static readonly Regex TagVersion = new Regex(@"^v(\d+)", RegexOptions.CultureInvariant);

        private readonly HttpClient _http;

        public SeforimLibraryReleaseFeed(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        /// <summary>Releases, newest version first. Drafts and pre-releases are left out.</summary>
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
                .OrderByDescending(r => VersionOfTag(r.Tag)) // stable sort: GitHub's own order breaks ties
                .ToList();
        }

        /// <summary>The newest release's library, or null when no stable release carries one.</summary>
        public async Task<SeforimLibraryDatabaseArchive?> FindNewestDatabaseAsync(CancellationToken cancellationToken) =>
            await NewestDatabaseOfAsync(await ListAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

        /// <summary>The first (newest) of <paramref name="releases"/> that carries a library.</summary>
        public async Task<SeforimLibraryDatabaseArchive?> NewestDatabaseOfAsync(
            IReadOnlyList<SeforimLibraryRelease> releases, CancellationToken cancellationToken)
        {
            foreach (var release in releases)
            {
                var archive = await DatabaseOfAsync(release, FetchTextAsync, cancellationToken).ConfigureAwait(false);
                if (archive != null) return archive;
            }
            return null;
        }

        /// <summary>
        /// The library <paramref name="release"/> carries, or null. Several schemas in one release:
        /// the highest. Per schema: the single file if there is one, else the parts as the split
        /// manifest lists them (sizes checked against the release), else - no manifest - the
        /// parts by number, which must run 000, 001, ... with no gap.
        /// </summary>
        /// <param name="fetchText">Reads a small asset (the split manifest).</param>
        public static async Task<SeforimLibraryDatabaseArchive?> DatabaseOfAsync(
            SeforimLibraryRelease release,
            Func<SeforimLibraryReleaseAsset, CancellationToken, Task<string>> fetchText,
            CancellationToken cancellationToken)
        {
            var schemas = release.Assets
                .Select(a => SchemaOfName(a.Name))
                .Where(s => s != null)
                .Select(s => s!.Value)
                .Distinct()
                .OrderByDescending(s => s)
                .ToList();
            int version = VersionOfTag(release.Tag);

            foreach (int schema in schemas)
            {
                string archiveName = $"seforim-schema{schema.ToString(CultureInfo.InvariantCulture)}.db.zst";

                var single = release.FindAsset(archiveName);
                if (single != null && single.Size > 0)
                    return new SeforimLibraryDatabaseArchive(release.Tag, version, schema, archiveName, single.Size, single.Sha256, new[] { single });

                var manifestAsset = release.FindAsset(archiveName + ".manifest.json");
                if (manifestAsset != null)
                {
                    string json = await fetchText(manifestAsset, cancellationToken).ConfigureAwait(false);
                    var split = SeforimLibrarySplitManifest.Parse(json, archiveName);
                    var fromManifest = split?.ResolveAgainst(release, version, schema);
                    if (fromManifest != null) return fromManifest;
                    continue; // a manifest that does not hold up: this schema's parts cannot be trusted
                }

                var parts = release.Assets
                    .Select(a => (Asset: a, Match: PartName.Match(a.Name)))
                    .Where(p => p.Match.Success && p.Asset.Name.StartsWith(archiveName + ".part-", StringComparison.Ordinal))
                    .Select(p => (p.Asset, Index: int.Parse(p.Match.Groups[2].Value, CultureInfo.InvariantCulture)))
                    .OrderBy(p => p.Index)
                    .ToList();
                if (parts.Count > 0
                    && parts.Select((p, i) => p.Index == i).All(ok => ok)
                    && parts.All(p => p.Asset.Size > 0))
                {
                    var ordered = parts.Select(p => p.Asset).ToList();
                    return new SeforimLibraryDatabaseArchive(release.Tag, version, schema, archiveName, ordered.Sum(p => p.Size), null, ordered);
                }
            }
            return null;
        }

        private async Task<string> FetchTextAsync(SeforimLibraryReleaseAsset asset, CancellationToken cancellationToken) =>
            await _http.GetStringAsync(asset.DownloadUrl, cancellationToken).ConfigureAwait(false);

        /// <summary>The schema a database asset's name declares (single file, part or split
        /// manifest), or null when the name is not a database asset.</summary>
        private static int? SchemaOfName(string name)
        {
            foreach (var pattern in new[] { SingleArchiveName, SplitManifestName, PartName })
            {
                var match = pattern.Match(name);
                if (match.Success) return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            }
            return null;
        }

        /// <summary>"v32-20261010190601" -> 32; 0 when the tag carries no version.</summary>
        public static int VersionOfTag(string tag)
        {
            var match = TagVersion.Match(tag);
            return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
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
