using System.Collections.Generic;
using System.Linq;

namespace FtsLib.Index
{
    /// <summary>
    /// Public API for the full-text index.
    /// Finds which lines contain all search terms (AND semantics).
    /// </summary>
    public class IndexManager
    {
        private readonly RamIndex _index = new RamIndex();

        public int TermCount => _index.Count;

        public void Add(string term, int lineId)
        {
            _index.Add(term, lineId);
        }

        /// <summary>
        /// Returns line IDs that contain ALL of the supplied terms.
        /// Returns empty if any term is missing from the index.
        /// </summary>
        public IEnumerable<int> Search(IEnumerable<string> terms)
        {
            HashSet<int> result = null;

            foreach (var term in terms)
            {
                if (!_index.ContainsKey(term))
                    return Enumerable.Empty<int>();

                var docs = _index.GetDocs(term);

                if (result == null)
                    result = new HashSet<int>(docs);
                else
                    result.IntersectWith(docs);

                if (result.Count == 0)
                    return Enumerable.Empty<int>();
            }

            return result ?? Enumerable.Empty<int>();
        }
    }
}
