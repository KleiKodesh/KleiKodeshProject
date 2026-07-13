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
            IReadOnlyList<MirroredRecentInfo> recentItems)
        {
            Tabs = tabs;
            ActiveTabId = activeTabId;
            RecentItems = recentItems;
        }

        public IReadOnlyList<MirroredTabInfo> Tabs { get; }
        public string ActiveTabId { get; }

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
            TabsStateChanged?.Invoke(this, new TabsStateChangedEventArgs(tabs, activeTabId, recent));
        }

        /// <summary>Forwards a native tab-strip selection to Vue.</summary>
        public void NotifyChromeTabActivated(string tabId)
            => _bridge?.PushEvent(new { @event = "chromeTabActivated", tabId });

        /// <summary>Forwards a native tab-strip close gesture to Vue (Vue decides and closes).</summary>
        public void NotifyChromeTabCloseRequested(string tabId)
            => _bridge?.PushEvent(new { @event = "chromeTabCloseRequested", tabId });

        /// <summary>Forwards the native "+" / Ctrl+T gesture to Vue.</summary>
        public void NotifyChromeNewTabRequested()
            => _bridge?.PushEvent(new { @event = "chromeTabNewRequested" });

        /// <summary>Forwards a recently-opened-document activation from the tab-list dropdown to Vue.</summary>
        public void NotifyChromeRecentActivated(string key)
            => _bridge?.PushEvent(new { @event = "chromeRecentActivated", key });
    }
}
