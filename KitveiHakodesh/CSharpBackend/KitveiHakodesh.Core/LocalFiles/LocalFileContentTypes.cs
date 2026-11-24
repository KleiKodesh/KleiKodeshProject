#nullable enable
using System.IO;
using KitveiHakodesh.Core.Common;

namespace KitveiHakodesh.Core.LocalFiles;

/// <summary>
/// The HTTP Content-Type for a local file a host serves to the page (an opened HTML page and its
/// sibling CSS/JS/images, a PDF, a .txt). Shared by the service's HTTP host and the WebView2
/// host's served folders, so both answer the same type for the same file.
///
/// Text types carry the charset detected from the bytes (<see cref="TextEncodingDetector"/>):
/// legacy Hebrew files (Otzaria addin pages, .txt) are often Windows-1255, and a declared
/// charset in the response wins over sniffing and over the page's own &lt;meta charset&gt;.
/// </summary>
public static class LocalFileContentTypes
{
    /// <summary>True when the type needs a charset, i.e. the caller should read a head of the
    /// file and pass it to <see cref="For"/>.</summary>
    public static bool IsText(string path) => TextBaseType(path) != null;

    /// <summary>The full Content-Type for <paramref name="path"/>. For a text type,
    /// <paramref name="head"/> is the file's bytes (or a prefix of them, with
    /// <paramref name="truncated"/> true); it is ignored for binary types.</summary>
    public static string For(string path, byte[]? head, bool truncated)
    {
        string? text = TextBaseType(path);
        if (text != null)
            return text + "; charset=" + TextEncodingDetector.DetectCharset(head ?? new byte[0], truncated);

        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".pdf": return "application/pdf";
            case ".png": return "image/png";
            case ".jpg":
            case ".jpeg": return "image/jpeg";
            case ".gif": return "image/gif";
            case ".webp": return "image/webp";
            case ".ico": return "image/x-icon";
            case ".woff": return "font/woff";
            case ".woff2": return "font/woff2";
            case ".ttf": return "font/ttf";
            default: return "application/octet-stream";
        }
    }

    private static string? TextBaseType(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".htm":
            case ".html": return "text/html";
            case ".txt": return "text/plain";
            case ".css": return "text/css";
            case ".js":
            case ".mjs": return "text/javascript";
            case ".json": return "application/json";
            case ".svg": return "image/svg+xml";
            default: return null;
        }
    }
}
