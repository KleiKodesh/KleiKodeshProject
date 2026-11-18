using KitveiHakodesh.Core.Settings;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Threading.Tasks;

namespace KitveiHakodeshLib
{
    // The Kiwix offline reader on the Websites page. It is not shipped with this app: it reuses the
    // Kiwix.js folder the Word add-in installs beside its own files (KleiKodeshVsto.csproj copies it
    // to {OutDir}\Kiwix.js), served the same way the Word pane serves it (https://kiwix-app/).
    // No folder, no Kiwix: the page only lists it when the host reports its address.
    public partial class AppViewer
    {
        private const string KiwixHost = "kiwix-app";
        private const string KiwixUrl = "https://" + KiwixHost + "/index.html";

        private static readonly string KiwixDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Kiwix.js");

        private static bool IsKiwixInstalled => File.Exists(Path.Combine(KiwixDir, "index.html"));

        private string _kiwixFactsScriptId;

        private void MapKiwixIfInstalled()
        {
            if (!IsKiwixInstalled) return;
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                KiwixHost, KiwixDir, CoreWebView2HostResourceAccessKind.Allow);
            _ = RegisterKiwixFactsAsync();
        }

        /// <summary>
        /// Kiwix.js reads its UI language and light/dark theme at startup from window.__khUiLanguage /
        /// window.__khDarkTheme (www/js/init.js - the Word Kiwix pane, KiwixWebview.cs, sets them the
        /// same way). Here they follow the app's own language and theme, and are re-registered when
        /// those change (AppViewerTheme), so the next Kiwix load uses the new ones. Kiwix frames only.
        /// </summary>
        internal async Task RegisterKiwixFactsAsync()
        {
            if (!IsKiwixInstalled) return;
            if (InvokeRequired) { BeginInvoke(new Action(() => _ = RegisterKiwixFactsAsync())); return; } // WebView2 is UI-thread only
            if (_webView?.CoreWebView2 == null) return;
            // Taken before the await, so two quick theme changes cannot both remove the same old script.
            string previous = _kiwixFactsScriptId;
            _kiwixFactsScriptId = null;
            if (previous != null)
                _webView.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(previous);
            string language = AppSettingsRegistry.GetUiLanguage() == "en" ? "en" : "he";
            bool dark = AppSettingsRegistry.GetDarkMode();
            string added = await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                "if (location.hostname === '" + KiwixHost + "') {" +
                " window.__khUiLanguage = '" + language + "';" +
                " window.__khDarkTheme = " + (dark ? "true" : "false") + "; }");
            if (_kiwixFactsScriptId != null) _webView.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(added); // a newer call won
            else _kiwixFactsScriptId = added;
        }

        /// <summary>Host fact for the page: Kiwix's address when installed (websitesList.ts adds it to the sites).</summary>
        private static string KiwixHostFact() =>
            IsKiwixInstalled ? "window.__webviewKiwixUrl=\"" + KiwixUrl + "\";" : "";

        private static bool IsKiwixAddress(Uri uri) =>
            string.Equals(uri.Host, KiwixHost, StringComparison.OrdinalIgnoreCase);
    }
}
