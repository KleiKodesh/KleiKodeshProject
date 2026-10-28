#nullable enable
using System;
using System.IO;

namespace KitveiHakodesh.Core.LocalFiles;

/// <summary>
/// Writes a PDF.js incremental save straight back into the open PDF file (the viewer's TOC
/// autosave). PDF.js's saveDocument() returns the bytes it LOADED followed by one incremental
/// section, so only that section travels: it is written at <c>offset</c> (= the loaded length)
/// and the file is cut right after it.
///
/// Why this shape:
///   • the bytes before <c>offset</c> never change, so the viewer that is still range-reading
///     the same file keeps getting the bytes it expects;
///   • every save in one viewer session REPLACES the previous section instead of stacking a
///     new one, so autosaving after each edit does not grow the file endlessly — a session adds
///     one section at most.
///
/// Guard: <c>anchor</c> is the last bytes the viewer loaded before <c>offset</c>. They must
/// still be on disk at the same place, or the file changed under the viewer (replaced,
/// re-downloaded) and writing would corrupt it.
/// </summary>
public static class PdfIncrementalSaveWriter
{
    /// <summary>Returns null on success, else an error message (nothing written).</summary>
    public static string? WriteTail(string? path, long offset, byte[]? tail, byte[]? anchor)
    {
        // A drive-letter path (also compiled into the net48 host, which has no IsPathFullyQualified).
        if (string.IsNullOrWhiteSpace(path) || path!.Length < 3 || path[1] != ':' || (path[2] != '\\' && path[2] != '/'))
            return "invalid path";
        if (!string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase)) return "not a pdf";
        if (tail is null || tail.Length == 0) return "nothing to write";
        if (anchor is null || anchor.Length == 0 || offset < anchor.Length) return "missing anchor";
        if (!File.Exists(path)) return "file not found";

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length < offset) return "file changed on disk";

            byte[] onDisk = new byte[anchor.Length];
            fs.Seek(offset - anchor.Length, SeekOrigin.Begin);
            int read = 0;
            while (read < onDisk.Length)
            {
                int n = fs.Read(onDisk, read, onDisk.Length - read);
                if (n <= 0) return "file changed on disk";
                read += n;
            }
            for (int i = 0; i < onDisk.Length; i++)
                if (onDisk[i] != anchor[i]) return "file changed on disk";

            fs.Seek(offset, SeekOrigin.Begin);
            fs.Write(tail, 0, tail.Length);
            fs.SetLength(offset + tail.Length);
            fs.Flush(flushToDisk: true);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }
}
