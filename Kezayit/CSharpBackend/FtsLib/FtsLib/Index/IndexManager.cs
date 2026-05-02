using System.Collections.Generic;
using System.Linq;

namespace FtsLib.Index
{
    /// <summary>
    /// Public API for the full-text index.
    /// Supports adding term→entryId pairs and AND-searching across multiple terms.
    /// </summary>
    public class IndexManager
    {
        private readonly RamIndex _index = new RamIndex();

        /// <summary>Number of unique terms currently in the index.</summary>
        public int TermCount => _index.Count;

        /// <summary>Adds a single term for the given entry.</summary>
        public void Add(string term, int entryId)
        {
            _index.Add(term, entryId);
        }

        /// <summary>
        /// Returns doc IDs that contain ALL of the supplied terms (AND semantics).
        /// Returns an empty sequence if any term is not in the index.
        /// </summary>
        public IEnumerable<int> Search(IEnumerable<string> terms)
        {
            var termList = terms.ToList();

            // Fast-exit: any missing term means no possible match
            foreach (var term in termList)
                if (!_index.ContainsKey(term))
                    return Enumerable.Empty<int>();

            HashSet<int> result = null;

            foreach (var term in termList)
            {
                var docs = _index.GetDocs(term);

                if (result == null)
                    result = new HashSet<int>(docs);
                else
                    result.IntersectWith(docs);

                if (result.Count == 0)
                    break;
            }

            return result ?? Enumerable.Empty<int>();
        }
    }
}
