using KitveiHakodeshLib.Db;
using KitveiHakodeshLib.Diagnostics;
using KitveiHakodeshLib.Dictionary;
using KitveiHakodeshLib.Helpers;
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
                    string action = root.TryGetProperty("action", out var a)
                        ? a.GetString()
                        : root.TryGetProperty("sql", out _) ? "sql" : null;

                    switch (action)
                    {
                        case "sql": await _db.HandleSql(root, id); break;
                        case "dict-sql": await HandleDictSql(root, id); break;
                        case "setDbPath": _db.HandleSetDbPath(root, id); break;
                        case "pickDbPath": _db.HandlePickDbPath(id, this); break;
                        case "resetSettings": _db.HandleResetSettings(id); break;
                        case "reload": _bridge.Reply(id, new { }); await HandleReload(); break;
                        case "pickFile": _localFile.HandlePickFile(id, this); break;
                        case "pickFolder": _localFile.HandlePickFolder(id, this); break;
                        case "restoreLocalFile": await _localFile.HandleRestoreLocalFile(root, id); break;
                        case "disposeLocalFileHost": _localFile.HandleDisposeLocalFileHost(root, id); break;
                        case "appReady": HandleAppReady(id); break;
                        case "restoreHbPdf": _hb.HandleRestoreHbPdf(root, id); break;
                        case "triggerHbDownload": _hb.HandleTriggerHbDownload(root, id); break;
                        case "triggerHbSaveAs": _hb.HandleTriggerHbSaveAs(root, id); break;
                        case "hbSearch": HandleHebrewBooksSearch(root, id); break;
                        case "GetFtsIndexingProgress": _search.HandleGetProgress(id); break;
                        case "FtsSearchStart": _search.HandleSearchStart(root, id); break;
                        case "FtsSearchCancel": _search.HandleSearchCancel(root, id); break;
                        case "DeleteFtsIndex": _search.HandleDeleteIndex(id); break;
                        case "ResetFtsIndex": _search.HandleResetFtsIndex(id); break;
                        case "TogglePopOut": HandleTogglePopOut(id); break;
                        case "toggleFullscreen": HandleToggleFullscreen(id); break;
                        case "getWordSynonyms": HandleGetWordSynonyms(root, id); break;
                        case "getFonts": HandleGetFonts(id); break;
                        case "getDiagnostics": HandleGetDiagnostics(id); break;
                        case "fileSystemSearchPageLoad": _fileSystemSearch.HandlePageLoad(id); break;
                        case "fileSystemSearch": _fileSystemSearch.HandleSearch(root, id); break;
                        case "ResetDocumentLocatorIndex": _fileSystemSearch.HandleReindex(id); break;
                        case "userSettingsQuery": await _userSettings.HandleQuery(root, id); break;
                        case "userSettingsExecute": await _userSettings.HandleExecute(root, id); break;
                        case "exportToWord": HandleExportToWord(root, id); break;
                        case "pasteIntoWord": HandlePasteIntoWord(root, id); break;
                        case "setTheme": HandleSetTheme(root, id); break;
                        default: _bridge.Reply(id, new { error = "Unknown action: " + action }); break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (id != null) _bridge.Reply(id, new { error = ex.Message });
            }
        }

        private void HandleGetWordSynonyms(JsonElement root, string id)
        {
            string word = root.TryGetProperty("word", out var w) ? w.GetString() : null;
            var groups = WordThesaurusProvider.GetSynonyms(word);
            _bridge.Reply(id, new { groups });
        }

        private async Task HandleDictSql(JsonElement root, string id)
        {
            if (!_dictionary.IsReady)
            {
                _bridge.Reply(id, new { error = "Dictionary database not available" });
                return;
            }
            string sql = root.GetProperty("sql").GetString();
            try
            {
                var rows = await Task.Run(() => _dictionary.Query(sql, DbHandler.ParseParamsStatic(root)));
                _bridge.Reply(id, new { rows });
            }
            catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); }
        }

        private async Task HandleWikiDictSql(JsonElement root, string id)
        {
            if (!_dictionary.IsWikiReady)
            {
                _bridge.Reply(id, new { error = "Wikidict database not available" });
                return;
            }
            string sql = root.GetProperty("sql").GetString();
            try
            {
                var rows = await Task.Run(() => _dictionary.QueryWiki(sql, DbHandler.ParseParamsStatic(root)));
                _bridge.Reply(id, new { rows });
            }
            catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); }
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
            if (hostForm.FormBorderStyle == FormBorderStyle.None && hostForm.WindowState == FormWindowState.Maximized)
            {
                // Exit fullscreen — restore to whatever state we were in before entering.
                hostForm.FormBorderStyle = FormBorderStyle.Sizable;
                hostForm.WindowState = _preFullscreenWindowState;
            }
            else
            {
                // Save the current state before entering fullscreen so we can restore it on exit.
                _preFullscreenWindowState = hostForm.WindowState;

                // Enter fullscreen — must be Normal before removing the border,
                // otherwise setting Maximized again does nothing and chrome doesn't get removed.
                if (hostForm.WindowState == FormWindowState.Maximized)
                    hostForm.WindowState = FormWindowState.Normal;

                hostForm.FormBorderStyle = FormBorderStyle.None;
                hostForm.WindowState = FormWindowState.Maximized;
            }
        }

        private void HandleGetFonts(string id)
        {
            _bridge.Reply(id, new { fonts = FontsProvider.GetHebrewFonts() });
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
        }

        private void HandleGetDiagnostics(string id)
        {
            var report = EnvironmentDiagnostics.Collect();
            _bridge.Reply(id, new { diagnostics = report });
        }

        private void HandleExportToWord(JsonElement root, string id)
        {
            _bridge.Reply(id, new { ok = true });
            string html = root.TryGetProperty("html", out var h) ? h.GetString() ?? "" : "";
            string title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            _ = WordExporter.ExportAsync(html, title);
        }

        private void HandlePasteIntoWord(JsonElement root, string id)
        {
            _bridge.Reply(id, new { ok = true });
            _ = WordExporter.PasteAtCursorAsync();
        }

        private void HandleHebrewBooksSearch(JsonElement root, string id)
        {
            if (!_hebrewBooksDb.IsInitialized)
            {
                _bridge.Reply(id, new { error = "Hebrew Books database not available" });
                return;
            }
            string query = root.TryGetProperty("query", out var q) ? (q.GetString() ?? "") : "";
            try
            {
                var results = _hebrewBooksDb.Search(query);
                _bridge.Reply(id, new { books = results });
            }
            catch (Exception ex)
            {
                _bridge.Reply(id, new { error = ex.Message });
            }
        }
    }
}
