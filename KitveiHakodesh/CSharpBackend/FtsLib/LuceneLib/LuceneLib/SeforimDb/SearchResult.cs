namespace LuceneLib.SeforimDb
{
    /// <summary>
    /// One row returned by <see cref="SeforimIndex.Search"/>.
    /// Mirrors FtsLib.SeforimDb.SearchResult exactly so callers can switch
    /// implementations without changing their code.
    /// </summary>
    public sealed class SearchResult
    {
        /// <summary>The line ID in the seforim database.</summary>
        public int LineId { get; }

        /// <summary>Title of the book this line belongs to.</summary>
        public string BookTitle { get; }

        /// <summary>Raw HTML content of the line as stored in the database.</summary>
        public string Content { get; }

        /// <summary>
        /// The query groups used to find this result — one group per query token,
        /// each containing the concrete index terms that were OR-expanded from that
        /// token. Skipped groups (e.g. wildcards with no expansions) are absent.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<
            System.Collections.Generic.IReadOnlyCollection<string>> MatchedGroups { get; }

        /// <summary>
        /// The number of query groups in the original parsed query, before any
        /// zero-expansion wildcards were skipped.
        /// </summary>
        public int OriginalGroupCount { get; }

        public SearchResult(int lineId, string bookTitle, string content,
            System.Collections.Generic.IReadOnlyList<
                System.Collections.Generic.IReadOnlyCollection<string>> matchedGroups = null,
            int originalGroupCount = 0)
        {
            LineId             = lineId;
            BookTitle          = bookTitle ?? string.Empty;
            Content            = content   ?? string.Empty;
            MatchedGroups      = matchedGroups ??
                System.Array.Empty<System.Collections.Generic.IReadOnlyCollection<string>>();
            OriginalGroupCount = originalGroupCount > 0
                ? originalGroupCount
                : MatchedGroups.Count;
        }

        public override string ToString() => $"[{LineId}] {BookTitle}";
    }
}
