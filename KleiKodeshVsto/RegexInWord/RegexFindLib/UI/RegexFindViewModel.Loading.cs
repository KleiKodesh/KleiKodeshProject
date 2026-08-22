using System.Linq;
using System.Windows;
using WpfLib.Helpers;

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
                    // WpfLib.Helpers.FontsProvider is the solution's shared font source: one
                    // DirectWrite enumeration that already returns Hebrew families first,
                    // alphabetical within each group. It replaced InstalledFontCollection, which
                    // reports GDI families rather than the ones WPF resolves by name and is a
                    // process-lifetime snapshot that never sees fonts installed mid-session.
                    var items = FontsProvider.GetFontFamilies()
                        .Select(f => new FontItem(f.Name, f.HasHebrew))
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

        // ── Style loading — per-instance, refreshed on visibility/focus ──
        // Styles are document-specific and filtered by InUse — they can change mid-session.
        // COM (Word Styles) must be accessed on the STA/UI thread — no Task.Run.

        bool _styleRefreshInProgress = false;

        void LoadStyles()
        {
            // Guard against re-entrant calls (IsVisible + GotFocus firing together)
            if (_styleRefreshInProgress) return;
            _styleRefreshInProgress = true;

            try
            {
                var names = _word.GetStyleNames().ToList();

                // Only rebuild if actually changed — avoids clearing ComboBox selection
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
            }
            finally
            {
                _styleRefreshInProgress = false;
            }
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
