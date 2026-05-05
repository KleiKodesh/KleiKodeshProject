using System.Collections.Generic;

namespace FtsLib.Core
{
    /// <summary>
    /// Sequences multiple PostingIterators end-to-end.
    /// Doc IDs are globally ascending across segments (flush order = index order),
    /// so simple sequencing produces a valid sorted posting list.
    /// </summary>
    internal sealed class ConcatIterator : PostingIterator
    {
        private readonly PostingIterator[] _iters;
        private int  _idx;
        private bool _exhausted;

        public override int  Current => _exhausted ? 0 : _iters[_idx].Current;
        public override bool IsDone  => _exhausted;

        public ConcatIterator(PostingIterator[] iters) : base() { _iters = iters; }

        public override bool MoveNext()
        {
            if (_exhausted) return false;

            while (_idx < _iters.Length)
            {
                if (_iters[_idx].MoveNext()) return true;
                _idx++;
            }

            _exhausted = true;
            return false;
        }

        public override bool SkipTo(int target)
        {
            if (_exhausted) return false;

            while (_idx < _iters.Length)
            {
                if (_iters[_idx].IsDone) { _idx++; continue; }
                if (_iters[_idx].SkipTo(target)) return true;
                _idx++;
            }

            _exhausted = true;
            return false;
        }

        public override IEnumerable<int> AsEnumerable()
        {
            while (MoveNext()) yield return Current;
        }
    }
}
