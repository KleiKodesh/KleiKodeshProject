using SearchEngine.Snippets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SearchEngine.SeforimDb
{
    /// <summary>
    /// Snippet generation for <see cref="SeforimIndex"/>.
    ///
    /// Uses <see cref="SnippetGenerator"/> (direct IndexOf / NikudSkipIterator scan)
    /// instead of Lucene's token-stream highlighter.
    /// </summary>
    public sealed partial class SeforimIndex
    {
        // ── Public snippet API ────────────────────────────────────────

        /// <summary>
        /// Generates a highlighted HTML snippet for a line ID and query string.
        /// Fetches the line content from the database.
        /// </summary>
        public SnippetResult GenerateSnippet(
            int    lineId,
            string query,
            int    maxCharDistance = 50,
            int    contextChars    = 200)
        {
            if (string.IsNullOrWhiteSpace(query)) { Console.WriteLine($"[GenerateSnippet] lineId={lineId} — empty query"); return SnippetResult.NoMatch; }

            using (var db = new ZayitDb(_dbPath))
            {
                if (!db.IsOpen) { Console.WriteLine($"[GenerateSnippet] lineId={lineId} — DB not open"); return SnippetResult.NoMatch; }
                string content = db.GetLineById(lineId);
                if (content == null) { Console.WriteLine($"[GenerateSnippet] lineId={lineId} — content is null"); return SnippetResult.NoMatch; }

                Console.WriteLine($"[GenerateSnippet] lineId={lineId} contentLen={content.Length} query=\"{query}\"");

                string effectiveQuery = query;
                var groups = BuildMatchedGroups(effectiveQuery);
                if (groups.Count == 0) { Console.WriteLine($"[GenerateSnippet] lineId={lineId} — no groups from query"); return SnippetResult.NoMatch; }

                var stemGroups = ToStemGroups(groups);
                Console.WriteLine($"[GenerateSnippet] lineId={lineId} groups={groups.Count} stems=[{string.Join(", ", System.Linq.Enumerable.SelectMany(stemGroups, g => g))}]");

                bool isWeak;
                var inner = SnippetGenerator.Build(content, stemGroups, maxCharDistance, contextChars, out isWeak);
                Console.WriteLine($"[GenerateSnippet] lineId={lineId} isMatch={inner.IsMatch} score={inner.Score} isWeak={isWeak} htmlLen={inner.Html?.Length ?? 0}");
                if (!inner.IsMatch) return SnippetResult.NoMatch;
                return inner;
            }
        }

        /// <summary>
        /// Generates a highlighted HTML snippet from an already-fetched
        /// <see cref="SearchResult"/>. No database round-trip.
        /// </summary>
        public SnippetResult GenerateSnippet(
            SearchResult result,
            int          maxCharDistance = 50,
            int          contextChars    = 200)
        {
            if (result == null || string.IsNullOrEmpty(result.Content))
                return SnippetResult.NoMatch;

            IReadOnlyList<IReadOnlyCollection<string>> groups;
            if (result.MatchedGroups != null && result.MatchedGroups.Count > 0)
                groups = result.MatchedGroups;
            else
                return SnippetResult.NoMatch;

            var stemGroups = ToStemGroups(groups);
            bool isWeak;
            return SnippetGenerator.Build(result.Content, stemGroups, maxCharDistance, contextChars, out isWeak);
        }

        /// <summary>
        /// Returns the normalised stem tokens for a query string.
        /// Used by FtsSearchExecutor to populate matchedTerms in the snippet payload.
        /// </summary>
        public IReadOnlyList<string> ExtractQueryTerms(string query)
        {
            var groups = BuildMatchedGroups(query);
            var terms  = new List<string>();
            foreach (var group in groups)
                foreach (var term in group)
                {
                    string stem = StripWildcardStar(term);
                    if (stem.Length > 0) terms.Add(stem);
                }
            return terms;
        }

        // ── Private helpers ───────────────────────────────────────────

        /// <summary>
        /// Converts matched groups (which may contain '*') into stem groups for
        /// NikudSkipIterator. "ישר*" → "ישר", "יצחק" → "יצחק".
        /// </summary>
        private static IReadOnlyList<IReadOnlyCollection<string>> ToStemGroups(
            IReadOnlyList<IReadOnlyCollection<string>> groups)
        {
            var result = new List<IReadOnlyCollection<string>>(groups.Count);
            foreach (var group in groups)
            {
                var stems = new List<string>(group.Count > 0 ? group.Count : 1);
                foreach (var term in group)
                {
                    string stem = StripWildcardStar(term);
                    if (stem.Length > 0) stems.Add(stem);
                }
                if (stems.Count > 0) result.Add(stems);
            }
            return result;
        }

        private static string StripWildcardStar(string term)
        {
            if (string.IsNullOrEmpty(term)) return string.Empty;
            return term.Replace("*", string.Empty);
        }
    }
}
