using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace KitveiHakodeshLib
{
    // Moving a tab between desktop-app windows. Each window is its own page with its own
    // storage, so a tab travels as data (vue-frontend/src/webview-host/tabMove.ts):
    //   PackTabAsync (source) -> UnpackTabAsync or StartWithMovedTab (target) -> CloseMovedTab (source).
    // The host (KitveiHakodeshDesktopApp TabMoves) drives the order; this file is the transport.
    public partial class AppViewer
    {
        // The page answers each request with a 'tabMoveResult' message carrying its requestId.
        private readonly Dictionary<string, TaskCompletionSource<string>> _tabMoveRequests =
            new Dictionary<string, TaskCompletionSource<string>>();
        private int _tabMoveRequestCounter;


        /// <summary>
        /// Asks the page for everything a restart would restore for one tab (its entry, its
        /// per-tab state and reading positions), after it has saved where the reader is.
        /// Returns that as JSON, or null when the tab cannot move (gone, unsaved PDF edits,
        /// the page not answering in time). UI thread only.
        /// </summary>
        public Task<string> PackTabAsync(string tabId, int timeoutMs = 3000)
            => CallTabMoveAsync("__khsPackTab", JsonString(tabId), timeoutMs);

        /// <summary>
        /// Opens a packed tab in this page at <paramref name="pane1Index"/> among pane 1's tabs
        /// (-1 = last) and activates it. Returns the new tab's id once its state is stored, or
        /// null on failure (then this page has not kept it, or keeps a copy the source still has).
        /// </summary>
        public async Task<string> UnpackTabAsync(string movedJson, int pane1Index, int timeoutMs = 5000)
        {
            if (string.IsNullOrEmpty(movedJson)) return null;
            string result = await CallTabMoveAsync("__khsUnpackTab",
                JsonString(movedJson) + "," + pane1Index.ToString(System.Globalization.CultureInfo.InvariantCulture), timeoutMs);
            return ParseTabId(result);
        }

        private readonly TaskCompletionSource<bool> _pageReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes once the page has mounted and can take requests ('appReady').</summary>
        public Task PageReady => _pageReady.Task;

        /// <summary>Closes the source copy of a tab another window now has. Fire and forget.</summary>
        public void CloseMovedTab(string tabId)
        {
            if (IsDisposed || _webView.IsDisposed || _webView.CoreWebView2 == null) return;
            try { _ = _webView.CoreWebView2.ExecuteScriptAsync("window.__khsCloseMovedTab&&window.__khsCloseMovedTab(" + JsonString(tabId) + ")"); }
            catch (Exception) { /* the page went away: its tabs go with it */ }
        }

        private async Task<string> CallTabMoveAsync(string pageFunction, string argumentsJs, int timeoutMs)
        {
            if (IsDisposed || _webView.IsDisposed || _webView.CoreWebView2 == null) return null;
            string requestId = NextTabMoveRequestId();
            var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _tabMoveRequests[requestId] = answer;
            try
            {
                Task deadline = Task.Delay(timeoutMs);
                Task<string> script = _webView.CoreWebView2.ExecuteScriptAsync(
                    "typeof window." + pageFunction + "==='function'?window." + pageFunction + "(" + JsonString(requestId) + "," + argumentsJs + "):'none'");
                Task first = await Task.WhenAny(script, answer.Task, deadline);
                if (first == script && (script.Status != TaskStatus.RanToCompletion || script.Result != "\"pending\""))
                    return null; // an older page, or one that never mounted
                if (first == deadline) return null;
                await Task.WhenAny(answer.Task, deadline);
                return answer.Task.IsCompleted ? answer.Task.Result : null;
            }
            catch (Exception) { return null; }
            finally { _tabMoveRequests.Remove(requestId); }
        }

        // 'tabMoveResult' { requestId, ok, result | error }: result is the payload of the call
        // (a packed tab, or the new tab id), kept as raw JSON.
        private void HandleTabMoveResult(JsonElement root)
        {
            string requestId = root.TryGetProperty("requestId", out var r) ? r.GetString() : null;
            bool ok = root.TryGetProperty("ok", out var okElement) && okElement.ValueKind == JsonValueKind.True;
            string result = ok && root.TryGetProperty("result", out var value) ? value.GetRawText() : null;
            if (!ok && root.TryGetProperty("error", out var error))
                Console.WriteLine("[AppViewer] tab move refused by the page: " + error.GetString());
            CompleteTabMoveRequest(requestId, result);
        }

        private void CompleteTabMoveRequest(string requestId, string result)
        {
            if (requestId != null && _tabMoveRequests.TryGetValue(requestId, out var pending))
                pending.TrySetResult(result);
        }

        private string NextTabMoveRequestId() => "move" + (++_tabMoveRequestCounter).ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static string JsonString(string value) => KitveiHakodeshLib.Bridge.JsonPayloadWriter.ToJson(value ?? "");

        // The unpack result is the new tab id as a JSON string.
        private static string ParseTabId(string resultJson)
        {
            if (string.IsNullOrEmpty(resultJson)) return null;
            try
            {
                using (var doc = JsonDocument.Parse(resultJson))
                    return doc.RootElement.ValueKind == JsonValueKind.String ? doc.RootElement.GetString() : null;
            }
            catch (Exception) { return null; }
        }
    }
}
