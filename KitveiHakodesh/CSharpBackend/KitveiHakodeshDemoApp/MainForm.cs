using KitveiHakodeshLib;
using KitveiHakodeshLib.Settings;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace KitveiHakodeshDemoApp
{
    public class MainForm : Form
    {
        private readonly AppViewer _viewer;
        private Form _popoutWindow;

        public MainForm()
        {
            Text = "כתבי הקודש";
            ClientSize = new System.Drawing.Size(1000, 750);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Font;
            Icon = CreateWindowIcon();
            RightToLeftLayout = true;
            RightToLeft = RightToLeft.Yes;

            _viewer = new AppViewer { Dock = DockStyle.Fill };
            _viewer.TogglePopOut = Toggle;
            Controls.Add(_viewer);

            Load        += MainForm_Load;
            FormClosing += MainForm_FormClosing;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            FormSettingsHelper.LoadFormSettings(this, "KitveiHakodesh", "KitveiHakodeshMain");
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            FormSettingsHelper.SaveFormSettings(this, "KitveiHakodesh", "KitveiHakodeshMain");
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

            // If requested, enter fullscreen mode immediately after showing
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
