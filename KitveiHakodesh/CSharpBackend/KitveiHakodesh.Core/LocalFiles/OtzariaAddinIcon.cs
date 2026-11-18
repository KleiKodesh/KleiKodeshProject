using System;
using System.IO;
using System.Text.Json;
using KitveiHakodesh.Core.Common;
using KitveiHakodesh.Core.WebsiteIcons;
using MessagePack;

namespace KitveiHakodesh.Core.LocalFiles;

[MessagePackObject(keyAsPropertyName: true)]
public sealed class OtzariaAddinIconArgs
{
    /// <summary>The addin's entry-point file (as the file search returns it).</summary>
    public string Path { get; set; } = "";
}

/// <summary>
/// An Otzaria addin's own icon: the "icon" field of the manifest.json in the addin's folder
/// (e.g. "icon/icon.png", relative to that folder). Read-only; serves only an image file that
/// lies inside the addin's folder, so the op cannot be used to read arbitrary files.
/// </summary>
public static class OtzariaAddinIcon
{
    private const int MaxIconBytes = 1024 * 1024;
    private const int MaxLevelsUp = 4;

    /// <summary>The icon of the addin whose entry point is <paramref name="entryPath"/>; null when it has none.</summary>
    public static WebsiteIcon? Read(string entryPath)
    {
        try
        {
            string? folder = FindManifestFolder(entryPath);
            if (folder is null) return null;

            using var doc = JsonDocument.Parse(TextEncodingDetector.ReadAllText(Path.Combine(folder, "manifest.json")));
            if (!doc.RootElement.TryGetProperty("icon", out var iconEl) || iconEl.ValueKind != JsonValueKind.String) return null;
            string relative = (iconEl.GetString() ?? "").Replace('/', '\\').TrimStart('\\');
            if (relative.Length == 0) return null;

            string root = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            string iconPath = Path.GetFullPath(Path.Combine(root, relative));
            if (!iconPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;

            string? contentType = ContentTypeOf(iconPath);
            if (contentType is null) return null;
            var info = new FileInfo(iconPath);
            if (!info.Exists || info.Length == 0 || info.Length > MaxIconBytes) return null;
            return new WebsiteIcon { ContentType = contentType, Bytes = File.ReadAllBytes(iconPath) };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The nearest folder at or above the entry point's folder that holds a manifest.json.</summary>
    private static string? FindManifestFolder(string entryPath)
    {
        if (string.IsNullOrWhiteSpace(entryPath)) return null;
        string? dir = Path.GetDirectoryName(Path.GetFullPath(entryPath));
        for (int i = 0; i <= MaxLevelsUp && !string.IsNullOrEmpty(dir); i++)
        {
            if (File.Exists(Path.Combine(dir, "manifest.json"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static string? ContentTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".ico" => "image/x-icon",
        ".svg" => "image/svg+xml",
        _ => null,
    };
}
