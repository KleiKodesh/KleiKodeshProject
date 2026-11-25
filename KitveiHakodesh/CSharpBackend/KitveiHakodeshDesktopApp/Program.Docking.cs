using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using FluentChromeTabs;
using KitveiHakodesh.Core.Settings;
using KitveiHakodeshLib;

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

        // The window a page opens next for a dropped tab: placed by this once it shows that tab.
        private sealed class PendingPlacement
        {
            public MainForm Page;
            public string TabId;
            public Action<Form> Place;
        }

        private static PendingPlacement _pendingPlacement;

        /// <summary>A main window takes part in tab drags and screen docking.</summary>
        private static void AttachDocking(MainForm window)
        {
            // Pictures of minimized docked windows (the group's taskbar preview): on disk beside the app.
            if (ScreenDocking.PictureFolder == null)
                ScreenDocking.PictureFolder = KitveiHakodeshLib.AppViewer.WindowPicturesFolder;
            ScreenDocking.Attach(window, Docking);
        }

        /// <summary>A page window was made for <paramref name="owner"/>'s page: it joins the drag and drop.</summary>
        private static void AddPageWindow(MainForm owner, PageWindowForm window)
        {
            PageWindows.Add(window);
            ScreenDocking.Attach(window, Docking);
            window.Activated += (s, e) =>
            {
                owner.LastActiveWindow = window;
                lock (EarlyRequestsLock) _activeWindow = owner;
            };
            owner.LastActiveWindow = owner.LastActiveWindow ?? window;
            // Widgets opened while an app window is on screen need no taskbar button of their own.
            owner.Viewer.HostWindowHidden = false;
            // The page host's place for the next start: the last of its windows to close.
            window.FormClosing += (s, e) => { if (!owner.IsDisposed) owner.SavePlaceFrom(window); };
            window.FormClosed += (s, e) =>
            {
                PageWindows.Remove(window);
                if (owner.LastActiveWindow == window)
                    owner.LastActiveWindow = PageWindows.LastOrDefault(w => w.PageOwner == owner && !w.IsDisposed);
                owner.Viewer.HostWindowHidden = !PageWindows.Exists(w => w.PageOwner == owner && !w.IsDisposed);
                if (owner.IsDisposed) return;
                owner.Viewer.ForgetTabsState(window.WindowKey);
                // The main window was closed while this one stayed: with the last of its page's
                // windows gone, it closes for real (the app quits, or its page goes).
                CloseHiddenMainIfNothingLeft(owner);
            };

            // Only the window opened for the dropped tab takes its placement: one opened for
            // anything else (a new window, another page) is left alone.
            PendingPlacement pending = _pendingPlacement;
            if (pending == null || pending.Page != owner) return;
            _pendingPlacement = null;
            // Invisible until it has its place: shown first at its default spot and moved after,
            // it would flash there on a busy computer.
            window.Opacity = 0;
            bool settled = false;
            EventHandler<TabsStateChangedEventArgs> onTabs = null;
            Action settle = () =>
            {
                if (settled || window.IsDisposed || !window.Visible) return;
                TabsStateChangedEventArgs tabs = owner.Viewer.LastTabsState(window.WindowKey);
                if (tabs == null) return; // its first tab list is still on its way
                settled = true;
                owner.Viewer.TabsStateChanged -= onTabs;
                if (tabs.Tabs.Any(t => t.Id == pending.TabId)) pending.Place(window);
                window.BeginInvoke(new Action(() => { if (!window.IsDisposed) window.Opacity = 1; }));
            };
            onTabs = (s, e) => { if (e.WindowKey == window.WindowKey && !window.IsDisposed) window.BeginInvoke(settle); };
            owner.Viewer.TabsStateChanged += onTabs;
            window.Shown += (s, e) => settle();
            window.FormClosed += (s, e) => owner.Viewer.TabsStateChanged -= onTabs;
        }

        /// <summary>A tool window (widget, commentary pop-out) of a page: it docks and tiles like the others.</summary>
        private static void AttachToolWindow(MainForm owner, Form window)
        {
            ToolWindowPages[window] = owner;
            window.FormClosed += (s, e) =>
            {
                ToolWindowPages.Remove(window);
                CloseHiddenMainIfNothingLeft(owner);
            };
            ScreenDocking.AttachWindow(window, Docking);
        }

        // The main window whose page draws each open tool window.
        private static readonly Dictionary<Form, MainForm> ToolWindowPages = new Dictionary<Form, MainForm>();

        // ── The main window hidden while its page runs other windows ──────────────

        /// <summary>
        /// The main window only hosts the page and never shows: shown invisible once (its page has
        /// to start), then hidden. In its place a window shows at once with the startup picture,
        /// which the page's windows take over (Program.StartupWindows; for the calendar launched on its
        /// own, only the calendar's window).
        /// </summary>
        private static void MakePageHost(MainForm main)
        {
            main.Viewer.IsPageHost = true;
            main.Viewer.HostWindowHidden = true;
            main.Opacity = 0;
            main.Load += (s, e) => ShowStartupWindows(main);
            main.Viewer.TakeWaitingPopupWindow = name => TakeWaitingWindow(main, name);
            main.Viewer.ContentReady += (s, e) => main.BeginInvoke(new Action(() => CloseUnclaimedWaitingWindows(main)));
            main.Shown += (s, e) => main.BeginInvoke(new Action(() =>
            {
                if (main.IsDisposed) return;
                main.Hide();
                Form front = (Form)main.LastActiveWindow ?? main.WaitingWindows.Values.LastOrDefault();
                if (front != null && !front.IsDisposed && front.Visible) front.Activate();
            }));
        }

        /// <summary>
        /// A launch request for a page host: a file or link opens in the window the reader worked
        /// in last, brought to the front; with no window on screen (the calendar alone) the page
        /// opens one first - with its tabs, if it has any waiting.
        /// </summary>
        private static void DispatchToPageWindows(MainForm host, string request)
        {
            PageWindowForm shown = host.LastActiveWindow;
            if (shown == null || shown.IsDisposed || !shown.Visible)
                shown = PageWindows.LastOrDefault(w => w.PageOwner == host && !w.IsDisposed && w.Visible);
            if (shown == null)
            {
                Form waiting = host.WaitingWindows.Values.FirstOrDefault(w => w is PageWindowForm);
                if (waiting == null)
                {
                    host.Viewer.OpenWindowForWaitingTabs();
                    ShowAnyAppWindow(host);
                }
                else waiting.Activate();
                if (request != NewWindowRequest) host.OpenRequest(request);
                return;
            }
            if (request == NewWindowRequest)
            {
                host.Viewer.OpenNewAppWindow();
                return;
            }
            if (IsIconic(shown.Handle)) ShowWindow(shown.Handle, SW_RESTORE);
            // Our own window, on our own UI thread (see DispatchRequest): the launcher handed us
            // the foreground rights.
            shown.Activate();
            SetForegroundWindow(shown.Handle);
            host.OpenRequest(request);
        }

        /// <summary>Windows its page draws besides the main one are on screen: extra app windows or tool windows.</summary>
        private static bool OtherWindowsOfPageShown(MainForm main)
        {
            if (PageWindows.Exists(w => w.PageOwner == main && !w.IsDisposed && w.Visible)) return true;
            foreach (var kv in ToolWindowPages)
                if (kv.Value == main && !kv.Key.IsDisposed && kv.Key.Visible) return true;
            return false;
        }

        /// <summary>
        /// A window of a hidden main window's page closed: with nothing of that page left on screen,
        /// the main window closes for real (the app quits, or that page goes).
        /// </summary>
        private static void CloseHiddenMainIfNothingLeft(MainForm main)
        {
            if (main.IsDisposed || !main.Viewer.IsPageHost) return;
            if (!OtherWindowsOfPageShown(main)) main.BeginInvoke(new Action(() => { if (!main.IsDisposed) main.Close(); }));
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
            public void OpenWindowForTab(FluentChromeTabsForm source, FluentTab tab, Action<Form> place)
            {
                MainForm page = PageOf(source);
                if (!(tab?.Tag is string tabId) || page == null || page.IsDisposed) return;
                _pendingPlacement = new PendingPlacement { Page = page, TabId = tabId, Place = place };
                page.Viewer.MoveTabToAppWindow(tabId, null, -1);
            }

            public bool AcceptsDrops(Form window)
            {
                if (ToolWindowPages.TryGetValue(window, out MainForm toolOwner)) return !window.IsDisposed && AcceptsDrops(toolOwner);
                if (window is PageWindowForm page) return PageWindows.Contains(page) && !page.IsDisposed && PageIsOpen(page.PageOwner);
                var main = window as MainForm;
                return main != null && main.Visible && PageIsOpen(main);
            }

            // A tool window let go on a strip: a widget becomes a tab there (any of our windows),
            // a commentary pop-out goes back into its book (its own window only).
            public void MergeIntoTabs(Form window, FluentChromeTabsForm target)
            {
                if (!ToolWindowPages.TryGetValue(window, out MainForm owner) || owner.IsDisposed) return;
                MainForm into = PageOf(target);
                if (into == null || into.IsDisposed) return;
                if (owner.Viewer.MergeToolWindowIntoTabs(window, into.Viewer)) target.Activate();
            }

            // A main window whose page still runs: open and not closing. It may be hidden - closed by
            // the reader while windows its page draws stay - and those windows still take drops.
            private static bool PageIsOpen(MainForm main)
                => main != null && !main.IsDisposed && OpenWindows.Contains(main) && !ClosingWindows.Contains(main);

            public int TabCount(FluentChromeTabsForm window)
                => (window as MainForm)?.TabsMirror.PageTabCount ?? (window as PageWindowForm)?.TabsMirror?.PageTabCount ?? window.Tabs.Count;

            // Called as a drag starts: the floating tab takes the app's current colors, and shows
            // the page (a page window draws in its owner's renderer: no picture yet).
            public Task<Image> CaptureTabPicture(FluentChromeTabsForm window)
            {
                ScreenDocking.DarkMode = AppSettingsRegistry.GetDarkMode();
                ScreenDocking.AccentColor = ParseColor(AppSettingsRegistry.GetAccentColor(), Color.FromArgb(124, 92, 252));
                if (window is MainForm main) return main.Viewer.CapturePagePictureAsync();
                // A window the page draws: its own view (the popup's WebView2) is pictured.
                if (window is PageWindowForm page) return AppViewer.CaptureViewPictureAsync(page.Content);
                return Task.FromResult<Image>(null);
            }

            private static Color ParseColor(string hex, Color fallback)
            {
                try { return string.IsNullOrEmpty(hex) ? fallback : ColorTranslator.FromHtml(hex.StartsWith("#") ? hex : "#" + hex); }
                catch (Exception) { return fallback; }
            }
        }
    }
}
