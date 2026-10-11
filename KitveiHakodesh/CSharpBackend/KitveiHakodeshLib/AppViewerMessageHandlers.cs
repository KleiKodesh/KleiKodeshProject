using KitveiHakodeshLib.Diagnostics;
using KitveiHakodeshLib.Dictionary;
using KitveiHakodeshLib.HebrewBooks;
using KitveiHakodeshLib.Settings;
using Microsoft.Web.WebView2.Core;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KitveiHakodeshLib
{
    // Bridge message dispatch and individual action handlers for AppViewer.
    // Owns: OnMessageReceived, OnMessageReceivedAsync, and all Handle* methods.
    public partial class AppViewer
    {
        private void OnDownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs e)
            => _hb.OnDownloadStarting(sender, e);

        private async void OnMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                await OnMessageReceivedAsync(e);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AppViewer] Unhandled exception in OnMessageReceived: " + ex);
            }
        }

        private async Task OnMessageReceivedAsync(CoreWebView2WebMessageReceivedEventArgs e)
        {
            string id = null;
            try
            {
                using (var doc = JsonDocument.Parse(e.WebMessageAsJson))
                {
                    var root = doc.RootElement;
                    id = root.GetProperty("id").GetString();
                    // No implicit "sql" fallback any more: a message without an action used to
                    // mean the raw-SQL passthrough, which is retired.
                    string action = root.TryGetProperty("action", out var a) ? a.GetString() : null;

                    // No data op is answered here. Seforim and dictionary queries go from the
                    // page straight to the KitveiHakodesh service through ServiceRpcRelay; this
                    // switch is for what only the host process can do (window, Word, files, chrome).
                    switch (action)
                    {
                        // The dialog only: the chosen path goes to the service from the page.
                        case "pickDbPath": HandlePickDbPathDialog(id); break;
                        case "resetSettings": HandleResetSettings(id); break;
                        // Replies before reloading on purpose, unlike DeleteFtsIndex: the caller
                        // cannot meaningfully await a navigation that destroys the page making the
                        // call, so the reply exists only to settle the promise. Both frontend
                        // callers ignore it for that reason.
                        // clearBrowsingData is opt-in — see HandleReload; only the app reset sets it.
                        case "reload":
                            _bridge.Reply(id, new { });
                            await HandleReload(
                                root.TryGetProperty("clearBrowsingData", out var cbd) &&
                                cbd.ValueKind == JsonValueKind.True);
                            break;
                        case "pickFile": _localFile.HandlePickFile(root, id, this); break;
                        case "pickFolder": _localFile.HandlePickFolder(id, this); break;
                        case "restoreLocalFile": await _localFile.HandleRestoreLocalFile(root, id); break;
                        case "openInDefaultApp": _localFile.HandleOpenInDefaultApp(root, id); break;
                        case "readTxtFileContent": await _localFile.HandleReadTxtFileContent(root, id); break;
                        case "disposeLocalFileHost": _localFile.HandleDisposeLocalFileHost(root, id); break;
                        case "appReady": HandleAppReady(id); break;
                        case "triggerHbSaveAs": _hb.HandleTriggerHbSaveAs(root, id); break;
                        case "revealHbLocalFile": _hb.HandleRevealHbLocalFile(root, id); break;
                        case "TogglePopOut": HandleTogglePopOut(id); break;
                        case "toggleFullscreen": HandleToggleFullscreen(id); break;
                        case "getWordSynonyms": HandleGetWordSynonyms(root, id); break;
                        case "getDiagnostics": HandleGetDiagnostics(id); break;
                        case "getFonts": await HandleGetFonts(id); break;
                        case "exportToWord": await HandleExportToWord(root, id); break;
                        case "pasteIntoWord": await HandlePasteIntoWord(root, id); break;
                        case "copyImageToClipboard": HandleCopyImageToClipboard(root, id); break;
                        case "setTheme": HandleSetTheme(root, id); break;
                        case "tabsChanged": HandleTabsChanged(root, id); break;
                        case "tabIcons": HandleTabIcons(root, id); break;
                        case "toggleChromeTabList": HandleToggleChromeTabList(root, id); break;
                        default: _bridge.Reply(id, new { error = "Unknown action: " + action }); break;
                    }
                }
            }
            catch (Exception ex)
            {
                // Full exception (type + message + stack + inner exceptions), not just
                // ex.Message — this is the only place a handler that throws before its
                // own try/catch gets reported, and
                // the truncated form made those failures undiagnosable from the field.
                if (id != null) _bridge.Reply(id, new { error = ex.ToString() });
            }
        }

        private void HandleGetWordSynonyms(JsonElement root, string id)
        {
            string word = root.TryGetProperty("word", out var w) ? w.GetString() : null;
            var groups = WordThesaurusProvider.GetSynonyms(word);
            _bridge.Reply(id, new { groups });
        }

        private void HandleTogglePopOut(string id)
        {
            _bridge.Reply(id, new { });
            if (InvokeRequired)
                Invoke(new Action(() => TogglePopOut?.Invoke(false)));
            else
                TogglePopOut?.Invoke(false);
        }

        private void HandleToggleFullscreen(string id)
        {
            _bridge.Reply(id, new { });
            if (InvokeRequired)
                Invoke(new Action(() => ToggleFormFullscreen()));
            else
                ToggleFormFullscreen();
        }

        // The window state saved just before entering fullscreen, so we can restore
        // it exactly (Normal or Maximized) when the user exits fullscreen.
        private FormWindowState _preFullscreenWindowState = FormWindowState.Normal;

        private void ToggleFormFullscreen()
        {
            // AppViewer itself stays in the task pane host even when popped out —
            // TaskPanePopOut moves _webView (the first child) into the floating form.
            // So we must look for the form that contains _webView, not AppViewer itself.
            Form hostForm = _webView.FindForm();

            // If not hosted in a window (e.g., still in the VSTO task pane), pop out first.
            if (hostForm == null)
            {
                TogglePopOut?.Invoke(true); // pop out and go fullscreen in one step
                return;
            }

            // Already in a floating window — just toggle fullscreen, never touch popout.
            var chromeTabsForm = hostForm as FluentChromeTabs.FluentChromeTabsForm;

            if (hostForm.FormBorderStyle == FormBorderStyle.None && hostForm.WindowState == FormWindowState.Maximized)
            {
                // Exit fullscreen — restore to whatever state we were in before entering.
                hostForm.FormBorderStyle = FormBorderStyle.Sizable;
                hostForm.WindowState = _preFullscreenWindowState;

                // Bring the native tab strip back once the frame is restored.
                if (chromeTabsForm != null)
                    chromeTabsForm.StripVisible = true;
            }
            else
            {
                // Save the current state before entering fullscreen so we can restore it on exit.
                _preFullscreenWindowState = hostForm.WindowState;

                // Fullscreen means no chrome at all — hide the native tab strip so the
                // WebView2 content fills the entire screen.
                if (chromeTabsForm != null)
                    chromeTabsForm.StripVisible = false;

                // Enter fullscreen — must be Normal before removing the border,
                // otherwise setting Maximized again does nothing and chrome doesn't get removed.
                if (hostForm.WindowState == FormWindowState.Maximized)
                    hostForm.WindowState = FormWindowState.Normal;

                hostForm.FormBorderStyle = FormBorderStyle.None;
                hostForm.WindowState = FormWindowState.Maximized;
            }
        }

        /// <summary>
        /// The native open-file dialog for a seforim database. Only the dialog: the page hands
        /// the chosen path to the service (setSeforimDbPath), which validates, persists and
        /// restarts. An installed service may have no desktop to show a dialog on, which is
        /// why this one stays with the window.
        /// </summary>
        private void HandlePickDbPathDialog(string id)
        {
            BeginInvoke(new Action(() =>
            {
                try
                {
                    using (var dlg = new OpenFileDialog())
                    {
                        dlg.Title  = SeforimDbPickerTexts.DialogTitle;
                        dlg.Filter = SeforimDbPickerTexts.Filter;
                        dlg.CheckFileExists = true;
                        if (dlg.ShowDialog(this) != DialogResult.OK)
                        {
                            _bridge.Reply(id, new { cancelled = true });
                            return;
                        }
                        _bridge.Reply(id, new { path = dlg.FileName });
                    }
                }
                catch (Exception ex)
                {
                    _bridge.Reply(id, new { error = ex.ToString() });
                }
            }));
        }

        /// <summary>
        /// Wipes the host's whole KitveiHakodesh settings subtree (window geometry, pop-out
        /// bounds, the theme mirror). The data settings are reset through the service by the
        /// page before it calls this. Clearing the persisted dark mode means the app comes back
        /// light, so the title bar goes light at once to match the theme Vue applies after the
        /// reload that follows.
        /// </summary>
        private void HandleResetSettings(string id)
        {
            try { Microsoft.VisualBasic.Interaction.DeleteSetting("KitveiHakodesh"); } catch { }
            if (InvokeRequired)
                Invoke(new Action(() => ApplyTitleBarTheme(false)));
            else
                ApplyTitleBarTheme(false);
            ApplySplashTheme(false);
            _bridge.Reply(id, new { });
        }

        private void HandleAppReady(string id)
        {
            _bridge.Reply(id, new { });
            _appReady = true;
            if (_pendingFilePath != null)
            {
                string path = _pendingFilePath;
                _pendingFilePath = null;
                _ = _localFile.OpenFileFromPathAsync(path);
            }
            if (_pendingSearch != null)
            {
                var (text, target) = _pendingSearch.Value;
                _pendingSearch = null;
                _bridge.PushEvent(new { @event = "hostSearch", target, text });
            }
            if (_pendingOpenBook != null)
            {
                var link = _pendingOpenBook;
                _pendingOpenBook = null;
                PushOpenBook(link);
            }
        }

        /// <summary>The Hebrew-capable font families on THIS machine, enumerated in this process
        /// through DirectWrite (Common/HebrewFonts, linked from Core): the picker needs no service
        /// round trip for something the window already knows. A cold enumeration walks the cmap of
        /// every typeface, so it runs off the UI thread; Reply marshals itself back.</summary>
        private async Task HandleGetFonts(string id)
        {
            var fonts = await Task.Run(() => KitveiHakodesh.Core.Common.HebrewFonts.GetHebrewFonts());
            _bridge.Reply(id, new { fonts });
        }

        private void HandleGetDiagnostics(string id)
        {
            var report = EnvironmentDiagnostics.Collect();
            _bridge.Reply(id, new { diagnostics = report });
        }

        // Both Word actions AWAIT the result and report it. They used to reply ok=true
        // immediately and drop the task on the floor, so every failure inside Word automation
        // ended at a Debug.WriteLine — a no-op in Release. A user whose paste did nothing had
        // no way to find out why, and neither did anyone reading a bug report about it.
        // WordExporter never throws: it returns null on success or a message to show.

        private async Task HandleExportToWord(JsonElement root, string id)
        {
            string html = root.TryGetProperty("html", out var h) ? h.GetString() ?? "" : "";
            string title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            string error = await WordExporter.ExportAsync(html, title);
            _bridge.Reply(id, error == null ? (object)new { ok = true } : new { error });
        }

        private async Task HandlePasteIntoWord(JsonElement root, string id)
        {
            string error = await WordExporter.PasteAtCursorAsync();
            _bridge.Reply(id, error == null ? (object)new { ok = true } : new { error });
        }

        /// <summary>
        /// Places a PNG image (sent as a data: URL from the frontend) on the Windows
        /// clipboard. Used by the PDF viewer's "copy page as image" — the browser's
        /// navigator.clipboard.write() for images is unreliable inside WebView2, so we
        /// set it host-side where System.Windows.Forms.Clipboard just works.
        /// </summary>
        private void HandleCopyImageToClipboard(JsonElement root, string id)
        {
            string dataUrl = root.TryGetProperty("dataUrl", out var d) ? d.GetString() ?? "" : "";
            bool ok = SetClipboardImageFromDataUrl(dataUrl);
            _bridge.Reply(id, new { ok });
        }

        // Clipboard access requires an STA thread; the WebView2 message pump may run
        // this handler on a thread that isn't STA, so do the work on a dedicated one.
        private static bool SetClipboardImageFromDataUrl(string dataUrl)
        {
            byte[] bytes;
            try
            {
                if (string.IsNullOrEmpty(dataUrl)) return false;
                int comma = dataUrl.IndexOf(',');
                string b64 = comma >= 0 ? dataUrl.Substring(comma + 1) : dataUrl;
                bytes = Convert.FromBase64String(b64);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Clipboard] bad data URL: " + ex.Message);
                return false;
            }

            bool ok = false;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    using (var ms = new System.IO.MemoryStream(bytes))
                    using (var source = System.Drawing.Image.FromStream(ms))
                    // Copy into a standalone bitmap so the image survives the stream being disposed.
                    using (var bmp = new System.Drawing.Bitmap(source))
                    {
                        // SetImage flushes to the OLE clipboard (persists after the bitmap is freed).
                        Clipboard.SetImage(bmp);
                        ok = true;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[Clipboard] SetImage failed: " + ex.Message);
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();
            return ok;
        }

    }
}
