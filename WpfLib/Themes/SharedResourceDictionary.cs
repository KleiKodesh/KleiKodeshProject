using System;
using System.Collections.Generic;
using System.Windows;

namespace WpfLib.Themes
{
    /// <summary>
    /// A ResourceDictionary that parses each file once per process instead of
    /// once per pane.
    ///
    /// WPF does not cache by URI. Every
    ///
    ///     &lt;ResourceDictionary Source="pack://.../officepalette.xaml"/&gt;
    ///
    /// builds and parses the whole thing again, and every pane in this suite
    /// has one. Measured with the gallery benchmark, five panes each loading
    /// their own copy cost 108.6ms; five panes sharing one instance cost 0.4ms.
    /// That is paid on the UI thread while a pane is opening, which is exactly
    /// where it is felt.
    ///
    /// Use it in place of ResourceDictionary wherever a Source is set:
    ///
    ///     &lt;ResourceDictionary&gt;
    ///         &lt;ResourceDictionary.MergedDictionaries&gt;
    ///             &lt;themes:SharedResourceDictionary Source="pack://.../officepalette.xaml"/&gt;
    ///         &lt;/ResourceDictionary.MergedDictionaries&gt;
    ///     &lt;/ResourceDictionary&gt;
    ///
    /// It is only worth it for dictionaries that are actually shared. A
    /// dictionary used by one view once gains nothing and reads as noise.
    ///
    /// The cache is per thread, deliberately. Most of what lives in a
    /// ResourceDictionary has thread affinity, and handing a Style built on
    /// Word's UI thread to a dictionary on some other thread is a crash that
    /// would arrive far from its cause. A second thread simply gets its own
    /// copy and pays the parse again, which is correct and rare.
    /// </summary>
    public class SharedResourceDictionary : ResourceDictionary
    {
        [ThreadStatic]
        private static Dictionary<Uri, ResourceDictionary> _cache;

        private Uri _source;

        /// <summary>
        /// Hides ResourceDictionary.Source rather than overriding it, because
        /// it is not virtual. That means it only takes effect when the static
        /// type is SharedResourceDictionary - which XAML guarantees, since the
        /// element name is what chooses the type.
        /// </summary>
        public new Uri Source
        {
            get { return _source; }
            set
            {
                _source = value;
                if (value == null)
                {
                    MergedDictionaries.Clear();
                    return;
                }

                var cache = _cache ?? (_cache = new Dictionary<Uri, ResourceDictionary>());

                ResourceDictionary shared;
                if (!cache.TryGetValue(value, out shared))
                {
                    shared = new ResourceDictionary { Source = value };
                    cache[value] = shared;
                }

                // Merge the shared instance rather than assigning Source, which
                // would parse it all over again and defeat the whole point.
                MergedDictionaries.Clear();
                MergedDictionaries.Add(shared);
            }
        }

        /// <summary>
        /// Drops the cache for the current thread. For tests that need a clean
        /// parse; nothing in the shipping path should want this.
        /// </summary>
        public static void ClearCache()
        {
            if (_cache != null) _cache.Clear();
        }

        /// <summary>How many dictionaries this thread has cached.</summary>
        public static int CachedCount
        {
            get { return _cache == null ? 0 : _cache.Count; }
        }
    }
}
