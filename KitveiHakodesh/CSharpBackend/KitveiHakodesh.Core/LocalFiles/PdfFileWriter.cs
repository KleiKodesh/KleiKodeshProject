#nullable enable
using System;
using System.IO;

namespace KitveiHakodesh.Core.LocalFiles;

/// <summary>
/// Writes a whole PDF to disk: the PDF viewer's Save / Save As and the page organizer's save.
///
/// Atomic: the bytes go to a temp file BESIDE the target (same folder, so the swap is a rename on
/// one volume and nothing is written outside the user's own folder), flushed, then swapped in
/// with File.Replace (or a move when the target does not exist yet). A crash or a full disk
/// mid-write leaves the original untouched. No .bak is kept: the swap already protects the
/// original, and a backup file next to every saved PDF would clutter the user's folders.
///
/// When the swap is refused because another reader holds the file without delete sharing
/// (e.g. the browser engine still streaming it), the temp file's bytes are copied over the
/// target in place instead - the same in-place write the outline autosave always did.
///
/// Guards: a drive-letter path, a .pdf name, bytes that start with "%PDF-", and - for an
/// in-place save - a file that already exists. Callers (host / service) add their own
/// "this file was opened or picked by the user" check.
/// </summary>
public static class PdfFileWriter
{
    /// <summary>Returns null on success, else an error message (the target is left as it was).</summary>
    public static string? Write(string? path, byte[]? bytes, bool mustExist)
    {
        // A drive-letter path (also compiled into the net48 host, which has no IsPathFullyQualified).
        if (string.IsNullOrWhiteSpace(path) || path!.Length < 3 || path[1] != ':' || (path[2] != '\\' && path[2] != '/'))
            return "invalid path";
        if (!string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase)) return "not a pdf";
        if (bytes is null || bytes.Length < 5 || bytes[0] != '%' || bytes[1] != 'P' || bytes[2] != 'D' || bytes[3] != 'F' || bytes[4] != '-')
            return "not a pdf";
        bool exists = File.Exists(path);
        if (mustExist && !exists) return "file not found";
        string? folder = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return "folder not found";

        string temp = Path.Combine(folder, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".saving");
        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }
            try { File.SetAttributes(temp, FileAttributes.Hidden); } catch { /* cosmetic only */ }

            if (!exists)
            {
                File.Move(temp, path);
                File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.Hidden);
                return null;
            }
            try
            {
                File.Replace(temp, path, null, ignoreMetadataErrors: true);
                return null;
            }
            catch (IOException) { return OverwriteInPlace(temp, path); }
            catch (UnauthorizedAccessException) { return OverwriteInPlace(temp, path); }
        }
        catch (Exception ex) { return ex.Message; }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
        }
    }

    /// <summary>The fallback when the target cannot be swapped: copy the (complete, flushed)
    /// temp file over it in place and cut it to the new length.</summary>
    private static string? OverwriteInPlace(string temp, string path)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(temp);
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            fs.Seek(0, SeekOrigin.Begin);
            fs.Write(bytes, 0, bytes.Length);
            fs.SetLength(bytes.Length);
            fs.Flush(flushToDisk: true);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }
}
