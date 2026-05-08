using System.Collections.Generic;

namespace FtsLib.Search
{
    /// <summary>
    /// Wraps a <see cref="RoaringBitmap"/> as a <see cref="PostingIterator"/> so that
    /// a materialised OR-union result can be fed directly into
    /// <see cref="PostingMatcher.Intersect"/> without changing any of the AND
    /// intersection logic.
    ///
    /// The bitmap is iterated once via <see cref="RoaringBitmap.GetValues()"/>.
    /// <see cref="SkipTo"/> advances the enumerator until the current value meets
    /// the target — O(k) where k is the number of values skipped, but in practice
    /// the AND intersection calls SkipTo with monotonically increasing targets so
    /// the total work across all SkipTo calls is O(n) in the bitmap size.
    /// </summary>
    internal sealed class RoaringBitmapIterator : PostingIterator
    {
        private readonly IEnumerator<int> _enumerator;
        private bool _started;
        private bool _done;
        private int  _current;

        public override int  Current => _current;
        public override bool IsDone  => _done;

        public RoaringBitmapIterator(RoaringBitmap bitmap) : base()
        {
            _enumerator = bitmap.GetValues().GetEnumerator();
        }

        public override bool MoveNext()
        {
            if (_done) return false;
            _started = true;
            if (_enumerator.MoveNext())
            {
                _current = _enumerator.Current;
                return true;
            }
            _done = true;
            return false;
        }

        public override bool SkipTo(int target)
        {
            if (_done) return false;
            if (!_started && !MoveNext()) return false;
            while (_current < target)
            {
                if (!MoveNext()) return false;
            }
            return true;
        }

        public override IEnumerable<int> AsEnumerable()
        {
            while (MoveNext()) yield return Current;
        }
    }
}
