using Microsoft.Office.Tools;
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KleiKodesh.Helpers
{
    public sealed class TaskPanePopOut
    {
        readonly UserControl _host;
        readonly CustomTaskPane _pane;
        Control _content;
        Form _form;

        public TaskPanePopOut(UserControl host, CustomTaskPane pane)
        {
            _host = host;
            _pane = pane;

            // Listen to host visibility changes to trigger popout/pop-in
            _host.VisibleChanged += OnHostVisibilityChanged;
        }

        Control GetContent()
        {
            // If content was provided in constructor and is valid, use it
            if (_content != null && !_content.IsDisposed)
                return _content;

            // Otherwise, get the first child control from host (for ZayitViewerHost case)
            if (_host.Controls.Count > 0)
            {
                _content = _host.Controls[0];
                return _content;
            }

            return null;
        }

        void OnHostVisibilityChanged(object sender, EventArgs e)
        {
            // When host becomes invisible, pop out
            if (!_host.Visible && (_form == null || _form.IsDisposed))
            {
                PopOut();
            }
            // When host becomes visible while popped out, pop in
            else if (_host.Visible && _form != null && !_form.IsDisposed)
            {
                PopIn();
            }
        }

        public void Toggle()
        {
            if (_form == null || _form.IsDisposed)
                PopOut();
            else
                PopIn();
        }

        void PopOut()
        {
            try
            {
                if (_form != null && !_form.IsDisposed)
                    return; // Already popped out

                var content = GetContent();
                if (content == null)
                {
                    Console.WriteLine("[TaskPanePopOut] No content to pop out");
                    return;
                }

                Console.WriteLine("[TaskPanePopOut] Popping out");

                // Remove content from host
                if (_host.Controls.Contains(content))
                    _host.Controls.Remove(content);

                // Create popout window
                _form = CreateForm();
                content.Dock = DockStyle.Fill;
                _form.Controls.Add(content);

                SetOwner(_form.Handle);

                _form.FormClosing += OnFormClosing;
                _pane.VisibleChanged += OnPaneVisibilityChanged;

                _pane.Visible = false;
                _form.Show();
            }
            catch (Exception ex { Console.WriteLine(ex.Message); }
        }

        void PopIn()
        {
            if (_form == null || _form.IsDisposed)
                return; // Already popped in

            var content = GetContent();
            if (content == null)
            {
                Console.WriteLine("[TaskPanePopOut] No content to pop in");
                return;
            }

            Console.WriteLine("[TaskPanePopOut] Popping in");

            _pane.VisibleChanged -= OnPaneVisibilityChanged;
            _form.FormClosing -= OnFormClosing;

            // Remove content from form
            if (_form.Controls.Contains(content))
                _form.Controls.Remove(content);

            // Add content back to host
            if (!_host.IsDisposed)
            {
                content.Dock = DockStyle.Fill;
                _host.Controls.Add(content);
            }

            if (!_form.IsDisposed)
                _form.Close();

            _form = null;

            _host.BeginInvoke(new Action(() => _pane.Visible = true));
        }

        void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            // When user closes popout window, make host visible (which triggers pop-in)
            if (!_host.IsDisposed)
                _host.Invoke(new Action(() => _host.Visible = true));
        }

        void OnPaneVisibilityChanged(object sender, EventArgs e)
        {
            // When taskpane becomes visible while popped out, make host visible (which triggers pop-in)
            if (_pane.Visible && !_host.IsDisposed)
                _host.Invoke(new Action(() => _host.Visible = true));
        }

        static Form CreateForm() => new Form
        {
            Width = 570,
            Height = 850,
            StartPosition = FormStartPosition.CenterParent,
            Icon = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KleiKodesh_Main.ico"))
        ? new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KleiKodesh_Main.ico"))
        : null
        };

        void SetOwner(IntPtr formHandle)
        {
            var word = new IntPtr(Globals.ThisAddIn.Application.ActiveWindow.Hwnd);
            SetWindowLong(formHandle, GWL_HWNDPARENT, word.ToInt32());
        }

        const int GWL_HWNDPARENT = -8;

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
