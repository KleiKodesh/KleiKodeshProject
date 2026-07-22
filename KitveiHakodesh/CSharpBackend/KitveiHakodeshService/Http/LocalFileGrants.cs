using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace KitveiHakodeshService.Http;

/// <summary>
/// Capability store for the loopback <c>GET /file</c> endpoint — the security boundary for
/// serving local files, designed to be safe WITHOUT relying on any external allow-list
/// (e.g. the DocumentLocator index).
///
/// A path becomes servable only by first passing the token-gated <c>openLocalFile</c> op,
/// which fully validates it (see <see cref="TryGrant"/>) and mints an unguessable 128-bit
/// handle. <c>GET /file</c> then serves STRICTLY by handle — it never accepts a raw path, so
/// it has no path-traversal or arbitrary-file surface of its own. The handle IS the
/// capability: possessing a valid one proves it was minted through the authenticated op.
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

    private readonly ConcurrentDictionary<string, string> _byHandle = new(StringComparer.Ordinal);
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
        string handle = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)); // 128-bit, unguessable
        _byHandle[handle] = canonicalPath;
        _order.Enqueue(handle);
        while (_order.Count > MaxGrants && _order.TryDequeue(out string? old) && old is not null)
            _byHandle.TryRemove(old, out _);
        return handle;
    }

    /// <summary>Resolve a handle to its canonical path. False for unknown/evicted handles.</summary>
    public bool TryResolve(string? handle, out string path)
    {
        path = "";
        if (string.IsNullOrEmpty(handle)) return false;
        return _byHandle.TryGetValue(handle, out path!) && !string.IsNullOrEmpty(path);
    }
}
