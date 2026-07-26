using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using KitveiHakodeshLib.Pdf;
using Word = Microsoft.Office.Interop.Word;

namespace KitveiHakodeshLib
{
    /// <summary>
    /// Exports book content (provided as an HTML string) to a new Microsoft Word document,
    /// or pastes from the Windows clipboard into the active Word document at the cursor.
    ///
    /// Word detection order for both operations:
    ///   1. Reuse WordToPdfConverter.HostApplication if set (VSTO scenario).
    ///   2. Bind to an already-running Word instance via Marshal.GetActiveObject.
    ///   3. Spawn a new Word instance.
    /// </summary>
    public static class WordExporter
    {
        // ── Public API ────────────────────────────────────────────────────────

        public static Task ExportAsync(string html, string title = "")
        {
            return Task.Run(() => ExportCore(html, title));
        }

        /// <summary>
        /// Pastes the current clipboard content at the cursor position in the active Word document.
        /// The caller is responsible for placing the HTML on the clipboard before calling this.
        /// If no document is open, creates a new blank document first.
        ///
        /// Pastes with "Merge Formatting" so the text adopts the destination document's font
        /// rather than the one Word's HTML importer would otherwise substitute. See
        /// PasteAtCursorCore.
        /// </summary>
        public static Task PasteAtCursorAsync()
        {
            return Task.Run(() => PasteAtCursorCore());
        }

        // ── Private implementation ────────────────────────────────────────────

        private static void ExportCore(string html, string title)
        {
            Word.Application app = null;
            Word.Document doc = null;
            bool ownsApp = false;

            string safeName = BuildSafeFileName(title);
            string tempFile = Path.Combine(Path.GetTempPath(), safeName + ".html");

            try
            {
                File.WriteAllText(tempFile, html, System.Text.Encoding.UTF8);

                app = AcquireWordApplication(out ownsApp);
                app.Visible = true;

                doc = app.Documents.Open(
                    tempFile,
                    ConfirmConversions: false,
                    ReadOnly: false,
                    AddToRecentFiles: false,
                    Visible: true);

                app.Activate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[WordExporter] Export failed: " + ex.Message);
                try { File.Delete(tempFile); } catch { }

                if (doc != null)
                {
                    try { doc.Close(false); } catch { }
                }

                if (ownsApp && app != null)
                {
                    try { app.Quit(); } catch { }
                }
            }
            finally
            {
                if (doc != null) Marshal.ReleaseComObject(doc);
                if (app != null && !ownsApp) Marshal.ReleaseComObject(app);
            }
        }

        private static void PasteAtCursorCore()
        {
            Word.Application app = null;
            bool ownsApp = false;

            try
            {
                app = AcquireWordApplication(out ownsApp);
                app.Visible = true;

                if (ownsApp)
                    System.Threading.Thread.Sleep(800);

                if (app.Documents.Count == 0)
                {
                    app.Documents.Add();
                    System.Threading.Thread.Sleep(300);
                }

                // The clipboard already contains the formatted HTML set by the frontend.
                //
                // Paste with "Merge Formatting" rather than plain Selection.Paste(). Paste()
                // does a Keep-Source-Formatting web import, and because the copied HTML carries
                // no font of its own (the reading font lives in a stylesheet the clipboard does
                // not travel with) Word substitutes its own web default — David / Times New
                // Roman — as direct character formatting, which then overrides the document's
                // styles. wdFormatSurroundingFormattingWithEmphasis (20) is Word's own
                // "Merge Formatting" option: it matches the pasted text to the formatting of
                // the surrounding text while keeping emphasis, so the text takes the
                // destination's font instead of an imported one.
                var selection = app.ActiveDocument.ActiveWindow.Selection;
                try
                {
                    selection.PasteAndFormat(Word.WdRecoveryType.wdFormatSurroundingFormattingWithEmphasis);
                }
                catch (Exception pasteEx)
                {
                    // PasteAndFormat can reject a clipboard payload that plain Paste accepts
                    // (the enum is documented against table cells and is format-sensitive), so
                    // fall back rather than silently dropping the user's paste.
                    System.Diagnostics.Debug.WriteLine("[WordExporter] PasteAndFormat failed, falling back to Paste: " + pasteEx.Message);
                    selection.Paste();
                }

                app.Activate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[WordExporter] PasteAtCursor failed: " + ex.Message);
            }
            finally
            {
                if (app != null && !ownsApp) Marshal.ReleaseComObject(app);
            }
        }

        private static string BuildSafeFileName(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return "kitvei_export_" + Guid.NewGuid().ToString("N");

            char[] invalid = Path.GetInvalidFileNameChars();
            var safe = new System.Text.StringBuilder();
            foreach (char c in title.Trim())
            {
                if (Array.IndexOf(invalid, c) < 0)
                    safe.Append(c);
            }

            string result = safe.ToString().Trim();
            if (result.Length == 0)
                return "kitvei_export_" + Guid.NewGuid().ToString("N");

            if (result.Length > 80)
                result = result.Substring(0, 80).TrimEnd();

            return result;
        }

        private static Word.Application AcquireWordApplication(out bool ownsApp)
        {
            ownsApp = false;

            // Reuse VSTO host application if available.
            if (WordToPdfConverter.HostApplication != null)
                return WordToPdfConverter.HostApplication;

            // Bind to an already-running Word instance.
            try
            {
                var running = (Word.Application)Marshal.GetActiveObject("Word.Application");
                if (running != null) return running;
            }
            catch (COMException) { }

            // Spawn a new Word instance.
            ownsApp = true;
            var app = new Word.Application { Visible = false };
            app.DisplayAlerts = Word.WdAlertLevel.wdAlertsNone;
            return app;
        }
    }
}
