using FluentChromeTabs;
using KitveiHakodeshLib;
using System.Windows.Forms;

namespace KitveiHakodeshDesktopApp
{
    /// <summary>
    /// An extra app window drawn by another window's page (the VS Code model: one page, one
    /// renderer, many windows). Its tab strip shows that page's tabs for this window; the
    /// page itself (a popup of the main page) fills <see cref="Content"/>.
    /// </summary>
    internal sealed class PageWindowForm : FluentChromeTabsForm
    {
        /// <summary>The control the window's page fills (AppViewer adds the popup's WebView2 here).</summary>
        internal Panel Content { get; }

        /// <summary>The window whose page draws this one.</summary>
        internal MainForm PageOwner { get; }

        internal ChromeTabsMirror TabsMirror { get; }

        /// <summary>The page's key for this window (its tabs carry it).</summary>
        internal string WindowKey { get; }

        /// <summary>
        /// Set when the page itself closes the window (its last tab left, or the reader's
        /// close went through the page), or when the owner window goes: the close goes ahead.
        /// </summary>
        internal bool ClosingByPage { get; set; }

        // The reader's close goes through the page: it closes the window's tabs (asking about
        // unsaved work first) and then the window. Its tabs are gone for good.
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!ClosingByPage && e.CloseReason == CloseReason.UserClosing && !PageOwner.IsDisposed)
            {
                e.Cancel = true;
                PageOwner.Viewer.CloseAppWindow(WindowKey);
                return;
            }
            base.OnFormClosing(e);
        }

        // A move or an edge resize by the user: windows placed side by side keep their shared
        // edge (Program.SideBySide), as MainForm does.
        protected override void WndProc(ref Message m)
        {
            const int WM_SIZING = 0x0214, WM_MOVING = 0x0216, WM_EXITSIZEMOVE = 0x0232, WM_WINDOWPOSCHANGING = 0x0046;
            if (m.Msg == WM_WINDOWPOSCHANGING && m.LParam != System.IntPtr.Zero)
            {
                var pos = (Program.WINDOWPOS)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(Program.WINDOWPOS));
                System.IntPtr before = pos.hwndInsertAfter;
                Program.OnWindowPosChanging(this, ref pos);
                if (pos.hwndInsertAfter != before)
                    System.Runtime.InteropServices.Marshal.StructureToPtr(pos, m.LParam, false);
            }
            if (m.Msg == WM_SIZING)
            {
                var proposed = (Program.RECT)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(Program.RECT));
                Program.OnWindowSizing(this, m.WParam.ToInt32(), ref proposed);
                System.Runtime.InteropServices.Marshal.StructureToPtr(proposed, m.LParam, false);
            }
            base.WndProc(ref m);
            if (m.Msg == WM_MOVING) Program.OnWindowMoving(this);
            else if (m.Msg == WM_EXITSIZEMOVE) Program.OnWindowMoveEnded(this);
        }

        internal PageWindowForm(MainForm pageOwner, string windowKey)
        {
            PageOwner = pageOwner;
            WindowKey = windowKey;
            Text = pageOwner.Text;
            Icon = pageOwner.Icon;
            RightToLeftLayout = true;
            RightToLeft = RightToLeft.Yes;
            // Same strip as the main window (MainForm).
            StripHeight = 36;
            TabHeight = 28;
            ShowTabListButton = true;

            Content = new Panel { Dock = DockStyle.Fill };
            Controls.Add(Content);
            TabsMirror = new ChromeTabsMirror(this, pageOwner.Viewer, windowKey,
                () => { if (Content.Controls.Count > 0) Content.Controls[0].Focus(); });
        }
    }
}
