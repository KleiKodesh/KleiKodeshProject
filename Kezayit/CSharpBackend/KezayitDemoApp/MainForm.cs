using KezayitLib;
using KezayitLib.Settings;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace KezayitDemoApp
{
    public class MainForm : Form
    {
        private readonly AppViewer _viewer;
        private Form _popoutWindow;

        public MainForm()
        {
            Text = "זית";
            ClientSize = new System.Drawing.Size(600, 750);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Font;
            Icon = CreateWindowIcon();

            _viewer = new AppViewer { Dock = DockStyle.Fill };
            _viewer.TogglePopOut = Toggle;
            Controls.Add(_viewer);
        }

        private static Icon CreateWindowIcon()
        {
            using (var executableIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                return executableIcon == null ? null : (Icon)executableIcon.Clone();
        }

        private void Toggle()
        {
            if (_popoutWindow == null || _popoutWindow.IsDisposed)
                PopOut();
            else
                PopIn();
        }

        private void PopOut()
        {
            Controls.Remove(_viewer);

            var saved = AppSettings.LoadPopoutBounds();
            bool hasSaved = saved.X != -1 && saved.Y != -1;

            _popoutWindow = new Form
            {
                Text = "זית",
                Size = new System.Drawing.Size(saved.Width, saved.Height),
                StartPosition = hasSaved ? FormStartPosition.Manual : FormStartPosition.CenterScreen,
                Icon = CreateWindowIcon(),
            };
            if (hasSaved)
                _popoutWindow.Location = new System.Drawing.Point(saved.X, saved.Y);

            _viewer.Dock = DockStyle.Fill;
            _popoutWindow.Controls.Add(_viewer);
            _popoutWindow.FormClosing += OnPopoutClosing;
            _popoutWindow.ResizeEnd += OnPopoutBoundsChanged;
            _popoutWindow.Move += OnPopoutBoundsChanged;
            _popoutWindow.Show();
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
