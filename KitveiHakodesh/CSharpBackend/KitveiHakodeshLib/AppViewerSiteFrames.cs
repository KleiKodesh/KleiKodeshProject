using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

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

        // Tells the app once per page that the reader typed in a site (protects it from automatic
        // unloading - siteFrames.ts). Same text as SITE_FRAME_ACTIVITY_SCRIPT in siteFrameShortcutKeys.ts.
        private const string SiteFrameActivityScript = @"(function () {
  if (window === window.top || window.parent !== window.top || !/^site-/.test(window.name)) return;
  var told = false;
  window.addEventListener('input', function () {
    if (told) return;
    told = true;
    window.parent.postMessage({ type: 'kitveiSiteInput' }, '*');
  }, true);
})();";

        private Timer _lowMemoryTimer;
        private IntPtr _lowMemoryNotification = IntPtr.Zero;

        private void AttachSiteFrameNavigation()
        {
            _webView.CoreWebView2.FrameCreated += OnAppChildFrameCreated;
            _ = _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(SiteFrameActivityScript);
            WatchLowMemory();
            WatchMinimized();
        }

        // ── Memory per site (the leave prompt and low-memory choice use it) ──────────────────

        /// <summary>
        /// Reply: { frames: { "site-<tab id>": MB } } - the private memory of the renderer process(es)
        /// showing each website frame, from WebView2's process list. A process shared by several
        /// site frames is split between them.
        /// </summary>
        internal async Task HandleSiteFrameMemory(string id)
        {
            var frames = new Dictionary<string, double>();
            try
            {
                IReadOnlyList<CoreWebView2ProcessExtendedInfo> infos = await _webView.CoreWebView2.Environment.GetProcessExtendedInfosAsync();
                foreach (CoreWebView2ProcessExtendedInfo info in infos)
                {
                    var names = info.AssociatedFrameInfos
                        .Select(f => f.Name ?? "")
                        .Where(n => n.StartsWith(SiteFrameNamePrefix, StringComparison.Ordinal))
                        .Distinct()
                        .ToList();
                    if (names.Count == 0) continue;
                    double mb;
                    try { mb = System.Diagnostics.Process.GetProcessById(info.ProcessInfo.ProcessId).PrivateMemorySize64 / 1048576.0; }
                    catch (Exception) { continue; } // the process just ended
                    foreach (string name in names)
                        frames[name] = (frames.TryGetValue(name, out double sum) ? sum : 0) + mb / names.Count;
                }
            }
            catch (Exception ex)
            {
                Log("site frame memory: " + ex.Message);
            }
            _bridge.Reply(id, new { frames });
        }

        // ── Windows low memory: the page unloads one hidden site per signal (siteFrames.ts) ───

        [DllImport("kernel32.dll")] private static extern IntPtr CreateMemoryResourceNotification(int notificationType);
        [DllImport("kernel32.dll")] private static extern bool QueryMemoryResourceNotification(IntPtr handle, out bool state);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

        private void StopWatchingLowMemory()
        {
            _lowMemoryTimer?.Dispose();
            _lowMemoryTimer = null;
            if (_lowMemoryNotification != IntPtr.Zero) CloseHandle(_lowMemoryNotification);
            _lowMemoryNotification = IntPtr.Zero;
        }
        private const int LowMemoryResourceNotification = 0;

        private void WatchLowMemory()
        {
            _lowMemoryNotification = CreateMemoryResourceNotification(LowMemoryResourceNotification);
            if (_lowMemoryNotification == IntPtr.Zero) return;
            // Polled (cheap), so the UI thread pushes the event; one signal per 30 s while memory stays low.
            _lowMemoryTimer = new Timer { Interval = 30000 };
            _lowMemoryTimer.Tick += (s, e) =>
            {
                if (QueryMemoryResourceNotification(_lowMemoryNotification, out bool low) && low)
                    _bridge?.PushEvent(new { @event = "lowMemory" });
            };
            _lowMemoryTimer.Start();
        }

        // ── App minimized: WebView2 keeps less in memory (scripts still run) ───────────────────

        private void WatchMinimized()
        {
            Form form = FindForm();
            if (form == null) return;
            form.Resize += (s, e) =>
            {
                try
                {
                    _webView.CoreWebView2.MemoryUsageTargetLevel = form.WindowState == FormWindowState.Minimized
                        ? CoreWebView2MemoryUsageTargetLevel.Low
                        : CoreWebView2MemoryUsageTargetLevel.Normal;
                }
                catch (Exception ex) { Log("memory target level: " + ex.Message); }
            };
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
