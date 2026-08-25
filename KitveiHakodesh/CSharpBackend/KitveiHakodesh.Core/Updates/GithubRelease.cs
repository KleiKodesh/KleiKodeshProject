using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KitveiHakodesh.Core.Updates
{
    /// <summary>
    /// A release as the GitHub API reports it.
    ///
    /// JSON, and legitimately so: this is somebody else's wire format, not ours. Rule 0e bans
    /// re-encoding OUR payloads as JSON — it does not ask us to pretend an external API speaks
    /// MessagePack.
    ///
    /// Serialization is SOURCE-GENERATED through <see cref="GithubJsonContext"/>, because the
    /// reflection-based path cannot run under native AOT and would fail at the moment an update
    /// check happens rather than at build time.
    /// </summary>
    public sealed class GithubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("assets")]
        public List<GithubReleaseAsset> Assets { get; set; } = new List<GithubReleaseAsset>();

        [JsonPropertyName("body")]
        public string Body { get; set; } = "";

        [JsonPropertyName("published_at")]
        public DateTime PublishedAt { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";
    }

    /// <summary>One downloadable file attached to a release. <see cref="Size"/> is what makes a
    /// truncated download detectable — see the download verification.</summary>
    public sealed class GithubReleaseAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
    }

    /// <summary>Source-generated serialization for the GitHub response. Without this, native AOT
    /// has no way to read it.</summary>
    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(GithubRelease))]
    internal sealed partial class GithubJsonContext : JsonSerializerContext
    {
    }
}
