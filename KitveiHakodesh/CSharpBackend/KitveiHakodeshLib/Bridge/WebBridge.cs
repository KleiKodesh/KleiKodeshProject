using Microsoft.Web.WebView2.WinForms;
using System;
using System.Text.Json;
using System.Windows.Forms;

namespace KitveiHakodeshLib.Bridge
{
    /// <summary>
    /// Thin wrapper around the WebView2 postMessage API.
    /// Passed to each handler so they can reply to JS without depending on AppViewer.
    /// </summary>
    public class WebBridge
    {
        private readonly WebView2 _webView;
        private readonly Control _control;

        public WebBridge(WebView2 webView, Control control)
        {
            _webView = webView;
            _control = control;
        }

        public void Reply(string id, object payload)
        {
            string json = JsonSerializer.Serialize(payload);
            string withId = json.Length > 2
                ? "{\"id\":\"" + id + "\"," + json.Substring(1)
                : "{\"id\":\"" + id + "\"}";
            Post(withId);
        }

        public void PushEvent(object payload)
        {
            Post(JsonSerializer.Serialize(payload));
        }

        private void Post(string json)
        {
            if (_control.IsDisposed || _webView.IsDisposed) return;

            void Send()
            {
                try
                {
                    if (!_control.IsDisposed && !_webView.IsDisposed && _webView.CoreWebView2 != null)
                        _webView.CoreWebView2.PostWebMessageAsJson(json);
                }
                catch (Exception) { /* WebView2 torn down during shutdown */ }
            }

            try
            {
                // BeginInvoke, not Invoke: a background caller must not block on a busy
                // UI thread — accelerator keys (Ctrl+Tab etc.) are serviced by that same
                // thread and any queued work directly delays keyboard input delivery.
                if (_control.InvokeRequired)
                    _control.BeginInvoke(new Action(Send));
                else
                    Send();
            }
            catch (Exception) { /* Control disposed between check and invoke */ }
        }
    }
}
