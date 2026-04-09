using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Word = Microsoft.Office.Interop.Word;

namespace KezayitLib.Pdf
{
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
            => Task.Run(() => { using (var conv = new WordConversion()) return conv.Convert(sourcePath, outputPath, isHtml: false, _hostCts.Token); }, _hostCts.Token);

        public static Task<string> ConvertHtmlToPdfAsync(string sourcePath, string outputPath)
            => Task.Run(() => { using (var conv = new WordConversion()) return conv.Convert(sourcePath, outputPath, isHtml: true, _hostCts.Token); }, _hostCts.Token);

        public static bool TxtFileContainsHtml(string filePath)
        {
            try
            {
                var lines = new List<string>();
                using (var reader = new StreamReader(filePath))
                {
                    string line; int count = 0;
                    while ((line = reader.ReadLine()) != null && count++ < 20) lines.Add(line);
                }
                return System.Text.RegularExpressions.Regex.IsMatch(string.Join(" ", lines),
                    @"<\s*(html|head|body|div|span|p|br|table|tr|td|th|ul|ol|li|a|h[1-6]|img|script|style)\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            catch { return false; }
        }

        private static string WrapWithRtlHtmlDocument(string html)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(html, @"<\s*html", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return System.Text.RegularExpressions.Regex.Replace(html, @"(<\s*html)(?![^>]*\bdir\s*=)([^>]*>)", "$1 dir=\"rtl\"$2", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            html = System.Text.RegularExpressions.Regex.Replace(html, @"<\s*br\s*/?>", "</p><p dir=\"rtl\">", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return "<!DOCTYPE html>\n<html dir=\"rtl\" lang=\"he\">\n<head><meta charset=\"UTF-8\" />" +
                   "<style>body{direction:rtl;font-family:Arial,sans-serif;text-align:justify}</style></head>" +
                   "<body dir=\"rtl\">\n" + html + "\n</body></html>";
        }

        private static string WrapWithHtmlClipboardFormat(string html)
        {
            string body = "<html><body><!--StartFragment-->" + html + "<!--EndFragment--></body></html>";
            string end = (97 + body.Length).ToString("D8");
            return "Version:0.9\r\nStartHTML:00000097\r\nEndHTML:" + end + "\r\nStartFragment:00000097\r\nEndFragment:" + end + "\r\n" + body;
        }

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

            public string Convert(string sourcePath, string outputPath, bool isHtml, CancellationToken ct = default)
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

                if (isHtml)
                {
                    _app.Options.UpdateLinksAtOpen = false;
                    string html = WrapWithRtlHtmlDocument(File.ReadAllText(sourcePath));
                    var dataObject = new DataObject();
                    dataObject.SetData(DataFormats.Html, WrapWithHtmlClipboardFormat(html));
                    Clipboard.SetDataObject(dataObject, true);
                    _doc = _app.Documents.Add(Visible: false);
                    var sel = _app.Selection;
                    sel.WholeStory(); sel.Delete(); sel.Paste();
                    Log("Paste: " + sw.ElapsedMilliseconds + "ms");
                }
                else
                {
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
                }

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
