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
    /// <param name="TargetVersion">The newest release version.</param>
    public sealed record SeforimLibraryUpdatePlan(int LocalVersion, int TargetVersion, IReadOnlyList<SeforimLibraryPatchStep> Patches)
    {
        public bool IsUpToDate => LocalVersion >= TargetVersion;

        /// <summary>Behind, and no chain of patches reaches the target: download the whole library.</summary>
        public bool NeedsFullDownload => !IsUpToDate && Patches.Count == 0;
    }

    /// <summary>
    /// Plans an update the way Otzaria does (library_update_planner.dart): every applicable
    /// patch manifest in the releases is an edge vA -> vB; the route is the fewest patches from
    /// the local version to the newest release, ties broken by the smaller download. No route -
    /// a fullRebase barrier, a missing link, a schema we cannot hash, an unversioned database -
    /// means a full download of the newest release, whatever its schema.
    ///
    /// Schemas are followed along the route: a node is (version, schema), and an edge leaves a
    /// node only when its fromSchemaVersion is that node's schema - so a schema-changing patch
    /// (v32 schema 6 -> v36 schema 7) is a step like any other.
    /// </summary>
    public sealed class SeforimLibraryUpdatePlanner
    {
        private static readonly Regex ManifestName =
            new Regex(@"^patch-v(\d+)-v(\d+)\.db\.zst\.manifest\.json$", RegexOptions.CultureInvariant);

        private static readonly Regex DatabaseAssetName =
            new Regex(@"^seforim-schema\d+\.db\.zst(\.part-\d+|\.manifest\.json)?$", RegexOptions.CultureInvariant);

        private readonly HttpClient _http;
        private readonly SeforimLibraryReleaseFeed _feed;

        public SeforimLibraryUpdatePlanner(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _feed = new SeforimLibraryReleaseFeed(http);
        }

        public async Task<SeforimLibraryUpdatePlan> PlanAsync(int localVersion, int? localSchema, CancellationToken cancellationToken)
        {
            var releases = await _feed.ListAsync(cancellationToken).ConfigureAwait(false);

            // The newest version a full download would give.
            int target = releases
                .Where(r => r.Assets.Any(a => DatabaseAssetName.IsMatch(a.Name)))
                .Select(r => SeforimLibraryReleaseFeed.VersionOfTag(r.Tag))
                .DefaultIfEmpty(localVersion)
                .Max();
            if (localVersion >= target)
                return new SeforimLibraryUpdatePlan(localVersion, target, Array.Empty<SeforimLibraryPatchStep>());

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
                    if (from < localVersion || to <= from || to > target) continue;

                    var manifest = await FetchManifestAsync(asset, cancellationToken).ConfigureAwait(false);
                    if (manifest == null) continue;
                    var patch = release.FindAsset(manifest.PatchFiles[0].File);
                    if (patch == null) continue;
                    edges.Add(new SeforimLibraryPatchStep(release.Tag, manifest, patch));
                }
            }

            return Plan(localVersion, localSchema, target, edges);
        }

        /// <summary>The plan for a library at (<paramref name="localVersion"/>, <paramref name="localSchema"/>)
        /// given the release's newest version and every published edge. Pure: no I/O.</summary>
        public static SeforimLibraryUpdatePlan Plan(int localVersion, int? localSchema, int target, IEnumerable<SeforimLibraryPatchStep> edges)
        {
            if (localVersion >= target)
                return new SeforimLibraryUpdatePlan(localVersion, target, Array.Empty<SeforimLibraryPatchStep>());
            var usable = edges.Where(e => e.Manifest.IsApplicable && e.Manifest.ToVersion > e.Manifest.FromVersion).ToList();
            return new SeforimLibraryUpdatePlan(localVersion, target, ShortestRoute(localVersion, localSchema, target, usable));
        }

        /// <summary>Fewest patches, then fewest bytes - Dijkstra over (version, schema) nodes, as
        /// _findBestPath does. A null schema (an old library without db_schema_version) matches
        /// the first edge's start schema.</summary>
        private static IReadOnlyList<SeforimLibraryPatchStep> ShortestRoute(int from, int? fromSchema, int to, List<SeforimLibraryPatchStep> edges)
        {
            var best = new Dictionary<(int Version, int? Schema), (int Hops, long Bytes, List<SeforimLibraryPatchStep> Path)>
            {
                [(from, fromSchema)] = (0, 0, new List<SeforimLibraryPatchStep>()),
            };
            var visited = new HashSet<(int, int?)>();
            while (true)
            {
                var open = best.Where(kv => !visited.Contains(kv.Key)).ToList();
                if (open.Count == 0) return Array.Empty<SeforimLibraryPatchStep>();
                var current = open.OrderBy(kv => kv.Value.Hops).ThenBy(kv => kv.Value.Bytes).First();
                if (current.Key.Version == to) return current.Value.Path;
                visited.Add(current.Key);

                foreach (var edge in edges.Where(e => e.Manifest.FromVersion == current.Key.Version))
                {
                    if (current.Key.Schema != null && edge.Manifest.FromSchemaVersion != current.Key.Schema) continue;
                    var next = (edge.Manifest.ToVersion, (int?)edge.Manifest.ToSchemaVersion);
                    if (visited.Contains(next)) continue;
                    var candidate = (current.Value.Hops + 1, current.Value.Bytes + edge.Patch.Size,
                        new List<SeforimLibraryPatchStep>(current.Value.Path) { edge });
                    if (!best.TryGetValue(next, out var known)
                        || candidate.Item1 < known.Hops
                        || (candidate.Item1 == known.Hops && candidate.Item2 < known.Bytes))
                        best[next] = candidate;
                }
            }
        }

        private async Task<SeforimLibraryPatchManifest?> FetchManifestAsync(SeforimLibraryReleaseAsset asset, CancellationToken cancellationToken)
        {
            string json = await _http.GetStringAsync(asset.DownloadUrl, cancellationToken).ConfigureAwait(false);
            return SeforimLibraryPatchManifest.Parse(json);
        }
    }
}
