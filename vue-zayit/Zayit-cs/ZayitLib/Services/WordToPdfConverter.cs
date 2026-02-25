using Microsoft.Office.Interop.Word;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Zayit.Services
{
    /// <summary>
    /// Handles all Word/HTML to PDF conversion logic using Word Interop.
    /// All conversions run on a background thread so the UI is never blocked.
    /// </summary>
    public static class WordToPdfConverter
    {
        public static Microsoft.Office.Interop.Word.Application wordApp = null;
        public static bool IsVstoMode;
        /// <summary>
        /// Converts a Word document (doc, docx, rtf, etc.) to PDF asynchronously.
        /// Returns the output PDF path on success, or the original path on failure.
        /// </summary>
        public static Task<string> ConvertWordToPdfAsync(string sourcePath, string outputPath)
        {
            return System.Threading.Tasks.Task.Run(() =>
            {
                Document doc = null;
                try
                {
                    if (wordApp == null)
                        wordApp = new Microsoft.Office.Interop.Word.Application();
                    wordApp.Visible = false;
                    wordApp.ScreenUpdating = false;

                    doc = wordApp.Documents.Open(sourcePath);
                    doc.ExportAsFixedFormat(outputPath, WdExportFormat.wdExportFormatPDF);
                    doc.Close(false);

                    Console.WriteLine($"[WordToPdfConverter] Converted {sourcePath} -> {outputPath}");
                    return outputPath;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WordToPdfConverter] Word to PDF conversion failed: {ex}");
                    return sourcePath;
                }
                finally
                {
                    if (doc != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(doc);
                    if (wordApp != null && IsVstoMode)
                    {
                        wordApp.Quit();
                        System.Runtime.InteropServices.Marshal.ReleaseComObject(wordApp);
                    }
                }
            });
        }

        /// <summary>
        /// Converts an HTML file (or .txt containing HTML) to PDF asynchronously.
        /// The HTML content is wrapped in a full RTL document shell before being
        /// pasted into Word via the clipboard.
        /// Returns the output PDF path on success, or the original path on failure.
        /// </summary>
        public static Task<string> ConvertHtmlToPdfAsync(string sourcePath, string outputPath)
        {
            return System.Threading.Tasks.Task.Run(() =>
            {
                Microsoft.Office.Interop.Word.Application wordApp = null;
                Document doc = null;
                try
                {
                    string rawHtml = File.ReadAllText(sourcePath);
                    string wrappedHtml = WrapWithRtlHtmlDocument(rawHtml);
                    string clipboardPayload = WrapWithHtmlClipboardFormat(wrappedHtml);

                    // Clipboard must be set on an STA thread; Task.Run uses MTA.
                    SetClipboardOnStaThread(clipboardPayload);

                    wordApp = new Microsoft.Office.Interop.Word.Application();
                    wordApp.Visible = false;
                    wordApp.ScreenUpdating = false;

                    doc = wordApp.Documents.Add();

                    var selection = wordApp.Selection;
                    selection.WholeStory();
                    selection.Delete();
                    selection.Paste();

                    doc.ExportAsFixedFormat(outputPath, WdExportFormat.wdExportFormatPDF);
                    doc.Close(false);

                    Console.WriteLine($"[WordToPdfConverter] Converted HTML {sourcePath} -> {outputPath}");
                    return outputPath;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WordToPdfConverter] HTML to PDF conversion failed: {ex}");
                    return sourcePath;
                }
                finally
                {
                    if (doc != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(doc);
                    if (wordApp != null)
                    {
                        wordApp.Quit();
                        System.Runtime.InteropServices.Marshal.ReleaseComObject(wordApp);
                    }
                }
            });
        }

        /// <summary>
        /// Returns true if the first 20 lines of a .txt file contain recognisable HTML tags.
        /// </summary>
        public static bool TxtFileContainsHtml(string filePath)
        {
            try
            {
                var lines = new List<string>();
                using (var reader = new StreamReader(filePath))
                {
                    string line;
                    int count = 0;
                    while ((line = reader.ReadLine()) != null && count < 20)
                    {
                        lines.Add(line);
                        count++;
                    }
                }

                var preview = string.Join(" ", lines);
                return System.Text.RegularExpressions.Regex.IsMatch(
                    preview,
                    @"<\s*(html|head|body|div|span|p|br|table|tr|td|th|ul|ol|li|a|h[1-6]|img|script|style|form|input|meta|link)\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WordToPdfConverter] HTML detection failed: {ex}");
                return false;
            }
        }

        // -------------------------------------------------------------------------
        // Private helpers
        // -------------------------------------------------------------------------

        /// <summary>
        /// Wraps an HTML fragment (or full HTML) in a complete RTL document shell.
        /// If the content already has an &lt;html&gt; tag, dir="rtl" is injected into
        /// it instead of double-wrapping.
        /// </summary>
        private static string WrapWithRtlHtmlDocument(string html)
        {
            bool isFullDocument = System.Text.RegularExpressions.Regex.IsMatch(
                html,
                @"<\s*html",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            if (isFullDocument)
            {
                // Inject dir="rtl" into existing <html> tag only if not already present
                return System.Text.RegularExpressions.Regex.Replace(
                    html,
                    @"(<\s*html)(?![^>]*\bdir\s*=)([^>]*>)",
                    "$1 dir=\"rtl\"$2",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );
            }

            // Word's clipboard paste engine swallows <br> tags — replace them with
            // proper paragraph breaks which Word handles reliably.
            html = System.Text.RegularExpressions.Regex.Replace(
                html,
                @"<\s*br\s*/?>",
                "</p><p dir=\"rtl\">",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            // Wrap fragment in a full RTL shell
            return $@"<!DOCTYPE html>
<html dir=""rtl"" lang=""he"">
<head>
  <meta charset=""UTF-8"" />
  <style>
    body {{
      direction: rtl;
      unicode-bidi: embed;
      font-family: Arial, sans-serif;
      text-align: justify;
    }}
  </style>
</head>
<body dir=""rtl"">
{html}
</body>
</html>";
        }

        /// <summary>
        /// Wraps HTML in the Windows clipboard HTML format header required by Word.
        /// </summary>
        private static string WrapWithHtmlClipboardFormat(string html)
        {
            const string headerTemplate =
                "Version:0.9\r\n" +
                "StartHTML:00000097\r\n" +
                "EndHTML:{0}\r\n" +
                "StartFragment:00000097\r\n" +
                "EndFragment:{0}\r\n";

            string body = $"<html><body><!--StartFragment-->{html}<!--EndFragment--></body></html>";
            string endPos = (97 + body.Length).ToString("D8");
            return string.Format(headerTemplate, endPos) + body;
        }

        /// <summary>
        /// Sets clipboard HTML data on a dedicated STA thread.
        /// The Windows clipboard API requires an STA thread; Task.Run uses MTA threads.
        /// </summary>
        private static void SetClipboardOnStaThread(string htmlClipboardData)
        {
            Exception staException = null;

            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    var dataObject = new DataObject();
                    dataObject.SetData(DataFormats.Html, htmlClipboardData);
                    Clipboard.SetDataObject(dataObject, true);
                }
                catch (Exception ex)
                {
                    staException = ex;
                }
            });

            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (staException != null)
                throw new InvalidOperationException("Clipboard set failed on STA thread.", staException);
        }
    }
}