using System;
using System.Collections.Generic;
using System.Linq;
using FtsLib.Codec;

namespace FtsLib.Index
{
    /// <summary>
    /// In-memory inverted index: maps each term to its compressed posting list.
    /// </summary>
    internal class RamIndex : Dictionary<string, PostingStream>
    {
        public RamIndex() : base(1_500_000, StringComparer.Ordinal) { }

        public void Add(string term, int lineId)
        {
            if (!TryGetValue(term, out var stream))
            {
                stream     = new PostingStream();
                this[term] = stream;
            }
            stream.Add(lineId);
        }

        public IEnumerable<int> GetDocs(string term)
        {
            return TryGetValue(term, out var stream)
                ? stream.Read()
                : Enumerable.Empty<int>();
        }
    }
}
