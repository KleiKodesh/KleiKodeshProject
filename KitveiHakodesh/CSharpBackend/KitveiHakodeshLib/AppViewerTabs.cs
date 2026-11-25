using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.Json;

namespace KitveiHakodeshLib
{
    /// <summary>A single Vue tab as reported by the frontend tab mirror.</summary>
    public class MirroredTabInfo
    {
        public string Id { get; set; }
        public string Title { get; set; }
        /// <summary>Full breadcrumb ("title · toc path") for the tab-list dropdown; falls back to Title when empty.</summary>
        public string ListTitle { get; set; }
        /// <summary>
        /// Which favicon this tab shows ("book", "pdf", …). Looked up in the icon set Vue
        /// sends separately via 'tabIcons'; empty or unknown means draw no icon.
        /// </summary>
        public string IconKey { get; set; }
    }

    /// <summary>
    /// A Vue request to open the native tab-list dropdown, via the 'toggleChromeTabList' action.
    /// </summary>
    public class ChromeTabListToggleEventArgs : EventArgs
    {
        public ChromeTabListToggleEventArgs(int quickSwitchStep)
        {
            QuickSwitchStep = quickSwitchStep;
        }

        /// <summary>
        /// 0 for a plain Ctrl+T toggle. +1 / -1 when Ctrl+Tab (or Ctrl+Shift+Tab) asked for the
        /// hold-to-scroll switcher, giving the direction to step in.
        /// </summary>
        public int QuickSwitchStep { get; }
    }

    /// <summary>The rasterized favicon set from Vue, keyed by icon name.</summary>
    public class TabIconsChangedEventArgs : EventArgs
    {
        public TabIconsChangedEventArgs(IReadOnlyDictionary<string, Image> icons)
        {
            Icons = icons;
        }

        public IReadOnlyDictionary<string, Image> Icons { get; }
    }

    /// <summary>Full snapshot of the Vue tab store, sent via the 'tabsChanged' bridge action.</summary>
    public class TabsStateChangedEventArgs : EventArgs
    {
        public TabsStateChangedEventArgs(IReadOnlyList<MirroredTabInfo> tabs, string activeTabId, string windowKey = "")
        {
            Tabs = tabs;
            ActiveTabId = activeTabId;
            WindowKey = windowKey ?? "";
        }

        /// <summary>
        /// The app window these tabs belong to: "" for the viewer's own window, else an extra
        /// window the page opened and draws itself (AppWindowRequested).
        /// </summary>
        public string WindowKey { get; }

        public IReadOnlyList<MirroredTabInfo> Tabs { get; }

        /// <summary>The active tab's id.</summary>
        public string ActiveTabId { get; }
    }

    // Tab mirroring between the Vue tab store and a native chrome-tabs host.
    //
    // Vue is the source of truth: it pushes full snapshots via the 'tabsChanged'
    // action whenever its tab list changes. Native tab-strip gestures are never
    // applied locally — they are forwarded to Vue as push events (chromeTabActivated /
    // chromeTabCloseRequested / chromeTabNewRequested); Vue applies them to its store
    // and the resulting snapshot flows back through TabsStateChanged.
    public partial class AppViewer
    {
        /// <summary>Raised on the UI thread whenever Vue reports a new tab snapshot.</summary>
        public event EventHandler<TabsStateChangedEventArgs> TabsStateChanged;

        /// <summary>
        /// Raised on the UI thread when Vue sends its rasterized favicon set — once at
        /// startup and again on a DPI change.
        /// </summary>
        public event EventHandler<TabIconsChangedEventArgs> TabIconsChanged;

        /// <summary>
        /// Raised when Vue asks to toggle the native chrome tab-strip's tab-list dropdown
        /// (Ctrl+T in the standalone/desktop app). ChromeTabsMirror handles it by calling
        /// <c>ShowTabListMenu</c> on the strip form; it must work even in fullscreen.
        ///
        /// A non-zero <see cref="ChromeTabListToggleEventArgs.QuickSwitchStep" /> means the
        /// request came from Ctrl+Tab rather than Ctrl+T, and the dropdown should open as a
        /// hold-to-scroll switcher stepping in that direction.
        /// </summary>
        public event EventHandler<ChromeTabListToggleEventArgs> ChromeTabListToggleRequested;

        private void HandleTabsChanged(JsonElement root, string id)
        {
            _bridge.Reply(id, new { });

            var tabs = new List<MirroredTabInfo>();
            if (root.TryGetProperty("tabs", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in arr.EnumerateArray())
                {
                    string tabId = t.TryGetProperty("id", out var i) ? i.GetString() : null;
                    if (string.IsNullOrEmpty(tabId)) continue;
                    tabs.Add(new MirroredTabInfo
                    {
                        Id = tabId,
                        Title = t.TryGetProperty("title", out var ti) ? (ti.GetString() ?? "") : "",
                        ListTitle = t.TryGetProperty("listTitle", out var lt) ? (lt.GetString() ?? "") : "",
                        IconKey = t.TryGetProperty("iconKey", out var ik) ? (ik.GetString() ?? "") : "",
                    });
                }
            }

            string activeTabId = root.TryGetProperty("activeTabId", out var a) ? a.GetString() : null;
            string windowKey = root.TryGetProperty("windowKey", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() : "";
            var state = new TabsStateChangedEventArgs(tabs, activeTabId, windowKey);
            _lastTabsStates[state.WindowKey] = state;
            TabsStateChanged?.Invoke(this, state);
        }

        // The last snapshot per app window: a window's strip may be made after its first one came.
        private readonly Dictionary<string, TabsStateChangedEventArgs> _lastTabsStates = new Dictionary<string, TabsStateChangedEventArgs>();

        /// <summary>An extra app window closed: its last tab snapshot goes.</summary>
        public void ForgetTabsState(string windowKey) => _lastTabsStates.Remove(windowKey ?? "");

        /// <summary>The page's last tab snapshot for an app window ("" = this viewer's own), or null.</summary>
        public TabsStateChangedEventArgs LastTabsState(string windowKey)
            => _lastTabsStates.TryGetValue(windowKey ?? "", out var state) ? state : null;

        /// <summary>The page's last favicon set, or null before the first one came.</summary>
        public IReadOnlyDictionary<string, Image> LastTabIcons { get; private set; }

        /// <summary>
        /// Vue's rasterized favicon set, keyed by icon name. Sent once per session and
        /// again whenever the device pixel ratio changes, so the bitmaps always match the
        /// size the strip draws them at.
        /// </summary>
        private void HandleTabIcons(JsonElement root, string id)
        {
            _bridge.Reply(id, new { });

            var icons = new Dictionary<string, Image>();
            if (root.TryGetProperty("icons", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in arr.EnumerateArray())
                {
                    string key = entry.TryGetProperty("key", out var k) ? k.GetString() : null;
                    string png = entry.TryGetProperty("png", out var p) ? p.GetString() : null;
                    if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(png)) continue;

                    // "data:image/png;base64,...." — take what follows the comma.
                    int comma = png.IndexOf(',');
                    if (comma >= 0) png = png.Substring(comma + 1);

                    try
                    {
                        byte[] bytes = Convert.FromBase64String(png);
                        // The stream must stay alive for the Image's lifetime (GDI+ reads
                        // lazily), so no using block here.
                        icons[key] = Image.FromStream(new System.IO.MemoryStream(bytes));
                    }
                    catch
                    {
                        // A malformed icon just means that tab draws without one.
                    }
                }
            }

            if (icons.Count == 0) return;
            LastTabIcons = icons;

            if (InvokeRequired)
                Invoke(new Action(() => TabIconsChanged?.Invoke(this, new TabIconsChangedEventArgs(icons))));
            else
                TabIconsChanged?.Invoke(this, new TabIconsChangedEventArgs(icons));
        }

        private void HandleToggleChromeTabList(JsonElement root, string id)
        {
            _bridge.Reply(id, new { });

            // Absent (plain Ctrl+T) means a plain toggle; ±1 opens the quick switcher.
            int step = root.TryGetProperty("quickSwitchStep", out var s) && s.TryGetInt32(out int parsed)
                ? parsed
                : 0;

            var args = new ChromeTabListToggleEventArgs(step);
            if (InvokeRequired)
                Invoke(new Action(() => ChromeTabListToggleRequested?.Invoke(this, args)));
            else
                ChromeTabListToggleRequested?.Invoke(this, args);
        }

        /// <summary>
        /// Moves a tab into another window this page draws - live, nothing reloads:
        /// <paramref name="windowKey"/> "" is this viewer's own window, null a new window.
        /// </summary>
        public void MoveTabToAppWindow(string tabId, string windowKey, int position)
            => _bridge?.PushEvent(new { @event = "moveTabToAppWindow", tabId, windowKey, position });

        /// <summary>The reader closed a window this page draws: the page closes its tabs, then the window.</summary>
        public void CloseAppWindow(string windowKey)
            => _bridge?.PushEvent(new { @event = "closeAppWindow", windowKey });

        /// <summary>Opens another window of this page, with a new tab in it.</summary>
        public void OpenNewAppWindow()
            => _bridge?.PushEvent(new { @event = "openNewAppWindow" });

        /// <summary>Forwards a native tab-strip selection to Vue.</summary>
        public void NotifyChromeTabActivated(string tabId)
            => _bridge?.PushEvent(new { @event = "chromeTabActivated", tabId });

        /// <summary>Forwards a native tab-strip close gesture to Vue (Vue decides and closes).</summary>
        public void NotifyChromeTabCloseRequested(string tabId)
            => _bridge?.PushEvent(new { @event = "chromeTabCloseRequested", tabId });

        /// <summary>Forwards the native "+" / Ctrl+T gesture to Vue, for the strip of app window <paramref name="windowKey"/> ("" = this viewer's own).</summary>
        public void NotifyChromeNewTabRequested(string windowKey = "")
            => _bridge?.PushEvent(new { @event = "chromeTabNewRequested", windowKey = windowKey ?? "" });
    }
}
