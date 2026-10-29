using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DocConvertLib;

namespace KitveiHakodesh.Core.WordDocuments;

/// <summary>
/// Renders Word-family documents to something a browser can display, with a caching layer.
///
///   1. PRIMARY: Word COM automation → PDF (<see cref="AotWordConverter"/>, full fidelity).
///   2. FALLBACK: Office-free OOXML → HTML (<see cref="OoxmlHtmlConverter"/>, footnotes as
///      wiki-style endnotes) when Word fails or isn't installed — but only for the zip-based
///      OOXML formats (.docx/.docm/.dotx/.dotm). Binary .doc / .rtf / .odt can only go
///      through Word, so those surface an error when Word is unavailable.
///
/// Cache key = source filename + last-write-ticks, LRU-evicted to a small cap; a cached render
/// is reused across requests and restarts. Conversions are serialized (one Word at a time) and
/// run off the caller's thread, so a ~4s Word cold-start never blocks the caller.
/// </summary>
public sealed class WordDocumentRenderer
{
    private const int MaxCachedRenders = 12;
    private readonly string CacheDir;
    private readonly bool WordDisabled;
    private readonly Action<WordDocumentRenderLogLevel, string, Exception?> _log;

    /// <param name="cacheDirectory">Where renders are cached (the host decides; the service
    /// keeps it next to its exe).</param>
    /// <param name="wordDisabled">Skip the Word attempt entirely (exercises the fallback).</param>
    /// <param name="log">The host's log sink.</param>
    public WordDocumentRenderer(string cacheDirectory, bool wordDisabled,
        Action<WordDocumentRenderLogLevel, string, Exception?> log)
    {
        CacheDir = cacheDirectory;
        WordDisabled = wordDisabled;
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    // One Word instance at a time — automating several concurrently is fragile and pointless here.
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Cancellation source per in-flight render, keyed by source path. <see cref="Cancel"/>
    /// (the cancelConversion op) trips it: a queued render stops waiting; a running Word conversion
    /// ends the Word instance it started (never another) so the blocked COM call fails at once; then
    /// the partial cache file is deleted and the queue moves on. The HTML fallback (ConvertToHtml)
    /// is observed when it returns.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _renders =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Abort an in-flight render of <paramref name="sourcePath"/> if one is running.
    /// Returns true when a render was actually cancelled. Safe to call when nothing is running.</summary>
    public bool Cancel(string sourcePath)
    {
        string key = NormalizeKey(sourcePath);
        if (_renders.TryGetValue(key, out var cts))
        {
            try { cts.Cancel(); } catch { /* already disposed/cancelled */ }
            return true;
        }
        return false;
    }

    private static string NormalizeKey(string path)
    {
        try { return Path.GetFullPath(path); } catch { return path; }
    }

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

        // Register a cancellation source linked to the request ct so the separate cancelConversion
        // op (the ביטול button) can abort THIS render. Registered BEFORE the gate wait so a cancel
        // while queued behind another conversion is honoured too.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        string key = NormalizeKey(sourcePath);
        _renders[key] = linked;
        var rct = linked.Token;
        try
        {
            await _gate.WaitAsync(rct);
            try
            {
                if (File.Exists(pdf)) return (pdf, false);   // won the race while waiting
                if (WordDisabled && File.Exists(html)) return (html, true);
                Directory.CreateDirectory(CacheDir);

                if (!WordDisabled)
                {
                    try
                    {
                        _log(WordDocumentRenderLogLevel.Information, $"Converting {Path.GetFileName(sourcePath)} → PDF via Word…", null);
                        // Blocking COM. A cancel ends the Word instance this conversion started (only
                        // that one) so the blocked call fails at once; the queue never waits for it.
                        await ConvertWithWordAsync(sourcePath, pdf, rct);
                        EvictCache();
                        return (pdf, false);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) when (IsOoxml(sourcePath))
                    {
                        _log(WordDocumentRenderLogLevel.Warning, "Word conversion failed — falling back to OOXML→HTML", ex);
                    }
                }

                if (File.Exists(html)) return (html, true);  // reuse a prior fallback render

                if (!IsOoxml(sourcePath))
                    throw new InvalidOperationException(
                        "לא ניתן להמיר את הקובץ. ודא ש-Microsoft Word מותקן (המרה ללא Word נתמכת רק עבור docx).");

                _log(WordDocumentRenderLogLevel.Information, $"Rendering {Path.GetFileName(sourcePath)} → HTML (Office-free fallback)…", null);
                string rendered = await Task.Run(() => OoxmlHtmlConverter.ConvertToHtml(sourcePath), rct);
                rct.ThrowIfCancellationRequested();
                await File.WriteAllTextAsync(html, rendered, rct);
                EvictCache();
                return (html, true);
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Cancelled via ביטול (not host shutdown). Delete any partial output we produced so a
            // later cache-hit never serves a truncated/incomplete file; surface as "cancelled".
            _log(WordDocumentRenderLogLevel.Information, $"Conversion of {Path.GetFileName(sourcePath)} cancelled by user", null);
            TryDelete(pdf);
            TryDelete(html);
            throw new OperationCanceledException("cancelled");
        }
        finally
        {
            // Only this request's entry: a newer request for the same file may own the key now.
            _renders.TryRemove(new KeyValuePair<string, CancellationTokenSource>(key, linked));
        }
    }

    /// <summary>How long a cancelled conversion may take to end (its Word is terminated) before the
    /// queue moves on without it.</summary>
    private static readonly TimeSpan CancelledConversionGrace = TimeSpan.FromSeconds(5);

    /// <summary>Word -> PDF off the caller's thread. On cancel: the converter terminates its own
    /// Word, which ends the call within moments; the wait for that is bounded so the next request
    /// is never stuck behind a cancelled one, and a late finish deletes its output.</summary>
    private async Task ConvertWithWordAsync(string sourcePath, string pdf, CancellationToken cancellation)
    {
        // Word writes a private file, renamed into the cache only on success: a cancelled or
        // killed conversion never leaves a cut-off PDF under the cache name, and a late finish
        // of an abandoned conversion cannot touch a newer request's output.
        string partial = Path.ChangeExtension(pdf, "." + Guid.NewGuid().ToString("N") + ".part.pdf");
        Task conversion = Task.Run(() => AotWordConverter.Convert(sourcePath, partial, cancellation), CancellationToken.None);
        try
        {
            await conversion.WaitAsync(cancellation);
            cancellation.ThrowIfCancellationRequested();   // cancelled just as the convert returned
            File.Move(partial, pdf, overwrite: true);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Task ended = await Task.WhenAny(conversion, Task.Delay(CancelledConversionGrace));
            if (ended != conversion)
            {
                _log(WordDocumentRenderLogLevel.Warning, $"Cancelled conversion of {Path.GetFileName(sourcePath)} is still running; the queue moves on", null);
                _ = conversion.ContinueWith(_ => TryDelete(partial), TaskScheduler.Default);
            }
            throw;
        }
        finally
        {
            TryDelete(partial);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* in use / gone */ }
    }

    private string CachePathFor(string sourcePath, string ext)
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
        catch (Exception ex) { _log(WordDocumentRenderLogLevel.Debug, "word-cache eviction failed", ex); }
    }
}

/// <summary>How serious a renderer message is; the host maps it to its logger.</summary>
public enum WordDocumentRenderLogLevel
{
    Debug,
    Information,
    Warning,
}
