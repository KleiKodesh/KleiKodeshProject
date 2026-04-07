using KezayitLib;
using System;
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

            _viewer = new AppViewer { Dock = DockStyle.Fill };
            _viewer.TogglePopOut = Toggle;
            Controls.Add(_viewer);
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

            _popoutWindow = new Form
            {
                Text = "זית",
                Size = new System.Drawing.Size(900, 750),
                StartPosition = FormStartPosition.CenterScreen,
            };
            _viewer.Dock = DockStyle.Fill;
            _popoutWindow.Controls.Add(_viewer);
            _popoutWindow.FormClosing += OnPopoutClosing;
            _popoutWindow.Show();
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
