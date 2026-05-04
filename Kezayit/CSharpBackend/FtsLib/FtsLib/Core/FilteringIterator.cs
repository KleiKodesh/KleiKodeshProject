namespace FtsLib.Core
{
    /// <summary>
    /// Wraps a <see cref="PostingIterator"/> and skips any doc ID that appears in
    /// a <see cref="DeleteSet"/>.
    ///
    /// Used by IndexReader when the delete set is non-empty. When the delete set is
    /// empty, IndexReader uses the raw iterator directly — zero overhead on the
    /// common path.
    ///
    /// Allocation: one object per term per query. No per-doc allocation.
    /// </summary>
    internal sealed class FilteringIterator : PostingIterator
    {
        private readonly PostingIterator _inner;
        private readonly DeleteSet       _deletes;
        private bool                     _done;
        private int                      _current;

        public override int  Current => _current;
        public override bool IsDone  => _done;

        public FilteringIterator(PostingIterator inner, DeleteSet deletes)
        {
            _inner   = inner;
            _deletes = deletes;
        }

        public override bool MoveNext()
        {
            while (_inner.MoveNext())
            {
                if (!_deletes.Contains(_inner.Current))
                {
                    _current = _inner.Current;
                    return true;
                }
            }
            _done = true;
            return false;
        }

        public override bool SkipTo(int target)
        {
            if (_done) return false;

            // Delegate the skip to the inner iterator for skip-list acceleration,
            // then advance past any deleted IDs at or after the target.
            if (!_inner.SkipTo(target)) { _done = true; return false; }

            // Walk forward until we land on a non-deleted ID.
            while (_deletes.Contains(_inner.Current))
            {
                if (!_inner.MoveNext()) { _done = true; return false; }
            }

            _current = _inner.Current;
            return true;
        }
    }
}
