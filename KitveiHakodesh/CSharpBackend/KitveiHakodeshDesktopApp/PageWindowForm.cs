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

        /// <summary>Its strip glue; null while it is the startup window, not yet the page's.</summary>
        internal ChromeTabsMirror TabsMirror { get; private set; }

        /// <summary>The page's key for this window (its tabs carry it); null while it is the startup window.</summary>
        internal string WindowKey { get; private set; }

        // The startup picture, until the window's page draws.
        private Control _splash;

        /// <summary>
        /// Set when the page itself closes the window (its last tab left, or the reader's
        /// close went through the page), or when the owner window goes: the close goes ahead.
        /// </summary>
        internal bool ClosingByPage { get; set; }

        // The reader's close goes through the page: it closes the window's tabs (asking about
        // unsaved work first) and then the window. Its tabs are gone for good.
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!ClosingByPage && e.CloseReason == CloseReason.UserClosing && !PageOwner.IsDisposed
                && (WindowKey != null || Program.IsLastOpenWindow(this)))
            {
                e.Cancel = true;
                // The last window of the app: closing it is quitting, and quitting keeps the
                // tabs for the next start - the page's window closes, and this one with it.
                if (Program.IsLastOpenWindow(this)) PageOwner.Close();
                else PageOwner.Viewer.CloseAppWindow(WindowKey);
                return;
            }
            base.OnFormClosing(e);
        }


        /// <summary>
        /// A window of <paramref name="pageOwner"/>'s page. Without a key it is the startup
        /// window: shown at once with the startup picture, before the page is up; the page's
        /// first window takes it over (<see cref="BecomeWindow"/>).
        /// </summary>
        internal PageWindowForm(MainForm pageOwner, string windowKey)
        {
            PageOwner = pageOwner;
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
            if (windowKey != null) BecomeWindow(windowKey);
            else
            {
                _splash = AppViewer.CreateStartupSplash(KitveiHakodesh.Core.Settings.AppSettingsRegistry.GetDarkMode());
                Content.Controls.Add(_splash);
            }
            // The page's view arrives: the startup picture goes once it has drawn.
            Content.ControlAdded += (s, e) =>
            {
                if (_splash == null || !(e.Control is Microsoft.Web.WebView2.WinForms.WebView2 view)) return;
                _splash.BringToFront();
                view.CoreWebView2InitializationCompleted += (s2, e2) =>
                {
                    if (view.CoreWebView2 != null) view.CoreWebView2.NavigationCompleted += (s3, e3) => RemoveSplash();
                    else RemoveSplash();
                };
            };
        }

        /// <summary>The page's window <paramref name="windowKey"/> takes this one over.</summary>
        internal void BecomeWindow(string windowKey)
        {
            WindowKey = windowKey;
            TabsMirror = new ChromeTabsMirror(this, PageOwner.Viewer, windowKey,
                () => { foreach (Control c in Content.Controls) if (c != _splash) { c.Focus(); break; } });
        }

        private void RemoveSplash()
        {
            if (_splash == null || IsDisposed) return;
            Content.Controls.Remove(_splash);
            _splash.Dispose();
            _splash = null;
        }
    }
}
