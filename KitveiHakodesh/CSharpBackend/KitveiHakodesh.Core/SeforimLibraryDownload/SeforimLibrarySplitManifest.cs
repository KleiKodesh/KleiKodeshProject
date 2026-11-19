using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>
    /// The manifest beside a split library (seforim-schemaN.db.zst.manifest.json): the archive's
    /// size and sha256, and its parts in order with their own size and sha256. Concatenating the
    /// parts gives the archive. Port of otzaria_library_updater models/split_asset.dart, with
    /// its checks: format 1, bare part names, no duplicates, sizes summing to the archive.
    ///
    /// Also read beside parts the user downloaded by hand (<see cref="SeforimLibraryArchiveImport"/>).
    /// </summary>
    public sealed class SeforimLibrarySplitManifest
    {
        private static readonly Regex Sha256Hex = new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
        [JsonPropertyName("archive")] public string Archive { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
        [JsonPropertyName("parts")] public List<SeforimLibrarySplitPart> Parts { get; set; } = new();

        /// <summary>The manifest, or null when it is malformed or describes another archive than
        /// <paramref name="expectedArchive"/> (null: any).</summary>
        public static SeforimLibrarySplitManifest? Parse(string json, string? expectedArchive)
        {
            SeforimLibrarySplitManifest? m;
            try { m = JsonSerializer.Deserialize(json, SeforimLibrarySplitManifestJsonContext.Default.SeforimLibrarySplitManifest); }
            catch (JsonException) { return null; }
            if (m == null || m.SchemaVersion != 1) return null;
            if (!IsBareName(m.Archive) || (expectedArchive != null && m.Archive != expectedArchive)) return null;
            if (m.Size <= 0 || !Sha256Hex.IsMatch(m.Sha256) || m.Parts.Count == 0) return null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in m.Parts)
            {
                if (!IsBareName(part.Name) || !seen.Add(part.Name)) return null;
                if (part.Size <= 0 || !Sha256Hex.IsMatch(part.Sha256)) return null;
            }
            if (m.Parts.Sum(p => p.Size) != m.Size) return null;
            return m;
        }

        /// <summary>The parts as published in <paramref name="release"/>; null when one is missing
        /// or its published size differs from the manifest's.</summary>
        public SeforimLibraryDatabaseArchive? ResolveAgainst(SeforimLibraryRelease release, int version, int schema)
        {
            var parts = new List<SeforimLibraryReleaseAsset>();
            foreach (var part in Parts)
            {
                var asset = release.FindAsset(part.Name);
                if (asset == null || (asset.Size > 0 && asset.Size != part.Size)) return null;
                parts.Add(asset with { Size = part.Size, Sha256 = part.Sha256 });
            }
            return new SeforimLibraryDatabaseArchive(release.Tag, version, schema, Archive, Size, Sha256, parts);
        }

        private static bool IsBareName(string name) =>
            name.Length > 0 && name != "." && name != ".." && name.IndexOfAny(new[] { '/', '\\' }) < 0;
    }

    public sealed class SeforimLibrarySplitPart
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    }

    [JsonSerializable(typeof(SeforimLibrarySplitManifest))]
    internal sealed partial class SeforimLibrarySplitManifestJsonContext : JsonSerializerContext;
}
