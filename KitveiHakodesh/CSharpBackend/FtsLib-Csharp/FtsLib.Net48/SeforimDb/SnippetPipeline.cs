using FtsLib.Indexing;
using FtsLib.Search;
using FtsLib.Snippets;
using FtsLib.Tokenization;
using System.Collections.Generic;

namespace FtsLib.SeforimDb
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
    ///   4. Expand the window by contextWords tokens on each side using the already-built
    ///      token list — no second pass over the source string.
    ///
    /// Results with <see cref="SnippetResult.IsMatch"/> = false indicate the line
    /// did not actually contain all query terms (index false positive) and should
    /// be filtered out by the caller.
    /// </summary>
    internal static class SnippetPipeline
    {
        // One SnippetBuilder per thread — the builder holds only per-call SCRATCH
        // buffers (token stream, group counters, render buffer), so [ThreadStatic]
        // gives each thread its own instance with zero per-call allocation overhead.
        // All QUERY state (the term→group map) lives in PreparedQueryGroups —
        // immutable, built once per query, shared read-only by every thread.
        public const int DefaultContextWords = 8;

        [System.ThreadStatic]
        private static SnippetBuilder _builder;
        [System.ThreadStatic]
        private static int _builderContextWords;

        private static SnippetBuilder GetBuilder(int contextWords)
        {
            if (_builder == null || _builderContextWords != contextWords)
            {
                _builder             = new SnippetBuilder(contextWords: contextWords);
                _builderContextWords = contextWords;
            }
            return _builder;
        }

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
            bool                                           requireOrdered     = false,
            int                                            originalGroupCount = 0,
            int                                            contextWords       = DefaultContextWords)
        {
            if (string.IsNullOrEmpty(content) || queryGroups == null || queryGroups.Count == 0)
                return SnippetResult.NoMatch;

            var inner = GetBuilder(contextWords).Build(content, queryGroups, requireOrdered, originalGroupCount);
            return new SnippetResult(inner.Html, inner.Score, inner.WordDistance, inner.IsMatch, inner.WindowWordCount);
        }

        /// <summary>
        /// The hot path: builds from a query-level <see cref="PreparedQueryGroups"/>
        /// prepared once per query — per-line cost is independent of how many terms
        /// the query expanded to. Used by
        /// <see cref="SeforimIndex.GenerateSnippet(SearchResult, bool, int)"/>.
        /// </summary>
        internal static SnippetResult Generate(
            string              content,
            PreparedQueryGroups prepared,
            bool                requireOrdered     = false,
            int                 originalGroupCount = 0,
            int                 contextWords       = DefaultContextWords)
        {
            if (string.IsNullOrEmpty(content) || prepared == null || prepared.IsEmpty)
                return SnippetResult.NoMatch;

            var inner = GetBuilder(contextWords).Build(content, prepared, requireOrdered, originalGroupCount);
            return new SnippetResult(inner.Html, inner.Score, inner.WordDistance, inner.IsMatch, inner.WindowWordCount);
        }

        /// <summary>
        /// Embellished build: renders the snippet over the matched line PLUS the given
        /// surrounding lines (already fetched, in document order). The neighbor text is
        /// concatenated around the matched line with a single space separator, then the
        /// normal single-pass builder runs — so the match is found in the matched line
        /// and <c>contextWords</c> naturally expands into the neighbor text.
        ///
        /// Used only for lines whose own snippet came out shorter than the requested
        /// context (see <see cref="SnippetResult.WindowWordCount"/>); every other line
        /// keeps the plain single-line path with zero added cost.
        /// </summary>
        internal static SnippetResult GenerateWithNeighbors(
            string              prevContent,
            string              content,
            string              nextContent,
            PreparedQueryGroups prepared,
            bool                requireOrdered     = false,
            int                 originalGroupCount = 0,
            int                 contextWords       = DefaultContextWords)
        {
            if (string.IsNullOrEmpty(content) || prepared == null || prepared.IsEmpty)
                return SnippetResult.NoMatch;

            // Concatenate around the matched line. A space keeps boundary words distinct
            // (they must not fuse across a line break) and renders as a normal gap.
            var sb = new System.Text.StringBuilder(
                (prevContent?.Length ?? 0) + content.Length + (nextContent?.Length ?? 0) + 2);
            if (!string.IsNullOrEmpty(prevContent)) sb.Append(prevContent).Append(' ');
            sb.Append(content);
            if (!string.IsNullOrEmpty(nextContent)) sb.Append(' ').Append(nextContent);

            var inner = GetBuilder(contextWords).Build(sb.ToString(), prepared, requireOrdered, originalGroupCount);
            return new SnippetResult(inner.Html, inner.Score, inner.WordDistance, inner.IsMatch, inner.WindowWordCount);
        }

        internal static SnippetResult Generate(
            string                content,
            IReadOnlyList<string> queryTerms,
            int                   contextWords = DefaultContextWords)
        {
            if (string.IsNullOrEmpty(content) || queryTerms == null || queryTerms.Count == 0)
                return SnippetResult.NoMatch;

            var inner = GetBuilder(contextWords).Build(content, queryTerms);
            return new SnippetResult(inner.Html, inner.Score, inner.WordDistance, inner.IsMatch, inner.WindowWordCount);
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
            string                dbPath,
            int                   contextWords = DefaultContextWords)
        {
            if (queryTerms == null || queryTerms.Count == 0)
                return SnippetResult.NoMatch;

            using (var db = new ZayitDb(dbPath))
            {
                string content = db.GetLineContent(lineId);
                if (content == null) return SnippetResult.NoMatch;
                return Generate(content, queryTerms, contextWords);
            }
        }
    }
}
