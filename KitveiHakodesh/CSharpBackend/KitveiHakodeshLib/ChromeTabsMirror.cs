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

            // Hebrew caption-button tooltips (replace Windows' OS-language ones)
            _form.MinimizeToolTip = "מזער";
            _form.MaximizeToolTip = "הגדל";
            _form.RestoreToolTip = "שחזר";
            _form.CloseToolTip = "סגור";

            ApplyPersistedTheme();

            _form.SelectedTabChanged += OnSelectedTabChanged;
            _form.TabClosing += OnTabClosing;
            _form.NewTabRequested += OnNewTabRequested;
            _form.SplitRatioChanged += OnSplitRatioChanged;
            _form.TabDraggedToGroup += OnTabDraggedToGroup;

            _viewer.TabsStateChanged += OnTabsStateChanged;
            _viewer.ChromeThemeChanged += OnChromeThemeChanged;
            _viewer.ChromeTabListToggleRequested += OnChromeTabListToggleRequested;
        }

        // ── Strip gestures → Vue ────────────────────────────────────────────────────

        private void OnSelectedTabChanged(object sender, FluentTabEventArgs e)
        {
            if (_syncing) return;
            if (e.Tab?.Tag is string tabId)
                _viewer.NotifyChromeTabActivated(tabId);
            // Clicking a strip tab moved OS focus to the native strip — hand it back to the
            // web content so wheel/keyboard gestures keep working without a click in the page.
            _viewer.FocusWebContent();
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
            _viewer.NotifyChromeNewTabRequested(e.Group == 1 ? 2 : 1);
            _viewer.FocusWebContent();
        }

        private void OnTabDraggedToGroup(object sender, FluentTabGroupEventArgs e)
        {
            if (_syncing) return;
            // Region 0 = pane 1, region 1 = pane 2. Vue moves the tab between panes and
            // pushes back a fresh snapshot, which reconciles the strip's Group/Highlighted.
            if (e.Tab?.Tag is string tabId)
                _viewer.NotifyChromeTabMovedToPane(tabId, e.Group == 1 ? 2 : 1);
        }

        private void OnSplitRatioChanged(object sender, EventArgs e)
        {
            if (_syncing) return;
            // The strip's SplitRatio is region 0's (pane 1's) share; Vue's splitViewFraction
            // is pane 2's share — mirror images of the same divider.
            _viewer.NotifyChromeSplitFractionChanged(1.0 - _form.SplitRatio);
        }

        // ── Vue Ctrl+T → open the strip's tab-list dropdown ─────────────────────────

        // While the tab list is shown in fullscreen we reveal the strip just for the
        // duration of the popup (ShowTabListMenu anchors to the tab-list button, which
        // is not painted while the strip is hidden), then re-hide it on close. This
        // timer polls IsTabListOpen because the strip has no "tab list closed" event.
        private System.Windows.Forms.Timer _fullscreenTabListTimer;

        private void OnChromeTabListToggleRequested(object sender, EventArgs e)
        {
            if (_form.IsDisposed) return;
            if (_form.InvokeRequired)
            {
                _form.BeginInvoke(new Action(ToggleTabListMenu));
                return;
            }
            ToggleTabListMenu();
        }

        private void ToggleTabListMenu()
        {
            if (_form.IsDisposed) return;

            // Group 0 is the normal (non-split) region; when split, the focused pane's
            // region owns the visible active tab, so target the region of the selected tab.
            int group = _form.SplitStrip && _form.SelectedTab != null ? _form.SelectedTab.Group : 0;

            // Not in fullscreen — the strip is visible, so just toggle the menu.
            if (_form.StripVisible)
            {
                _form.ShowTabListMenu(group);
                return;
            }

            // Fullscreen: the strip is hidden. If the menu is already up (from a prior
            // toggle) let ShowTabListMenu close it; otherwise reveal the strip so the
            // popup can anchor, then re-hide it once the user dismisses the menu.
            if (_form.IsTabListOpen)
            {
                _form.ShowTabListMenu(group);
                return;
            }

            _form.StripVisible = true;
            _form.ShowTabListMenu(group);

            _fullscreenTabListTimer?.Stop();
            _fullscreenTabListTimer?.Dispose();
            _fullscreenTabListTimer = new System.Windows.Forms.Timer { Interval = 150 };
            _fullscreenTabListTimer.Tick += (s, _) =>
            {
                if (_form.IsDisposed || !_form.IsTabListOpen)
                {
                    _fullscreenTabListTimer.Stop();
                    _fullscreenTabListTimer.Dispose();
                    _fullscreenTabListTimer = null;
                    // Only re-hide if we're still in fullscreen (the user may have exited
                    // it while the menu was open, which legitimately shows the strip).
                    if (!_form.IsDisposed && _form.FormBorderStyle == System.Windows.Forms.FormBorderStyle.None
                        && _form.WindowState == System.Windows.Forms.FormWindowState.Maximized)
                        _form.StripVisible = false;
                }
            };
            _fullscreenTabListTimer.Start();
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

            _syncing = true;
            try
            {
                _form.SplitStrip = e.SplitView;

                if (e.SplitView)
                {
                    // Keep the ratio as the drag baseline / fallback...
                    _form.SplitRatio = 1.0 - e.SplitFraction;

                    // ...but pin the divider to the exact device pixels Vue rendered, so the
                    // two bars form one seamless line at any window width. Vue measures from
                    // the viewport's left edge; the mirrored (RTL) strip's client X axis runs
                    // the other way, so flip.
                    if (e.DividerLeftPx >= 0 && e.DividerWidthPx > 0)
                    {
                        // -1 in the mirrored flip: GDI renders fills in a WS_EX_LAYOUTRTL
                        // window shifted one device pixel (measured constant across widths).
                        int left = _form.RightToLeftLayout
                            ? _form.ClientSize.Width - e.DividerLeftPx - e.DividerWidthPx - 1
                            : e.DividerLeftPx;
                        _form.SetSplitDividerPixels(left, e.DividerWidthPx);
                    }
                }

                var wanted = new HashSet<string>(e.Tabs.Select(t => t.Id));

                // Remove strip tabs that no longer exist in the Vue store.
                foreach (var tab in _form.Tabs.Where(t => !(t.Tag is string id && wanted.Contains(id))).ToList())
                    _form.CloseTab(tab);

                // Index the surviving strip tabs by Vue tab id.
                var byId = new Dictionary<string, FluentTab>();
                foreach (var tab in _form.Tabs)
                    if (tab.Tag is string id && !byId.ContainsKey(id))
                        byId[id] = tab;

                // Add new tabs (appended in snapshot order); refresh titles, regions,
                // and each region's own active-tab highlight.
                foreach (var info in e.Tabs)
                {
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

                    // "prefix: title" caption, drawn only when the tab has room for it
                    // (the setter no-ops when unchanged, so no repaint churn).
                    tab.WideTitle = info.StripTitle;

                    // Group follows the pane only while split view is open; an adopted
                    // orphan (pane 2, split off) lives in region 0 and can be pane 1's
                    // active tab — so the highlight follows the region, not the pane.
                    int group = e.SplitView && info.Pane == 2 ? 1 : 0;
                    tab.Group = group;
                    tab.Highlighted = group == 1
                        ? info.Id == e.Pane2ActiveTabId
                        : info.Id == e.ActiveTabId;
                }

                // Selection follows the focused pane's active tab.
                string focusedActiveId = e.SplitView && e.FocusedPane == 2 ? e.Pane2ActiveTabId : e.ActiveTabId;
                if (focusedActiveId != null && byId.TryGetValue(focusedActiveId, out var active))
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
                _form.BeginInvoke(new Action(() => ApplyTheme(e.IsDark, e.ChromeColor, e.AccentColor, e.BorderColor)));
                return;
            }
            ApplyTheme(e.IsDark, e.ChromeColor, e.AccentColor, e.BorderColor);
        }

        private void ApplyPersistedTheme()
            => ApplyTheme(
                AppSettings.LoadDarkMode(),
                AppSettings.LoadChromeColor(),
                AppSettings.LoadAccentColor(),
                AppSettings.LoadBorderColor());

        private void ApplyTheme(bool isDark, string chromeColorHex, string accentColorHex, string borderColorHex)
        {
            _form.AccentColor = TryParseColor(accentColorHex, out Color accent) ? accent : (Color?) null;
            _form.SplitDividerColor = TryParseColor(borderColorHex, out Color border) ? border : (Color?) null;

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
