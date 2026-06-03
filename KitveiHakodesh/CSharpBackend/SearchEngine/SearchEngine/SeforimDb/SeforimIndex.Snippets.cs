using SearchEngine.Search;
using SearchEngine.Snippets;
using System;
using System.Collections.Generic;
using System.Threading;

namespace SearchEngine.SeforimDb
{
    /// <summary>
    /// Snippet generation and word-distance computation for <see cref="SeforimIndex"/>.
    /// </summary>
    public sealed partial class SeforimIndex
    {
        // ── Public snippet API ────────────────────────────────────────

        /// <summary>
        /// Generates a highlighted HTML snippet for a line ID and query string.
        /// Fetches the line content from the database.
        /// </summary>
        public SnippetResult GenerateSnippet(int lineId, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return SnippetResult.NoMatch;

            var searcher = EnsureSearcher();
            if (searcher == null) return SnippetResult.NoMatch;

            using (var db = new ZayitDb(_dbPath))
            {
                if (!db.IsOpen) return SnippetResult.NoMatch;
                string content = db.GetLineById(lineId);
                if (content == null) return SnippetResult.NoMatch;
                int minMarks = BuildMatchedGroups(query).Count;
                return GenerateSnippetFromContent(searcher, content, query,
                    minMarks: minMarks);
            }
        }

        /// <summary>
        /// Generates a highlighted HTML snippet from an already-fetched
        /// <see cref="SearchResult"/>. No database round-trip.
        /// </summary>
        public SnippetResult GenerateSnippet(
            SearchResult result,
            bool         requireOrdered = false,
            int          contextWords   = DefaultContextWords)
        {
            if (result == null)                       return SnippetResult.NoMatch;
            if (result.MatchedGroups.Count == 0)      return SnippetResult.NoMatch;
            if (string.IsNullOrEmpty(result.Content)) return SnippetResult.NoMatch;

            var searcher = EnsureSearcher();
            if (searcher == null) return SnippetResult.NoMatch;

            string reconstructedQuery = ReconstructQuery(result.MatchedGroups);
            return GenerateSnippetFromContent(
                searcher, result.Content, reconstructedQuery,
                requireOrdered, contextWords,
                minMarks: result.MatchedGroups.Count);
        }

        /// <summary>
        /// Computes word distance for a query against content without generating
        /// the full HTML snippet. Used for fast filtering before snippet generation.
        /// Returns int.MaxValue if the query does not match.
        /// </summary>
        public int ComputeWordDistance(string query, string content,
            bool requireOrdered = false, int contextWords = DefaultContextWords)
        {
            if (string.IsNullOrWhiteSpace(query) || string.IsNullOrEmpty(content))
                return int.MaxValue;

            var searcher = EnsureSearcher();
            if (searcher == null) return int.MaxValue;

            int fragmentSize = contextWords * 8 * 2;
            int slop         = requireOrdered ? contextWords : int.MaxValue;

            foreach (var (_, _, _, _, snippet) in searcher.SearchWithSnippets(
                query,
                _ => content,
                fragmentSize: fragmentSize,
                slop:         slop,
                inOrder:      requireOrdered))
            {
                return ComputeWordDistanceFromHtml(snippet.Html);
            }

            return int.MaxValue;
        }

        // ── Private helpers ───────────────────────────────────────────

        private static SnippetResult GenerateSnippetFromContent(
            LuceneSearcher    searcher,
            string            content,
            string            query,
            bool              requireOrdered = false,
            int               contextWords   = DefaultContextWords,
            int               minMarks       = 0,
            CancellationToken ct             = default)
        {
            int fragmentSize = contextWords * 8 * 2;
            int slop         = requireOrdered ? contextWords : int.MaxValue;

            foreach (var (_, _, _, _, snippet) in searcher.SearchWithSnippets(
                query,
                _ => content,
                fragmentSize: fragmentSize,
                slop:         slop,
                inOrder:      requireOrdered,
                minMarks:     minMarks,
                ct:           ct))
            {
                int wordDistance = ComputeWordDistance(snippet.Html);
                int score        = snippet.Html.Length;
                return new SnippetResult(snippet.Html, score, wordDistance, snippet.IsMatch);
            }

            return SnippetResult.NoMatch;
        }

        /// <summary>
        /// Private overload — computes word distance from an already-rendered HTML
        /// snippet string. Used by <see cref="SearchWithSnippets"/> after each result.
        /// </summary>
        private static int ComputeWordDistance(string html)
        {
            if (string.IsNullOrEmpty(html)) return int.MaxValue;
            return ComputeWordDistanceFromHtml(html);
        }

        /// <summary>
        /// Counts tokens between the first &lt;mark&gt; and last &lt;/mark&gt;,
        /// minus the number of matched terms — mirrors FtsLib's WordDistance metric.
        /// </summary>
        private static int ComputeWordDistanceFromHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return int.MaxValue;

            int firstMark = html.IndexOf("<mark>",  StringComparison.OrdinalIgnoreCase);
            int lastClose = html.LastIndexOf("</mark>", StringComparison.OrdinalIgnoreCase);
            if (firstMark < 0 || lastClose < 0) return int.MaxValue;

            int windowEnd = lastClose + "</mark>".Length;
            string window = html.Substring(firstMark, windowEnd - firstMark);

            var sb = new System.Text.StringBuilder(window.Length);
            bool inTag = false;
            foreach (char c in window)
            {
                if      (c == '<') { inTag = true;  continue; }
                if      (c == '>') { inTag = false; sb.Append(' '); continue; }
                if (!inTag) sb.Append(c);
            }

            int totalTokens = 0;
            foreach (var tok in sb.ToString().Split(
                new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                totalTokens++;

            int markCount = 0;
            int pos = 0;
            while ((pos = html.IndexOf("<mark>", pos, StringComparison.OrdinalIgnoreCase)) >= 0)
            { markCount++; pos += 6; }

            int dist = totalTokens - markCount;
            return dist < 0 ? 0 : dist;
        }
    }
}
