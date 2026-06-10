using System.Collections.Generic;

namespace FtsLib.Search
{
    /// <summary>
    /// Forward-only iterator over a delta+varint compressed posting list.
    /// Supports skip-list acceleration via SkipTo.
    /// </summary>
    internal class PostingIterator
    {
        public static readonly PostingIterator Empty = new PostingIterator();

        private readonly byte[] _buf;
        private readonly int    _len;
        private readonly int[]  _skip;
        private readonly int    _skipLen;

        private int  _pos;
        private uint _encoded;
        private bool _started;
        private bool _done;

        public virtual int  Current { get; private set; }
        public virtual bool IsDone  => _done;

        protected PostingIterator() { _done = true; }

        public PostingIterator(byte[] buf, int len, int[] skip, int skipLen)
        {
            _buf     = buf;
            _len     = len;
            _skip    = skip;
            _skipLen = skipLen;
        }

        public virtual IEnumerable<int> AsEnumerable()
        {
            while (MoveNext()) yield return Current;
        }

        public virtual bool MoveNext()
        {
            if (_done) return false;
            if (!_started)
            {
                _started = true;
                if (_pos >= _len) { _done = true; return false; }
                _encoded = ReadVarInt();
                Current  = Decode(_encoded);
                return true;
            }
            if (_pos >= _len) { _done = true; return false; }
            _encoded += ReadVarInt();
            Current   = Decode(_encoded);
            return true;
        }

        public virtual bool SkipTo(int target)
        {
            if (_done) return false;
            if (!_started && !MoveNext()) return false;
            if (Current >= target) return true;

            if (_skip != null)
            {
                int  bestOffset      = -1;
                uint bestPrevEncoded = 0;

                for (int i = 0; i < _skipLen; i += 3)
                {
                    if (_skip[i] >= target) break;
                    if (_skip[i + 1] > _pos)
                    {
                        bestOffset      = _skip[i + 1];
                        bestPrevEncoded = (uint)_skip[i + 2];
                    }
                }

                if (bestOffset > _pos)
                {
                    _pos     = bestOffset;
                    _encoded = bestPrevEncoded;
                    _encoded += ReadVarInt();
                    Current  = Decode(_encoded);
                    if (Current >= target) return true;
                }
            }

            while (Current < target)
            {
                if (_pos >= _len) { _done = true; return false; }
                _encoded += ReadVarInt();
                Current   = Decode(_encoded);
            }
            return true;
        }

        private uint ReadVarInt() => VarInt.Read(_buf, ref _pos, _len);

        private static uint Encode(int v) => (uint)((long)v - int.MinValue);
        private static int  Decode(uint v) => (int)((long)v + int.MinValue);
    }
}
