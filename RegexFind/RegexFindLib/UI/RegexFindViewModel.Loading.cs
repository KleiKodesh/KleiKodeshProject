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
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var names = _word.GetFontNames().ToList();
                    Application.Current?.Dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        lock (_fontLock)
                        {
                            if (_fontsLoaded) return;
                            foreach (var name in names)
                                FontList.Add(name);
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
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var names = _word.GetStyleNames().ToList();
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                        new System.Action(() =>
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
