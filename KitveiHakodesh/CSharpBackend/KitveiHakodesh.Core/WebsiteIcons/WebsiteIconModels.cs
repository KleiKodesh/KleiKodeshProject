using MessagePack;

namespace KitveiHakodesh.Core.WebsiteIcons;

[MessagePackObject(keyAsPropertyName: true)]
public sealed class WebsiteIconArgs
{
    /// <summary>The icon's absolute http(s) address.</summary>
    public string Url { get; set; } = "";
}

/// <summary>What <c>websiteIcon</c> returns: one icon's bytes (null when it could not be had).</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed class WebsiteIcon
{
    public string ContentType { get; set; } = "";
    public byte[] Bytes { get; set; } = [];
}
