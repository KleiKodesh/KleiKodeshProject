namespace FtsLib.Seforim
{
    /// <summary>
    /// One row returned by <see cref="SeforimIndex.Search"/>.
    /// Immutable — all properties are set at construction time.
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
        /// token (e.g. all fuzzy neighbors of יצחק~ form one group).
        ///
        /// Used by <see cref="SeforimIndex.GenerateSnippet(SearchResult)"/> to:
        ///   - highlight the actual matched forms (not the raw pattern)
        ///   - find the tightest proximity window with correct OR-group semantics
        ///
        /// Never null; may be empty for results produced outside the normal pipeline.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<
            System.Collections.Generic.IReadOnlyCollection<string>> MatchedGroups { get; }

        public SearchResult(int lineId, string bookTitle, string content,
            System.Collections.Generic.IReadOnlyList<
                System.Collections.Generic.IReadOnlyCollection<string>> matchedGroups = null)
        {
            LineId        = lineId;
            BookTitle     = bookTitle     ?? string.Empty;
            Content       = content      ?? string.Empty;
            MatchedGroups = matchedGroups ??
                System.Array.Empty<System.Collections.Generic.IReadOnlyCollection<string>>();
        }

        public override string ToString() => $"[{LineId}] {BookTitle}";
    }
}
