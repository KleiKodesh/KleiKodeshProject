using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Zayit.Services
{
    /// <summary>
    /// Handles all Word/HTML to PDF conversion logic using Word Interop.
    /// In VSTO mode, runs synchronously on calling thread using existing Word instance.
    /// In standalone mode, creates new Word instance on background STA thread.
    /// </summary>
    public static class WordToPdfConverter
    {
        /// <summary>
        /// Optional Word Application instance (set by VSTO add-in to reuse existing instance).
        /// Set this to a Microsoft.Office.Interop.Word.Application object when running inside VSTO.
        /// </summary>
        public static object WordApp { get; set; }

        /// <summary>
        /// Converts a Word document (doc, docx, rtf, etc.) to PDF asynchronously.
        /// Returns the output PDF path on success, or the original path on failure.
        /// Always runs on a dedicated STA thread to ensure proper COM apartment state.
        /// </summary>
        public static Task<string> ConvertWordToPdfAsync(System.Windows.Forms.Control control, string sourcePath, string outputPath)
        {
            var tcs = new TaskCompletionSource<string>();
            Form progressForm = null;

            // Create and show progress dialog on UI thread
            if (control != null && control.InvokeRequired == false)
            {
                progressForm = BuildInProgressForm(Path.GetFileName(sourcePath));
                progressForm.Show();
            }
            else if (control != null)
            {
                control.Invoke(new Action(() =>
                {
                    progressForm = BuildInProgressForm(Path.GetFileName(sourcePath));
                    progressForm.Show();
                }));
            }

            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    Console.WriteLine($"[WordToPdfConverter] Starting conversion: {sourcePath}");
                    var result = ConvertWordToPdfSync(sourcePath, outputPath);
                    Console.WriteLine($"[WordToPdfConverter] Conversion completed: {result}");
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WordToPdfConverter] Thread exception: {ex}");
                    tcs.SetResult(sourcePath);
                }
                finally
                {
                    // Close progress dialog on UI thread
                    if (progressForm != null)
                    {
                        if (progressForm.InvokeRequired)
                        {
                            progressForm.Invoke(new Action(() =>
                            {
                                progressForm.Close();
                                progressForm.Dispose();
                            }));
                        }
                        else
                        {
                            progressForm.Close();
                            progressForm.Dispose();
                        }
                    }
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            return tcs.Task;
        }

        private static Form BuildInProgressForm(string fileName)
        {
            var form = new Form
            {
                Text = "ממיר קובץ…",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                Size = new System.Drawing.Size(400, 100),
                MinimizeBox = false,
                MaximizeBox = false,
                ControlBox = false,
                RightToLeft = RightToLeft.Yes,
                RightToLeftLayout = true,
                TopMost = true
            };

            var label = new Label
            {
                Text = $"ממיר את \"{fileName}\" ל-PDF...",
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Regular),
                Padding = new Padding(20)
            };

            form.Controls.Add(label);
            return form;
        }


        private static string ConvertWordToPdfSync(string sourcePath, string outputPath)
        {
            dynamic wordApp = null;
            bool createdNewApp = false;
            dynamic doc = null;
            try
            {
                if (WordApp != null)
                {
                    wordApp = WordApp;
                    createdNewApp = false;
                }
                else
                {
                    var type = Type.GetTypeFromProgID("Word.Application");
                    if (type == null) throw new InvalidOperationException("Microsoft Word is not installed.");
                    wordApp = Activator.CreateInstance(type);
                    wordApp.Visible = false;
                    wordApp.ScreenUpdating = false;
                    wordApp.DisplayAlerts = 0; // wdAlertsNone
                    createdNewApp = true;
                }

                doc = wordApp.Documents.Open(
                    sourcePath,
                    ConfirmConversions: false,
                    ReadOnly: true,
                    AddToRecentFiles: false,
                    Visible: false,
                    NoEncodingDialog: true);

                doc.ExportAsFixedFormat(
                    outputPath,
                    17,    // wdExportFormatPDF
                    OpenAfterExport: false,
                    OptimizeFor: 1,   // wdExportOptimizeForOnScreen
                    CreateBookmarks: 0,
                    DocStructureTags: false,
                    BitmapMissingFonts: false,
                    UseISO19005_1: false);

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
                if (createdNewApp && wordApp != null)
                {
                    wordApp.Quit();
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(wordApp);
                }
            }
        }

        /// <summary>
        /// Converts an HTML file (or .txt containing HTML) to PDF asynchronously.
        /// The HTML content is wrapped in a full RTL document shell before being
        /// pasted into Word via the clipboard.
        /// Returns the output PDF path on success, or the original path on failure.
        /// Always runs on a dedicated STA thread to ensure proper COM apartment state.
        /// </summary>
        public static Task<string> ConvertHtmlToPdfAsync(System.Windows.Forms.Control control, string sourcePath, string outputPath)
        {
            var tcs = new TaskCompletionSource<string>();
            Form progressForm = null;

            // Create and show progress dialog on UI thread
            if (control != null && control.InvokeRequired == false)
            {
                progressForm = BuildInProgressForm(Path.GetFileName(sourcePath));
                progressForm.Show();
            }
            else if (control != null)
            {
                control.Invoke(new Action(() =>
                {
                    progressForm = BuildInProgressForm(Path.GetFileName(sourcePath));
                    progressForm.Show();
                }));
            }

            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    Console.WriteLine($"[WordToPdfConverter] Starting HTML conversion: {sourcePath}");
                    var result = ConvertHtmlToPdfSync(sourcePath, outputPath);
                    Console.WriteLine($"[WordToPdfConverter] HTML conversion completed: {result}");
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WordToPdfConverter] Thread exception: {ex}");
                    tcs.SetResult(sourcePath);
                }
                finally
                {
                    // Close progress dialog on UI thread
                    if (progressForm != null)
                    {
                        if (progressForm.InvokeRequired)
                        {
                            progressForm.Invoke(new Action(() =>
                            {
                                progressForm.Close();
                                progressForm.Dispose();
                            }));
                        }
                        else
                        {
                            progressForm.Close();
                            progressForm.Dispose();
                        }
                    }
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            return tcs.Task;
        }

        private static string ConvertHtmlToPdfSync(string sourcePath, string outputPath)
        {
            dynamic wordApp = null;
            bool createdNewApp = false;
            dynamic doc = null;
            try
            {
                string rawHtml = File.ReadAllText(sourcePath);
                string wrappedHtml = WrapWithRtlHtmlDocument(rawHtml);
                string clipboardPayload = WrapWithHtmlClipboardFormat(wrappedHtml);

                var dataObject = new DataObject();
                dataObject.SetData(DataFormats.Html, clipboardPayload);
                Clipboard.SetDataObject(dataObject, true);

                if (WordApp != null)
                {
                    wordApp = WordApp;
                    createdNewApp = false;
                }
                else
                {
                    var type = Type.GetTypeFromProgID("Word.Application");
                    if (type == null) throw new InvalidOperationException("Microsoft Word is not installed.");
                    wordApp = Activator.CreateInstance(type);
                    wordApp.Visible = false;
                    wordApp.ScreenUpdating = false;
                    wordApp.DisplayAlerts = 0; // wdAlertsNone
                    createdNewApp = true;
                }

                doc = wordApp.Documents.Add(Visible: false);
                var selection = wordApp.Selection;
                selection.WholeStory();
                selection.Delete();
                selection.Paste();

                doc.ExportAsFixedFormat(
                    outputPath,
                    17,    // wdExportFormatPDF
                    OpenAfterExport: false,
                    OptimizeFor: 1,
                    CreateBookmarks: 0,
                    DocStructureTags: false,
                    BitmapMissingFonts: false,
                    UseISO19005_1: false);

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
                if (createdNewApp && wordApp != null)
                {
                    wordApp.Quit();
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(wordApp);
                }
            }
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
    }
}