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
        public void Add(string term, int entryId)
        {
            if (!TryGetValue(term, out var stream))
            {
                stream    = new PostingStream();
                this[term] = stream;
            }

            stream.Add(entryId);
        }

        public IEnumerable<int> GetDocs(string term)
        {
            return TryGetValue(term, out var stream)
                ? stream.Read()
                : Enumerable.Empty<int>();
        }
    }
}
