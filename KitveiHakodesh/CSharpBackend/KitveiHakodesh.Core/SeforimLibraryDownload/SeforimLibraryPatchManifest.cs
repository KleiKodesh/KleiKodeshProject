using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>
    /// The manifest beside each update patch in a SeforimLibrary release
    /// (patch-vA-vB.db.zst.manifest.json). One manifest is one edge in the update graph: it
    /// takes a database at <see cref="FromVersion"/> to <see cref="ToVersion"/>.
    ///
    /// Port of otzaria_library_updater models/delta_manifest.dart. Unknown fields are ignored,
    /// as there, for forward compatibility.
    /// </summary>
    public sealed class SeforimLibraryPatchManifest
    {
        /// <summary>The patch format this app applies (patch_meta.schema_version).</summary>
        public const int SupportedPatchFormatVersion = 4;

        [JsonPropertyName("fromVersion")] public int FromVersion { get; set; }
        [JsonPropertyName("toVersion")] public int ToVersion { get; set; }
        [JsonPropertyName("fromSchemaVersion")] public int FromSchemaVersion { get; set; }
        [JsonPropertyName("toSchemaVersion")] public int ToSchemaVersion { get; set; }
        [JsonPropertyName("patchFormatVersion")] public int? PatchFormatVersion { get; set; }

        /// <summary>A barrier, not a patch: nothing at or after FromVersion can reach ToVersion by delta.</summary>
        [JsonPropertyName("fullRebase")] public bool FullRebase { get; set; }

        [JsonPropertyName("fromContentHash")] public string FromContentHash { get; set; } = "";
        [JsonPropertyName("toContentHash")] public string ToContentHash { get; set; } = "";
        [JsonPropertyName("fromTableContentHashes")] public Dictionary<string, string>? FromTableContentHashes { get; set; }
        [JsonPropertyName("toTableContentHashes")] public Dictionary<string, string>? ToTableContentHashes { get; set; }
        [JsonPropertyName("patchFiles")] public List<SeforimLibraryPatchFile> PatchFiles { get; set; } = new();

        /// <summary>True when this edge can be applied here: a real patch, one zstd file, a
        /// format we read, and a schema this app reads on both ends.</summary>
        public bool IsApplicable =>
            !FullRebase
            && PatchFormatVersion is >= 1 and <= SupportedPatchFormatVersion
            && FromSchemaVersion == SeforimLibraryReleaseFeed.SupportedSchemaVersion
            && ToSchemaVersion == SeforimLibraryReleaseFeed.SupportedSchemaVersion
            && PatchFiles.Count == 1
            && PatchFiles[0].Compression == "zstd";

        /// <summary>Parses a manifest; null when it is not one.</summary>
        public static SeforimLibraryPatchManifest? Parse(string json)
        {
            try
            {
                var manifest = JsonSerializer.Deserialize(json, SeforimLibraryPatchManifestJsonContext.Default.SeforimLibraryPatchManifest);
                if (manifest == null || manifest.PatchFiles.Count == 0) return null;
                // The two table maps come as a pair; one alone is an inconsistent manifest.
                if (manifest.FromTableContentHashes == null || manifest.ToTableContentHashes == null)
                {
                    manifest.FromTableContentHashes = null;
                    manifest.ToTableContentHashes = null;
                }
                return manifest;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>One compressed patch file a manifest names.</summary>
    public sealed class SeforimLibraryPatchFile
    {
        [JsonPropertyName("file")] public string File { get; set; } = "";
        [JsonPropertyName("compression")] public string Compression { get; set; } = "";
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("uncompressedSha256")] public string UncompressedSha256 { get; set; } = "";
        [JsonPropertyName("uncompressedSize")] public long UncompressedSize { get; set; }
    }

    [JsonSerializable(typeof(SeforimLibraryPatchManifest))]
    internal sealed partial class SeforimLibraryPatchManifestJsonContext : JsonSerializerContext;
}
