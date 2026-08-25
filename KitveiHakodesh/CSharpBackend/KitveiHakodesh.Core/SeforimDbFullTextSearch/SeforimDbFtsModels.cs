using System.Collections.Generic;
using MessagePack;

namespace KitveiHakodesh.Core.SeforimDbFullTextSearch
{
    /// <summary>
    /// One full-text hit, ready for the frontend: the engine's match plus the corpus-side
    /// enrichment (book id, TOC path) the searcher adds, so no consumer ever needs a second
    /// round-trip to display a result.
    ///
    /// A wire type — the property names are the payload keys and must not change casually.
    /// The `Ready`/`Done`/`Error` envelopes these ride in stay with each transport.
    /// </summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class FtsHit
    {
        public int LineId { get; set; }
        public int BookId { get; set; }
        public string BookTitle { get; set; } = "";
        public string TocText { get; set; } = "";
        public int Score { get; set; }

        /// <summary>Word distance of the tightest window (0 = query words adjacent). The
        /// frontend's relevancy sort key.</summary>
        public int WordDistance { get; set; }

        public string Snippet { get; set; } = "";
        public List<string> MatchedTerms { get; set; } = new List<string>();
    }

    /// <summary>
    /// Where background indexing stands — what the frontend's indexing overlay renders.
    /// "Chunks" are lines; the names are historical and on the wire, so they stay.
    /// </summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class FtsIndexStatus
    {
        public bool IsReady { get; set; }
        public bool IsIndexing { get; set; }
        public double Percentage { get; set; }
        public int ProcessedChunks { get; set; }
        public int TotalChunks { get; set; }

        /// <summary>True when no seforim database exists at the resolved path — there is
        /// nothing to index, which is a different message than "still building".</summary>
        public bool DbMissing { get; set; }
    }
}
