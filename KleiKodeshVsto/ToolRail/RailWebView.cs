using KleiKodesh.Helpers;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KleiKodesh.ToolRail
{
    /// <summary>
    /// A web page from tool-rail.json, shown in the shared pane.
    ///
    /// Its browser data lives in its own folder (%LOCALAPPDATA%\KleiKodesh\rail-web),
    /// apart from Kitvei Hakodesh's, so a user's page can never touch that app's
    /// storage or its cached browser environment. All rail pages share one
    /// environment - one set of browser processes - which is cached only once it
    /// has succeeded: a failed start is retried on the next page, rather than
    /// failing every page for the rest of Word's life.
    /// </summary>
    internal sealed class RailWebView : UserControl
    {
        static Task<CoreWebView2Environment> _env;

        readonly string _url;
        readonly WebView2 _web = new WebView2 { Dock = DockStyle.Fill };
        readonly Label _error = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Visible = false };
        bool _started;

        public RailWebView(string url)
        {
            _url = url;
            Controls.Add(_web);
            Controls.Add(_error);
        }

        static Task<CoreWebView2Environment> Environment_()
        {
            var env = _env;
            if (env == null || env.IsFaulted || env.IsCanceled)
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KleiKodesh", "rail-web");
                _env = env = CoreWebView2Environment.CreateAsync(null, folder);
            }
            return env;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_started) return;
            _started = true;
            _ = InitAsync();
        }

        async Task InitAsync()
        {
            try
            {
                await _web.EnsureCoreWebView2Async(await Environment_());
                _web.CoreWebView2.Navigate(_url);
            }
            catch (Exception ex)
            {
                _web.Visible = false;
                _error.Text = AddinUiLanguage.T($"לא ניתן לפתוח את הדף:\n{_url}\n\n{ex.Message}",
                                                $"Could not open the page:\n{_url}\n\n{ex.Message}");
                _error.Visible = true;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _web.Dispose();
            base.Dispose(disposing);
        }
    }
}
