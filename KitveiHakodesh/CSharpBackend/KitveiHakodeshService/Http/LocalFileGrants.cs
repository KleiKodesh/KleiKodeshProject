using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace KitveiHakodeshService.Http;

/// <summary>
/// Capability store for the loopback <c>GET /file</c> endpoint — the security boundary for
/// serving local files, designed to be safe WITHOUT relying on any external allow-list
/// (e.g. the DocumentLocator index).
///
/// A path becomes servable only by first passing the token-gated <c>openLocalFile</c> op,
/// which fully validates it and mints an unguessable 128-bit handle. <c>GET /file</c> then
/// serves STRICTLY by handle — it never accepts a raw path, so it has no path-traversal or
/// arbitrary-file surface of its own. The handle IS the capability: possessing a valid one
/// proves it was minted through the authenticated op.
///
/// Two grant kinds:
///   • File grant  — <see cref="Grant"/> / <see cref="TryResolve"/> — serves one specific file.
///     Used for PDFs and single-file direct-serve types where no sibling assets are needed.
///   • Folder grant — <see cref="GrantFolder"/> / <see cref="TryResolveFolder"/> — serves ANY
///     file inside a specific folder (and its sub-directories), validated to prevent traversal
///     outside the root. Used for HTML files so their sibling CSS/JS/image assets load at
///     /file/&lt;folderHandle&gt;/relative/path. The capability covers the entire folder tree that
///     was opened, matching the hosted mode's SetVirtualHostNameToFolderMapping behaviour.
///
/// Handles are per service instance (they die on restart — the client re-opens by path to get
/// a fresh one) and the store is bounded (FIFO eviction), so a caller can't grow it without
/// limit.
/// </summary>
public sealed class LocalFileGrants
{
    private const int MaxGrants = 512;

    // Types served DIRECTLY (the file's own bytes): the browser/pdf.js render these as-is.
    private static readonly string[] DirectExtensions = [".pdf", ".htm", ".html", ".txt"];
    // Types served after CONVERSION to PDF via Word (see WordConversionService). Matches the
    // hosted picker's convert-family, minus the dropped .wps/.mht/.mhtml/.xps.
    private static readonly string[] ConvertExtensions =
        [".doc", ".docx", ".docm", ".dot", ".dotx", ".dotm", ".odt", ".rtf"];

    // File handles: handle → absolute file path.
    private readonly ConcurrentDictionary<string, string> _byHandle = new(StringComparer.Ordinal);
    // Folder handles: handle → absolute folder path (trailing backslash normalised away).
    private readonly ConcurrentDictionary<string, string> _folderByHandle = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _order = new();

    /// <summary>
    /// Validate + classify a source path the app asked to open. Validation is strict and
    /// self-contained (no reliance on any external index):
    ///   • fully-qualified absolute path (no relative paths),
    ///   • not a UNC/device path (\\server\share, \\?\, \\.\),
    ///   • normalizes to itself — <c>GetFullPath(p) == p</c> — so any '..'/'.' traversal is rejected,
    ///   • extension is a supported direct OR convert type,
    ///   • the file exists.
    /// On success returns the canonical path and whether it must be Word-converted before serving.
    /// </summary>
    public bool TryValidateSource(string? rawPath, out string full, out bool needsConvert, out string error)
    {
        full = ""; needsConvert = false; error = "";

        string p = (rawPath ?? "").Trim().Trim('"').Replace('/', '\\');
        if (string.IsNullOrWhiteSpace(p)) { error = "empty path"; return false; }
        if (!Path.IsPathFullyQualified(p)) { error = "not an absolute path"; return false; }
        if (p.StartsWith(@"\\", StringComparison.Ordinal)) { error = "UNC/device paths are not allowed"; return false; }

        try { full = Path.GetFullPath(p); }
        catch { error = "invalid path"; return false; }
        if (!string.Equals(full, p, StringComparison.OrdinalIgnoreCase)) { error = "non-canonical path"; return false; }

        string ext = Path.GetExtension(full).ToLowerInvariant();
        bool direct = Array.IndexOf(DirectExtensions, ext) >= 0;
        needsConvert = Array.IndexOf(ConvertExtensions, ext) >= 0;
        if (!direct && !needsConvert) { error = "file type not allowed"; return false; }
        if (!File.Exists(full)) { error = "file not found"; return false; }
        return true;
    }

    /// <summary>
    /// Validate a path the app asked to hand off to the OS's default program. Applies the same
    /// traversal-safe checks as <see cref="TryValidateSource"/> (absolute, canonical, no
    /// UNC/device, exists) but does NOT restrict the extension: "open in default program" is a
    /// deliberate hand-off to whatever handler the OS registered, so any file type is allowed.
    /// Nothing is served over HTTP as a result — this only gates a shell-execute launch — so the
    /// GET /file capability model is unaffected.
    /// </summary>
    public bool TryValidateForShellOpen(string? rawPath, out string full, out string error)
    {
        full = ""; error = "";

        string p = (rawPath ?? "").Trim().Trim('"').Replace('/', '\\');
        if (string.IsNullOrWhiteSpace(p)) { error = "empty path"; return false; }
        if (!Path.IsPathFullyQualified(p)) { error = "not an absolute path"; return false; }
        if (p.StartsWith(@"\\", StringComparison.Ordinal)) { error = "UNC/device paths are not allowed"; return false; }

        try { full = Path.GetFullPath(p); }
        catch { error = "invalid path"; return false; }
        if (!string.Equals(full, p, StringComparison.OrdinalIgnoreCase)) { error = "non-canonical path"; return false; }
        if (!File.Exists(full)) { error = "file not found"; return false; }
        return true;
    }

    /// <summary>Mint an unguessable capability handle for a path that has ALREADY been validated
    /// (a validated direct source, or a PDF we generated in our own cache dir). The GET /file
    /// endpoint serves strictly by handle, so it never sees a raw client path.</summary>
    public string Grant(string canonicalPath)
    {
        string handle = MintHandle();
        _byHandle[handle] = canonicalPath;
        _order.Enqueue(handle);
        Evict();
        return handle;
    }

    /// <summary>Mint an unguessable capability handle for a folder. The GET /file endpoint can
    /// serve any file within (and below) this folder using the path
    /// <c>/file/&lt;handle&gt;/relative/path</c>. Traversal outside the root is rejected by
    /// <see cref="TryResolveFolder"/>.</summary>
    public string GrantFolder(string canonicalFolderPath)
    {
        // Normalise: strip trailing separator so prefix-check in TryResolveFolder is consistent.
        string folder = canonicalFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string handle = MintHandle();
        _folderByHandle[handle] = folder;
        _order.Enqueue(handle);
        Evict();
        return handle;
    }

    /// <summary>Resolve a handle to its canonical path. False for unknown/evicted handles.</summary>
    public bool TryResolve(string? handle, out string path)
    {
        path = "";
        if (string.IsNullOrEmpty(handle)) return false;
        return _byHandle.TryGetValue(handle, out path!) && !string.IsNullOrEmpty(path);
    }

    /// <summary>Resolve a folder handle + a relative path segment to a canonical file path,
    /// rejecting traversal attempts that would escape the folder root. Returns false when the
    /// handle is unknown/evicted, the relative path is empty or invalid, or the resolved path
    /// would lie outside the folder.</summary>
    public bool TryResolveFolder(string? handle, string? relativePath, out string fullPath)
    {
        fullPath = "";
        if (string.IsNullOrEmpty(handle) || string.IsNullOrEmpty(relativePath)) return false;
        if (!_folderByHandle.TryGetValue(handle, out string? root) || string.IsNullOrEmpty(root)) return false;

        // Normalise the relative path: replace forward slashes, strip leading separator.
        string rel = relativePath.Replace('/', Path.DirectorySeparatorChar)
                                 .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(rel)) return false;

        // Resolve to an absolute path and ensure it lives strictly inside the root.
        // GetFullPath collapses any '..' segments, so a traversal attempt like
        // '../../Windows/System32/calc.exe' resolves to something outside root and is rejected.
        string candidate;
        try { candidate = Path.GetFullPath(Path.Combine(root, rel)); }
        catch { return false; }

        // The candidate must start with the root path followed by a separator (or equal it
        // exactly for a file right in the root). This prevents a handle for 'C:\foo' from
        // serving 'C:\foobar\secret.txt' via a crafted relative path.
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
            return false;

        fullPath = candidate;
        return true;
    }

    private static string MintHandle() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)); // 128-bit, unguessable

    private void Evict()
    {
        while (_order.Count > MaxGrants && _order.TryDequeue(out string? old) && old is not null)
        {
            _byHandle.TryRemove(old, out _);
            _folderByHandle.TryRemove(old, out _);
        }
    }
}
