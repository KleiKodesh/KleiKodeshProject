using System.Collections.Generic;
using System.Threading;

namespace FtsLib
{
    /// <summary>
    /// Optional companion to <see cref="IFtsCorpus"/>: a corpus whose documents have an order of
    /// their own that search results should come back in (for the seforim library, the catalog
    /// tree order the app shows). An index that supports it stores documents by POSITION in this
    /// order, so matches stream out already ordered. Without it, documents are in id order.
    ///
    /// The order must be stable for an unchanged corpus: an interrupted build resumes by
    /// position, so the same position has to name the same document every time it is read.
    /// </summary>
    public interface IFtsOrderedCorpus
    {
        /// <summary>
        /// Every document in result order, skipping the first <paramref name="skip"/>. Streamed;
        /// each document exactly once.
        /// </summary>
        IEnumerable<(int Id, string Text)> ReadDocumentsInResultOrder(long skip, CancellationToken ct = default);
    }
}
