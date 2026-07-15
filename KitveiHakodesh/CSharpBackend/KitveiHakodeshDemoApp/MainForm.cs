using FluentChromeTabs;
using KitveiHakodeshLib;
using KitveiHakodeshLib.Settings;
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using UpdateCheckerLib;

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
        private Form _popoutWindow;

        public MainForm(string initialFilePath = null)
        {
            Text = "כתבי הקודש";
            ClientSize = new System.Drawing.Size(1000, 750);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = CreateWindowIcon();
            RightToLeftLayout = true;
            RightToLeft = RightToLeft.Yes;

            // Slightly taller than the Vue app-shell title bar (compact density: 32 CSS px,
            // which WebView2 scales by DPI and app zoom). Logical px — scaled for DPI.
            StripHeight = 34;
            TabHeight = 26;
            // Tab-list dropdown in the slot just before the tab strip (visual right in RTL) —
            // lists all open tabs; picking one activates it (mirrored back into Vue).
            ShowTabListButton = true;

            _viewer = new AppViewer("webcache-standalone") { Dock = DockStyle.Fill };
            _viewer.TogglePopOut = Toggle;
            Controls.Add(_viewer);

            // Keeps the strip in sync with the Vue tab store, both directions.
            _tabsMirror = new ChromeTabsMirror(this, _viewer);

            // Queue the file to open as soon as the WebView2 bridge is ready.
            if (!string.IsNullOrEmpty(initialFilePath))
                _viewer.OpenFileFromPath(initialFilePath);

            Load        += MainForm_Load;
            FormClosing += MainForm_FormClosing;
            ResizeEnd   += MainForm_ResizeEnd;
        }

        /// <summary>
        /// Opens a file in the viewer. Called from the pipe listener when a second instance
        /// forwards a file path to this running instance.
        /// </summary>
        public void OpenFile(string filePath)
        {
            _viewer.OpenFileFromPath(filePath);
        }

        private bool _updateCheckDone = false;

        private void MainForm_Load(object sender, EventArgs e)
        {
            FormSettingsHelper.LoadFormSettings(this, "KitveiHakodesh", "KitveiHakodeshMain");
            if (AppSettings.LoadMainWindowMaximized())
                WindowState = FormWindowState.Maximized;

            if (_updateCheckDone) return;
            _updateCheckDone = true;

            // Respect the shared "turn off automatic updates" toggle (same registry key
            // as the KleiKodesh Word add-in — set from the Vue settings "מתקדם" card).
            // When on, skip the network/GitHub check entirely. RunPendingInstaller() on
            // close is intentionally NOT gated — an already-downloaded update still applies.
            if (AppSettings.LoadTurnOffUpdates()) return;

            // ── Step 1: sync disk check — no network, instant ────────────────────
            // Reads %TEMP%\KleiKodeshSetup.exe version and compares to registry.
            // Arms RunPendingInstaller() and returns the version if newer.
            // Deletes the file if it's stale or already installed.
            var readyVersion = UpdateChecker.GetReadyUpdateVersion();
            if (readyVersion != null)
            {
                UpdateNotificationForm.Show(
                    $"עדכון זמין לגרסה {readyVersion}.\nהעדכון יותקן אוטומטית עם סגירת האפליקציה."
                );
            }

            // ── Step 2: async GitHub check — always runs regardless of Step 1 ────
            // Downloads a newer installer silently if one exists.
            // No UI. PendingInstallerPath is never touched here.
            _ = Task.Run(async () =>
            {
                try { await UpdateChecker.CheckForUpdateAsync(); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainForm] Update check failed: {ex.Message}");
                }
            });
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            AppSettings.SaveMainWindowMaximized(WindowState == FormWindowState.Maximized);
            FormSettingsHelper.SaveFormSettings(this, "KitveiHakodesh", "KitveiHakodeshMain");
            UpdateChecker.RunPendingInstaller();
        }

        private void MainForm_ResizeEnd(object sender, EventArgs e)
        {
            AppSettings.SaveMainWindowMaximized(WindowState == FormWindowState.Maximized);
        }

        private static Icon CreateWindowIcon()
        {
            using (var executableIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                return executableIcon == null ? null : (Icon)executableIcon.Clone();
        }

        private void Toggle(bool goFullScreen = false)
        {
            if (_popoutWindow == null || _popoutWindow.IsDisposed)
                PopOut(goFullScreen);
            else
                PopIn();
        }

        private void PopOut(bool goFullScreen = false)
        {
            Controls.Remove(_viewer);

            var saved = AppSettings.LoadPopoutBounds();
            bool hasSaved = saved.X != -1 && saved.Y != -1;

            _popoutWindow = new Form
            {
                Text = "כתבי הקודש",
                Size = new System.Drawing.Size(saved.Width, saved.Height),
                StartPosition = hasSaved ? FormStartPosition.Manual : FormStartPosition.CenterScreen,
                Icon = CreateWindowIcon(),
                RightToLeftLayout = true,
                RightToLeft = RightToLeft.Yes,
            };
            if (hasSaved)
                _popoutWindow.Location = new System.Drawing.Point(saved.X, saved.Y);

            _viewer.Dock = DockStyle.Fill;
            _popoutWindow.Controls.Add(_viewer);
            _popoutWindow.FormClosing += OnPopoutClosing;
            _popoutWindow.ResizeEnd += OnPopoutBoundsChanged;
            _popoutWindow.Move += OnPopoutBoundsChanged;
            _popoutWindow.Show();

            if (goFullScreen)
            {
                _popoutWindow.FormBorderStyle = FormBorderStyle.None;
                _popoutWindow.WindowState = FormWindowState.Maximized;
            }
        }

        private void OnPopoutBoundsChanged(object sender, EventArgs e)
        {
            if (_popoutWindow == null || _popoutWindow.IsDisposed) return;
            if (_popoutWindow.WindowState != FormWindowState.Normal) return;
            AppSettings.SavePopoutBounds(_popoutWindow.Bounds);
        }

        private void PopIn()
        {
            if (_popoutWindow == null || _popoutWindow.IsDisposed) return;

            _popoutWindow.FormClosing -= OnPopoutClosing;
            _popoutWindow.Controls.Remove(_viewer);
            _popoutWindow.Close();
            _popoutWindow.Dispose();
            _popoutWindow = null;

            _viewer.Dock = DockStyle.Fill;
            Controls.Add(_viewer);
        }

        private void OnPopoutClosing(object sender, FormClosingEventArgs e)
        {
            if (_popoutWindow != null && !_popoutWindow.IsDisposed &&
                _popoutWindow.WindowState == FormWindowState.Normal)
                AppSettings.SavePopoutBounds(_popoutWindow.Bounds);
            PopIn();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _popoutWindow?.Dispose();
                _viewer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
