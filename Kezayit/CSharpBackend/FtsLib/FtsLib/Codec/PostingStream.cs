using System;
using System.Collections.Generic;
using System.IO;

namespace FtsLib.Codec
{
    /// <summary>
    /// Holds the compressed posting list for a single term.
    /// Entry IDs must be non-negative and added in ascending order.
    /// </summary>
    internal class PostingStream
    {
        public MemoryStream Stream { get; } = new MemoryStream();

        private int  _last;
        private bool _hasLast;

        public void Add(int entryId)
        {
            if (_hasLast && entryId <= _last)
                throw new ArgumentException(
                    $"Entry IDs must be added in strictly ascending order. Got {entryId} after {_last}.",
                    nameof(entryId));

            PostingCodec.Write(Stream, entryId, ref _last, ref _hasLast);
        }

        public IEnumerable<int> Read()
        {
            return PostingCodec.Read(Stream);
        }
    }
}
