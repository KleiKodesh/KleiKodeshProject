using System.Collections.Generic;

namespace FtsLib.Search
{
    /// <summary>
    /// Wraps a <see cref="RoaringBitmap"/> as a <see cref="PostingIterator"/> so that
    /// a materialised OR-union result can be fed directly into
    /// <see cref="PostingMatcher.Intersect"/> without changing any of the AND
    /// intersection logic.
    ///
    /// ## Hybrid MoveNext / SkipTo design
    ///
    /// The iterator separates two access patterns that have different cost profiles:
    ///
    /// <b>MoveNext()</b> — streams sequentially through <c>_tail</c>, an
    /// <c>IEnumerator&lt;int&gt;</c> from <see cref="RoaringBitmap.GetValues"/> (or from
    /// <see cref="RoaringBitmap.GetValuesFrom"/> after a jump).  Cost is O(1) amortised
    /// per value — identical to the original behaviour.
    ///
    /// <b>SkipTo(target)</b> — hybrid based on whether the target is in the same
    /// 64K block as the current position:
    ///
    ///   • Same block  (target &gt;&gt; 16 == _currentBlock): keeps draining <c>_tail</c>
    ///     until <c>Current &gt;= target</c>.  Cheap — the target is nearby so the
    ///     linear scan is short.
    ///
    ///   • Different block (target &gt;&gt; 16 &gt; _currentBlock): abandons the current
    ///     cursor and calls <see cref="RoaringBitmap.GetValuesFrom(int)"/>, which
    ///     binary-searches <c>_keys</c> for the target block (O(log blocks)) and
    ///     binary-searches / masks within the container to find the floor value,
    ///     then yields sequentially from there.  Cost: O(log blocks + per-container
    ///     floor) for the jump, then O(1) per step afterwards.
    ///
    /// This means far-apart skips (the expensive case) are O(log) instead of
    /// O(distance), while nearby skips and plain draining are completely unaffected.
    /// Nothing is materialised into memory — results are still produced lazily via
    /// the <c>IEnumerator</c> from <c>GetValues()</c> / <c>GetValuesFrom()</c>.
    /// </summary>
    internal sealed class RoaringBitmapIterator : PostingIterator
    {
        private IEnumerator<int> _tail;
        private bool             _started;
        private bool             _done;
        private int              _current;
        private int              _currentBlock; // high 16 bits of _current

        public override int  Current => _current;
        public override bool IsDone  => _done;

        /// <summary>
        /// The underlying bitmap. Exposed so <see cref="PostingIntersector"/> can
        /// merge it via <see cref="RoaringBitmap.Or"/> instead of iterating doc-by-doc.
        /// </summary>
        internal RoaringBitmap Bitmap { get; }

        public RoaringBitmapIterator(RoaringBitmap bitmap) : base()
        {
            Bitmap = bitmap;
            _tail  = bitmap.GetValues().GetEnumerator();
        }

        public override bool MoveNext()
        {
            if (_done) return false;
            _started = true;
            if (_tail.MoveNext())
            {
                _current      = _tail.Current;
                _currentBlock = (int)((uint)_current >> 16);
                return true;
            }
            _done = true;
            return false;
        }

        public override bool SkipTo(int target)
        {
            if (_done) return false;
            if (!_started && !MoveNext()) return false;
            if (_current >= target) return true;

            int targetBlock = (int)((uint)target >> 16);

            if (targetBlock == _currentBlock)
            {
                // Target is in the same 64K block — keep draining the existing cursor.
                // The skip distance is at most 65 535 values, so the linear scan is short.
                while (_current < target)
                {
                    if (!MoveNext()) return false;
                }
                return true;
            }

            // Target is in a different (later) block — abandon the current cursor and
            // jump directly to the target position via GetValuesFrom.
            // This replaces an O(distance) linear walk with an O(log blocks) block-jump
            // plus an O(log n) or O(64) within-container floor search.
            _tail.Dispose();
            _tail = Bitmap.GetValuesFrom(target).GetEnumerator();

            if (_tail.MoveNext())
            {
                _current      = _tail.Current;
                _currentBlock = (int)((uint)_current >> 16);
                return true;
            }

            _done = true;
            return false;
        }

        public override IEnumerable<int> AsEnumerable()
        {
            while (MoveNext()) yield return Current;
        }

        public override void DrainInto(RoaringBitmap bitmap)
        {
            // Must be overridden: the base implementation reads the base class's
            // private decode state, which a wrapper constructed via the protected
            // ctor leaves permanently "done" — it would silently drain NOTHING.
            // (PostingIntersector prefers the SIMD RoaringBitmap.Or fast path over
            // this; the override is the correctness backstop for any other caller.)
            while (MoveNext())
                bitmap.Add(Current);
        }
    }
}
