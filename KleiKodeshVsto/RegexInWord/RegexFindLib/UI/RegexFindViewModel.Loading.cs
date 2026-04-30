using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace RegexFindLib.UI
{
    public partial class RegexFindViewModel
    {
        // ── Font loading — async, shared across instances ─────────────────────

        internal static void ScheduleFontLoad()
        {
            lock (_fontLock)
            {
                if (_fontsLoaded) return;
            }
            var dispatcher = System.Windows.Application.Current?.Dispatcher
                          ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    List<FontItem> items;
                    using (var col = new System.Drawing.Text.InstalledFontCollection())
                        items = col.Families
                            .Select(f => new FontItem(f.Name, FontItem.DetectHebrew(f.Name)))
                            .OrderBy(f => f.IsHebrew ? 0 : 1)
                            .ThenBy(f => f.Name)
                            .ToList();

                    dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        lock (_fontLock)
                        {
                            if (_fontsLoaded) return;
                            foreach (var item in items)
                                FontList.Add(item);
                            _fontsLoaded = true;
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
                catch { }
            });
        }

        // ── Style loading — per-instance, async, refreshed on visibility/focus ──
        // Styles are document-specific and filtered by InUse — they can change mid-session.
        // Load asynchronously to avoid blocking UI, but refresh when control becomes visible or focused.

        readonly object _styleLock = new object();
        bool _styleRefreshInProgress = false;

        void LoadStyles()
        {
            lock (_styleLock)
            {
                if (_styleRefreshInProgress) return;
                _styleRefreshInProgress = true;
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher
                          ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var names = _word.GetStyleNames().ToList();
                    dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        lock (_styleLock)
                        {
                            // Only rebuild the list if it actually changed — avoids
                            // clearing the ComboBox selection on every focus/visibility event.
                            bool changed = names.Count != StyleList.Count
                                        || !names.SequenceEqual(StyleList);

                            if (changed)
                            {
                                var findStyle    = FindFormatting.StyleName;
                                var replaceStyle = ReplaceFormatting.StyleName;

                                StyleList.Clear();
                                foreach (var name in names)
                                    StyleList.Add(name);

                                // Restore selections by value so the ComboBox doesn't go blank
                                if (!string.IsNullOrEmpty(findStyle))
                                    FindFormatting.StyleName = findStyle;
                                if (!string.IsNullOrEmpty(replaceStyle))
                                    ReplaceFormatting.StyleName = replaceStyle;
                            }

                            _styleRefreshInProgress = false;
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
                catch
                {
                    lock (_styleLock)
                    {
                        _styleRefreshInProgress = false;
                    }
                }
            });
        }

        public void EnsureStylesLoaded()
        {
            // Refresh styles asynchronously when control becomes visible or focused.
            // This ensures styles stay up-to-date as user applies/removes styles mid-session.
            LoadStylesCommand.Execute(null);
        }

        // ── History — shared static collections ───────────────────────────────

        public static void LoadRecentSearches()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher
                          ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var finds    = SearchHistory.Find.Load().ToList();
                    var replaces = SearchHistory.Replace.Load().ToList();
                    dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        RecentSearches.Clear();
                        foreach (var s in finds)    RecentSearches.Add(s);
                        RecentReplacements.Clear();
                        foreach (var s in replaces) RecentReplacements.Add(s);
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
                catch { }
            });
        }

        public void AddSearchToHistory()
        {
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                SearchHistory.Find.Add(SearchText);
                LoadRecentSearches();
            }
        }

        public void AddReplaceToHistory()
        {
            if (!string.IsNullOrWhiteSpace(ReplaceText))
            {
                SearchHistory.Replace.Add(ReplaceText);
                LoadRecentSearches();
            }
        }
    }
}
