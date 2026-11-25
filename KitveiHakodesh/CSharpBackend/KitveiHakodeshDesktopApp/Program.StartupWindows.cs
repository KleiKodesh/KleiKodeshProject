using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using KitveiHakodesh.Core.Settings;
using KitveiHakodeshLib;

namespace KitveiHakodeshDesktopApp
{
    // Windows shown at once on a cold start, with the startup picture, before the page is up: every
    // window the page had on screen when the app last quit (and the calendar window, for the
    // calendar shortcut). The page's windows take them over where they are as it opens them; the
    // picture goes once the page has drawn (AppViewer.RemoveSplashWhenDrawn). Any the page does not
    // open again close once it has drawn. UI thread only.
    internal static partial class Program
    {
        // The popup name the page gives an app window, and the calendar shortcut's widget window.
        private const string AppWindowName = "appwindow-";
        private const string CalendarShortcutWindowName = "widget-calendar-shortcut";
        // A waiting app window for whichever window the page opens first (nothing was remembered).
        private const string AnyAppWindow = AppWindowName + "*";

        /// <summary>Shows the windows a cold start of this page host waits on.</summary>
        private static void ShowStartupWindows(MainForm main)
        {
            if (main.StartsWithCalendarOnly)
            {
                ShowWaitingCalendarWindow(main);
                return;
            }
            foreach (string entry in AppSettingsRegistry.GetStartupWindows(main.WindowId).Split('|'))
            {
                if (!TryParseStartupWindow(entry, out string name, out Rectangle bounds, out bool maximized)) continue;
                if (!name.StartsWith(AppWindowName, StringComparison.Ordinal)) continue;
                var window = new PageWindowForm(main, null) { StartPosition = FormStartPosition.Manual, Bounds = bounds };
                if (maximized) window.WindowState = FormWindowState.Maximized;
                ShowWaitingWindow(main, name, window);
            }
            if (!main.WaitingWindows.Keys.Any(k => k.StartsWith(AppWindowName, StringComparison.Ordinal)))
                ShowAnyAppWindow(main);
        }

        /// <summary>A waiting window for the page's next app window, at the page host's saved place.</summary>
        private static void ShowAnyAppWindow(MainForm main)
        {
            if (main.WaitingWindows.ContainsKey(AnyAppWindow)) return;
            var window = new PageWindowForm(main, null) { ClientSize = new Size(1000, 750), StartPosition = FormStartPosition.CenterScreen };
            main.LoadPlaceInto(window);
            ShowWaitingWindow(main, AnyAppWindow, window);
        }

        // The calendar shortcut's window, where the calendar was last, until the page opens it.
        private static void ShowWaitingCalendarWindow(MainForm main)
        {
            var (x, y, width, height) = AppSettingsRegistry.GetPopupBounds(CalendarShortcutWindowName, 1040, 760);
            var window = new Form
            {
                Text = "",
                Icon = main.Icon,
                ShowInTaskbar = true,
                RightToLeft = RightToLeft.Yes,
                RightToLeftLayout = true,
                StartPosition = x == -1 ? FormStartPosition.CenterScreen : FormStartPosition.Manual,
                ClientSize = new Size(width, height),
            };
            if (x != -1) window.Location = new Point(x, y);
            window.Controls.Add(AppViewer.CreateStartupSplash(AppSettingsRegistry.GetDarkMode()));
            ShowWaitingWindow(main, CalendarShortcutWindowName, window);
        }

        private static void ShowWaitingWindow(MainForm main, string name, Form window)
        {
            main.WaitingWindows[name] = window;
            // Closed by the reader before the page took it over: that window is simply gone (closing
            // the last one quits, PageWindowForm).
            window.FormClosed += (s, e) =>
            {
                if (!main.WaitingWindows.TryGetValue(name, out Form w) || w != window) return; // taken over
                main.WaitingWindows.Remove(name);
                // The calendar's window was all there was: closing it quits, as it would once open.
                if (!(window is PageWindowForm) && main.WaitingWindows.Count == 0) CloseHiddenMainIfNothingLeft(main);
            };
            window.Show();
        }

        /// <summary>The waiting window for the page's app window <paramref name="key"/>: its own, else any app window waiting.</summary>
        private static PageWindowForm TakeWaitingAppWindow(MainForm main, string key)
        {
            string name = AppWindowName + key;
            if (!main.WaitingWindows.ContainsKey(name))
                name = main.WaitingWindows.Keys.FirstOrDefault(k => k.StartsWith(AppWindowName, StringComparison.Ordinal));
            return name == null ? null : TakeWaitingWindow(main, name) as PageWindowForm;
        }

        private static Form TakeWaitingWindow(MainForm main, string name)
        {
            if (name == null || !main.WaitingWindows.TryGetValue(name, out Form window)) return null;
            main.WaitingWindows.Remove(name);
            return window.IsDisposed ? null : window;
        }

        /// <summary>The page has drawn: windows it did not open again close.</summary>
        private static void CloseUnclaimedWaitingWindows(MainForm main)
        {
            foreach (Form window in main.WaitingWindows.Values.ToList())
            {
                if (window is PageWindowForm page) page.ClosingByPage = true;
                if (!window.IsDisposed) window.Close();
            }
            main.WaitingWindows.Clear();
        }

        /// <summary>The app quits: the windows of this page on screen now come back first, next start.</summary>
        private static void RememberStartupWindows(MainForm main)
        {
            var entries = new List<string>();
            foreach (PageWindowForm window in PageWindows)
            {
                if (window.PageOwner != main || window.IsDisposed || !window.Visible || window.WindowKey == null) continue;
                Rectangle normal = window.WindowState == FormWindowState.Normal ? window.Bounds : window.RestoreBounds;
                entries.Add(string.Format(CultureInfo.InvariantCulture, "{0}@{1},{2},{3},{4},{5}",
                    AppWindowName + window.WindowKey, normal.X, normal.Y, normal.Width, normal.Height,
                    window.WindowState == FormWindowState.Maximized ? 1 : 0));
            }
            try { AppSettingsRegistry.SetStartupWindows(main.WindowId, string.Join("|", entries)); }
            catch (Exception) { /* registry unwritable: the next start shows one window */ }
        }

        private static bool TryParseStartupWindow(string entry, out string name, out Rectangle bounds, out bool maximized)
        {
            name = null; bounds = Rectangle.Empty; maximized = false;
            int at = entry.IndexOf('@');
            if (at <= 0) return false;
            string[] p = entry.Substring(at + 1).Split(',');
            if (p.Length != 5) return false;
            int[] n = new int[5];
            for (int i = 0; i < 5; i++)
                if (!int.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out n[i])) return false;
            if (n[2] <= 0 || n[3] <= 0) return false;
            name = entry.Substring(0, at);
            bounds = new Rectangle(n[0], n[1], n[2], n[3]);
            maximized = n[4] == 1;
            // A place on a screen no longer there: centred on the main screen instead.
            Rectangle place = bounds;
            if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(place)))
            {
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                bounds = new Rectangle(area.Left + Math.Max(0, (area.Width - bounds.Width) / 2),
                    area.Top + Math.Max(0, (area.Height - bounds.Height) / 2), Math.Min(bounds.Width, area.Width), Math.Min(bounds.Height, area.Height));
            }
            return true;
        }
    }
}
