using System.Collections.Generic;

namespace KitveiHakodesh.Core.SeforimDbFullTextSearch
{
    /// <summary>
    /// One finished batch out of <see cref="SeforimDbFtsSearcher.SearchInBatches"/>: the hits,
    /// plus the snippet texts of the search's first
    /// <see cref="SeforimDbFtsSearcher.SeedSnippetTextHitCount"/> hits — the matched lines'
    /// text from the content the batch already fetched, and only a short line's neighbours
    /// read. Empty once the seed count is spent, which is every batch after the first one or two.
    /// </summary>
    public sealed class SeforimDbFtsHitBatch
    {
        public SeforimDbFtsHitBatch(List<FtsHit> hits, List<FtsSnippetText> snippetTexts)
        {
            Hits = hits;
            SnippetTexts = snippetTexts;
        }

        public List<FtsHit> Hits { get; }
        public List<FtsSnippetText> SnippetTexts { get; }
    }
}
