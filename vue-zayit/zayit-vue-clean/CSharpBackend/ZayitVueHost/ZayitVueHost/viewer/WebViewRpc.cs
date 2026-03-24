using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZayitVueHost.viewer
{
    /// <summary>
    /// Generic JSON-RPC layer over the WebView2 postMessage channel.
    ///
    /// Register handlers with On() then call Attach() once CoreWebView2 is ready.
    /// Each handler receives the full message root and a reply callback.
    ///
    /// Routing:
    ///   - Messages with an "action" property are dispatched by action name.
    ///   - Messages with a "sql" property are dispatched to the "sql" handler.
    /// </summary>
    internal class WebViewRpc
    {
        public delegate Task HandlerFunc(JsonElement root, Action<object> reply);

        private readonly CoreWebView2 _core;
        private readonly Control _invokeTarget;
        private readonly Dictionary<string, HandlerFunc> _handlers = new Dictionary<string, HandlerFunc>();

        public WebViewRpc(CoreWebView2 core, Control invokeTarget)
        {
            _core = core;
            _invokeTarget = invokeTarget;
        }

        /// <summary>Register a handler for a named action (or "sql" for query messages).</summary>
        public void On(string action, HandlerFunc handler)
        {
            _handlers[action] = handler;
        }

        public void Attach()
        {
            _core.WebMessageReceived += OnMessageReceived;
        }

        /// <summary>Post a raw JSON string back to JS. Thread-safe.</summary>
        public void Post(string json)
        {
            if (_invokeTarget.InvokeRequired)
                _invokeTarget.Invoke(new Action(() => _core.PostWebMessageAsJson(json)));
            else
                _core.PostWebMessageAsJson(json);
        }

        /// <summary>Convenience: serialize an anonymous object and post it.</summary>
        public void Reply(object payload) => Post(JsonSerializer.Serialize(payload));

        // ── Internal ──────────────────────────────────────────────────────────

        private async void OnMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string id = null;
            try
            {
                using (var doc = JsonDocument.Parse(e.WebMessageAsJson))
                {
                    var root = doc.RootElement;
                    id = root.GetProperty("id").GetString();

                    string key = root.TryGetProperty("action", out var actionEl)
                        ? actionEl.GetString()
                        : root.TryGetProperty("sql", out _) ? "sql" : null;

                    System.Diagnostics.Debug.WriteLine("[WebViewRpc] received id=" + id + " key=" + key);

                    if (key == null || !_handlers.TryGetValue(key, out var handler))
                    {
                        System.Diagnostics.Debug.WriteLine("[WebViewRpc] no handler for key=" + key);
                        Reply(new { id, error = $"Unknown action: {key}" });
                        return;
                    }

                    string capturedId = id;
                    await handler(root, payload =>
                    {
                        System.Diagnostics.Debug.WriteLine("[WebViewRpc] replying id=" + capturedId);
                        Reply(AddId(payload, capturedId));
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[WebViewRpc] exception: " + ex.Message);
                if (id != null) Reply(new { id, error = ex.Message });
            }
        }

        // Merges { id } into the payload object via re-serialization
        private static string AddId(object payload, string id)
        {
            string inner = JsonSerializer.Serialize(payload);
            // Inject "id":"..." as first property
            return inner.Length > 2
                ? $"{{\"id\":\"{id}\",{inner.Substring(1)}"
                : $"{{\"id\":\"{id}\"}}";
        }
    }
}
