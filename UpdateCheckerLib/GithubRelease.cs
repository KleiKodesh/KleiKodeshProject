using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace UpdateCheckerLib
{
    /// <summary>
    /// A release as the GitHub API reports it.
    ///
    /// JSON, and legitimately so: this is somebody else's wire format, not ours. Rule 0e bans
    /// re-encoding OUR payloads as JSON — it does not ask us to pretend an external API speaks
    /// MessagePack.
    ///
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
}
