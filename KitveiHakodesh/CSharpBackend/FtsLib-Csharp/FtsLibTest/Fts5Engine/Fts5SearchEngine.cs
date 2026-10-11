using FtsLib;
using FtsLib.Indexing;
using FtsLib.Search;
using FtsLib.SeforimDb;
using PreparedQueryGroups = FtsLib.Snippets.PreparedQueryGroups;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Threading;

namespace FtsLibTest.Fts5Engine
{
    /// <summary>
    /// PROTOTYPE search over an <see cref="Fts5Index"/>, with the same semantics as FtsLib's
    /// SearchPipeline: QueryParser groups (AND across groups, OR within), the production
    /// wildcard / fuzzy / grammar / ketiv expansion rules, AND across groups and OR within, ascending
    /// line ids, content fetched from the corpus, and FtsLib's snippet pipeline — the same
    /// results, with only FtsLib's own limits (expansion caps, wildcard budgets, anchor and
    /// '?' rules). What differs is storage and lookup:
    ///   * the search pass is FTS5's: ONE MATCH expression for the whole query (FTS5 does the
    ///     AND/OR), streamed lazily off its cursor; content is fetched per batch as the caller
    ///     pulls; filterIds is applied to the stream;
    ///   * leading-star wildcards (*x, *x*) and fuzzy: candidates from the FM-index sidecar,
    ///     then FtsLib's own budget / cap rules; other wildcard shapes and grammar use the
    ///     production expanders over terms.db.
    /// Not thread-safe: one engine per thread (it holds one connection).
    /// </summary>
    internal sealed class Fts5SearchEngine : IDisposable
    {
        readonly Func<IFtsCorpus> _openCorpus;
        readonly SqliteConnection _lines;
        readonly SegmentHandle _terms;
        readonly SegmentHandle[] _termSegments;
        readonly FmIndex.Reader _fm;
        readonly SqliteCommand _termsByRowId;

        public Fts5SearchEngine(string dir, Func<IFtsCorpus> openCorpus)
        {
            _openCorpus = openCorpus;
            _lines = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = Fts5Index.LinesDb(dir), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ConnectionString);
            try
            {
                _lines.Open();
                _terms = new SegmentHandle(Fts5Index.TermsDat(dir), Fts5Index.TermsDb(dir));
                _termSegments = new[] { _terms };
                // The FM sidecar is keyed by terms.db rowids: rebuild it when missing or older.
                string fmi = Fts5Index.TermsFmi(dir);
                if (!System.IO.File.Exists(fmi) ||
                    System.IO.File.GetLastWriteTimeUtc(fmi) < System.IO.File.GetLastWriteTimeUtc(Fts5Index.TermsDb(dir)))
                    Fts5Index.BuildFmSidecar(dir);
                _fm = new FmIndex.Reader(fmi);
                _termsByRowId = _terms.Conn.CreateCommand();
            }
            catch { Dispose(); throw; }
        }

        // ── Public API (mirrors SeforimIndex) ─────────────────────────

        const int ContentBatchSize = 200;

        /// <summary>Matching line ids, ascending, streamed straight off the FTS5 cursor.</summary>
        public IEnumerable<int> SearchIds(string query, bool expandKetiv = false,
                                          IEnumerable<int> filterIds = null, CancellationToken ct = default)
        {
            var plan = Plan(query, expandKetiv, filterIds, ct);
            if (plan == null) yield break;
            foreach (int id in StreamIds(plan, ct)) yield return id;
        }

        /// <summary>
        /// Matching lines, ascending, streamed: FTS5 produces the ids as the caller pulls, and
        /// content is fetched one batch of <see cref="ContentBatchSize"/> ids at a time. Nothing
        /// is collected up front, so the first results cost one batch however broad the query.
        /// </summary>
        public IEnumerable<SearchResult> Search(string query, int cap = 0, bool expandKetiv = false,
                                                IEnumerable<int> filterIds = null, CancellationToken ct = default)
        {
            var plan = Plan(query, expandKetiv, filterIds, ct);
            if (plan == null) yield break;
            var prepared = PreparedQueryGroups.FromGroups(plan.MatchedGroups);

            IFtsCorpus corpus = null;   // opened on the first hit, like SearchPipeline
            try
            {
                var batch = new List<int>(ContentBatchSize);
                int taken = 0;              // cap counts ids, as SearchPipeline does
                foreach (int id in StreamIds(plan, ct))
                {
                    batch.Add(id);
                    bool capReached = cap > 0 && ++taken >= cap;
                    if (batch.Count < ContentBatchSize && !capReached) continue;
                    corpus ??= _openCorpus();
                    foreach (var result in Fetch(corpus, batch, plan, prepared, ct)) yield return result;
                    batch.Clear();
                    if (capReached) yield break;
                }
                if (batch.Count > 0)
                {
                    corpus ??= _openCorpus();
                    foreach (var result in Fetch(corpus, batch, plan, prepared, ct)) yield return result;
                }
            }
            finally { corpus?.Dispose(); }
        }

        public static SnippetResult GenerateSnippet(SearchResult result, bool requireOrdered = false,
                                                    int contextWords = SeforimIndex.DefaultContextWords)
        {
            if (result == null || result.MatchedGroups.Count == 0) return SnippetResult.NoMatch;
            var prepared = result.Prepared ?? PreparedQueryGroups.FromGroups(result.MatchedGroups);
            return SnippetPipeline.Generate(result.Content, prepared, requireOrdered, result.OriginalGroupCount, contextWords);
        }

        // ── Pipeline ──────────────────────────────────────────────────

        /// <summary>A parsed and expanded query, ready to run.</summary>
        sealed class QueryPlan
        {
            public string MatchExpression;
            public IReadOnlyList<IReadOnlyCollection<string>> MatchedGroups;
            public int OriginalGroupCount;
            public HashSet<int> Filter;   // null = no filter
        }

        /// <summary>Parse + expand. Null when the query can match nothing.</summary>
        QueryPlan Plan(string query, bool expandKetiv, IEnumerable<int> filterIds, CancellationToken ct)
        {
            var parsed = QueryParser.Parse(query);
            if (parsed.IsEmpty) return null;

            HashSet<int> filter = null;   // same keep-set rule as SearchPipeline.BuildFilter
            if (filterIds != null)
            {
                filter = new HashSet<int>();
                foreach (int id in filterIds) if (id >= 0) filter.Add(id);
                if (filter.Count == 0) return null;
            }

            var groups = new List<IReadOnlyCollection<string>>(parsed.Groups.Count);
            foreach (var group in parsed.Groups)
            {
                ct.ThrowIfCancellationRequested();
                var groupTerms = ExpandGroup(group, expandKetiv, ct, out bool hardMiss);
                if (hardMiss) return null;
                if (groupTerms.Count == 0) continue;
                groups.Add(groupTerms);
            }
            if (groups.Count == 0) return null;

            return new QueryPlan
            {
                MatchExpression = BuildMatchExpression(groups),
                MatchedGroups = groups,
                OriginalGroupCount = parsed.Groups.Count,
                Filter = filter,
            };
        }

        /// <summary>
        /// One FTS5 MATCH for the whole query — FTS5 does the AND across groups and the OR
        /// within each — read lazily. Each call has its own command, so streams may overlap.
        /// </summary>
        IEnumerable<int> StreamIds(QueryPlan plan, CancellationToken ct)
        {
            using var cmd = _lines.CreateCommand();
            cmd.CommandText = "SELECT rowid FROM line_fts WHERE line_fts MATCH $q ORDER BY rowid";
            cmd.Parameters.AddWithValue("$q", plan.MatchExpression);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                int id = (int)r.GetInt64(0);
                if (plan.Filter == null || plan.Filter.Contains(id)) yield return id;
            }
        }

        static IEnumerable<SearchResult> Fetch(IFtsCorpus corpus, List<int> ids, QueryPlan plan,
                                               PreparedQueryGroups prepared, CancellationToken ct)
        {
            if (ids.Count == 0) yield break;
            foreach (var (lineId, content, bookTitle) in corpus.FetchDocuments(ids))
            {
                ct.ThrowIfCancellationRequested();
                yield return new SearchResult(lineId, bookTitle, content, plan.MatchedGroups, plan.OriginalGroupCount, prepared);
            }
        }

        static string BuildMatchExpression(IReadOnlyList<IReadOnlyCollection<string>> groups)
        {
            var sb = new System.Text.StringBuilder();
            for (int g = 0; g < groups.Count; g++)
            {
                if (g > 0) sb.Append(" AND ");
                sb.Append('(');
                bool first = true;
                foreach (string term in groups[g])
                {
                    if (!first) sb.Append(" OR ");
                    first = false;
                    sb.Append('"').Append(term.Replace("\"", "\"\"")).Append('"');
                }
                sb.Append(')');
            }
            return sb.ToString();
        }

        // ── FM-index expansion (same rules as the production expanders) ──

        /// <summary>
        /// FuzzyExpander.Expand semantics: every term within Levenshtein distance d (d clamped
        /// to 1..MaxAllowedDistance), capped at FuzzyExpander.MaxExpandedTerms keeping the
        /// closest (distance, then shorter, then higher document count, then ordinal).
        /// </summary>
        List<string> ExpandFuzzy(string pattern, int maxDistance)
        {
            if (maxDistance > FuzzyExpander.MaxAllowedDistance) maxDistance = FuzzyExpander.MaxAllowedDistance;
            if (maxDistance < 1) maxDistance = 1;
            var hits = _fm.FindFuzzy(pattern, maxDistance);
            var distanceOf = new Dictionary<int, int>(hits.Count);
            foreach (var h in hits) distanceOf[h.RowId] = h.Distance;
            var terms = TermsByRowId(distanceOf.Keys);

            int cap = FuzzyExpander.MaxExpandedTerms;
            if (cap > 0 && terms.Count > cap)
            {
                terms.Sort((a, b) =>
                {
                    int c = distanceOf[a.RowId].CompareTo(distanceOf[b.RowId]);
                    if (c != 0) return c;
                    c = a.Term.Length.CompareTo(b.Term.Length);
                    if (c != 0) return c;
                    c = b.Count.CompareTo(a.Count);
                    return c != 0 ? c : string.CompareOrdinal(a.Term, b.Term);
                });
                terms.RemoveRange(cap, terms.Count - cap);
            }
            return terms.ConvertAll(t => t.Term);
        }

        /// <summary>
        /// True when <paramref name="pattern"/> is a leading-star shape the FM-index answers
        /// exactly: no '?', one literal core with '*' only at the start (suffix) or at both
        /// ends (infix), core at least the production minimum anchor length.
        /// </summary>
        static bool IsFmWildcard(string pattern, out string core, out bool suffixOnly)
        {
            core = null; suffixOnly = false;
            if (pattern.IndexOf('?') >= 0 || !pattern.StartsWith("*")) return false;
            core = pattern.Trim('*');
            if (core.Length < HebrewWildcardExpander.MinAnchorLength || core.IndexOf('*') >= 0) return false;
            suffixOnly = !pattern.EndsWith("*");
            return true;
        }

        /// <summary>
        /// HebrewWildcardExpander.ExpandStar semantics for *core / *core*: LIKE-equivalent
        /// matches, then the affix budgets (suffix: at most MaxPrefixWildcardChars extra
        /// chars; infix: at most MaxPrefix + MaxSuffix), then the shortest-first cap.
        /// </summary>
        List<string> ExpandWildcardWithFm(string core, bool suffixOnly)
        {
            int budget = suffixOnly ? HebrewWildcardExpander.MaxPrefixWildcardChars
                                    : HebrewWildcardExpander.MaxPrefixWildcardChars + HebrewWildcardExpander.MaxSuffixWildcardChars;
            var kept = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var t in TermsByRowId(_fm.FindRowIds(core, suffixOnly)))
                if (t.Term.Length - core.Length <= budget) kept[t.Term] = t.Count;

            int cap = HebrewWildcardExpander.MaxExpandedTerms;
            if (cap <= 0 || kept.Count <= cap) return new List<string>(kept.Keys);
            var entries = new List<KeyValuePair<string, long>>(kept);
            entries.Sort(HebrewWildcardExpander.CompareShortestFirst);
            return entries.GetRange(0, cap).ConvertAll(e => e.Key);
        }

        List<(int RowId, string Term, long Count)> TermsByRowId(IEnumerable<int> rowIds)
        {
            var result = new List<(int, string, long)>();
            var batch = new List<int>(5000);
            foreach (int id in rowIds)
            {
                batch.Add(id);
                if (batch.Count == 5000) { ReadTerms(batch, result); batch.Clear(); }
            }
            if (batch.Count > 0) ReadTerms(batch, result);
            return result;
        }

        void ReadTerms(List<int> batch, List<(int, string, long)> into)
        {
            _termsByRowId.CommandText = "SELECT rowid, term, count FROM term_index WHERE rowid IN (" + string.Join(",", batch) + ")";
            using var r = _termsByRowId.ExecuteReader();
            while (r.Read()) into.Add(((int)r.GetInt64(0), r.GetString(1), r.GetInt64(2)));
        }

        /// <summary>
        /// Copy of SearchPipeline.ExpandGroup (FtsLib/SeforimDb/SearchPipeline.cs), with the
        /// IndexReader calls replaced by the same production expanders over terms.db.
        /// </summary>
        List<string> ExpandGroup(QueryGroup group, bool expandKetiv, CancellationToken ct, out bool hardMiss)
        {
            hardMiss = false;

            if (group.IsSingle && !group.IsWildcard && !group.IsFuzzy && !group.IsGrammar)
            {
                var single = new List<string> { group.Pattern };
                if (expandKetiv)
                    foreach (var variant in KetivExpander.Expand(group.Pattern)) single.Add(variant);
                return single;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var seenAlternatives = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<string>();
            bool anyRealConstraint = false;

            foreach (var alt in group.Alternatives)
            {
                ct.ThrowIfCancellationRequested();
                if (!seenAlternatives.Add(AlternativeKey(alt))) continue;

                List<string> expanded;
                if (alt.IsFuzzy)
                {
                    anyRealConstraint = true;
                    expanded = ExpandFuzzy(alt.Pattern, alt.FuzzyDistance);
                }
                else if (alt.IsWildcard)
                {
                    if (IsFmWildcard(alt.Pattern, out string core, out bool suffixOnly))
                    {
                        anyRealConstraint = true;
                        expanded = ExpandWildcardWithFm(core, suffixOnly);
                    }
                    else
                    {
                        expanded = HebrewWildcardExpander.Expand(alt.Pattern, _termSegments, null, out bool rejected);
                        if (!rejected) anyRealConstraint = true;
                    }
                }
                else if (alt.IsGrammar)
                {
                    anyRealConstraint = true;
                    expanded = GrammarExpander.Expand(alt.Pattern, alt.GrammarExpandPrefixes, alt.GrammarExpandSuffixes, _termSegments);
                }
                else
                {
                    anyRealConstraint = true;
                    expanded = new List<string> { alt.Pattern };
                    if (expandKetiv)
                        foreach (var variant in KetivExpander.Expand(alt.Pattern)) expanded.Add(variant);
                }

                foreach (var term in expanded)
                    if (seen.Add(term)) list.Add(term);
            }

            if (list.Count == 0 && anyRealConstraint) hardMiss = true;
            return list;
        }

        static string AlternativeKey(SubPattern alt)
        {
            if (alt.IsFuzzy)    return "f" + alt.FuzzyDistance + ":" + alt.Pattern;
            if (alt.IsWildcard) return "w:" + alt.Pattern;
            if (alt.IsGrammar)
                return "g" + (alt.GrammarExpandPrefixes ? "p" : "") + (alt.GrammarExpandSuffixes ? "s" : "") + ":" + alt.Pattern;
            return "l:" + alt.Pattern;
        }

        public void Dispose()
        {
            _termsByRowId?.Dispose();
            _fm?.Dispose();
            _lines?.Dispose();
            _terms?.Dispose();
        }
    }
}
