using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KitveiHakodeshLib.Pdf
{
    /// <summary>
    /// PDF viewer Print: prints a PDF file from disk (or a downloaded one from its http(s) address),
    /// without its bytes passing through the page.
    ///
    /// The system print dialog picks the printer, copies and pages; then a hidden WebView2 (same
    /// environment as the app) loads the file itself (file:// URL - its own PDF engine reads it)
    /// and prints it there with those settings (CoreWebView2.PrintAsync). The hidden window
    /// goes away once the job is handed to the spooler.
    /// </summary>
    internal static class PdfFilePrinter
    {
        public enum Outcome { Printed, Cancelled }

        /// <summary>Show the print dialog and print. Runs on the UI thread. <paramref name="ct"/> ends the
        /// wait for the document (the print dialog itself is the user's own cancel).</summary>
        public static async Task<Outcome> PrintAsync(Control owner, CoreWebView2Environment environment, Uri address, int pageCount, CancellationToken ct = default(CancellationToken), string password = null)
        {
            using (var dlg = new PrintDialog { UseEXDialog = true, AllowSomePages = pageCount > 1, AllowCurrentPage = false })
            {
                dlg.PrinterSettings.MinimumPage = 1;
                dlg.PrinterSettings.MaximumPage = Math.Max(1, pageCount);
                dlg.PrinterSettings.FromPage = 1;
                dlg.PrinterSettings.ToPage = Math.Max(1, pageCount);
                if (dlg.ShowDialog(owner) != DialogResult.OK) return Outcome.Cancelled;
                var ps = dlg.PrinterSettings;
                // The host window closing (app shutting down) ends the wait too.
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    var host = owner?.FindForm();
                    FormClosedEventHandler onClosed = (_, __) => cts.Cancel();
                    if (host != null) host.FormClosed += onClosed;
                    try
                    {
                        await WithHiddenWebView(owner, environment, address, async (core, readiness) =>
                        {
                            var settings = ToWebViewSettings(core.Environment, ps);
                            // Belt and braces after a verified wait: a failed print that is not a
                            // printer problem is tried again (bounded by attempts, not time).
                            CoreWebView2PrintStatus status = CoreWebView2PrintStatus.OtherError;
                            for (int attempt = 0; attempt < MaxPrintAttempts && status == CoreWebView2PrintStatus.OtherError; attempt++)
                            {
                                cts.Token.ThrowIfCancellationRequested();
                                status = await core.PrintAsync(settings);
                            }
                            if (status != CoreWebView2PrintStatus.Succeeded)
                                throw new InvalidOperationException("print failed: " + status);
                        }, pageCount, cts.Token, password);
                    }
                    finally { if (host != null) host.FormClosed -= onClosed; }
                }
            }
            return Outcome.Printed;
        }

        private const int MaxPrintAttempts = 3;
        private const int MaxProbeAttempts = 20;

        /// <summary>
        /// The viewer internals could not be read (Edge changed them), so readiness is proven
        /// by output: print the LAST page (expectedPages) to a memory stream until exactly that
        /// one page comes out - the last page only exists once the whole document is known, and a
        /// one-page print stays cheap on a huge file. A too-early print fails or is empty. Bounded
        /// by attempts, no clock; nothing is written to disk.
        /// </summary>
        internal static async Task WaitUntilPrintable(CoreWebView2 core, int expectedPages, CancellationToken ct)
        {
            for (int attempt = 1; attempt <= MaxProbeAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                int pages = 0;
                try
                {
                    using (var stream = await core.PrintToPdfStreamAsync(LastPageSettings(core.Environment, expectedPages)))
                    {
                        if (stream != null)
                        {
                            var bytes = new byte[stream.Length];
                            int read = 0, n;
                            while (read < bytes.Length && (n = stream.Read(bytes, read, bytes.Length - read)) > 0) read += n;
                            pages = CountPdfPages(bytes);
                        }
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException)) { System.Diagnostics.Debug.WriteLine("[PdfPrint] probe " + attempt + " failed: " + ex.Message); }
                System.Diagnostics.Debug.WriteLine("[PdfPrint] fallback probe " + attempt + ": last page " + expectedPages + " gave " + pages + " page(s)");
                if (pages == 1) return;
            }
            throw new TimeoutException("the PDF never became printable");
        }

        private static CoreWebView2PrintSettings LastPageSettings(CoreWebView2Environment environment, int lastPage)
        {
            var s = environment.CreatePrintSettings();
            s.PageRanges = Math.Max(1, lastPage).ToString();
            return s;
        }

        internal static int CountPdfPages(byte[] pdf)
        {
            string text = System.Text.Encoding.GetEncoding(28591).GetString(pdf);
            return System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page(?![A-Za-z])").Count;
        }

        /// <summary>The dialog's choices as WebView2 print settings.</summary>
        internal static CoreWebView2PrintSettings ToWebViewSettings(CoreWebView2Environment environment, PrinterSettings ps)
        {
            var s = environment.CreatePrintSettings();
            s.PrinterName = ps.PrinterName;
            s.Copies = Math.Max(1, (int)ps.Copies);
            s.Collation = ps.Collate ? CoreWebView2PrintCollation.Collated : CoreWebView2PrintCollation.Uncollated;
            s.Orientation = ps.DefaultPageSettings.Landscape ? CoreWebView2PrintOrientation.Landscape : CoreWebView2PrintOrientation.Portrait;
            s.ColorMode = ps.DefaultPageSettings.Color ? CoreWebView2PrintColorMode.Color : CoreWebView2PrintColorMode.Grayscale;
            s.Duplex = ps.Duplex == System.Drawing.Printing.Duplex.Simplex ? CoreWebView2PrintDuplex.OneSided
                : ps.Duplex == System.Drawing.Printing.Duplex.Vertical ? CoreWebView2PrintDuplex.TwoSidedLongEdge
                : ps.Duplex == System.Drawing.Printing.Duplex.Horizontal ? CoreWebView2PrintDuplex.TwoSidedShortEdge
                : CoreWebView2PrintDuplex.Default;
            if (ps.PrintRange == PrintRange.SomePages)
                s.PageRanges = ps.FromPage == ps.ToPage ? $"{ps.FromPage}" : $"{ps.FromPage}-{ps.ToPage}";
            // The PDF prints as it is: no browser header/footer, no shrink of the page.
            s.ShouldPrintHeaderAndFooter = false;
            s.ShouldPrintBackgrounds = true;
            return s;
        }

        /// <summary>
        /// Load the PDF file in an off-screen WebView2 and run <paramref name="work"/> on it once
        /// the viewer really holds the document (see <see cref="PdfViewerReady"/>); throws
        /// <see cref="PdfLoadException"/> when it cannot (password, damaged, not a PDF). The window
        /// is closed afterwards in every case.
        /// </summary>
        internal static async Task WithHiddenWebView(Control owner, CoreWebView2Environment environment, Uri address,
            Func<CoreWebView2, PdfViewerReady.Result, Task> work, int expectedPages, CancellationToken ct = default(CancellationToken), string password = null)
        {
            if (address.IsFile && !File.Exists(address.LocalPath)) throw new FileNotFoundException("file not found", address.LocalPath);
            var form = new Form
            {
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                // Off screen and see-through: WebView2 needs a shown window to load and print.
                Location = new Point(-32000, -32000),
                Size = new Size(800, 600),
                Opacity = 0,
            };
            var view = new WebView2 { Dock = DockStyle.Fill };
            form.Controls.Add(view);
            try
            {
                form.Show(owner?.FindForm());
                await view.EnsureCoreWebView2Async(environment);
                var core = view.CoreWebView2;
                ct.ThrowIfCancellationRequested();
                var readiness = await PdfViewerReady.WaitAsync(core, () => core.Navigate(address.AbsoluteUri), ct,
                    m => System.Diagnostics.Debug.WriteLine("[PdfPrint] " + m),
                    probeCt => WaitUntilPrintable(core, expectedPages, probeCt), password);
                await work(core, readiness);
            }
            finally
            {
                view.Dispose();
                form.Close();
                form.Dispose();
            }
        }
    }
}
