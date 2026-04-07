using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Word = Microsoft.Office.Interop.Word;

namespace KezayitLib.Pdf
{
    public static class WordToPdfConverter
    {
        public static Task<string> ConvertWordToPdfAsync(string sourcePath, string outputPath)
            => Task.Run(() => ConvertSync(sourcePath, outputPath, isHtml: false));

        public static Task<string> ConvertHtmlToPdfAsync(string sourcePath, string outputPath)
            => Task.Run(() => ConvertSync(sourcePath, outputPath, isHtml: true));

        private static string ConvertSync(string sourcePath, string outputPath, bool isHtml)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var app = new Word.Application { Visible = false, ScreenUpdating = false };
            app.DisplayAlerts = Word.WdAlertLevel.wdAlertsNone;
            Log("App ready: " + sw.ElapsedMilliseconds + "ms");

            Word.Document doc;
            if (isHtml)
            {
                app.Options.UpdateLinksAtOpen = false;
                string html = WrapWithRtlHtmlDocument(File.ReadAllText(sourcePath));
                var dataObject = new DataObject();
                dataObject.SetData(DataFormats.Html, WrapWithHtmlClipboardFormat(html));
                Clipboard.SetDataObject(dataObject, true);
                doc = app.Documents.Add(Visible: false);
                var sel = app.Selection;
                sel.WholeStory(); sel.Delete(); sel.Paste();
                Log("Paste: " + sw.ElapsedMilliseconds + "ms");
            }
            else
            {
                app.Options.UpdateLinksAtOpen = false;
                app.Options.CheckSpellingAsYouType = false;
                app.Options.CheckGrammarAsYouType = false;
                doc = app.Documents.Open(sourcePath, ConfirmConversions: false,
                    ReadOnly: true, AddToRecentFiles: false, Visible: false, NoEncodingDialog: true);
                Log("Open: " + sw.ElapsedMilliseconds + "ms");
                doc.Fields.Unlink();
                foreach (Word.Hyperlink hl in doc.Hyperlinks) try { hl.Delete(); } catch { }
                Log("Links cleared: " + sw.ElapsedMilliseconds + "ms");
            }

            try
            {
                doc.ExportAsFixedFormat(outputPath, Word.WdExportFormat.wdExportFormatPDF,
                    OpenAfterExport: false,
                    OptimizeFor: Word.WdExportOptimizeFor.wdExportOptimizeForOnScreen,
                    CreateBookmarks: Word.WdExportCreateBookmarks.wdExportCreateNoBookmarks,
                    DocStructureTags: false, BitmapMissingFonts: true, UseISO19005_1: false, IncludeDocProps: false);
                Log("Export: " + sw.ElapsedMilliseconds + "ms");
                return outputPath;
            }
            finally
            {
                try { doc.Close(false); } catch { }
                try { app.Quit(); } catch { }
            }
        }

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
    }
}
