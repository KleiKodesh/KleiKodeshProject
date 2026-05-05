using FtsLib.Core;
using FtsLib.Misc;
using System.Collections.Generic;

namespace FtsLib.Seforim
{
    /// <summary>
    /// Generates a highlighted HTML snippet for a single search result line.
    ///
    /// Pipeline (single pass over the token stream):
    ///   1. Use already-fetched content from <see cref="SearchResult.Content"/> —
    ///      no second DB round-trip.
    ///   2. Tokenize via <see cref="TokenStream"/> (preserves raw char positions).
    ///   3. Find the tightest proximity window covering all query groups
    ///      via <see cref="ProximityWindow"/> (OR within each group, AND across groups).
    ///   4. Expand the window to a readable length and render highlighted HTML.
    ///
    /// Results with <see cref="SnippetResult.IsMatch"/> = false indicate the line
    /// did not actually contain all query terms (index false positive) and should
    /// be filtered out by the caller.
    /// </summary>
    internal static class SnippetPipeline
    {
        // One SnippetBuilder per thread — not safe to share across threads because
        // SnippetBuilder reuses internal data structures across calls. [ThreadStatic]
        // gives each thread its own instance with zero per-call allocation overhead.
        [System.ThreadStatic]
        private static SnippetBuilder _builder;

        // ── Primary path: content already in hand ────────────────────

        /// <summary>
        /// Builds a snippet from already-fetched content using pre-computed query
        /// groups. Each group is a set of alternative terms (OR within group, AND
        /// across groups) — correctly handles fuzzy/wildcard expansions.
        ///
        /// No DB access. This is the fast path used by
        /// <see cref="SeforimIndex.GenerateSnippet(SearchResult)"/>.
        /// </summary>
        internal static SnippetResult Generate(
            string                                         content,
            IReadOnlyList<IReadOnlyCollection<string>>     queryGroups,
            bool                                           requireOrdered = false,
            int                                            originalGroupCount = 0)
        {
            if (string.IsNullOrEmpty(content) || queryGroups == null || queryGroups.Count == 0)
                return SnippetResult.NoMatch;

            if (_builder == null) _builder = new SnippetBuilder();
            var inner = _builder.Build(content, queryGroups, requireOrdered, originalGroupCount);
            return new SnippetResult(inner.Html, inner.Score, inner.WordDistance, inner.IsMatch);
        }

        internal static SnippetResult Generate(
            string                content,
            IReadOnlyList<string> queryTerms)
        {
            if (string.IsNullOrEmpty(content) || queryTerms == null || queryTerms.Count == 0)
                return SnippetResult.NoMatch;

            if (_builder == null) _builder = new SnippetBuilder();
            var inner = _builder.Build(content, queryTerms);
            return new SnippetResult(inner.Html, inner.Score, inner.WordDistance, inner.IsMatch);
        }

        // ── Fallback path: fetch content from DB ──────────────────────

        /// <summary>
        /// Fetches content from the DB then builds the snippet.
        /// Only used by <see cref="SeforimIndex.GenerateSnippet(int,string)"/> when
        /// the caller has a line ID but no <see cref="SearchResult"/> with content
        /// (e.g. a direct lookup by ID outside the normal search flow).
        /// </summary>
        internal static SnippetResult GenerateFromDb(
            int                   lineId,
            IReadOnlyList<string> queryTerms,
            string                dbPath)
        {
            if (queryTerms == null || queryTerms.Count == 0)
                return SnippetResult.NoMatch;

            using (var db = new ZayitDb(dbPath))
            {
                string content = db.GetLineContent(lineId);
                if (content == null) return SnippetResult.NoMatch;
                return Generate(content, queryTerms);
            }
        }
    }
}
