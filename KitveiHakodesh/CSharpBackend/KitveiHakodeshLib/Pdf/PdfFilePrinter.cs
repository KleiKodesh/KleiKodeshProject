using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Text.Json;
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

        /// <summary>Show the print dialog and print. Runs on the UI thread.</summary>
        public static async Task<Outcome> PrintAsync(Control owner, CoreWebView2Environment environment, Uri address, int pageCount)
        {
            using (var dlg = new PrintDialog { UseEXDialog = true, AllowSomePages = pageCount > 1, AllowCurrentPage = false })
            {
                dlg.PrinterSettings.MinimumPage = 1;
                dlg.PrinterSettings.MaximumPage = Math.Max(1, pageCount);
                dlg.PrinterSettings.FromPage = 1;
                dlg.PrinterSettings.ToPage = Math.Max(1, pageCount);
                if (dlg.ShowDialog(owner) != DialogResult.OK) return Outcome.Cancelled;
                var ps = dlg.PrinterSettings;
                await WithHiddenWebView(owner, environment, address, async core =>
                {
                    var settings = ToWebViewSettings(core.Environment, ps);
                    CoreWebView2PrintStatus status = await core.PrintAsync(settings);
                    if (status != CoreWebView2PrintStatus.Succeeded)
                        throw new InvalidOperationException("print failed: " + status);
                });
            }
            return Outcome.Printed;
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
        /// its document has loaded; the window is closed afterwards in every case.
        /// </summary>
        internal static async Task WithHiddenWebView(Control owner, CoreWebView2Environment environment, Uri address, Func<CoreWebView2, Task> work)
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
                // Loaded = the browser's PDF viewer has its document: NavigationCompleted comes
                // before that (a print then is blank), so wait for the viewer's own frame (a
                // "webview" DevTools target) to get its title - the viewer sets it once the
                // document is loaded.
                var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnTarget(object _, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
                {
                    using (var json = JsonDocument.Parse(e.ParameterObjectAsJson))
                    {
                        if (!json.RootElement.TryGetProperty("targetInfo", out var info)) return;
                        bool viewer = info.TryGetProperty("type", out var type) && type.GetString() == "webview";
                        bool titled = info.TryGetProperty("title", out var title) && !string.IsNullOrEmpty(title.GetString());
                        if (viewer && titled) loaded.TrySetResult(true);
                    }
                }
                core.GetDevToolsProtocolEventReceiver("Target.targetCreated").DevToolsProtocolEventReceived += OnTarget;
                core.GetDevToolsProtocolEventReceiver("Target.targetInfoChanged").DevToolsProtocolEventReceived += OnTarget;
                await core.CallDevToolsProtocolMethodAsync("Target.setDiscoverTargets", "{\"discover\":true}");
                core.NavigationCompleted += (_, e) =>
                {
                    if (!e.IsSuccess) loaded.TrySetException(new IOException("the file could not be loaded: " + e.WebErrorStatus));
                };
                core.Navigate(address.AbsoluteUri);
                // A viewer that never reports (not a PDF, a broken file): give up instead of waiting forever.
                if (await Task.WhenAny(loaded.Task, Task.Delay(TimeSpan.FromSeconds(60))) != loaded.Task)
                    throw new TimeoutException("the PDF did not load");
                await loaded.Task;
                await work(core);
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
