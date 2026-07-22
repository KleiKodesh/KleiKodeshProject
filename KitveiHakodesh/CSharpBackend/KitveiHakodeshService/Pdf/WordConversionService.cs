using DocConvertLib;

namespace KitveiHakodeshService.Pdf;

/// <summary>
/// Renders Word-family documents to something the browser can display, with a caching layer.
///
///   1. PRIMARY: Word COM automation → PDF (<see cref="AotWordConverter"/>, full fidelity).
///   2. FALLBACK: Office-free OOXML → HTML (<see cref="OoxmlHtmlConverter"/>, footnotes as
///      wiki-style endnotes) when Word fails or isn't installed — but only for the zip-based
///      OOXML formats (.docx/.docm/.dotx/.dotm). Binary .doc / .rtf / .odt can only go
///      through Word, so those surface an error when Word is unavailable.
///
/// Cache lives in a "convert-cache" folder next to the service executable (portable: moves
/// with the install, no per-user %LOCALAPPDATA% dependency). Key = source filename +
/// last-write-ticks, LRU-evicted to a small cap; a cached render is reused across requests
/// and restarts. Conversions are serialized (one Word at a time) and run off the request
/// thread, so a ~4s Word cold-start never blocks the pipe/HTTP accept loops.
///
/// Test seam: KHS_DISABLE_WORD=1 skips the Word attempt entirely (exercises the fallback).
/// </summary>
public sealed class WordConversionService(ILogger<WordConversionService> logger)
{
    private const int MaxCachedRenders = 12;
    // Folder next to the service exe (AppContext.BaseDirectory), so the cache is portable
    // with the install and self-contained rather than scattered under %LOCALAPPDATA%.
    private static readonly string CacheDir = Path.Combine(AppContext.BaseDirectory, "convert-cache");

    private static readonly bool WordDisabled =
        Environment.GetEnvironmentVariable("KHS_DISABLE_WORD") is "1" or "true";

    // One Word instance at a time — automating several concurrently is fragile and pointless here.
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>True when <paramref name="path"/> is a zip-based OOXML document the HTML
    /// fallback can parse (as opposed to binary .doc / .rtf / .odt, which only Word reads).</summary>
    private static bool IsOoxml(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".docx" or ".docm" or ".dotx" or ".dotm";

    /// <summary>Render <paramref name="sourcePath"/> to a cached PDF (Word) or HTML (fallback).
    /// Returns the cache path and whether it is HTML. Throws when no route can render it.</summary>
    public async Task<(string path, bool isHtml)> RenderAsync(string sourcePath, CancellationToken ct)
    {
        string pdf = CachePathFor(sourcePath, ".pdf");
        string html = CachePathFor(sourcePath, ".html");
        // A cached PDF always wins. A cached HTML (from a Word-less session) only satisfies
        // when Word is still unavailable — otherwise try Word and UPGRADE to the better PDF.
        if (File.Exists(pdf)) return (pdf, false);
        if (WordDisabled && File.Exists(html)) return (html, true);

        await _gate.WaitAsync(ct);
        try
        {
            if (File.Exists(pdf)) return (pdf, false);   // won the race while waiting
            if (WordDisabled && File.Exists(html)) return (html, true);
            Directory.CreateDirectory(CacheDir);

            if (!WordDisabled)
            {
                try
                {
                    logger.LogInformation("Converting {Src} → PDF via Word…", Path.GetFileName(sourcePath));
                    // Blocking COM — run it off the request thread.
                    await Task.Run(() => AotWordConverter.Convert(sourcePath, pdf), ct);
                    EvictCache();
                    return (pdf, false);
                }
                catch (Exception ex) when (IsOoxml(sourcePath))
                {
                    logger.LogWarning(ex, "Word conversion failed — falling back to OOXML→HTML");
                }
            }

            if (File.Exists(html)) return (html, true);  // reuse a prior fallback render

            if (!IsOoxml(sourcePath))
                throw new InvalidOperationException(
                    "לא ניתן להמיר את הקובץ. ודא ש-Microsoft Word מותקן (המרה ללא Word נתמכת רק עבור docx).");

            logger.LogInformation("Rendering {Src} → HTML (Office-free fallback)…", Path.GetFileName(sourcePath));
            string rendered = await Task.Run(() => OoxmlHtmlConverter.ConvertToHtml(sourcePath), ct);
            await File.WriteAllTextAsync(html, rendered, ct);
            EvictCache();
            return (html, true);
        }
        finally { _gate.Release(); }
    }

    private static string CachePathFor(string sourcePath, string ext)
    {
        long ticks;
        try { ticks = File.GetLastWriteTimeUtc(sourcePath).Ticks; } catch { ticks = 0; }
        string key = Path.GetFileNameWithoutExtension(sourcePath) + "-" + ticks;
        foreach (char c in Path.GetInvalidFileNameChars()) key = key.Replace(c, '_');
        if (key.Length > 80) key = key[..80];
        return Path.Combine(CacheDir, key + ext);
    }

    private void EvictCache()
    {
        try
        {
            var files = new DirectoryInfo(CacheDir).GetFiles();
            if (files.Length <= MaxCachedRenders) return;
            Array.Sort(files, (a, b) => a.LastAccessTimeUtc.CompareTo(b.LastAccessTimeUtc));
            for (int i = 0; i < files.Length - MaxCachedRenders; i++)
                try { files[i].Delete(); } catch { /* in use / gone */ }
        }
        catch (Exception ex) { logger.LogDebug(ex, "word-cache eviction failed"); }
    }
}
