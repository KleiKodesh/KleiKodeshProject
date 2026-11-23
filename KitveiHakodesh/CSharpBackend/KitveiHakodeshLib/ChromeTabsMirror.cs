using FluentChromeTabs;
using KitveiHakodesh.Core.Settings;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

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

        // Favicons by icon key, rasterized by Vue at the current device pixel ratio so
        // the strip and the in-page lists show the very same glyph. Arrives out of band
        // from the tab snapshots (they change constantly; these almost never do).
        private IReadOnlyDictionary<string, Image> _tabIcons = new Dictionary<string, Image>();

        // Which icon key each strip tab is currently showing, by Vue tab id. Kept here
        // rather than on FluentTab so the shared strip library stays unaware of Vue's
        // icon vocabulary; needed to re-stamp every tab when the set is resent on a DPI
        // change.
        private readonly Dictionary<string, string> _iconKeyByTabId = new Dictionary<string, string>();
        // Last snapshot applied from Vue — what SaveSession writes for the next launch.
        private TabsStateChangedEventArgs _lastState;

        // ── Tabs moving between windows (HostMovesTabsBetweenWindows) ──
        // Labels on this strip for a tab another window's page still owns: dropped here and
        // waiting for this page to take the tab over. Snapshots leave them alone.
        private readonly HashSet<FluentTab> _arrivingLabels = new HashSet<FluentTab>();
        // This page's tabs on their way to another window: their labels are gone from this
        // strip, and a snapshot must not bring them back while the move runs.
        private readonly HashSet<string> _departingTabIds = new HashSet<string>();
        // Departing tabs whose move succeeded: released once a snapshot no longer lists them.
        private readonly HashSet<string> _departedTabIds = new HashSet<string>();

        public ChromeTabsMirror(FluentChromeTabsForm form, AppViewer viewer)
        {
            _form = form ?? throw new ArgumentNullException(nameof(form));
            _viewer = viewer ?? throw new ArgumentNullException(nameof(viewer));

            // Mirrored tabs are metadata only — tearing one off into its own window
            // would detach it from the Vue store, and an empty strip must not close
            // the window (Vue guarantees at least one tab, but snapshots arrive async).
            _form.AllowTabDetach = false;
            _form.ExitOnLastTabClose = false;
            ApplyLanguage(AppSettingsRegistry.GetUiLanguage());

            ApplyPersistedTheme();

            _form.SelectedTabChanged += OnSelectedTabChanged;
            _form.TabPressed += OnTabPressed;
            _form.TabClosing += OnTabClosing;
            _form.NewTabRequested += OnNewTabRequested;

            _viewer.TabsStateChanged += OnTabsStateChanged;
            _viewer.TabIconsChanged += OnTabIconsChanged;
            _viewer.ChromeThemeChanged += OnChromeThemeChanged;
            _viewer.ChromeTabListToggleRequested += OnChromeTabListToggleRequested;
        }

        // ── Tabs moving between windows ─────────────────────────────────────────────
        //
        // The strip drags a label across windows; the tab itself moves page to page (the
        // host's tab-move coordinator). These keep each strip from fighting that move with
        // snapshots of a page that has not caught up yet.

        /// <summary>
        /// Lets the strip drag tabs off into new windows and onto other windows, with the host
        /// moving the tabs (FluentChromeTabsForm.TabTornOff / TabDroppedFromOtherWindow).
        /// </summary>
        public void EnableTabMovesBetweenWindows()
        {
            _form.AllowTabDetach = true;
            _form.HostMovesTabsBetweenWindows = true;
        }

        /// <summary>How many tabs this window's page has (both panes), from its last snapshot.</summary>
        public int PageTabCount => _lastState?.Tabs.Count ?? 0;

        /// <summary>The title the page gave a tab, or null.</summary>
        public string TitleOf(string tabId) => _lastState?.Tabs.FirstOrDefault(t => t.Id == tabId)?.Title;

        /// <summary>A label dropped here for a tab this page is about to take over: kept as it is until settled.</summary>
        public void HoldArrivingLabel(FluentTab label)
        {
            if (label != null) _arrivingLabels.Add(label);
        }

        /// <summary>
        /// The move of an arriving label's tab is over: with <paramref name="newTabId"/> the label
        /// now stands for that tab of this page (keeping the place it was dropped at); with null
        /// the move failed and the label goes.
        /// </summary>
        public void SettleArrivingLabel(FluentTab label, string newTabId)
        {
            if (label == null || !_arrivingLabels.Remove(label) || _form.IsDisposed) return;
            _syncing = true;
            try
            {
                if (newTabId == null)
                {
                    if (label.Owner == _form) _form.CloseTab(label);
                }
                else
                {
                    // A snapshot may have listed the new tab already, appended at the end.
                    foreach (var duplicate in _form.Tabs.Where(t => t != label && t.Tag as string == newTabId).ToList())
                        _form.CloseTab(duplicate);
                    _iconKeyByTabId.Remove(newTabId);
                    label.Tag = newTabId;
                }
            }
            finally { _syncing = false; }
            Resync();
        }

        /// <summary>A tab of this page is moving to another window: its label must not come back meanwhile.</summary>
        public void HoldDepartingTab(string tabId)
        {
            if (tabId != null) _departingTabIds.Add(tabId);
        }

        /// <summary>
        /// The tab now lives in another window and this page is closing its copy: its label
        /// stays away until a snapshot without it arrives (a snapshot already on its way still
        /// lists it).
        /// </summary>
        public void TabDeparted(string tabId)
        {
            if (tabId != null && _departingTabIds.Contains(tabId)) _departedTabIds.Add(tabId);
        }

        /// <summary>The move is over: a tab that stayed (failed move) gets its label back.</summary>
        public void ReleaseDepartingTab(string tabId)
        {
            if (tabId != null && _departingTabIds.Remove(tabId)) Resync();
        }

        /// <summary>Applies the last snapshot again (after the strip changed outside a snapshot).</summary>
        public void Resync()
        {
            if (_lastState != null && !_form.IsDisposed) Reconcile(_lastState);
        }

        // Labels of tabs this page does not own: held arrivals, and a label another window's
        // drag is carrying over this strip right now.
        private bool IsForeignLabel(FluentTab tab)
            => _arrivingLabels.Contains(tab)
               || (tab == _form.DraggedTab && _form.DraggedTabOrigin != null && _form.DraggedTabOrigin != _form);

        // Tabs of this page whose label is elsewhere: moving to another window, or carried by
        // a drag over another window's strip.
        private bool IsAwayTab(string tabId)
            => _departingTabIds.Contains(tabId) || (_form.TabAwayInDrag?.Tag as string) == tabId;

        // ── Strip gestures → Vue ────────────────────────────────────────────────────

        // A selection made without a press (the tab-list dropdown, the keyboard).
        private void OnSelectedTabChanged(object sender, FluentTabEventArgs e)
        {
            if (_syncing) return;
            if (e.Tab?.Tag is string tabId)
                _viewer.NotifyChromeTabActivated(tabId);
            _viewer.FocusWebContent();
        }

        // Every press on a tab reaches the page, also on the tab the strip already shows as
        // selected: the page is the source of truth, and a strip that fell out of step with
        // it would otherwise swallow the click (SelectedTabChanged does not fire for it).
        private void OnTabPressed(object sender, FluentTabEventArgs e)
        {
            if (_syncing) return;
            if (e.Tab?.Tag is string tabId)
                _viewer.NotifyChromeTabActivated(tabId);
            // No FocusWebContent here: the press may be the start of a drag, which needs
            // the strip to keep the mouse. A changed selection hands focus back above.
        }

        private void OnTabClosing(object sender, FluentTabClosingEventArgs e)
        {
            if (_syncing) return;
            // Never close locally — Vue owns the tab lifecycle. Forward the gesture;
            // the tab disappears when the updated snapshot comes back.
            e.Cancel = true;
            if (e.Tab?.Tag is string tabId)
                _viewer.NotifyChromeTabCloseRequested(tabId);
            _viewer.FocusWebContent();
        }

        private void OnNewTabRequested(object sender, NewTabRequestedEventArgs e)
        {
            e.Cancel = true;
            _viewer.NotifyChromeNewTabRequested();
            _viewer.FocusWebContent();
        }

        // ── Vue Ctrl+T / Ctrl+Tab → open the strip's tab-list dropdown ──────────────

        private void OnChromeTabListToggleRequested(object sender, ChromeTabListToggleEventArgs e)
        {
            if (_form.IsDisposed) return;
            int step = e?.QuickSwitchStep ?? 0;
            if (_form.InvokeRequired)
            {
                _form.BeginInvoke(new Action(() => ToggleTabListMenu(step)));
                return;
            }
            ToggleTabListMenu(step);
        }

        private void ToggleTabListMenu(int quickSwitchStep)
        {
            if (_form.IsDisposed) return;

            // Works whether or not the strip is painted: ShowTabListMenu anchors to the
            // tab-list button's rect, which the strip keeps computing from StripHeight
            // regardless of StripVisible. So a hidden strip (fullscreen, Ctrl+H) still
            // drops the popup exactly where a visible one would — no reveal, no flash.
            _form.ShowTabListMenu(0, quickSwitchStep);
        }

        // ── Vue snapshots → strip ───────────────────────────────────────────────────

        private void OnTabIconsChanged(object sender, TabIconsChangedEventArgs e)
        {
            if (_form.IsDisposed) return;
            if (_form.InvokeRequired)
            {
                _form.BeginInvoke(new Action(() => ApplyTabIcons(e.Icons)));
                return;
            }
            ApplyTabIcons(e.Icons);
        }

        private void ApplyTabIcons(IReadOnlyDictionary<string, Image> icons)
        {
            if (_form.IsDisposed) return;
            _tabIcons = icons;

            // Re-stamp the tabs already on the strip — a DPI change resends the set, and
            // the existing tabs are still holding bitmaps rendered for the old scale.
            foreach (var tab in _form.Tabs)
            {
                string tabId = tab.Tag as string;
                string iconKey;
                if (tabId != null && _iconKeyByTabId.TryGetValue(tabId, out iconKey))
                    tab.Icon = LookupIcon(iconKey);
            }
        }

        private Image LookupIcon(string iconKey)
        {
            if (string.IsNullOrEmpty(iconKey)) return null;
            Image image;
            return _tabIcons.TryGetValue(iconKey, out image) ? image : null;
        }

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

            _lastState = e;
            _syncing = true;
            try
            {
                var wanted = new HashSet<string>(e.Tabs.Select(t => t.Id));

                // A tab that has left for another window is held until the page drops it.
                foreach (var gone in _departedTabIds.Where(id => !wanted.Contains(id)).ToList())
                {
                    _departedTabIds.Remove(gone);
                    _departingTabIds.Remove(gone);
                }

                // Remove strip tabs that no longer exist in the Vue store.
                foreach (var tab in _form.Tabs.Where(t => !IsForeignLabel(t) && !(t.Tag is string id && wanted.Contains(id) && !_departingTabIds.Contains(id))).ToList())
                    _form.CloseTab(tab);

                foreach (var staleId in _iconKeyByTabId.Keys.Where(k => !wanted.Contains(k)).ToList())
                    _iconKeyByTabId.Remove(staleId);
                // Index the surviving strip tabs by Vue tab id.
                var byId = new Dictionary<string, FluentTab>();
                foreach (var tab in _form.Tabs)
                    if (!IsForeignLabel(tab) && tab.Tag is string id && !byId.ContainsKey(id))
                        byId[id] = tab;

                // Add new tabs (appended in snapshot order); refresh titles, regions,
                // and each region's own active-tab highlight.
                foreach (var info in e.Tabs)
                {
                    if (!byId.ContainsKey(info.Id) && IsAwayTab(info.Id)) continue;
                    if (!byId.TryGetValue(info.Id, out var tab))
                    {
                        tab = _form.AddTab(info.Title);
                        tab.Tag = info.Id;
                        byId[info.Id] = tab;
                    }

                    if (tab.Title != info.Title)
                        tab.Title = info.Title;

                    // Dropdown-only breadcrumb text — no strip repaint involved.
                    tab.ListTitle = info.ListTitle;

                    // Favicon. The set may not have arrived yet on a cold start, or may
                    // not know this key — either way the tab simply draws without one,
                    // and ApplyTabIcons stamps it as soon as the set lands.
                    string previousKey;
                    if (!_iconKeyByTabId.TryGetValue(info.Id, out previousKey) || previousKey != info.IconKey)
                    {
                        _iconKeyByTabId[info.Id] = info.IconKey;
                        tab.Icon = LookupIcon(info.IconKey);
                    }

                }

                // Selection follows the page's active tab.
                if (e.ActiveTabId != null && byId.TryGetValue(e.ActiveTabId, out var active))
                    _form.SelectedTab = active;
            }
            finally
            {
                _syncing = false;
            }
        }

        // ── Session: last tabs shown again at startup ───────────────────────────────
        //
        // The strip is otherwise empty until WebView2 and Vue are up. The last snapshot
        // (and the favicons) are saved on close and applied at startup as a stand-in;
        // Vue's first real snapshot then reconciles over it by tab id.

        // A small binary file, not JSON: it is read on the UI thread before the window
        // shows, and the first System.Text.Json deserialize on net48 (type loading + JIT of
        // the serializer) cost ~1 s there. BinaryReader is already loaded and costs nothing.
        // Bump SessionFormat when the layout changes; an unknown version reads as no session.
        private const int SessionFormat = 2;

        public void SaveSession(string path)
        {
            TabsStateChangedEventArgs s = _lastState;
            if (s == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var w = new BinaryWriter(File.Create(path)))
                {
                    w.Write(SessionFormat);
                    w.Write(s.Tabs.Count);
                    foreach (MirroredTabInfo t in s.Tabs)
                    {
                        WriteNullable(w, t.Id);
                        WriteNullable(w, t.Title);
                        WriteNullable(w, t.ListTitle);
                        WriteNullable(w, t.IconKey);
                    }
                    WriteNullable(w, s.ActiveTabId);
                    w.Write(_tabIcons.Count);
                    foreach (var pair in _tabIcons)
                        using (var ms = new MemoryStream())
                        {
                            pair.Value.Save(ms, ImageFormat.Png);
                            w.Write(pair.Key);
                            w.Write((int)ms.Length);
                            w.Write(ms.GetBuffer(), 0, (int)ms.Length);
                        }
                }
            }
            catch (Exception) { /* a missing session only means an empty strip at startup */ }
        }

        public void RestoreSession(string path)
        {
            var tabs = new List<MirroredTabInfo>();
            string activeTabId;
            var icons = new Dictionary<string, Image>();
            try
            {
                if (!File.Exists(path)) return;
                using (var r = new BinaryReader(File.OpenRead(path)))
                {
                    if (r.ReadInt32() != SessionFormat) return;
                    int count = r.ReadInt32();
                    for (int i = 0; i < count; i++)
                        tabs.Add(new MirroredTabInfo
                        {
                            Id = ReadNullable(r), Title = ReadNullable(r), ListTitle = ReadNullable(r),
                            IconKey = ReadNullable(r),
                        });
                    activeTabId = ReadNullable(r);
                    int iconCount = r.ReadInt32();
                    for (int i = 0; i < iconCount; i++)
                    {
                        string key = r.ReadString();
                        byte[] png = r.ReadBytes(r.ReadInt32());
                        try { icons[key] = Image.FromStream(new MemoryStream(png)); }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception) { return; }
            if (tabs.Count == 0) return;

            _tabIcons = icons;
            Reconcile(new TabsStateChangedEventArgs(tabs, activeTabId));
        }

        private static void WriteNullable(BinaryWriter w, string value)
        {
            w.Write(value != null);
            if (value != null) w.Write(value);
        }

        private static string ReadNullable(BinaryReader r) => r.ReadBoolean() ? r.ReadString() : null;

        // -- UI language ---------------------------------------------------------------
        //
        // English = LTR chrome + English texts; anything else = RTL + Hebrew. Setting
        // RightToLeftLayout recreates the form handle, so only touch it on a real change.

        private void ApplyLanguage(string uiLanguage)
        {
            bool english = uiLanguage == "en";

            if (_form.RightToLeftLayout == english)
                _form.RightToLeftLayout = !english;
            RightToLeft direction = english ? RightToLeft.No : RightToLeft.Yes;
            if (_form.RightToLeft != direction)
                _form.RightToLeft = direction;

            // Texts come from HostText (the registry is written before this event fires).
            // Caption tooltips replace Windows' OS-language ones.
            _form.Text = HostText.AppTitle;
            _form.TabListOpenTabsHeader = HostText.OpenTabsHeader;
            _form.MinimizeToolTip = HostText.Minimize;
            _form.MaximizeToolTip = HostText.Maximize;
            _form.RestoreToolTip = HostText.Restore;
            _form.CloseToolTip = HostText.Close;
            _form.CloseTabMenuText = HostText.CloseTab;
            _form.CloseOtherTabsMenuText = HostText.CloseOtherTabs;
            _form.CloseTabsToTheLeftMenuText = HostText.CloseTabsToTheLeft;
            _form.CloseTabsToTheRightMenuText = HostText.CloseTabsToTheRight;
        }

        // ── Theme ───────────────────────────────────────────────────────────────────

        private void OnChromeThemeChanged(object sender, ChromeThemeChangedEventArgs e)
        {
            if (_form.IsDisposed) return;
            if (_form.InvokeRequired)
            {
                _form.BeginInvoke(new Action(() =>
                {
                    ApplyTheme(e.IsDark, e.ChromeColor, e.AccentColor, e.BorderColor);
                    if (e.UiLanguage != null) ApplyLanguage(e.UiLanguage);
                }));
                return;
            }
            ApplyTheme(e.IsDark, e.ChromeColor, e.AccentColor, e.BorderColor);
            if (e.UiLanguage != null) ApplyLanguage(e.UiLanguage);
        }

        private void ApplyPersistedTheme()
            => ApplyTheme(
                AppSettingsRegistry.GetDarkMode(),
                AppSettingsRegistry.GetChromeColor(),
                AppSettingsRegistry.GetAccentColor(),
                AppSettingsRegistry.GetBorderColor());

        private void ApplyTheme(bool isDark, string chromeColorHex, string accentColorHex, string borderColorHex)
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
