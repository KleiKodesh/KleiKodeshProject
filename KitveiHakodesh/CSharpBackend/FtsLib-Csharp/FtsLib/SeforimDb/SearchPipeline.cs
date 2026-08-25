using System;
using FtsLib.Indexing;
using FtsLib.Search;
using System.Collections.Generic;
using System.Threading;

namespace FtsLib.SeforimDb
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
    ///   a | b       — OR: lines matching a OR b satisfy this AND slot
    ///
    /// Multiple tokens are AND-ed; '|'-separated tokens are OR-ed within one AND slot.
    /// Wildcard/fuzzy tokens are OR-expanded internally; OR groups merge all expansions.
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
        /// <param name="openCorpus">Opens the documents to fetch results from. A FACTORY: this
        /// returns a LAZY sequence, so the corpus must stay open until enumeration finishes, not
        /// until the call returns — which means the `using` has to live inside the iterator here.</param>
        /// <param name="cap">Maximum results to return. 0 = no cap.</param>
        /// <param name="filterIds">Optional line-ID keep-set: only these IDs can be
        /// returned. Null = no filtering; an empty collection matches nothing.</param>
        /// <param name="ct">Cancellation token — checked during expansion, intersection, and DB fetch.</param>
        internal static IEnumerable<SearchResult> Search(
            string            query,
            string            indexPath,
            Func<IFtsCorpus>  openCorpus,
            List<(string dat, string db)> livePaths,
            SearchLease       lease,
            int               cap = 0,
            bool              expandKetiv = false,
            IEnumerable<int>  filterIds = null,
            CancellationToken ct  = default)
        {
            var parsed = QueryParser.Parse(query);
            if (parsed.IsEmpty)
            {
                lease?.Dispose();
                yield break;
            }

            var filter = BuildFilter(filterIds);

            // Phase 1 — under the search lease: everything that touches segment
            // files (expansion + intersection). The matching IDs are materialized
            // so the lease is released BEFORE the DB content fetch below. Holding
            // it across the fetch (minutes for broad queries) would let a queued
            // merge commit block every new search behind the write lock for the
            // whole stream — the lease must only live for milliseconds-to-seconds.
            var ids = new List<int>();
            IReadOnlyList<IReadOnlyCollection<string>> matchedGroups;
            int originalGroupCount = parsed.Groups.Count;
            DocSourceMap docMap;

            using (var reader = new IndexReader(indexPath, livePaths, lease))
            {
                var groups         = new List<IEnumerable<string>>(parsed.Groups.Count);
                var expandedGroups = new List<IReadOnlyCollection<string>>(parsed.Groups.Count);

                foreach (var group in parsed.Groups)
                {
                    ct.ThrowIfCancellationRequested();

                    var groupTerms = ExpandGroup(group, reader, expandKetiv, ct, out bool hardMiss);
                    if (hardMiss) yield break;
                    if (groupTerms.Count == 0) continue;
                    groups.Add(groupTerms);
                    expandedGroups.Add(groupTerms);
                }

                if (groups.Count == 0) yield break;

                matchedGroups = expandedGroups;

                foreach (var id in reader.Search(groups, filter, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    ids.Add(id);
                    if (cap > 0 && ids.Count >= cap) break;
                }

                // Snapshot the docId→corpus map while the segment files are still
                // leased — after the reader disposes, a merge may delete them.
                // Zero results need no map (nothing will be fetched).
                docMap = ids.Count > 0 ? reader.GetDocSourceMap() : DocSourceMap.Identity;
            }

            if (ids.Count == 0) yield break;

            // Prepared ONCE per query: the snippet-side term→group map, shared by
            // every result row (immutable — safe across snippet threads). Building
            // it per line was O(#expanded terms) per snippet.
            var prepared = FtsLib.Snippets.PreparedQueryGroups.FromGroups(matchedGroups);

            // Phase 2 — lease released: stream content rows from the DB, routed by
            // corpus. Result ids are docIds; SplitBySource returns null when every
            // id is library-identity (docId == line.id) — the fast path, which is
            // byte-identical to the single-corpus fetch. Otherwise each contiguous
            // run is fetched from its corpus DB by source-local id and yielded
            // under its app-visible docId, preserving the ascending-id contract.
            var runs = docMap.SplitBySource(ids);
            if (runs == null)
            {
                using (var corpus = openCorpus())
                {
                    foreach (var (lineId, content, bookTitle) in corpus.FetchDocuments(ids))
                    {
                        ct.ThrowIfCancellationRequested();
                        yield return new SearchResult(lineId, bookTitle, content, matchedGroups, originalGroupCount, prepared);
                    }
                }
                yield break;
            }

            foreach (var run in runs)
            {
                // Only the library corpus has a content DB wired up so far. A
                // non-library run means the index was built with a multi-corpus
                // DocSourceMap this pipeline doesn't know how to fetch for yet —
                // fail loudly rather than mislabel content.
                if (run.Source != DocSourceMap.LibrarySource)
                    throw new System.NotSupportedException(
                        $"FTS: no content database registered for corpus {run.Source} " +
                        $"({run.Count} result(s)); only the library (source 0) is wired up.");

                long offset = run.Offset;
                using (var corpus = openCorpus())
                {
                    foreach (var (srcId, content, bookTitle) in
                             corpus.FetchDocuments(TranslateRun(ids, run)))
                    {
                        ct.ThrowIfCancellationRequested();
                        int docId = (int)(srcId - offset); // back to the app-visible id
                        yield return new SearchResult(docId, bookTitle, content, matchedGroups, originalGroupCount, prepared);
                    }
                }
            }
        }

        /// <summary>Lazily translates one source run's docIds to source-local ids.</summary>
        private static IEnumerable<int> TranslateRun(List<int> ids, DocSourceMap.SourceRun run)
        {
            for (int i = 0; i < run.Count; i++)
                yield return (int)(ids[run.Start + i] + run.Offset);
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
                foreach (var alt in g.Alternatives)
                    terms.Add(alt.Pattern);
            return terms;
        }

        /// <summary>
        /// Returns only the matching line IDs — no database fetch at all.
        /// Use when the caller only needs IDs (counting, on-demand content loading).
        /// </summary>
        internal static IEnumerable<int> SearchIds(
            string            query,
            string            indexPath,
            List<(string dat, string db)> livePaths,
            SearchLease       lease,
            bool              expandKetiv = false,
            IEnumerable<int>  filterIds = null,
            CancellationToken ct = default)
        {
            var parsed = QueryParser.Parse(query);
            if (parsed.IsEmpty)
            {
                lease?.Dispose();
                yield break;
            }

            var filter = BuildFilter(filterIds);

            // Materialize under the lease, yield after releasing it — a slow
            // consumer of this enumerable must not keep segment files leased
            // (see Search for the full rationale).
            var ids = new List<int>();
            using (var reader = new IndexReader(indexPath, livePaths, lease))
            {
                var groups = new List<IEnumerable<string>>(parsed.Groups.Count);

                foreach (var group in parsed.Groups)
                {
                    ct.ThrowIfCancellationRequested();

                    var groupTerms = ExpandGroup(group, reader, expandKetiv, ct, out bool hardMiss);
                    if (hardMiss) yield break;
                    if (groupTerms.Count == 0) continue;
                    groups.Add(groupTerms);
                }

                if (groups.Count == 0) yield break;

                foreach (var id in reader.Search(groups, filter, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    ids.Add(id);
                }
            }

            foreach (var id in ids)
                yield return id;
        }

        // ── Filter construction ───────────────────────────────────────

        /// <summary>
        /// Materializes caller-supplied line IDs into a <see cref="RoaringBitmap"/>
        /// keep-set. Null in → null out (no filtering). IDs may arrive in any
        /// order and contain duplicates. An empty collection yields an empty
        /// bitmap, which matches nothing.
        ///
        /// Negative IDs are dropped: no indexed line ever has one, and
        /// RoaringBitmap orders values as unsigned — a negative ID would sort
        /// AFTER every real ID, breaking the ascending-order contract the
        /// intersection leap-frog depends on (it would spin forever).
        /// </summary>
        private static RoaringBitmap BuildFilter(IEnumerable<int> filterIds)
        {
            if (filterIds == null) return null;
            var bitmap = new RoaringBitmap();
            foreach (var id in filterIds)
                if (id >= 0)
                    bitmap.Add(id);
            return bitmap;
        }

        // ── Group expansion ───────────────────────────────────────────

        /// <summary>
        /// Expands all OR alternatives in <paramref name="group"/> into a single
        /// deduplicated list of concrete index terms.
        ///
        /// Zero-expansion semantics (one consistent rule): an alternative that
        /// expands to nothing simply contributes nothing to the group. If the WHOLE
        /// group ends up empty even though it contained at least one real constraint
        /// (a literal, fuzzy, grammar, or supported-wildcard alternative), the group
        /// is unsatisfiable and <paramref name="hardMiss"/> is set — AND semantics
        /// require the whole query to return no results. Only a group consisting
        /// entirely of REJECTED wildcard patterns (unsupported: anchor too short /
        /// too many '?' operators — see <see cref="HebrewWildcardExpander"/>) is
        /// skipped as an AND slot, the documented behaviour for unsupported patterns.
        ///
        /// (Previously an empty FUZZY alternative aborted the whole query even when
        /// an OR sibling had matches, while empty wildcard/grammar alternatives
        /// silently DROPPED the AND slot — broadening the query instead of
        /// returning the correct empty result.)
        /// </summary>
        private static List<string> ExpandGroup(
            QueryGroup        group,
            IndexReader       reader,
            bool              expandKetiv,
            CancellationToken ct,
            out bool          hardMiss)
        {
            hardMiss = false;

            // Fast path: single literal alternative (the common case).
            if (group.IsSingle && !group.IsWildcard && !group.IsFuzzy && !group.IsGrammar)
            {
                var result = new List<string> { group.Pattern };
                // כתיב expansion: add spelling variants as additional OR alternatives.
                // Only for plain literals — wildcards and fuzzy already cover variants.
                if (expandKetiv)
                {
                    foreach (var variant in KetivExpander.Expand(group.Pattern))
                        result.Add(variant);
                }
                return result;
            }

            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            var list = new List<string>();

            // Alternatives already looked up in this group, keyed by their FULL
            // shape — the pattern alone is not the identity, since "word~2" and
            // "word~3" (or "%word" and "word%") are genuinely different lookups.
            // Query expansion can put the same alternative in a group more than
            // once (an expanded form colliding with the typed word, or with a form
            // reached from another channel), and each duplicate would otherwise
            // re-run a full index scan whose every term the `seen` set below then
            // discards. Skipping them here makes the work proportional to the
            // DISTINCT alternatives, and leaves the resulting term list identical.
            var seenAlts = new HashSet<string>(System.StringComparer.Ordinal);

            // True once any alternative was a genuine constraint (everything except
            // a REJECTED wildcard pattern). Literals count even when absent from the
            // index — the intersection layer's missing-term contract yields the
            // correct empty result for those.
            bool anyRealConstraint = false;

            foreach (var alt in group.Alternatives)
            {
                ct.ThrowIfCancellationRequested();

                // A repeat of an alternative already expanded above contributes
                // nothing new: its terms are all in `seen`, and its effect on
                // anyRealConstraint/hardMiss was recorded on the first pass.
                if (!seenAlts.Add(AltKey(alt))) continue;

                List<string> expanded;

                if (alt.IsFuzzy)
                {
                    anyRealConstraint = true;
                    expanded = reader.ExpandFuzzy(alt.Pattern, alt.FuzzyDistance);
                    if (expanded.Count == 0) continue;
                }
                else if (alt.IsWildcard)
                {
                    expanded = reader.ExpandWildcard(alt.Pattern, out bool rejected);
                    if (!rejected) anyRealConstraint = true;
                    if (expanded.Count == 0) continue;
                }
                else if (alt.IsGrammar)
                {
                    anyRealConstraint = true;
                    expanded = reader.ExpandGrammar(alt.Pattern,
                                                    alt.GrammarExpandPrefixes,
                                                    alt.GrammarExpandSuffixes);
                    if (expanded.Count == 0) continue;
                }
                else
                {
                    // Literal alternative — add כתיב variants if requested.
                    anyRealConstraint = true;
                    expanded = new List<string> { alt.Pattern };
                    if (expandKetiv)
                        foreach (var variant in KetivExpander.Expand(alt.Pattern))
                            expanded.Add(variant);
                }

                foreach (var term in expanded)
                    if (seen.Add(term))
                        list.Add(term);
            }

            // A group whose real constraints all matched nothing is unsatisfiable.
            if (list.Count == 0 && anyRealConstraint)
                hardMiss = true;

            return list;
        }

        /// <summary>
        /// A key identifying what an alternative actually LOOKS UP, so two
        /// alternatives share a key only when expanding them would run the same
        /// scan and yield the same terms. The kind, the fuzzy distance and the
        /// two grammar flags all change the result set, so all of them are part
        /// of the key; the leading tag also keeps a pattern from colliding across
        /// kinds. Ordinal — patterns are already normalised by the parser.
        /// </summary>
        private static string AltKey(SubPattern alt)
        {
            if (alt.IsFuzzy)    return "f" + alt.FuzzyDistance + ":" + alt.Pattern;
            if (alt.IsWildcard) return "w:" + alt.Pattern;
            if (alt.IsGrammar)
                return "g" + (alt.GrammarExpandPrefixes ? "p" : "")
                           + (alt.GrammarExpandSuffixes ? "s" : "") + ":" + alt.Pattern;
            return "l:" + alt.Pattern;
        }
    }
}
