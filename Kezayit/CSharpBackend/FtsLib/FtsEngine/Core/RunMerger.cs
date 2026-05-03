using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FtsEngine.Core
{
    /// <summary>
    /// K-way heap merge of sorted run files produced by OccurenceBuffer.
    ///
    /// Run file format per entry:
    ///   [int16 termByteLen][UTF-8 term bytes][int32 docCount][int32 byteLen][uint32 lastEncoded][encoded bytes]
    ///
    /// Merge strategy for a term that appears in multiple runs:
    ///   - Single run  → bytes passed through raw, zero re-encoding cost.
    ///   - Multi run   → stitch segments by prepending one corrective delta varint between
    ///                   each pair of segments. No full re-decode needed.
    ///
    ///   Stitch logic: run[i] ends with lastEncoded[i]. run[i+1]'s first value is encoded
    ///   as an absolute value (from its own baseline). We need to emit the delta from
    ///   lastEncoded[i] to that first value. We read the first varint from run[i+1],
    ///   compute correctedDelta = firstEncoded[i+1] - lastEncoded[i], write that as a
    ///   varint, then copy the rest of run[i+1]'s bytes raw.
    /// </summary>
    internal sealed class RunMerger : IDisposable
    {
        internal sealed class RunCursor : IDisposable
        {
            private readonly BinaryReader _reader;
            public bool   IsExhausted  { get; private set; }
            public string CurrentTerm  { get; private set; }
            public byte[] CurrentBytes { get; private set; }
            public int    CurrentCount { get; private set; }
            public uint   LastEncoded  { get; private set; }

            public RunCursor(string path)
            {
                _reader = new BinaryReader(
                    new FileStream(path, FileMode.Open, FileAccess.Read,
                                   FileShare.Read, 4 * 1024 * 1024), // 4 MB read buffer
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
                short  termLen  = _reader.ReadInt16();
                byte[] termBuf  = _reader.ReadBytes(termLen);
                CurrentTerm     = Encoding.UTF8.GetString(termBuf);
                CurrentCount    = _reader.ReadInt32();
                int byteLen     = _reader.ReadInt32();
                LastEncoded     = _reader.ReadUInt32();
                CurrentBytes    = _reader.ReadBytes(byteLen);
            }

            public void Dispose() => _reader.Dispose();
        }

        private struct HeapEntry : IComparable<HeapEntry>
        {
            public string Term;
            public int    RunIndex;
            public int CompareTo(HeapEntry other) => string.CompareOrdinal(Term, other.Term);
        }

        private readonly RunCursor[]     _cursors;
        private readonly List<HeapEntry> _heap;

        public RunMerger(string[] runPaths)
        {
            _cursors = new RunCursor[runPaths.Length];
            _heap    = new List<HeapEntry>(runPaths.Length);

            for (int i = 0; i < runPaths.Length; i++)
            {
                _cursors[i] = new RunCursor(runPaths[i]);
                if (!_cursors[i].IsExhausted)
                    HeapPush(new HeapEntry { Term = _cursors[i].CurrentTerm, RunIndex = i });
            }
        }

        public bool HasMore => _heap.Count > 0;

        /// <summary>
        /// Returns the next term and its merged posting bytes + total count.
        /// Handles the same term appearing in multiple runs via delta stitching.
        /// </summary>
        public (string term, byte[] bytes, int count, uint lastEncoded) Next()
        {
            var    min  = HeapPop();
            string term = min.Term;

            var segments = new List<(byte[] bytes, int count, uint lastEncoded)>();
            CollectFromCursor(min.RunIndex, segments);

            // Drain any other runs that also have this term right now
            while (_heap.Count > 0 && string.CompareOrdinal(_heap[0].Term, term) == 0)
                CollectFromCursor(HeapPop().RunIndex, segments);

            // Single segment — pass bytes through raw, no work needed
            if (segments.Count == 1)
            {
                var s = segments[0];
                return (term, s.bytes, s.count, s.lastEncoded);
            }

            // Multiple segments — stitch with corrective delta varints between segments
            return (term, StitchSegments(segments), TotalCount(segments),
                    segments[segments.Count - 1].lastEncoded);
        }

        private static byte[] StitchSegments(List<(byte[] bytes, int count, uint lastEncoded)> segs)
        {
            // Estimate output size: sum of all byte arrays + 5 bytes per stitch point (max varint)
            int totalSize = (segs.Count - 1) * 5;
            foreach (var s in segs) totalSize += s.bytes.Length;

            var  out_  = new byte[totalSize];
            int  pos   = 0;
            uint prevLastEncoded = 0;

            for (int i = 0; i < segs.Count; i++)
            {
                byte[] src = segs[i].bytes;

                if (i == 0)
                {
                    // First segment: copy as-is
                    Buffer.BlockCopy(src, 0, out_, pos, src.Length);
                    pos += src.Length;
                }
                else
                {
                    // Read the first varint from this segment (its first encoded value)
                    int    srcPos       = 0;
                    uint   firstEncoded = ReadVarInt(src, ref srcPos);

                    // The delta we need to emit = firstEncoded - prevLastEncoded
                    // (prevLastEncoded is the last encoded value of the previous segment)
                    uint correctedDelta = firstEncoded - prevLastEncoded;
                    pos += WriteVarInt(out_, pos, correctedDelta);

                    // Copy the rest of this segment's bytes raw (deltas within segment are correct)
                    int remaining = src.Length - srcPos;
                    Buffer.BlockCopy(src, srcPos, out_, pos, remaining);
                    pos += remaining;
                }

                prevLastEncoded = segs[i].lastEncoded;
            }

            // Return exact-sized slice
            var result = new byte[pos];
            Buffer.BlockCopy(out_, 0, result, 0, pos);
            return result;
        }

        private static int TotalCount(List<(byte[] bytes, int count, uint lastEncoded)> segs)
        {
            int total = 0;
            foreach (var s in segs) total += s.count;
            return total;
        }

        private static uint ReadVarInt(byte[] buf, ref int pos)
        {
            int  shift  = 0;
            uint result = 0;
            while (pos < buf.Length)
            {
                byte b = buf[pos++];
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
            }
            return result;
        }

        private static int WriteVarInt(byte[] buf, int pos, uint v)
        {
            int written = 0;
            while (v >= 0x80)
            {
                buf[pos++] = (byte)(v | 0x80);
                v >>= 7;
                written++;
            }
            buf[pos] = (byte)v;
            return written + 1;
        }

        private void CollectFromCursor(int runIndex,
            List<(byte[], int, uint)> segments)
        {
            var cursor = _cursors[runIndex];
            segments.Add((cursor.CurrentBytes, cursor.CurrentCount, cursor.LastEncoded));
            cursor.Advance();
            if (!cursor.IsExhausted)
                HeapPush(new HeapEntry { Term = cursor.CurrentTerm, RunIndex = runIndex });
        }

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
            var min  = _heap[0];
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
