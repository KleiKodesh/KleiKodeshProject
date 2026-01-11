using Microsoft.Office.Tools;
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KleiKodesh.Helpers
{
    public sealed class TaskPanePopOut
    {
        readonly UserControl _host;
        readonly Control _content;
        readonly CustomTaskPane _pane;
        Form _form;

        public TaskPanePopOut(UserControl host, Control content, CustomTaskPane pane)
        {
            _host = host;
            _content = content;
            _pane = pane;
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
            _form = CreateForm();
            _form.Controls.Add(_content);

            SetOwner(_form.Handle);

            _form.FormClosing += (_, __) => PopIn();
            _pane.VisibleChanged += PaneVisibleChanged;

            _pane.Visible = false;
            _form.Show();
        }

        void PopIn()
        {
            _pane.VisibleChanged -= PaneVisibleChanged;

            if (!_host.IsDisposed)
                _host.Controls.Add(_content);

            _host.BeginInvoke(new Action(() => _pane.Visible = true));

            if (!_form.IsDisposed)
                _form.Close();
        }

        void PaneVisibleChanged(object _, EventArgs __)
        {
            if (_pane.Visible && _form != null && !_form.IsDisposed)
                _host.BeginInvoke(new Action(() => _form.Close()));
        }

        static Form CreateForm() => new Form
        {
            Width = 1200,
            Height = 800,
            StartPosition = FormStartPosition.CenterScreen
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
