using System.Collections.Generic;

namespace FtsLib.Search
{
    /// <summary>
    /// Wraps multiple PostingIterators into a single sorted, deduplicated iterator
    /// using a min-heap. Implements the PostingIterator interface so it can be fed
    /// directly into PostingMatcher.Intersect for mixed AND/OR queries.
    /// </summary>
    internal sealed class UnionIterator : PostingIterator
    {
        private readonly PostingIterator[] _iters;
        private readonly int[]             _heap;
        private int                        _heapSize;
        private bool                       _started;
        private int                        _current;
        private bool                       _isDone;

        public override int  Current => _current;
        public override bool IsDone  => _isDone;

        public UnionIterator(PostingIterator[] iters) : base()
        {
            _iters    = iters;
            _heap     = new int[iters.Length];
            _heapSize = 0;
        }

        public override bool MoveNext()
        {
            if (_isDone) return false;

            if (!_started)
            {
                _started = true;
                // Sub-iterators are already pre-advanced (MoveNext was called by
                // StartedIterators before they were handed to us). Build the heap
                // using Current directly — do NOT call MoveNext again.
                for (int i = 0; i < _iters.Length; i++)
                    if (!_iters[i].IsDone)
                        _heap[_heapSize++] = i;
                for (int i = _heapSize / 2 - 1; i >= 0; i--)
                    SiftDown(i);

                if (_heapSize == 0) { _isDone = true; return false; }
                _current = _iters[_heap[0]].Current;
                return true;
            }

            // Advance all iterators currently sitting on _current (dedup)
            while (_heapSize > 0 && _iters[_heap[0]].Current == _current)
            {
                int topIdx = _heap[0];
                if (_iters[topIdx].MoveNext())
                    SiftDown(0);
                else
                {
                    _heapSize--;
                    if (_heapSize > 0) { _heap[0] = _heap[_heapSize]; SiftDown(0); }
                }
            }

            if (_heapSize == 0) { _isDone = true; return false; }
            _current = _iters[_heap[0]].Current;
            return true;
        }

        public override bool SkipTo(int target)
        {
            if (_isDone) return false;
            if (!_started && !MoveNext()) return false;
            if (_current >= target) return true;

            // Rebuild heap: skip all underlying iterators to target
            _heapSize = 0;
            for (int i = 0; i < _iters.Length; i++)
            {
                if (_iters[i].IsDone) continue;
                if (_iters[i].SkipTo(target))
                    _heap[_heapSize++] = i;
            }
            for (int i = _heapSize / 2 - 1; i >= 0; i--)
                SiftDown(i);

            if (_heapSize == 0) { _isDone = true; return false; }
            _current = _iters[_heap[0]].Current;
            return true;
        }

        public override IEnumerable<int> AsEnumerable()
        {
            while (MoveNext()) yield return Current;
        }

        private void SiftDown(int i)
        {
            while (true)
            {
                int smallest = i;
                int left     = (i << 1) + 1;
                int right    = left + 1;

                if (left  < _heapSize && _iters[_heap[left ]].Current < _iters[_heap[smallest]].Current)
                    smallest = left;
                if (right < _heapSize && _iters[_heap[right]].Current < _iters[_heap[smallest]].Current)
                    smallest = right;

                if (smallest == i) break;
                int tmp = _heap[i]; _heap[i] = _heap[smallest]; _heap[smallest] = tmp;
                i = smallest;
            }
        }
    }
}
