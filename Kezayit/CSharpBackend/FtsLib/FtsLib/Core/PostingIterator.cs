using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FtsLib.Core
{
    // ----------------------------------------------------------------
    // PostingIterator
    // ----------------------------------------------------------------
    public sealed class PostingIterator
    {
        public static readonly PostingIterator Empty = new PostingIterator();

        private readonly byte[] _buf;
        private readonly int _len;
        private readonly int[] _skip;
        private readonly int _skipLen;

        private int _pos;
        private uint _encoded;
        private bool _started;
        private bool _done;

        public int Current { get; private set; }
        public bool IsDone => _done;

        private PostingIterator() { _done = true; }

        public PostingIterator(byte[] buf, int len, int[] skip, int skipLen)
        {
            _buf = buf;
            _len = len;
            _skip = skip;
            _skipLen = skipLen;
        }

        public IEnumerable<int> AsEnumerable()
        {
            while (MoveNext()) yield return Current;
        }

        public bool MoveNext()
        {
            if (_done) return false;
            if (!_started)
            {
                _started = true;
                if (_pos >= _len) { _done = true; return false; }
                _encoded = ReadVarInt();
                Current = Decode(_encoded);
                return true;
            }
            if (_pos >= _len) { _done = true; return false; }
            _encoded += ReadVarInt();
            Current = Decode(_encoded);
            return true;
        }

        public bool SkipTo(int target)
        {
            if (_done) return false;
            if (!_started && !MoveNext()) return false;
            if (Current >= target) return true;

            // Use skip list to jump to the largest entry < target
            if (_skip != null)
            {
                int bestOffset = -1;
                uint bestPrevEncoded = 0;

                for (int i = 0; i < _skipLen; i += 3)
                {
                    if (_skip[i] >= target) break;       // skip docId >= target — stop
                    if (_skip[i + 1] > _pos)             // only jump forward
                    {
                        bestOffset = _skip[i + 1];
                        bestPrevEncoded = (uint)_skip[i + 2]; // encoded value of entry BEFORE this skip
                    }
                }

                if (bestOffset > _pos)
                {
                    // Jump to bestOffset. The varint there is the delta from bestPrevEncoded
                    // to the skip entry's docId. Set _encoded = bestPrevEncoded, then read
                    // the delta normally.
                    _pos = bestOffset;
                    _encoded = bestPrevEncoded;
                    _encoded += ReadVarInt();
                    Current = Decode(_encoded);
                    if (Current >= target) return true;
                }
            }

            // Linear scan
            while (Current < target)
            {
                if (_pos >= _len) { _done = true; return false; }
                _encoded += ReadVarInt();
                Current = Decode(_encoded);
            }
            return true;
        }

        private uint ReadVarInt()
        {
            int shift = 0;
            uint result = 0;
            while (_pos < _len)
            {
                byte b = _buf[_pos++];
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
            }
            return result;
        }

        private static uint Encode(int v) => (uint)((long)v - int.MinValue);
        private static int Decode(uint v) => (int)((long)v + int.MinValue);
    }
}
