using Microsoft.Web.WebView2.Core;
using System;

namespace KitveiHakodeshLib
{
    // Navigation inside a Torah website's iframe (WebViewPage.vue names it "site-<tab id>").
    //
    //  - A whitelisted address: allowed, and pushed to the page ("siteFrameNavigated") so the tab
    //    knows where the user is - tab restore, history cards and Back/Forward return there.
    //  - Any other web address: cancelled in the frame and opened in the system browser, so only
    //    listed sites ever show inside the app.
    //
    // Only the app's direct child frames named "site-..." are watched; the app's own frames
    // (local files, PDF.js) and a site's nested frames (players, sign-in widgets) are left alone.
    public partial class AppViewer
    {
        private const string SiteFrameNamePrefix = "site-";

        private void AttachSiteFrameNavigation()
        {
            _webView.CoreWebView2.FrameCreated += OnAppChildFrameCreated;
        }

        private void OnAppChildFrameCreated(object sender, CoreWebView2FrameCreatedEventArgs e)
        {
            // The name as the app set it (the iframe's name attribute), read once: Frame.Name follows
            // window.name, which a site's own script may change.
            string name = e.Frame.Name ?? "";
            if (!name.StartsWith(SiteFrameNamePrefix, StringComparison.Ordinal)) return;
            e.Frame.NavigationStarting += (s, args) => OnSiteFrameNavigationStarting(name, args);
        }

        private void OnSiteFrameNavigationStarting(string name, CoreWebView2NavigationStartingEventArgs e)
        {
            if (_webSites == null) return;
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri uri)) return;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return; // about:, data:, blob:

            if (_webSites.IsWebsiteAddress(uri.AbsoluteUri))
            {
                _bridge?.PushEvent(new { @event = "siteFrameNavigated", frame = name, url = uri.AbsoluteUri });
                return;
            }
            e.Cancel = true;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log("site frame: could not open " + uri + " externally: " + ex.Message);
            }
        }
    }
}
