using System;
using FtsLib.Indexing;
using FtsLib.Search;
using FtsLib.Snippets;
using FtsLib.Tokenization;
using System.Collections.Generic;

namespace FtsLib.SeforimDb
{
    /// <summary>
    /// Locates the snippet window for a single search result line and returns it as a
    /// <see cref="SnippetDescriptor"/> — offsets only, never HTML.
    ///
    /// Pipeline (single pass over the token stream):
    ///   1. Use already-fetched content from <see cref="SearchResult.Content"/> —
    ///      no second DB round-trip.
    ///   2. Tokenize via <see cref="TokenStream"/> (preserves raw char positions).
    ///   3. Find the tightest proximity window covering all query groups
    ///      (OR within each group, AND across groups).
    ///   4. Expand the window by contextWords tokens on each side using the already-built
    ///      token list — no second pass over the source string.
    ///
    /// Descriptors with <see cref="SnippetDescriptor.IsMatch"/> = false mean the line did
    /// not actually contain all query terms (index false positive) and should be dropped.
    ///
    /// A descriptor's mark ranges VIEW the per-thread locator's scratch — copy them before the
    /// next locate on the same thread (see <see cref="SnippetDescriptor.MarkRanges"/>).
    /// </summary>
    internal static class SnippetDescriptorPipeline
    {
        // One locator per thread — it holds only per-call SCRATCH buffers (token stream,
        // group counters, mark buffer), so [ThreadStatic] gives each thread its own
        // instance with zero per-call allocation overhead. All QUERY state (the term→group
        // map) lives in PreparedQueryGroups — immutable, built once per query, shared
        // read-only by every thread.
        public const int DefaultContextWords = 8;

        [System.ThreadStatic]
        private static SnippetWindowLocator _locator;
        [System.ThreadStatic]
        private static int _locatorContextWords;

        private static SnippetWindowLocator GetLocator(int contextWords)
        {
            if (_locator == null || _locatorContextWords != contextWords)
            {
                _locator             = new SnippetWindowLocator(contextWords);
                _locatorContextWords = contextWords;
            }
            return _locator;
        }

        // A locator's scratch buffers grow to the longest line it has seen and never shrink;
        // as a [ThreadStatic] on pool threads it would hold them for the life of the process
        // (measured: 2 x 2 MB token arrays after one search hit a huge line). Past this size,
        // drop the locator after the call so only ordinary-sized buffers stay cached.
        private const int MaxRetainedContentLength = 8192;

        private static void ReleaseLocatorIfOversized(string content)
        {
            if (content.Length > MaxRetainedContentLength) _locator = null;
        }

        // ── Primary path: content already in hand ────────────────────

        /// <summary>
        /// Locates from already-fetched content using pre-computed query groups. Each group
        /// is a set of alternative terms (OR within group, AND across groups) — correctly
        /// handles fuzzy/wildcard expansions. No DB access.
        /// </summary>
        internal static SnippetDescriptor Locate(
            string                                         content,
            IReadOnlyList<IReadOnlyCollection<string>>     queryGroups,
            bool                                           requireOrdered     = false,
            int                                            originalGroupCount = 0,
            int                                            contextWords       = DefaultContextWords)
        {
            if (string.IsNullOrEmpty(content) || queryGroups == null || queryGroups.Count == 0)
                return SnippetDescriptor.NoMatch;

            var descriptor = GetLocator(contextWords).Locate(content, queryGroups, requireOrdered, originalGroupCount);
            ReleaseLocatorIfOversized(content);
            return descriptor;
        }

        /// <summary>
        /// The hot path: locates from a query-level <see cref="PreparedQueryGroups"/>
        /// prepared once per query — per-line cost is independent of how many terms the
        /// query expanded to. Used by
        /// <see cref="SeforimIndex.LocateSnippetWindow(SearchResult, bool, int)"/>.
        /// </summary>
        internal static SnippetDescriptor Locate(
            string              content,
            PreparedQueryGroups prepared,
            bool                requireOrdered     = false,
            int                 originalGroupCount = 0,
            int                 contextWords       = DefaultContextWords)
        {
            if (string.IsNullOrEmpty(content) || prepared == null || prepared.IsEmpty)
                return SnippetDescriptor.NoMatch;

            var descriptor = GetLocator(contextWords).Locate(content, prepared, requireOrdered, originalGroupCount);
            ReleaseLocatorIfOversized(content);
            return descriptor;
        }

        /// <summary>
        /// Locates over the matched line PLUS the given surrounding lines (already fetched,
        /// in document order): the text is joined by <see cref="JoinWithNeighbourLines"/>,
        /// then the normal single-pass locate runs — the match is found in the matched line
        /// and <c>contextWords</c> naturally expands into the neighbour text. The returned
        /// offsets are into that JOINED text.
        ///
        /// Used only for lines whose own window came out shorter than the requested context
        /// (see <see cref="SnippetDescriptor.WindowWordCount"/>); every other line keeps the
        /// plain single-line path with zero added cost.
        /// </summary>
        internal static SnippetDescriptor LocateOverNeighbourLines(
            string              previousContent,
            string              content,
            string              nextContent,
            PreparedQueryGroups prepared,
            bool                requireOrdered     = false,
            int                 originalGroupCount = 0,
            int                 contextWords       = DefaultContextWords)
        {
            if (string.IsNullOrEmpty(content) || prepared == null || prepared.IsEmpty)
                return SnippetDescriptor.NoMatch;

            string joined = JoinWithNeighbourLines(previousContent, content, nextContent);
            var descriptor = GetLocator(contextWords).Locate(joined, prepared, requireOrdered, originalGroupCount);
            ReleaseLocatorIfOversized(joined);
            return descriptor;
        }

        /// <summary>
        /// previous + ' ' + content + ' ' + next, an empty side dropped together with its
        /// space. The space keeps boundary words distinct (they must not fuse across a line
        /// break) and renders as a normal gap. The page re-joins line texts the same way, so
        /// a descriptor's offsets land on the same characters.
        /// </summary>
        internal static string JoinWithNeighbourLines(string previousContent, string content, string nextContent)
        {
            bool hasPrevious = !string.IsNullOrEmpty(previousContent);
            bool hasNext     = !string.IsNullOrEmpty(nextContent);
            if (!hasPrevious && !hasNext) return content;

            var joined = new System.Text.StringBuilder(
                (previousContent?.Length ?? 0) + content.Length + (nextContent?.Length ?? 0) + 2);
            if (hasPrevious) joined.Append(previousContent).Append(' ');
            joined.Append(content);
            if (hasNext) joined.Append(' ').Append(nextContent);
            return joined.ToString();
        }

        internal static SnippetDescriptor Locate(
            string                content,
            IReadOnlyList<string> queryTerms,
            int                   contextWords = DefaultContextWords)
        {
            if (string.IsNullOrEmpty(content) || queryTerms == null || queryTerms.Count == 0)
                return SnippetDescriptor.NoMatch;

            var descriptor = GetLocator(contextWords).Locate(content, queryTerms);
            ReleaseLocatorIfOversized(content);
            return descriptor;
        }

        // ── Fallback path: fetch content from DB ──────────────────────

        /// <summary>
        /// Fetches content from the DB then locates. Only used by
        /// <see cref="SeforimIndex.LocateSnippetWindow(int,string)"/> when the caller has a
        /// line ID but no <see cref="SearchResult"/> with content (e.g. a direct lookup by ID
        /// outside the normal search flow).
        /// </summary>
        internal static SnippetDescriptor LocateFromDb(
            int                   lineId,
            IReadOnlyList<string> queryTerms,
            Func<IFtsCorpus>      openCorpus,
            int                   contextWords = DefaultContextWords)
        {
            if (queryTerms == null || queryTerms.Count == 0)
                return SnippetDescriptor.NoMatch;

            using (var corpus = openCorpus())
            {
                string content = corpus.GetDocumentText(lineId);
                if (content == null) return SnippetDescriptor.NoMatch;
                return Locate(content, queryTerms, contextWords);
            }
        }
    }
}
