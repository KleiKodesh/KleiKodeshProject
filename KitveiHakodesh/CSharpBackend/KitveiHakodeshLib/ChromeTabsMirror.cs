using FluentChromeTabs;
using KitveiHakodeshLib.Settings;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace KitveiHakodeshLib
{
    /// <summary>
    /// Two-way glue between a <see cref="FluentChromeTabsForm"/> tab strip (loose mode,
    /// content-less tabs) and the Vue tab store hosted in an <see cref="AppViewer"/>.
    ///
    /// Vue is the source of truth. Snapshots arriving via <see cref="AppViewer.TabsStateChanged"/>
    /// are reconciled into the strip — membership, titles, and selection, but never order:
    /// Vue keeps its list in MRU order while the strip keeps a stable visual order.
    /// User gestures on the strip (select / close / "+") are never applied locally;
    /// they are forwarded to Vue, which updates its store and pushes back a fresh snapshot.
    ///
    /// The strip theme follows the Vue theme via <see cref="AppViewer.ChromeThemeChanged"/>
    /// (the extended 'setTheme' bridge action), and the last theme is persisted so the
    /// strip is correctly colored before the WebView finishes loading.
    /// </summary>
    public class ChromeTabsMirror
    {
        private readonly FluentChromeTabsForm _form;
        private readonly AppViewer _viewer;

        // True while a Vue snapshot is being applied to the strip — suppresses the
        // gesture handlers so programmatic changes don't echo back to Vue.
        private bool _syncing;

        // Latest recently-opened documents from Vue (not currently open in any tab),
        // shown as the "נסגרו לאחרונה" section of the tab-list dropdown.
        private IReadOnlyList<MirroredRecentInfo> _recentItems = new List<MirroredRecentInfo>();

        public ChromeTabsMirror(FluentChromeTabsForm form, AppViewer viewer)
        {
            _form = form ?? throw new ArgumentNullException(nameof(form));
            _viewer = viewer ?? throw new ArgumentNullException(nameof(viewer));

            // Mirrored tabs are metadata only — tearing one off into its own window
            // would detach it from the Vue store, and an empty strip must not close
            // the window (Vue guarantees at least one tab, but snapshots arrive async).
            _form.AllowTabDetach = false;
            _form.ExitOnLastTabClose = false;
            _form.TabListOpenTabsHeader = "לשוניות פתוחות";

            ApplyPersistedTheme();

            _form.SelectedTabChanged += OnSelectedTabChanged;
            _form.TabClosing += OnTabClosing;
            _form.NewTabRequested += OnNewTabRequested;
            _form.TabListOpening += OnTabListOpening;

            _viewer.TabsStateChanged += OnTabsStateChanged;
            _viewer.ChromeThemeChanged += OnChromeThemeChanged;
        }

        // ── Strip gestures → Vue ────────────────────────────────────────────────────

        private void OnSelectedTabChanged(object sender, FluentTabEventArgs e)
        {
            if (_syncing) return;
            if (e.Tab?.Tag is string tabId)
                _viewer.NotifyChromeTabActivated(tabId);
        }

        private void OnTabClosing(object sender, FluentTabClosingEventArgs e)
        {
            if (_syncing) return;
            // Never close locally — Vue owns the tab lifecycle. Forward the gesture;
            // the tab disappears when the updated snapshot comes back.
            e.Cancel = true;
            if (e.Tab?.Tag is string tabId)
                _viewer.NotifyChromeTabCloseRequested(tabId);
        }

        private void OnNewTabRequested(object sender, NewTabRequestedEventArgs e)
        {
            e.Cancel = true;
            _viewer.NotifyChromeNewTabRequested();
        }

        private void OnTabListOpening(object sender, TabListOpeningEventArgs e)
        {
            if (_recentItems.Count == 0) return;

            var recent = new TabListSection("נסגרו לאחרונה");
            foreach (var item in _recentItems)
            {
                string key = item.Key;
                recent.Items.Add(new TabListItem(item.Title, false, () => _viewer.NotifyChromeRecentActivated(key)));
            }
            e.Sections.Add(recent);
        }

        // ── Vue snapshots → strip ───────────────────────────────────────────────────

        private void OnTabsStateChanged(object sender, TabsStateChangedEventArgs e)
        {
            if (_form.IsDisposed) return;
            if (_form.InvokeRequired)
            {
                _form.BeginInvoke(new Action(() => Reconcile(e)));
                return;
            }
            Reconcile(e);
        }

        private void Reconcile(TabsStateChangedEventArgs e)
        {
            if (_form.IsDisposed) return;

            _recentItems = e.RecentItems ?? _recentItems;

            _syncing = true;
            try
            {
                var wanted = new HashSet<string>(e.Tabs.Select(t => t.Id));

                // Remove strip tabs that no longer exist in the Vue store.
                foreach (var tab in _form.Tabs.Where(t => !(t.Tag is string id && wanted.Contains(id))).ToList())
                    _form.CloseTab(tab);

                // Index the surviving strip tabs by Vue tab id.
                var byId = new Dictionary<string, FluentTab>();
                foreach (var tab in _form.Tabs)
                    if (tab.Tag is string id && !byId.ContainsKey(id))
                        byId[id] = tab;

                // Add new tabs (appended in snapshot order) and refresh titles.
                foreach (var info in e.Tabs)
                {
                    if (byId.TryGetValue(info.Id, out var existing))
                    {
                        if (existing.Title != info.Title)
                            existing.Title = info.Title;
                    }
                    else
                    {
                        var added = _form.AddTab(info.Title);
                        added.Tag = info.Id;
                        byId[info.Id] = added;
                    }
                }

                // Selection follows the focused pane's active tab.
                if (e.ActiveTabId != null && byId.TryGetValue(e.ActiveTabId, out var active))
                    _form.SelectedTab = active;
            }
            finally
            {
                _syncing = false;
            }
        }

        // ── Theme ───────────────────────────────────────────────────────────────────

        private void OnChromeThemeChanged(object sender, ChromeThemeChangedEventArgs e)
        {
            if (_form.IsDisposed) return;
            if (_form.InvokeRequired)
            {
                _form.BeginInvoke(new Action(() => ApplyTheme(e.IsDark, e.ChromeColor, e.AccentColor)));
                return;
            }
            ApplyTheme(e.IsDark, e.ChromeColor, e.AccentColor);
        }

        private void ApplyPersistedTheme()
            => ApplyTheme(AppSettings.LoadDarkMode(), AppSettings.LoadChromeColor(), AppSettings.LoadAccentColor());

        private void ApplyTheme(bool isDark, string chromeColorHex, string accentColorHex)
        {
            _form.AccentColor = TryParseColor(accentColorHex, out Color accent) ? accent : (Color?) null;

            if (TryParseColor(chromeColorHex, out Color color))
                _form.CustomThemeColor = color;
            else
                _form.Theme = isDark ? FluentChromeTabsTheme.Dark : FluentChromeTabsTheme.Light;
        }

        private static bool TryParseColor(string hex, out Color color)
        {
            color = Color.Empty;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            try
            {
                color = ColorTranslator.FromHtml(hex);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
