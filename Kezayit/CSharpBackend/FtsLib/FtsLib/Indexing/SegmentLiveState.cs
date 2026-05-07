using System;
using System.Collections.Generic;
using System.IO;

namespace FtsLib.Indexing
{
    /// <summary>
    /// Thread-safe registry of which segment files are live at each LSM level.
    ///
    /// Owns:
    ///   - the level → segId sets
    ///   - the segment ID counter
    ///   - path helpers (SegDatPath / SegDbPath)
    ///   - RebuildLiveState (called once during crash recovery, before any tasks start)
    ///   - all mutations: AddToLive, RemoveFromLive, PromoteSegment, EnsureLevel
    ///   - all queries: LiveSegCount, TotalLiveSegs, FindLevelWithMultiple, GetLiveSegIds,
    ///     GetLiveSegmentPaths
    ///
    /// All public and internal members are safe to call from any thread.
    /// </summary>
    internal sealed class SegmentLiveState
    {
        private readonly string _dir;

        // Guards all mutable fields below.
        // Never held during long-running file I/O — only during bookkeeping.
        private readonly object _lock = new object();

        private int[]  _levelCount = new int[4];
        private int    _nextSegId;

        // level → set of live segIds
        private readonly Dictionary<int, HashSet<int>> _liveSegs =
            new Dictionary<int, HashSet<int>>();

        internal SegmentLiveState(string dir)
        {
            _dir = dir;
        }

        // ── Path helpers ─────────────────────────────────────────────

        internal string SegDatPath(int level, int segId) =>
            Path.Combine(_dir, $"seg_{level}_{segId}.dat");

        internal string SegDbPath(int level, int segId) =>
            Path.Combine(_dir, $"seg_{level}_{segId}.db");

        // ── ID counter ───────────────────────────────────────────────

        internal int NextSegId()
        {
            lock (_lock) { return _nextSegId++; }
        }

        // ── Queries ──────────────────────────────────────────────────

        internal int LiveSegCount(int level)
        {
            lock (_lock)
                return _liveSegs.TryGetValue(level, out var s) ? s.Count : 0;
        }

        internal int TotalLiveSegs()
        {
            lock (_lock)
            {
                int n = 0;
                foreach (var kv in _liveSegs) n += kv.Value.Count;
                return n;
            }
        }

        internal int FindLevelWithMultiple()
        {
            lock (_lock)
            {
                foreach (var kv in _liveSegs)
                    if (kv.Value.Count >= 2) return kv.Key;
                return -1;
            }
        }

        internal List<int> GetLiveSegIds(int level)
        {
            lock (_lock)
            {
                if (!_liveSegs.TryGetValue(level, out var set)) return new List<int>();
                return new List<int>(set);
            }
        }

        /// <summary>
        /// Returns all live (datPath, dbPath) pairs across every level.
        /// Used by IndexReader to open all searchable segments.
        /// </summary>
        internal List<(string dat, string db)> GetLiveSegmentPaths()
        {
            lock (_lock)
            {
                var result = new List<(string, string)>();
                foreach (var kv in _liveSegs)
                    foreach (int sid in kv.Value)
                        result.Add((SegDatPath(kv.Key, sid), SegDbPath(kv.Key, sid)));
                return result;
            }
        }

        // ── Mutations ────────────────────────────────────────────────

        internal void AddToLive(int level, int segId)
        {
            lock (_lock) { AddToLiveUnlocked(level, segId); }
        }

        internal void RemoveFromLive(int level, int segId)
        {
            lock (_lock)
            {
                if (_liveSegs.TryGetValue(level, out var set))
                {
                    set.Remove(segId);
                    _levelCount[level] = set.Count;
                }
            }
        }

        internal void PromoteSegment(int srcLevel, List<int> removed, int dstLevel, int newSegId)
        {
            lock (_lock)
            {
                if (_liveSegs.TryGetValue(srcLevel, out var src))
                {
                    src.ExceptWith(removed);
                    _levelCount[srcLevel] = src.Count;
                }
                AddToLiveUnlocked(dstLevel, newSegId);
            }
        }

        internal void EnsureLevel(int level)
        {
            lock (_lock) { EnsureLevelUnlocked(level); }
        }

        // ── Recovery ─────────────────────────────────────────────────

        /// <summary>
        /// Scans the segment directory and rebuilds live state from the files on disk.
        /// Must be called before any background tasks start — not thread-safe.
        /// </summary>
        /// <param name="maxSegId">
        /// The highest segment ID found during the pre-scan (including .tmp files).
        /// If >= 0, _nextSegId is set to maxSegId + 1. If -1, _nextSegId is computed
        /// from the live segments found on disk.
        /// </param>
        internal void RebuildFromDisk(int maxSegId = -1)
        {
            _liveSegs.Clear();
            _nextSegId = 0;

            foreach (var file in Directory.GetFiles(_dir, "seg_*.dat"))
            {
                string name  = Path.GetFileNameWithoutExtension(file);
                var    parts = name.Split('_');
                if (parts.Length != 3) continue;
                if (!int.TryParse(parts[1], out int level)) continue;
                if (!int.TryParse(parts[2], out int segId)) continue;

                AddToLiveUnlocked(level, segId);
                if (segId >= _nextSegId) _nextSegId = segId + 1;
            }

            // If a maxSegId was provided from the pre-scan, use it to ensure
            // _nextSegId accounts for any .tmp files that were deleted.
            if (maxSegId >= _nextSegId)
                _nextSegId = maxSegId + 1;

            if (_liveSegs.Count > 0)
                Console.WriteLine($"[Recovery] Found {TotalLiveSegs()} segment(s), nextSegId={_nextSegId}");
        }

        // ── Private ──────────────────────────────────────────────────

        private void AddToLiveUnlocked(int level, int segId)
        {
            if (!_liveSegs.TryGetValue(level, out var set))
            {
                set = new HashSet<int>();
                _liveSegs[level] = set;
            }
            set.Add(segId);
            EnsureLevelUnlocked(level);
            _levelCount[level] = set.Count;
        }

        private void EnsureLevelUnlocked(int level)
        {
            if (level >= _levelCount.Length)
                Array.Resize(ref _levelCount, level + 2);
        }
    }
}
