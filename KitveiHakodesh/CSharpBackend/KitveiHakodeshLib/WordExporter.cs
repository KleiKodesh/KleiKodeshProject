using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using KitveiHakodeshLib.Pdf;
using Word = Microsoft.Office.Interop.Word;

namespace KitveiHakodeshLib
{
    /// <summary>
    /// Exports book content (provided as an HTML string) to a new Microsoft Word document.
    ///
    /// Writes the HTML to a temp file, then opens it directly in Word (visible).
    /// Word detection order:
    ///   1. Reuse WordToPdfConverter.HostApplication if set (VSTO scenario).
    ///   2. Bind to an already-running Word instance via Marshal.GetActiveObject.
    ///   3. Spawn a new Word instance.
    /// </summary>
    public static class WordExporter
    {
        public static Task ExportAsync(string html, string title = "")
        {
            return Task.Run(() => ExportCore(html, title));
        }

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

        private static string BuildSafeFileName(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return "kitvei_export_" + Guid.NewGuid().ToString("N");

            // Strip characters that are invalid in file names
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

            // Cap length to avoid hitting MAX_PATH with the temp folder prefix
            if (result.Length > 80)
                result = result.Substring(0, 80).TrimEnd();

            return result;
        }

        private static Word.Application AcquireWordApplication(out bool ownsApp)
        {
            ownsApp = false;

            // Reuse VSTO host application if available
            if (WordToPdfConverter.HostApplication != null)
                return WordToPdfConverter.HostApplication;

            // Bind to an already-running Word instance
            try
            {
                var running = (Word.Application)Marshal.GetActiveObject("Word.Application");
                if (running != null) return running;
            }
            catch (COMException) { }

            // Spawn a new Word instance
            ownsApp = true;
            var app = new Word.Application { Visible = false };
            app.DisplayAlerts = Word.WdAlertLevel.wdAlertsNone;
            return app;
        }
    }
}
