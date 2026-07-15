using System;
using System.Collections.Generic;
using System.Text.Json;

namespace KitveiHakodeshLib
{
    /// <summary>A single Vue tab as reported by the frontend tab mirror.</summary>
    public class MirroredTabInfo
    {
        public string Id { get; set; }
        public string Title { get; set; }
        /// <summary>Which Vue split pane the tab belongs to (1 = main shell, 2 = secondary shell).</summary>
        public int Pane { get; set; } = 1;
    }

    /// <summary>A recently opened document (not currently open in a tab), as reported by Vue.</summary>
    public class MirroredRecentInfo
    {
        /// <summary>Stable key from the Vue recently-opened store; echoed back on activation.</summary>
        public string Key { get; set; }
        public string Title { get; set; }
    }

    /// <summary>Full snapshot of the Vue tab store, sent via the 'tabsChanged' bridge action.</summary>
    public class TabsStateChangedEventArgs : EventArgs
    {
        public TabsStateChangedEventArgs(
            IReadOnlyList<MirroredTabInfo> tabs,
            string activeTabId,
            string pane2ActiveTabId,
            bool splitView,
            int focusedPane,
            double splitFraction,
            int dividerLeftPx,
            int dividerWidthPx,
            IReadOnlyList<MirroredRecentInfo> recentItems)
        {
            Tabs = tabs;
            ActiveTabId = activeTabId;
            Pane2ActiveTabId = pane2ActiveTabId;
            SplitView = splitView;
            FocusedPane = focusedPane;
            SplitFraction = splitFraction;
            DividerLeftPx = dividerLeftPx;
            DividerWidthPx = dividerWidthPx;
            RecentItems = recentItems;
        }

        public IReadOnlyList<MirroredTabInfo> Tabs { get; }

        /// <summary>Pane 1's active tab id.</summary>
        public string ActiveTabId { get; }

        /// <summary>Pane 2's active tab id; empty/null when split view is off.</summary>
        public string Pane2ActiveTabId { get; }

        /// <summary>Whether Vue's split view is open (drives the split tab strip).</summary>
        public bool SplitView { get; }

        /// <summary>Which pane has focus (1 or 2); 1 when split view is off.</summary>
        public int FocusedPane { get; }

        /// <summary>Pane 2's share of the window width (Vue's splitViewFraction, 0.15–0.85).</summary>
        public double SplitFraction { get; }

        /// <summary>
        /// Exact device pixels of the rendered Vue split divider, measured from the
        /// webview's viewport left edge. -1/0 when not measured (fall back to fraction).
        /// </summary>
        public int DividerLeftPx { get; }
        public int DividerWidthPx { get; }

        /// <summary>Recently opened documents for the tab-list dropdown's extra section.</summary>
        public IReadOnlyList<MirroredRecentInfo> RecentItems { get; }
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
                        Pane = t.TryGetProperty("pane", out var p) && p.TryGetInt32(out int pane) ? pane : 1,
                    });
                }
            }

            var recent = new List<MirroredRecentInfo>();
            if (root.TryGetProperty("recent", out var recentArr) && recentArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in recentArr.EnumerateArray())
                {
                    string key = r.TryGetProperty("key", out var k) ? k.GetString() : null;
                    if (string.IsNullOrEmpty(key)) continue;
                    recent.Add(new MirroredRecentInfo
                    {
                        Key = key,
                        Title = r.TryGetProperty("title", out var rt) ? (rt.GetString() ?? "") : "",
                    });
                }
            }

            string activeTabId = root.TryGetProperty("activeTabId", out var a) ? a.GetString() : null;
            string pane2ActiveTabId = root.TryGetProperty("pane2ActiveTabId", out var a2) ? a2.GetString() : null;
            bool splitView = root.TryGetProperty("splitView", out var sv) && sv.ValueKind == JsonValueKind.True;
            int focusedPane = root.TryGetProperty("focusedPane", out var fp) && fp.TryGetInt32(out int fpv) ? fpv : 1;
            double splitFraction = root.TryGetProperty("splitFraction", out var sf) && sf.TryGetDouble(out double sfv)
                ? sfv
                : 0.5;
            int dividerLeftPx = root.TryGetProperty("splitDividerLeftPx", out var dl) && dl.TryGetInt32(out int dlv)
                ? dlv
                : -1;
            int dividerWidthPx = root.TryGetProperty("splitDividerWidthPx", out var dw) && dw.TryGetInt32(out int dwv)
                ? dwv
                : 0;

            TabsStateChanged?.Invoke(this,
                new TabsStateChangedEventArgs(
                    tabs, activeTabId, pane2ActiveTabId, splitView, focusedPane, splitFraction,
                    dividerLeftPx, dividerWidthPx, recent));
        }

        /// <summary>Forwards a native tab-strip selection to Vue.</summary>
        public void NotifyChromeTabActivated(string tabId)
            => _bridge?.PushEvent(new { @event = "chromeTabActivated", tabId });

        /// <summary>Forwards a native tab-strip close gesture to Vue (Vue decides and closes).</summary>
        public void NotifyChromeTabCloseRequested(string tabId)
            => _bridge?.PushEvent(new { @event = "chromeTabCloseRequested", tabId });

        /// <summary>Forwards the native "+" / Ctrl+T gesture to Vue, with the target pane (1 or 2).</summary>
        public void NotifyChromeNewTabRequested(int pane = 1)
            => _bridge?.PushEvent(new { @event = "chromeTabNewRequested", pane });

        /// <summary>Forwards a recently-opened-document activation from the tab-list dropdown to Vue.</summary>
        public void NotifyChromeRecentActivated(string key)
            => _bridge?.PushEvent(new { @event = "chromeRecentActivated", key });

        /// <summary>Forwards a live drag of the native split divider to Vue (pane 2's width share).</summary>
        public void NotifyChromeSplitFractionChanged(double fraction)
            => _bridge?.PushEvent(new { @event = "chromeSplitFractionChanged", fraction });

        /// <summary>Forwards a cross-region tab drag (split strip) to Vue so it moves the tab between panes.</summary>
        public void NotifyChromeTabMovedToPane(string tabId, int pane)
            => _bridge?.PushEvent(new { @event = "chromeTabMovedToPane", tabId, pane });
    }
}
