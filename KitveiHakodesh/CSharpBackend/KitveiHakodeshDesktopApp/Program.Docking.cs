using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using FluentChromeTabs;
using KitveiHakodesh.Core.Settings;

namespace KitveiHakodeshDesktopApp
{
    // The app's side of screen docking (FluentChromeTabs.ScreenDocking places the windows): what
    // a tab is and how it moves. A main window (MainForm) has a page of its own; a page window
    // (PageWindowForm) is drawn by its owner's page (the VS Code model). A tab moving between
    // windows of one page moves live; between two pages it is packed and unpacked (Program.TabMoves).
    // UI thread only.
    internal static partial class Program
    {
        private static readonly DockingHost Docking = new DockingHost();

        private static readonly List<PageWindowForm> PageWindows = new List<PageWindowForm>();

        // The window a page opens next for a dropped tab is placed by this, once shown.
        private static Action<FluentChromeTabsForm> _pendingPlacement;

        /// <summary>A main window takes part in tab drags and screen docking.</summary>
        private static void AttachDocking(MainForm window)
        {
            ScreenDocking.Attach(window, Docking);
        }

        /// <summary>A page window was made for <paramref name="owner"/>'s page: it joins the drag and drop.</summary>
        private static void AddPageWindow(MainForm owner, PageWindowForm window)
        {
            PageWindows.Add(window);
            ScreenDocking.Attach(window, Docking);
            window.Activated += (s, e) => { lock (EarlyRequestsLock) _activeWindow = owner; };
            window.FormClosed += (s, e) => PageWindows.Remove(window);
            Action<FluentChromeTabsForm> place = _pendingPlacement;
            _pendingPlacement = null;
            if (place != null) window.Shown += (s, e) => place(window);
        }

        // The main window whose page shows this window's tabs.
        private static MainForm PageOf(FluentChromeTabsForm window)
            => window as MainForm ?? (window as PageWindowForm)?.PageOwner;

        // The page's key for the window: "" for a main window.
        private static string WindowKeyOf(FluentChromeTabsForm window)
            => (window as PageWindowForm)?.WindowKey ?? "";

        private static IEnumerable<FluentChromeTabsForm> AllAppWindows()
        {
            foreach (MainForm open in OpenWindows) yield return open;
            foreach (PageWindowForm open in PageWindows) yield return open;
        }

        private sealed class DockingHost : IScreenDockingHost
        {
            public Task<bool> MoveTabAsync(FluentChromeTabsForm source, FluentTab tab, FluentChromeTabsForm target, int position)
            {
                MainForm from = PageOf(source), to = PageOf(target);
                if (!(tab?.Tag is string tabId) || from == null || to == null || from.IsDisposed || to.IsDisposed)
                    return Task.FromResult(false);
                if (from != to) return MoveTabToWindowAsync(from, tabId, to, position, null);
                from.Viewer.MoveTabToAppWindow(tabId, WindowKeyOf(target), position);
                return Task.FromResult(true);
            }

            // A new window of the source's page for the tab, placed once it shows (AddPageWindow).
            public void OpenWindowForTab(FluentChromeTabsForm source, FluentTab tab, Action<FluentChromeTabsForm> place)
            {
                MainForm page = PageOf(source);
                if (!(tab?.Tag is string tabId) || page == null || page.IsDisposed) return;
                _pendingPlacement = place;
                page.Viewer.MoveTabToAppWindow(tabId, null, -1);
            }

            public bool AcceptsDrops(FluentChromeTabsForm window)
            {
                if (window is PageWindowForm page) return PageWindows.Contains(page) && AcceptsDrops(page.PageOwner);
                var main = window as MainForm;
                return main != null && main.Visible && OpenWindows.Contains(main) && !ClosingWindows.Contains(main);
            }

            public int TabCount(FluentChromeTabsForm window)
                => (window as MainForm)?.TabsMirror.PageTabCount ?? (window as PageWindowForm)?.TabsMirror.PageTabCount ?? window.Tabs.Count;

            // Called as a drag starts: the floating tab takes the app's current colors, and shows
            // the page (a page window draws in its owner's renderer: no picture yet).
            public Task<Image> CaptureTabPicture(FluentChromeTabsForm window)
            {
                ScreenDocking.DarkMode = AppSettingsRegistry.GetDarkMode();
                ScreenDocking.AccentColor = ParseColor(AppSettingsRegistry.GetAccentColor(), Color.FromArgb(124, 92, 252));
                return window is MainForm main ? main.Viewer.CapturePagePictureAsync() : Task.FromResult<Image>(null);
            }

            private static Color ParseColor(string hex, Color fallback)
            {
                try { return string.IsNullOrEmpty(hex) ? fallback : ColorTranslator.FromHtml(hex.StartsWith("#") ? hex : "#" + hex); }
                catch (Exception) { return fallback; }
            }
        }
    }
}
