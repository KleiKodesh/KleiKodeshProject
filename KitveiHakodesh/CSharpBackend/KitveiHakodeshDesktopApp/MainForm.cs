using FluentChromeTabs;
using KitveiHakodeshLib;
using KitveiHakodesh.Core.Settings;
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KitveiHakodeshDesktopApp
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


        /// <summary>
        /// This window's id among the app's windows: "" for the first window, else a short
        /// number. Keys the page's saved tabs (AppViewer.WindowId) and this window's size.
        /// </summary>
        internal string WindowId { get; }

        /// <summary>
        /// Asked when the window closes: true when it closes for good while other windows
        /// stay open, so its saved tabs are deleted instead of kept. Set by Program.
        /// </summary>
        internal Func<MainForm, FormClosingEventArgs, System.Threading.Tasks.Task<bool>> ClosesForGood { get; set; }

        /// <summary>
        /// The window a new empty window was opened from (Program.OpenNewWindow): the new one
        /// takes its size, a little down and across, or is maximized like it - not the size
        /// saved for this id, which may belong to an earlier window long closed.
        /// </summary>
        internal MainForm OpenedFrom { get; set; }

        // The first window keeps the single-window settings names, so an existing
        // install opens where it was.
        private string FormSettingsName => "KitveiHakodeshMain" + (WindowId.Length == 0 ? "" : "-" + WindowId);

        public MainForm(string initialRequest = null, string windowId = "")
        {
            WindowId = windowId ?? "";
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

            _viewer = new AppViewer(WebCacheFolder) { Dock = DockStyle.Fill, IsVstoHost = HostTestMode.WordPane, WindowId = WindowId };
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
            DailyLearningLink dailyLearning;
            if (link != null)
                _viewer.OpenBookFromHost(link);
            else if ((dailyLearning = DailyLearningLink.TryParse(request)) != null)
                _viewer.OpenDailyLearningFromHost(dailyLearning);
            else if (System.IO.File.Exists(request))
                _viewer.OpenFileFromPath(request);
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            if (OpenedFrom != null && !OpenedFrom.IsDisposed)
            {
                Rectangle from = OpenedFrom.WindowState == FormWindowState.Normal ? OpenedFrom.Bounds : OpenedFrom._lastNormalBounds;
                int step = SystemInformation.CaptionHeight;
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(from.X + step, from.Y + step, from.Width, from.Height);
                _lastNormalBounds = Bounds;
                if (OpenedFrom.WindowState == FormWindowState.Maximized)
                    WindowState = FormWindowState.Maximized;
                return;
            }
            FormSettingsHelper.LoadFormSettings(this, "KitveiHakodesh", FormSettingsName);
            _lastNormalBounds = Bounds;
            if (AppSettingsRegistry.GetWindowMaximized(WindowId))
                WindowState = FormWindowState.Maximized;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            AppSettingsRegistry.SetWindowMaximized(WindowId, WindowState == FormWindowState.Maximized);
            FormSettingsHelper.SaveFormSettings(this, "KitveiHakodesh", FormSettingsName, _lastNormalBounds);
            // The page's pending persistence writes go out before the WebView2 is
            // disposed: the first close is held for the page's ack, the second runs through.
            // A window closed while others stay open deletes its saved tabs instead.
            _viewer.CloseAfterPersistenceFlush(this, e,
                ClosesForGood == null ? (Func<System.Threading.Tasks.Task<bool>>)null : () => ClosesForGood(this, e));
        }

        private void MainForm_ResizeEnd(object sender, EventArgs e)
        {
            AppSettingsRegistry.SetWindowMaximized(WindowId, WindowState == FormWindowState.Maximized);
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
