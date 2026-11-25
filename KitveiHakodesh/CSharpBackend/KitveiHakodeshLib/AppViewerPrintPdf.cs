using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace KitveiHakodeshLib
{
    // printPdfFile: prints a PDF file on disk through the browser engine's own PDF viewer
    // (full-quality vector print, system print dialog) instead of the page rasterizing pages.
    // A hidden browser view on the app's SAME browser environment (no new profile folder)
    // opens the file and shows the print dialog. It stays alive until the next print or until
    // this viewer closes, since the dialog gives no "closed" signal.
    public partial class AppViewer
    {
        private CoreWebView2Controller _printController;
        private bool _printDisposeHooked;

        private async Task HandlePrintPdfFile(JsonElement root, string id)
        {
            string filePath = root.TryGetProperty("filePath", out var fp) ? fp.GetString() : null;
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                _bridge.Reply(id, new { error = HostText.FileNotFound });
                return;
            }

            var env = _webView.CoreWebView2?.Environment;
            if (env == null) { _bridge.Reply(id, new { error = "browser not ready" }); return; }

            DisposePrintController();
            if (!_printDisposeHooked)
            {
                _printDisposeHooked = true;
                Disposed += (s, e) => DisposePrintController();
            }

            var controller = await env.CreateCoreWebView2ControllerAsync(Handle);
            _printController = controller;
            controller.IsVisible = false;
            var core = controller.CoreWebView2;

            var loaded = new TaskCompletionSource<bool>();
            core.NavigationCompleted += (s, e) => loaded.TrySetResult(e.IsSuccess);
            core.Navigate(new Uri(Path.GetFullPath(filePath)).AbsoluteUri);
            if (!await loaded.Task)
            {
                DisposePrintController();
                _bridge.Reply(id, new { error = "could not open file" });
                return;
            }

            // The system dialog prints the PDF itself; it also works with the view hidden.
            core.ShowPrintUI(CoreWebView2PrintDialogKind.System);
            _bridge.Reply(id, new { ok = true });
        }

        private void DisposePrintController()
        {
            var c = _printController;
            _printController = null;
            try { c?.Close(); } catch { }
        }
    }
}
