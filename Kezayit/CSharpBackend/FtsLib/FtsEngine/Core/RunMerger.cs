using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FtsEngine.Core
{
    /// <summary>
    /// K-way heap merge of sorted run files.
    ///
    /// Each run file contains records sorted by (term, docId).
    /// The merger maintains a min-heap of size k (one cursor per run),
    /// always yielding the globally smallest (term, docId) next.
    ///
    /// Complexity: O(N log k) where N = total pairs, k = number of runs.
    /// For k=8 runs, log k ≈ 3 — essentially free overhead.
    /// </summary>
    internal sealed class RunMerger : IDisposable
    {
        // ---- one cursor per run file ----
        private sealed class RunCursor : IDisposable
        {
            private readonly BinaryReader _reader;
            public bool IsExhausted { get; private set; }
            public string CurrentTerm  { get; private set; }
            public int    CurrentDocId { get; private set; }

            public RunCursor(string path)
            {
                _reader = new BinaryReader(
                    new FileStream(path, FileMode.Open, FileAccess.Read,
                                   FileShare.Read, 1 << 16),
                    Encoding.UTF8, leaveOpen: false);
                Advance();
            }

            public void Advance()
            {
                if (_reader.BaseStream.Position >= _reader.BaseStream.Length)
                {
                    IsExhausted = true;
                    return;
                }
                short termLen  = _reader.ReadInt16();
                byte[] termBuf = _reader.ReadBytes(termLen);
                CurrentTerm    = Encoding.UTF8.GetString(termBuf);
                CurrentDocId   = _reader.ReadInt32();
            }

            public void Dispose() => _reader.Dispose();
        }

        // ---- min-heap entry ----
        private struct HeapEntry : IComparable<HeapEntry>
        {
            public string Term;
            public int    DocId;
            public int    RunIndex;

            public int CompareTo(HeapEntry other)
            {
                int c = string.CompareOrdinal(Term, other.Term);
                if (c != 0) return c;
                return DocId.CompareTo(other.DocId);
            }
        }

        private readonly RunCursor[]    _cursors;
        private readonly List<HeapEntry> _heap;

        public RunMerger(string[] runPaths)
        {
            _cursors = new RunCursor[runPaths.Length];
            _heap    = new List<HeapEntry>(runPaths.Length);

            for (int i = 0; i < runPaths.Length; i++)
            {
                _cursors[i] = new RunCursor(runPaths[i]);
                if (!_cursors[i].IsExhausted)
                    HeapPush(new HeapEntry
                    {
                        Term     = _cursors[i].CurrentTerm,
                        DocId    = _cursors[i].CurrentDocId,
                        RunIndex = i
                    });
            }
        }

        public bool HasMore => _heap.Count > 0;

        /// <summary>Returns the next (term, docId) in globally sorted order.</summary>
        public (string term, int docId) Next()
        {
            var min = HeapPop();
            var cursor = _cursors[min.RunIndex];
            cursor.Advance();
            if (!cursor.IsExhausted)
                HeapPush(new HeapEntry
                {
                    Term     = cursor.CurrentTerm,
                    DocId    = cursor.CurrentDocId,
                    RunIndex = min.RunIndex
                });
            return (min.Term, min.DocId);
        }

        // ---- simple binary heap (min-heap) ----
        private void HeapPush(HeapEntry e)
        {
            _heap.Add(e);
            int i = _heap.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_heap[parent].CompareTo(_heap[i]) <= 0) break;
                var tmp = _heap[parent]; _heap[parent] = _heap[i]; _heap[i] = tmp;
                i = parent;
            }
        }

        private HeapEntry HeapPop()
        {
            var min = _heap[0];
            int last = _heap.Count - 1;
            _heap[0] = _heap[last];
            _heap.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1, r = 2 * i + 2, smallest = i;
                if (l < _heap.Count && _heap[l].CompareTo(_heap[smallest]) < 0) smallest = l;
                if (r < _heap.Count && _heap[r].CompareTo(_heap[smallest]) < 0) smallest = r;
                if (smallest == i) break;
                var tmp = _heap[i]; _heap[i] = _heap[smallest]; _heap[smallest] = tmp;
                i = smallest;
            }
            return min;
        }

        public void Dispose()
        {
            foreach (var c in _cursors) c?.Dispose();
        }
    }
}
