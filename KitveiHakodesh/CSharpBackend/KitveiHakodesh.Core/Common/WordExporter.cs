using System;
using System.IO;
using System.Text;
using Word = Microsoft.Office.Interop.Word;

namespace KitveiHakodesh.Core.Common
{
    /// <summary>
    /// Opens HTML content in Word as a new document, or pastes the clipboard into the document
    /// the user is already in.
    ///
    /// Word is found rather than injected — see <see cref="RunningWordFinder"/>.
    ///
    /// SYNCHRONOUS AND THROWING, both deliberately. The version this replaces wrapped every
    /// call in Task.Run and swallowed each failure into Debug.WriteLine, which is a no-op in
    /// Release: a user whose export silently did nothing had no way to find out why, and
    /// neither did anyone reading the code. Whether this belongs on a background thread is the
    /// caller's decision, and it is the caller who can tell the user it failed.
    ///
    /// net48 leg only — Office PIA.
    /// </summary>
    public static class WordExporter
    {
        /// <summary>Longest title we will build a file name from. Word has no trouble with more,
        /// but a temp file named after an entire chapter heading is unreadable in a folder.</summary>
        private const int MaxTitleLength = 80;

        /// <summary>
        /// Opens <paramref name="html"/> in Word as a visible document, ready for the user to
        /// edit and save wherever they like.
        ///
        /// Goes through a temp .html file because that is what Word's importer takes; the file
        /// is left behind on success — Word still has it open, and deleting it under a document
        /// the user is reading would be worse than a stray temp file.
        /// </summary>
        public static void Export(string html, string title = "")
        {
            if (html == null) throw new ArgumentNullException(nameof(html));

            string tempFile = Path.Combine(Path.GetTempPath(), BuildSafeFileName(title) + ".html");
            File.WriteAllText(tempFile, html, new UTF8Encoding(false));

            Word.Application? application = null;
            Word.Document? document = null;
            RunningWordFinder.Source source = RunningWordFinder.Source.Host;

            try
            {
                application = RunningWordFinder.Acquire(out source);
                application.Visible = true;

                document = application.Documents.Open(
                    tempFile,
                    ConfirmConversions: false,
                    ReadOnly: false,
                    AddToRecentFiles: false,
                    Visible: true);

                application.Activate();
            }
            catch (Exception)
            {
                // Leave nothing half-open behind: a document the user cannot see, or an
                // invisible Word we started, would both linger as a process they cannot close.
                TryClose(document);
                if (source == RunningWordFinder.Source.NewlyStarted) TryQuit(application);
                TryDelete(tempFile);
                throw;
            }
            finally
            {
                RunningWordFinder.ReleaseIfNotHost(application, source);
            }
        }

        /// <summary>
        /// Pastes whatever is on the clipboard at the cursor. The CALLER puts the content there
        /// first — this only performs the paste, because the clipboard is the user's and filling
        /// it is a separate decision from using it.
        ///
        /// Creates a blank document when Word has none open, so a paste never fails merely for
        /// want of somewhere to land.
        /// </summary>
        public static void PasteAtCursor()
        {
            Word.Application? application = null;
            RunningWordFinder.Source source = RunningWordFinder.Source.Host;

            try
            {
                application = RunningWordFinder.Acquire(out source);
                application.Visible = true;

                if (application.Documents.Count == 0)
                    application.Documents.Add();

                var selection = application.ActiveDocument.ActiveWindow.Selection;

                // MERGE FORMATTING, not a plain paste. Selection.Paste() does a
                // keep-source-formatting web import, and because the copied HTML carries no font
                // of its own — the reading font lives in a stylesheet the clipboard does not
                // travel with — Word substitutes its own web default as DIRECT character
                // formatting, which then overrides the destination document's styles.
                // wdFormatSurroundingFormattingWithEmphasis is Word's own "Merge Formatting":
                // the text takes the surrounding font and keeps its emphasis.
                try
                {
                    selection.PasteAndFormat(
                        Word.WdRecoveryType.wdFormatSurroundingFormattingWithEmphasis);
                }
                catch (Exception)
                {
                    // PasteAndFormat is format-sensitive and rejects some payloads plain Paste
                    // accepts. Losing the font match is much better than losing the paste.
                    selection.Paste();
                }

                application.Activate();
            }
            finally
            {
                RunningWordFinder.ReleaseIfNotHost(application, source);
            }
        }

        /// <summary>
        /// A file name from a document title: invalid characters dropped, length capped, and a
        /// generated name when nothing usable survives — a Hebrew title is entirely legal in a
        /// file name, so this removes very little in practice.
        /// </summary>
        private static string BuildSafeFileName(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return GeneratedName();

            char[] invalid = Path.GetInvalidFileNameChars();
            var safe = new StringBuilder(title.Length);
            foreach (char c in title.Trim())
            {
                if (Array.IndexOf(invalid, c) < 0) safe.Append(c);
            }

            string name = safe.ToString().Trim();
            if (name.Length == 0) return GeneratedName();

            return name.Length > MaxTitleLength
                ? name.Substring(0, MaxTitleLength).TrimEnd()
                : name;
        }

        private static string GeneratedName() => "export_" + Guid.NewGuid().ToString("N");

        private static void TryClose(Word.Document? document)
        {
            if (document == null) return;
            try { document.Close(false); }
            catch (Exception) { /* already closing, or Word is gone */ }
        }

        private static void TryQuit(Word.Application? application)
        {
            if (application == null) return;
            try { application.Quit(); }
            catch (Exception) { /* already quitting, or gone */ }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception) { /* Word may still hold it — a temp file is not worth a failure */ }
        }
    }
}
