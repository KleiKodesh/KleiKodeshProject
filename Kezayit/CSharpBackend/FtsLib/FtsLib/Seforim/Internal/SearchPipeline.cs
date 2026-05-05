using FtsLib.Core;
using FtsLib.Misc;
using System.Collections.Generic;

namespace FtsLib.Seforim
{
    /// <summary>
    /// Executes a parsed query against the index and fetches matching rows
    /// from the seforim database.
    ///
    /// Query syntax (handled by <see cref="QueryParser"/>):
    ///   word        — literal AND term
    ///   word*       — wildcard (prefix / infix / suffix)
    ///   wor?d       — optional char: 'd' before '?' is optional → matches "word" and "wrd"
    ///   word~       — fuzzy, edit distance 1 (default)
    ///   word~2      — fuzzy, edit distance 2
    ///   word~3      — fuzzy, edit distance 3 (maximum)
    /// Multiple tokens are AND-ed; wildcard/fuzzy tokens are OR-expanded internally.
    /// </summary>
    internal static class SearchPipeline
    {
        /// <summary>
        /// Parses <paramref name="query"/>, expands wildcards/fuzzy terms, runs the
        /// intersection search, fetches rows from the DB, and returns results as a
        /// lazy enumerable.
        ///
        /// Each <see cref="SearchResult"/> carries <c>MatchedTerms</c> — the full set
        /// of concrete index terms that were OR-expanded from the query. The snippet
        /// system uses these to highlight the actual matched forms (e.g. ביצחק when
        /// the query was יצחק~) rather than the raw pattern.
        /// </summary>
        /// <param name="query">Raw query string from the user.</param>
        /// <param name="indexPath">Directory containing the segment files.</param>
        /// <param name="dbPath">Path to the seforim SQLite database.</param>
        /// <param name="cap">Maximum results to return. 0 = no cap.</param>
        internal static IEnumerable<SearchResult> Search(
            string query,
            string indexPath,
            string dbPath,
            int    cap = 0)
        {
            var parsed = QueryParser.Parse(query);
            if (parsed.IsEmpty) yield break;

            using (var reader = new IndexReader(indexPath))
            {
                // Expand each token into a group of concrete terms.
                // Within a group terms are OR-ed; across groups they are AND-ed.
                var groups       = new List<IEnumerable<string>>(parsed.Groups.Count);
                // Per-group expanded terms — used for proximity window + highlighting.
                var expandedGroups = new List<IReadOnlyCollection<string>>(parsed.Groups.Count);

                foreach (var group in parsed.Groups)
                {
                    List<string> expanded;

                    if (group.IsFuzzy)
                    {
                        expanded = reader.ExpandFuzzy(group.Pattern, group.FuzzyDistance);
                        if (expanded.Count == 0) yield break; // no candidates → no results
                    }
                    else if (group.IsWildcard)
                    {
                        expanded = reader.ExpandWildcard(group.Pattern);
                        if (expanded.Count == 0)
                        {
                            // Anchor too short or no matches — skip this token so the
                            // remaining terms still produce results rather than killing
                            // the whole query with a dead AND group.
                            continue;
                        }
                    }
                    else
                    {
                        expanded = new List<string> { group.Pattern };
                    }

                    groups.Add(expanded);
                    expandedGroups.Add(expanded);
                }

                // Snapshot as a read-only list shared across all results.
                IReadOnlyList<IReadOnlyCollection<string>> matchedGroups = expandedGroups;

                // Collect matching IDs (lazy intersection across all groups).
                var ids = new List<int>(reader.Search(groups));
                if (ids.Count == 0) yield break;

                // Fetch rows from the DB and stream them out.
                int yielded = 0;
                using (var db = new ZayitDb(dbPath))
                {
                    foreach (var (lineId, content, bookTitle) in db.FetchSearchResults(ids))
                    {
                        yield return new SearchResult(lineId, bookTitle, content, matchedGroups);
                        yielded++;
                        if (cap > 0 && yielded >= cap) yield break;
                    }
                }
            }
        }

        /// <summary>
        /// Returns the normalised query terms for a raw query string.
        /// Fuzzy/wildcard markers are stripped — only the base word forms are returned.
        /// Used as a fallback when the caller does not have a <see cref="SearchResult"/>
        /// with pre-computed <c>MatchedTerms</c>.
        /// </summary>
        internal static IReadOnlyList<string> ExtractTerms(string query)
        {
            var parsed = QueryParser.Parse(query);
            var terms  = new List<string>(parsed.Groups.Count);
            foreach (var g in parsed.Groups)
                terms.Add(g.Pattern);
            return terms;
        }

        /// <summary>
        /// Returns only the matching line IDs — no database fetch at all.
        /// Use when the caller only needs IDs (counting, on-demand content loading).
        /// </summary>
        internal static IEnumerable<int> SearchIds(string query, string indexPath)
        {
            var parsed = QueryParser.Parse(query);
            if (parsed.IsEmpty) yield break;

            using (var reader = new IndexReader(indexPath))
            {
                var groups = new List<IEnumerable<string>>(parsed.Groups.Count);

                foreach (var group in parsed.Groups)
                {
                    List<string> expanded;
                    if (group.IsFuzzy)
                    {
                        expanded = reader.ExpandFuzzy(group.Pattern, group.FuzzyDistance);
                        if (expanded.Count == 0) yield break;
                    }
                    else if (group.IsWildcard)
                    {
                        expanded = reader.ExpandWildcard(group.Pattern);
                        if (expanded.Count == 0)
                        {
                            // Anchor too short or no matches — skip, don't kill the query.
                            continue;
                        }
                    }
                    else
                    {
                        expanded = new List<string> { group.Pattern };
                    }
                    groups.Add(expanded);
                }

                foreach (var id in reader.Search(groups))
                    yield return id;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────

        // (no helpers currently needed)
    }
}
