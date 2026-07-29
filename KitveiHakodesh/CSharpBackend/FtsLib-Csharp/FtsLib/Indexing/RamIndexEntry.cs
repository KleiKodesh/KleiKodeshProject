using FtsLib.Search;
using System;

namespace FtsLib.Indexing
{
    /// <summary>Per-term in-memory entry: compressed posting stream + skip list.</summary>
    internal sealed class RamIndexEntry
    {
        private const int SkipInterval = 128;

        public readonly PostingStream Stream = new PostingStream();
        public int[] Skip;
        public int   SkipLen;

        private readonly bool _useSkipList;

        public RamIndexEntry(bool useSkipList = true) { _useSkipList = useSkipList; }

        public void Add(int lineId)
        {
            int newCount = Stream.Count + 1;

            if (_useSkipList && newCount > 1 && (newCount - 1) % SkipInterval == 0)
            {
                if (Skip == null) Skip = new int[12];
                else if (SkipLen + 3 > Skip.Length)
                    Array.Resize(ref Skip, Skip.Length * 2);

                Skip[SkipLen]     = lineId;
                Skip[SkipLen + 1] = Stream.NextByteOffset;   // byte offset BEFORE writing
                Skip[SkipLen + 2] = (int)Stream.LastEncoded; // encoded value of PREVIOUS entry
                SkipLen += 3;
            }

            Stream.Add(lineId);
        }
    }
}
