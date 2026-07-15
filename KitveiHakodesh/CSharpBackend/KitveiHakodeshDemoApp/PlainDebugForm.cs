using KitveiHakodeshLib;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace KitveiHakodeshDemoApp
{
    // Debug harness — launch with the `--plain` command-line switch.
    //
    // Hosts the AppViewer/WebView2 in a BARE Form with NO FluentChromeTabs strip and
    // NO ChromeTabsMirror. Purpose: isolate whether the native tab strip is what leaves
    // the web content unfocused (the reason a two-finger trackpad swipe-to-switch-tab
    // did nothing until you clicked inside the page).
    //
    // Test: start with `--plain`, and WITHOUT clicking in the page, two-finger swipe.
    //   - If it switches tabs immediately here (but the normal chrome-tabs window needs
    //     a click first), the FluentChromeTabsForm strip is confirmed as the focus thief.
    // Uses its own webcache folder so it can run alongside the normal app instance.
    public class PlainDebugForm : Form
    {
        private readonly AppViewer _viewer;

        public PlainDebugForm(string initialFilePath = null)
        {
            Text = "כתבי הקודש — plain (no chrome tabs)";
            ClientSize = new Size(1000, 750);
            StartPosition = FormStartPosition.CenterScreen;
            RightToLeftLayout = true;
            RightToLeft = RightToLeft.Yes;

            _viewer = new AppViewer("plaindebug") { Dock = DockStyle.Fill };
            Controls.Add(_viewer);

            if (!string.IsNullOrEmpty(initialFilePath))
                _viewer.OpenFileFromPath(initialFilePath);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _viewer?.Dispose();
            base.Dispose(disposing);
        }
    }
}
