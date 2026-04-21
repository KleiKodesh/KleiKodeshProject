using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace RegexFindLib.UI
{
    public partial class RegexFindViewModel
    {
        // ── Font loading — async, shared across instances ─────────────────────

        void LoadFonts()
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
                    }));
                }
                catch { }
            });
        }

        // ── Style loading — per-instance, async, reloads on every focus ─────────
        // Styles are document-specific and filtered by InUse — they change mid-session.

        void LoadStyles()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher
                          ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var names = _word.GetStyleNames().ToList();
                    dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        StyleList.Clear();
                        foreach (var name in names)
                            StyleList.Add(name);
                    }));
                }
                catch { }
            });
        }

        public void EnsureStylesLoaded()
        {
            // Always reload — styles are document-specific and filtered by InUse,
            // so they can change as the user applies/removes styles mid-session.
            LoadStylesCommand.Execute(null);
        }

        // ── History — shared static collections ───────────────────────────────

        public static void LoadRecentSearches()
        {
            RecentSearches.Clear();
            foreach (var s in SearchHistory.Find.Load())    RecentSearches.Add(s);
            RecentReplacements.Clear();
            foreach (var s in SearchHistory.Replace.Load()) RecentReplacements.Add(s);
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
