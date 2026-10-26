using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>One patch on the route: the manifest, and the release file it names.</summary>
    public sealed record SeforimLibraryPatchStep(string ReleaseTag, SeforimLibraryPatchManifest Manifest, SeforimLibraryReleaseAsset Patch);

    /// <summary>What it takes to bring a library up to date.</summary>
    /// <param name="Patches">The chain to apply, in order; empty when none reaches the target.</param>
    /// <param name="TargetVersion">The newest release version this app can use.</param>
    public sealed record SeforimLibraryUpdatePlan(int LocalVersion, int TargetVersion, IReadOnlyList<SeforimLibraryPatchStep> Patches)
    {
        public bool IsUpToDate => LocalVersion >= TargetVersion;

        /// <summary>Behind, and no chain of patches reaches the target: download the whole library.</summary>
        public bool NeedsFullDownload => !IsUpToDate && Patches.Count == 0;
    }

    /// <summary>
    /// Plans an update the way Otzaria does (library_update_planner.dart): every applicable
    /// patch manifest in the releases is an edge vA -> vB; the route is the fewest patches from
    /// the local version to the newest, ties broken by the smaller download. No route — a
    /// barrier, a missing link, an unversioned database — means a full download.
    ///
    /// Only schema-6 -> schema-6 patches count (<see cref="SeforimLibraryPatchManifest.IsApplicable"/>);
    /// the target is the newest version reachable in that schema.
    /// </summary>
    public sealed class SeforimLibraryUpdatePlanner
    {
        private static readonly Regex ManifestName =
            new Regex(@"^patch-v(\d+)-v(\d+)\.db\.zst\.manifest\.json$", RegexOptions.CultureInvariant);

        private static readonly Regex TagVersion = new Regex(@"^v(\d+)", RegexOptions.CultureInvariant);

        private readonly HttpClient _http;
        private readonly SeforimLibraryReleaseFeed _feed;

        public SeforimLibraryUpdatePlanner(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _feed = new SeforimLibraryReleaseFeed(http);
        }

        public async Task<SeforimLibraryUpdatePlan> PlanAsync(int localVersion, CancellationToken cancellationToken)
        {
            var releases = await _feed.ListAsync(cancellationToken).ConfigureAwait(false);

            // The newest version a full download would give.
            int target = releases
                .Where(r => r.FindAsset(SeforimLibraryReleaseFeed.DatabaseAssetName) != null)
                .Select(r => VersionOfTag(r.Tag))
                .DefaultIfEmpty(localVersion)
                .Max();

            var edges = new List<SeforimLibraryPatchStep>();
            foreach (var release in releases)
            {
                foreach (var asset in release.Assets)
                {
                    var name = ManifestName.Match(asset.Name);
                    if (!name.Success) continue;
                    int from = int.Parse(name.Groups[1].Value, CultureInfo.InvariantCulture);
                    int to = int.Parse(name.Groups[2].Value, CultureInfo.InvariantCulture);
                    // Only edges that can sit on a route forward from here are worth fetching.
                    if (from < localVersion || to <= from) continue;

                    var manifest = await FetchManifestAsync(asset, cancellationToken).ConfigureAwait(false);
                    if (manifest == null || !manifest.IsApplicable) continue;
                    var patch = release.FindAsset(manifest.PatchFiles[0].File);
                    if (patch == null) continue;
                    edges.Add(new SeforimLibraryPatchStep(release.Tag, manifest, patch));
                    target = Math.Max(target, manifest.ToVersion);
                }
            }

            if (localVersion >= target)
                return new SeforimLibraryUpdatePlan(localVersion, target, Array.Empty<SeforimLibraryPatchStep>());
            return new SeforimLibraryUpdatePlan(localVersion, target, ShortestRoute(localVersion, target, edges));
        }

        /// <summary>Fewest patches, then fewest bytes. Edges only go forward, so a plain
        /// shortest-path over versions in ascending order is exact.</summary>
        private static IReadOnlyList<SeforimLibraryPatchStep> ShortestRoute(int from, int to, List<SeforimLibraryPatchStep> edges)
        {
            var best = new Dictionary<int, (int Hops, long Bytes, SeforimLibraryPatchStep? Via)> { [from] = (0, 0, null) };
            foreach (int version in edges.Select(e => e.Manifest.FromVersion).Append(from).Distinct().OrderBy(v => v))
            {
                if (!best.TryGetValue(version, out var here)) continue;
                foreach (var edge in edges.Where(e => e.Manifest.FromVersion == version))
                {
                    var candidate = (here.Hops + 1, here.Bytes + edge.Patch.Size, (SeforimLibraryPatchStep?)edge);
                    int next = edge.Manifest.ToVersion;
                    if (!best.TryGetValue(next, out var known)
                        || candidate.Item1 < known.Hops
                        || (candidate.Item1 == known.Hops && candidate.Item2 < known.Bytes))
                        best[next] = candidate;
                }
            }

            if (!best.ContainsKey(to)) return Array.Empty<SeforimLibraryPatchStep>();
            var route = new List<SeforimLibraryPatchStep>();
            for (int v = to; best[v].Via is { } step; v = step.Manifest.FromVersion) route.Add(step);
            route.Reverse();
            return route;
        }

        private async Task<SeforimLibraryPatchManifest?> FetchManifestAsync(SeforimLibraryReleaseAsset asset, CancellationToken cancellationToken)
        {
            string json = await _http.GetStringAsync(asset.DownloadUrl, cancellationToken).ConfigureAwait(false);
            return SeforimLibraryPatchManifest.Parse(json);
        }

        /// <summary>"v32-20261010190601" -> 32; 0 when the tag carries no version.</summary>
        private static int VersionOfTag(string tag)
        {
            var match = TagVersion.Match(tag);
            return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        }
    }
}
