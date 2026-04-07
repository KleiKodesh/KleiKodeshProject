using Microsoft.Web.WebView2.WinForms;
using System;
using System.Text.Json;
using System.Windows.Forms;

namespace KezayitLib.Bridge
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
                if (_control.InvokeRequired)
                    _control.Invoke(new Action(Send));
                else
                    Send();
            }
            catch (Exception) { /* Control disposed between check and invoke */ }
        }
    }
}
