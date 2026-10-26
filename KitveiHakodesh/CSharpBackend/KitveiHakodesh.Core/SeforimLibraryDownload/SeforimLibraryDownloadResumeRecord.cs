using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>
    /// What a .part file is a part of, written beside it. On restart the part is resumed only
    /// when this record names the SAME asset — same release, name, size and digest — and the
    /// server's ETag still matches (sent as If-Range). Anything else means the bytes belong to
    /// a different file, and appending to them would build a corrupt archive.
    ///
    /// JSON because a person may open it while diagnosing a stuck download; it is never wire.
    /// </summary>
    public sealed class SeforimLibraryDownloadResumeRecord
    {
        public string ReleaseTag { get; set; } = "";
        public string AssetName { get; set; } = "";
        public long Size { get; set; }
        public string? Sha256 { get; set; }

        /// <summary>The file host's strong ETag from the first response; null until seen.</summary>
        public string? ETag { get; set; }

        public bool Describes(string releaseTag, SeforimLibraryReleaseAsset asset) =>
            string.Equals(ReleaseTag, releaseTag, StringComparison.Ordinal)
            && string.Equals(AssetName, asset.Name, StringComparison.Ordinal)
            && Size == asset.Size
            && string.Equals(Sha256, asset.Sha256, StringComparison.Ordinal);

        /// <summary>The record at <paramref name="path"/>, or null when missing or unreadable.</summary>
        public static SeforimLibraryDownloadResumeRecord? Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize(File.ReadAllText(path),
                    SeforimLibraryDownloadResumeRecordJsonContext.Default.SeforimLibraryDownloadResumeRecord);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        public void Save(string path) =>
            File.WriteAllText(path, JsonSerializer.Serialize(this,
                SeforimLibraryDownloadResumeRecordJsonContext.Default.SeforimLibraryDownloadResumeRecord));
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(SeforimLibraryDownloadResumeRecord))]
    internal sealed partial class SeforimLibraryDownloadResumeRecordJsonContext : JsonSerializerContext;
}
