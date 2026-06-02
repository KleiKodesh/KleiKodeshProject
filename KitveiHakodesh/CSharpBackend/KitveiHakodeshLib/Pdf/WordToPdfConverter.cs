using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Word = Microsoft.Office.Interop.Word;

namespace KitveiHakodeshLib.Pdf
{
    /// <summary>
    /// Converts Word documents (.doc, .docx, .rtf) to PDF using the Microsoft Word
    /// COM interop. HTML and plain-text files are served directly via the virtual host
    /// and never pass through this converter.
    /// </summary>
    public static class WordToPdfConverter
    {
        /// <summary>
        /// Set this from VSTO startup (e.g. Globals.ThisAddIn.Application) to reuse
        /// the existing Word instance instead of spawning a new one.
        /// Leave null for standalone usage.
        /// </summary>
        public static Word.Application HostApplication { get; set; }

        private static CancellationTokenSource _hostCts = new CancellationTokenSource();

        /// <summary>
        /// Call from VSTO ThisAddIn_Shutdown to cancel any in-flight conversions.
        /// </summary>
        public static void CancelHostConversions()
        {
            _hostCts.Cancel();
            HostApplication = null;
            Log("HostApplication cleared, in-flight conversions cancelled");
        }

        public static Task<string> ConvertWordToPdfAsync(string sourcePath, string outputPath)
            => Task.Run(() => { using (var conv = new WordConversion()) return conv.Convert(sourcePath, outputPath, _hostCts.Token); }, _hostCts.Token);

        private static void Log(string msg) => System.Diagnostics.Debug.WriteLine("[Word] " + msg);

        // ── dedicated conversion class ────────────────────────────────────────────
        // Always instantiate with `using` — Dispose() guarantees the Word process
        // is fully released even if an exception is thrown mid-conversion.

        private sealed class WordConversion : IDisposable
        {
            private Word.Application _app;
            private Word.Document _doc;
            private bool _disposed;
            private bool _ownsApp;

            public string Convert(string sourcePath, string outputPath, CancellationToken ct = default)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();

                if (HostApplication != null)
                {
                    Log("VSTO host detected — using dedicated Word instance for conversion");
                }
                _app = new Word.Application { Visible = false, ScreenUpdating = false };
                _app.DisplayAlerts = Word.WdAlertLevel.wdAlertsNone;
                _ownsApp = true;
                Log("App ready: " + sw.ElapsedMilliseconds + "ms");

                ct.ThrowIfCancellationRequested();

                _app.Options.UpdateLinksAtOpen = false;
                _app.Options.CheckSpellingAsYouType = false;
                _app.Options.CheckGrammarAsYouType = false;
                _doc = _app.Documents.Open(sourcePath, ConfirmConversions: false,
                    ReadOnly: true, AddToRecentFiles: false, Visible: false, NoEncodingDialog: true);
                Log("Open: " + sw.ElapsedMilliseconds + "ms");
                _doc.Fields.Unlink();
                Log("Fields.Unlink: " + sw.ElapsedMilliseconds + "ms");
                foreach (Word.Hyperlink hl in _doc.Hyperlinks) try { hl.Delete(); } catch { }
                Log("Links cleared: " + sw.ElapsedMilliseconds + "ms");

                ct.ThrowIfCancellationRequested();

                _doc.SaveAs2(outputPath, Word.WdSaveFormat.wdFormatPDF);
                Log("Export: " + sw.ElapsedMilliseconds + "ms");
                Log("Total: " + sw.ElapsedMilliseconds + "ms");

                return outputPath;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                if (_doc != null)
                {
                    try { _doc.Close(false); } catch { }
                    Marshal.ReleaseComObject(_doc);
                    _doc = null;
                }

                if (_app != null)
                {
                    if (_ownsApp)
                    {
                        try { _app.Quit(); } catch { }
                        Marshal.ReleaseComObject(_app);
                    }
                    _app = null;
                }
            }
        }
    }
}
