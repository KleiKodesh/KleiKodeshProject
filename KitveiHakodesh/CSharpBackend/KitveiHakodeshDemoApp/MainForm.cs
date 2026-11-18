using FluentChromeTabs;
using KitveiHakodeshLib;
using KitveiHakodesh.Core.Settings;
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KitveiHakodeshDemoApp
{
    // FluentChromeTabsForm in loose mode: the strip tabs are pure metadata that mirror
    // the Vue tab store (via ChromeTabsMirror), while the single AppViewer/WebView2
    // fills the content area below the strip and switches tabs internally.
    //
    // RightToLeftLayout mirrors the whole strip (WS_EX_LAYOUTRTL): tabs flow from the
    // visual right, caption buttons sit at the visual left. FluentChromeTabsForm's
    // painting and hit testing are mirroring-aware (DrawUnmirroredImage,
    // TextFormatFlags.RightToLeft, PointToClient), so no strip changes are needed.
    public class MainForm : FluentChromeTabsForm
    {
        private readonly AppViewer _viewer;
        private readonly ChromeTabsMirror _tabsMirror;
        private Rectangle _lastNormalBounds;

        /// <summary>The WebView2 user-data folder of this window (Program starts its browser early).</summary>
        internal const string WebCacheFolder = "webcache-standalone";


        public MainForm(string initialRequest = null)
        {
            Text = "כתבי הקודש";
            ClientSize = new System.Drawing.Size(1000, 750);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = CreateWindowIcon();
            RightToLeftLayout = true;
            RightToLeft = RightToLeft.Yes;

            // Firefox Nova compact density: 28px tabs with 4px above and below. Normal
            // density (the library default, 44/32) would tower over the Vue app-shell title
            // bar (compact: 32 CSS px). Logical px — scaled for DPI.
            StripHeight = 36;
            TabHeight = 28;
            // Tab-list dropdown in the slot just before the tab strip (visual right in RTL) —
            // lists all open tabs; picking one activates it (mirrored back into Vue).
            ShowTabListButton = true;

            _viewer = new AppViewer(WebCacheFolder) { Dock = DockStyle.Fill };
            Controls.Add(_viewer);

            // Keeps the strip in sync with the Vue tab store, both directions.
            _tabsMirror = new ChromeTabsMirror(this, _viewer);

            // Queue what we were launched with; the viewer holds it until the bridge
            // is ready. Same call the pipe listener makes for a second launch.
            if (!string.IsNullOrEmpty(initialRequest))
                OpenRequest(initialRequest);

            _lastNormalBounds = Bounds;

            Load        += MainForm_Load;
            FormClosing += MainForm_FormClosing;
            ResizeEnd   += MainForm_ResizeEnd;
            Move        += MainForm_BoundsChanged;
            Resize      += MainForm_BoundsChanged;
        }

        /// <summary>
        /// Opens whatever this app can be launched with: an existing file, or a deep link
        /// into a book (<c>kitveihakodeshapp://book/&lt;id&gt;?index=&lt;n&gt;</c>, parsed by
        /// <see cref="HostLink"/>). Called from the constructor for our own command line and
        /// from the pipe listener when a second launch forwards its argument here, so both
        /// land on one code path and a link behaves exactly like a file: a new tab in this
        /// window, never a second window.
        ///
        /// The request is validated here rather than by the callers — this is the only place
        /// that knows what an open request may be. An unparseable link or a missing file is
        /// dropped silently, matching the previous behaviour for a path that no longer exists.
        /// </summary>
        public void OpenRequest(string request)
        {
            if (string.IsNullOrEmpty(request)) return;

            HostLink link = HostLink.TryParse(request);
            if (link != null)
                _viewer.OpenBookFromHost(link);
            else if (System.IO.File.Exists(request))
                _viewer.OpenFileFromPath(request);
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            FormSettingsHelper.LoadFormSettings(this, "KitveiHakodesh", "KitveiHakodeshMain");
            _lastNormalBounds = Bounds;
            if (AppSettingsRegistry.GetMainWindowMaximized())
                WindowState = FormWindowState.Maximized;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            AppSettingsRegistry.SetMainWindowMaximized(WindowState == FormWindowState.Maximized);
            FormSettingsHelper.SaveFormSettings(this, "KitveiHakodesh", "KitveiHakodeshMain", _lastNormalBounds);
            // The page's pending persistence writes go out before the WebView2 is
            // disposed: the first close is held for the page's ack, the second runs through.
            _viewer.CloseAfterPersistenceFlush(this, e);
        }

        private void MainForm_ResizeEnd(object sender, EventArgs e)
        {
            AppSettingsRegistry.SetMainWindowMaximized(WindowState == FormWindowState.Maximized);
        }

        private void MainForm_BoundsChanged(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Normal)
                _lastNormalBounds = Bounds;
        }

        private static Icon CreateWindowIcon()
        {
            try
            {
                using (var executableIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                    return executableIcon == null ? null : (Icon)executableIcon.Clone();
            }
            catch
            {
                // ExtractAssociatedIcon throws on UNC paths (documented limitation) —
                // running from a network share must not crash startup. Default icon.
                return null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _viewer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
