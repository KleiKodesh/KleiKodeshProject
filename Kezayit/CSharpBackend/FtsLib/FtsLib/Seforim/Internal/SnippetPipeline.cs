using FtsLib.Core;
using FtsLib.Misc;
using System.Collections.Generic;

namespace FtsLib.Seforim
{
    /// <summary>
    /// Generates a highlighted HTML snippet for a single search result line.
    ///
    /// Pipeline (single pass over the token stream):
    ///   1. Fetch raw HTML content from the seforim database.
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
        // One SnippetBuilder per call — not thread-safe, but SeforimIndex is
        // documented as single-threaded. Reuse avoids repeated allocation.
        private static readonly SnippetBuilder _builder = new SnippetBuilder();

        /// <summary>
        /// Fetches the content for <paramref name="lineId"/> from the database,
        /// runs the snippet pipeline using pre-computed query groups, and returns
        /// the result. Each group is a set of alternative terms (OR within group,
        /// AND across groups) — this correctly handles fuzzy/wildcard expansions.
        /// Returns <see cref="SnippetResult.NoMatch"/> when the line is not found
        /// or no query groups are present.
        /// </summary>
        internal static SnippetResult Generate(
            int                                                    lineId,
            IReadOnlyList<IReadOnlyCollection<string>>             queryGroups,
            string                                                 dbPath)
        {
            if (queryGroups == null || queryGroups.Count == 0)
                return SnippetResult.NoMatch;

            using (var db = new ZayitDb(dbPath))
            {
                string content = db.GetLineContent(lineId);
                if (content == null) return SnippetResult.NoMatch;

                var inner = _builder.Build(content, queryGroups);
                return new SnippetResult(inner.Html, inner.Score, inner.IsMatch);
            }
        }

        /// <summary>
        /// Fallback overload for callers that only have a flat term list (e.g. when
        /// re-parsing a query string). Each term is treated as its own group.
        /// </summary>
        internal static SnippetResult Generate(
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

                var inner = _builder.Build(content, queryTerms);
                return new SnippetResult(inner.Html, inner.Score, inner.IsMatch);
            }
        }
    }
}
