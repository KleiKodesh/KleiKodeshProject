using System;
using System.Collections.Generic;

namespace KitveiHakodesh.Core.SeforimDbFullTextSearch
{
    /// <summary>
    /// The rows a SEARCH could not decode (issue #13): one bad row must not abort the search,
    /// and must not vanish silently either ("0 hits" that are really "0 readable hits"). While a
    /// search runs its reads inside <see cref="Enter"/>, <see cref="SeforimDbFtsCorpus"/> reads
    /// an undecodable line or note as empty and records it here with its id; the search result
    /// carries the count and ids. Outside a scope (the index build) every read stays strict and
    /// throws the error naming the line (issue #8).
    ///
    /// The scope is per THREAD: the engine's fetch runs synchronously on the thread that pulls
    /// the search's results, and <see cref="SeforimDbFtsSearcher"/> enters the scope around
    /// each pull only, so it never leaks into other work on that thread.
    /// </summary>
    public sealed class SeforimDbFtsUnreadableRows
    {
        [ThreadStatic] private static SeforimDbFtsUnreadableRows? t_current;

        private readonly object _lock = new object();
        private readonly List<int> _lineIds = new List<int>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private int _taken;

        /// <summary>The scope of the current thread; null = strict reads.</summary>
        internal static SeforimDbFtsUnreadableRows? Current => t_current;

        /// <summary>Distinct unreadable rows so far.</summary>
        public int Count { get { lock (_lock) return _lineIds.Count; } }

        /// <summary>The first decode error (it names the line), or null.</summary>
        public string? FirstError { get; private set; }

        /// <summary>Every unreadable line id so far (virtual ids for notes), in first-seen order.</summary>
        public List<int> LineIds() { lock (_lock) return new List<int>(_lineIds); }

        /// <summary>The ids recorded since the previous call.</summary>
        public List<int> TakeNew()
        {
            lock (_lock)
            {
                var fresh = _lineIds.GetRange(_taken, _lineIds.Count - _taken);
                _taken = _lineIds.Count;
                return fresh;
            }
        }

        internal void Add(int lineId, string message)
        {
            lock (_lock)
            {
                if (!_seen.Add(lineId)) return;
                _lineIds.Add(lineId);
                FirstError ??= message;
            }
        }

        /// <summary>Makes this the current thread's scope until the returned handle is disposed.</summary>
        public IDisposable Enter() => new Scope(this);

        private sealed class Scope : IDisposable
        {
            private readonly SeforimDbFtsUnreadableRows? _previous;
            private bool _disposed;

            public Scope(SeforimDbFtsUnreadableRows rows)
            {
                _previous = t_current;
                t_current = rows;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                t_current = _previous;
            }
        }
    }
}
