using System.Collections.Generic;

namespace KitveiHakodesh.Core.SeforimDbFullTextSearch
{
    /// <summary>
    /// One finished batch out of <see cref="SeforimDbFtsSearcher.SearchInBatches"/>: the hits,
    /// plus the snippet line texts of the search's first
    /// <see cref="SeforimDbFtsSearcher.SeedLineTextHitCount"/> hits (line id -> text) — taken
    /// from the content the batch already fetched, never re-read. Empty once the seed count
    /// is spent, which is every batch after the first one or two.
    /// </summary>
    public sealed class SeforimDbFtsHitBatch
    {
        public SeforimDbFtsHitBatch(List<FtsHit> hits, Dictionary<int, string> lineTexts)
        {
            Hits = hits;
            LineTexts = lineTexts;
        }

        public List<FtsHit> Hits { get; }
        public Dictionary<int, string> LineTexts { get; }
    }
}
