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
    public static Task<string?> ExportHtmlAsNewDocumentAsync(string? html, string? title)
    {
        if (string.IsNullOrEmpty(html)) return Task.FromResult<string?>("No HTML to export.");
        return AotWordPaste.ExportAsync(html!, title ?? "");
    }

    /// <summary>Pastes the Windows clipboard into Word at the cursor. Reuses a running Word and
    /// never quits it, so the user is left looking at their document.</summary>
    public static Task<string?> PasteClipboardAtCursorAsync() => AotWordPaste.PasteAsync();
}
