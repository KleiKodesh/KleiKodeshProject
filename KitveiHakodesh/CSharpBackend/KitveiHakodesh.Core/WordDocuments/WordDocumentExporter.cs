using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using DocConvertLib;

namespace KitveiHakodesh.Core.WordDocuments;

/// <summary>
/// Sends content to Microsoft Word: assembled HTML opened as a new document (export), or the
/// clipboard pasted at the cursor of the running Word (paste). Both drive Word through
/// DocConvertLib's AOT-safe COM code on an STA thread. Each returns null on success or an error
/// message the host can show.
/// </summary>
public static class WordDocumentExporter
{
    /// <summary>Opens <paramref name="html"/> as a new Word document named after
    /// <paramref name="title"/>. Word imports HTML only by opening a file, so a temp .html is
    /// written and opened; it stays put because Word holds it open.</summary>
    /// <remarks><paramref name="requestId"/> (optional, chosen by the caller) lets
    /// <see cref="CancelExport"/> stop this export: a Word this export started is ended; a Word the
    /// user already had open is never closed - the export's own document is closed unsaved once Word
    /// finishes opening it. A cancelled export returns <see cref="CancelledResult"/>.</remarks>
    public static async Task<string?> ExportHtmlAsNewDocumentAsync(string? html, string? title, string? requestId = null)
    {
        if (string.IsNullOrEmpty(html)) return "No HTML to export.";
        return await RunCancellableAsync(requestId, cancellation => AotWordPaste.ExportAsync(html!, title ?? "", cancellation));
    }

    /// <summary>Runs one Word action that <see cref="CancelExport"/> can stop by <paramref name="requestId"/> (none: not cancellable).</summary>
    private static async Task<string?> RunCancellableAsync(string? requestId, Func<CancellationToken, Task<string?>> run)
    {
        if (string.IsNullOrEmpty(requestId)) return await run(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        if (!RunningExports.TryAdd(requestId!, cancellation)) return "A Word action with this request id is already running.";
        try { return await run(cancellation.Token); }
        finally { RunningExports.TryRemove(requestId!, out _); }
    }

    /// <summary>What a cancelled export returns (instead of an error message).</summary>
    public const string CancelledResult = AotWordPaste.CancelledResult;

    /// <summary>Exports that can still be cancelled, by the caller's request id.</summary>
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> RunningExports = new();

    /// <summary>Cancels the running export or paste with this request id. False when none is running
    /// (already finished, or never started). Idempotent.</summary>
    public static bool CancelExport(string? requestId)
    {
        if (string.IsNullOrEmpty(requestId) || !RunningExports.TryGetValue(requestId!, out CancellationTokenSource? cancellation)) return false;
        try { cancellation.Cancel(); return true; }
        catch (ObjectDisposedException) { return false; }    // finished between the lookup and the cancel
    }

    /// <summary>Pastes the Windows clipboard into Word at the cursor. Reuses a running Word and
    /// never quits it, so the user is left looking at their document.</summary>
    /// <remarks><paramref name="requestId"/>: <see cref="CancelExport"/> with it stops the paste. A Word
    /// this paste started is ended; the user's running Word is never ended - a cancel before the paste
    /// skips it, a paste Word has begun is left as Word made it. Cancelled: <see cref="CancelledResult"/>.</remarks>
    public static Task<string?> PasteClipboardAtCursorAsync(string? requestId = null)
        => RunCancellableAsync(requestId, cancellation => AotWordPaste.PasteAsync(cancellation));
}
