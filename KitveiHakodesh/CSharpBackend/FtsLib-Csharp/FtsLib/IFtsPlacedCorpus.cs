using System.Collections.Generic;

namespace FtsLib
{
    /// <summary>
    /// Where a document sits in its library: the book it belongs to and its 0-based position
    /// in that book. All zero for a corpus that has no placement to give.
    /// </summary>
    public readonly struct FtsDocumentPlacement
    {
        public FtsDocumentPlacement(int bookId, int lineIndex)
        {
            BookId = bookId;
            LineIndex = lineIndex;
        }

        public int BookId { get; }
        public int LineIndex { get; }
    }

    /// <summary>
    /// A corpus that can hand back each fetched document's <see cref="FtsDocumentPlacement"/>
    /// in the same read that fetches its text. The search engines use it when the corpus
    /// offers it, so a caller that needs the placement of every hit (book id, position
    /// for neighbour lookups) gets it from the one row the content fetch already
    /// reads, instead of a second query per batch.
    /// </summary>
    public interface IFtsPlacedCorpus : IFtsCorpus
    {
        /// <summary>
        /// <see cref="IFtsCorpus.FetchDocuments"/> with each document's placement: same
        /// contract (ascending id order, ids with no document skipped, streamed).
        /// </summary>
        IEnumerable<(int Id, string Text, FtsDocumentPlacement Placement)> FetchPlacedDocuments(IEnumerable<int> ids);
    }
}
