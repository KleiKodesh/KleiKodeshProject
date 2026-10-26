#if NET6_0_OR_GREATER
using FtsLib;
using FtsLib.Indexing;
using FtsLib.Search;
using FtsLib.SeforimDb;
using FtsLib.Snippets;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Threading;

namespace FtsLib.Fts5
{
    /// <summary>
    /// Search over an <see cref="Fts5Index"/>, with the semantics of FtsLib's former segment
    /// engine: QueryParser groups (AND across groups, OR within), the production
    /// wildcard / fuzzy / grammar / ketiv expansion rules, AND across groups and OR within, results
    /// in the index's order (catalog order for an IFtsOrderedCorpus, else ascending line ids), content fetched from the corpus, and FtsLib's snippet pipeline — the same
    /// results, with only FtsLib's own limits (expansion caps, wildcard budgets, anchor and
    /// '?' rules). What differs is storage and lookup:
    ///   * the search pass is FTS5's: ONE MATCH expression for the whole query (FTS5 does the
    ///     AND/OR), streamed lazily off its cursor; content is fetched per batch as the caller
    ///     pulls; filterIds is applied to the stream;
    ///   * leading-star wildcards (*x, *x*) and fuzzy: candidates from the FM-index sidecar,
    ///     then FtsLib's own budget / cap rules; other wildcard shapes and grammar use the
    ///     production expanders over terms.db;
    ///   * while a build is still running there is no terms.db yet: expansion runs the same
    ///     way over every live per-batch dictionary (Fts5TermDictionaries), counts summed —
    ///     exactly as FtsLib's expanders sum counts across its segments.
    /// Not thread-safe: one engine per thread (it holds one connection).
    /// </summary>
    internal sealed class Fts5SearchEngine : IDisposable
    {
        readonly Func<IFtsCorpus> _openCorpus;
        readonly SqliteConnection _lines;
        /// <summary>A term dictionary: term_index (term, count; rowid = ordinal rank) + FM-index.</summary>
        sealed class TermDictionary : IDisposable
        {
            public SegmentHandle Handle;
            public FmIndex.Reader Fm;          // null when the dictionary is empty
            public SqliteCommand TermsByRowId;
            public void Dispose() { TermsByRowId?.Dispose(); Fm?.Dispose(); Handle?.Dispose(); }
        }

        readonly List<TermDictionary> _dictionaries = new List<TermDictionary>();
        readonly SegmentHandle[] _termSegments;

        public Fts5SearchEngine(string dir, Func<IFtsCorpus> openCorpus)
        {
            _openCorpus = openCorpus;
            // ReadWrite (only ever read): the line table is in WAL mode, and a read-only
            // connection cannot create the shared-memory file WAL readers need.
            _lines = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = Fts5Index.LinesDb(dir), Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ConnectionString);
            try
            {
                _lines.Open();
                // A build can publish terms.db, or merge and delete dictionaries, between deciding
                // what to open and opening it: then the whole decision is simply made again.
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        if (System.IO.File.Exists(Fts5Index.TermsDb(dir)))
                        {
                            // Finished build: the single terms.db + FM sidecar.
                            // The FM sidecar is keyed by terms.db rowids: rebuild it when missing or older.
                            string fmi = Fts5Index.TermsFmi(dir);
                            if (!System.IO.File.Exists(fmi) ||
                                System.IO.File.GetLastWriteTimeUtc(fmi) < System.IO.File.GetLastWriteTimeUtc(Fts5Index.TermsDb(dir)))
                                Fts5Index.BuildFmSidecar(dir);
                            OpenDictionary(Fts5Index.TermsDat(dir), Fts5Index.TermsDb(dir), fmi);
                        }
                        else
                        {
                            // Build running: every dictionary the manifest lists right now. They are
                            // never empty, so a missing .fmi means a merge deleted it: retry.
                            foreach (var (name, _) in Fts5TermDictionaries.Read(dir).Dictionaries)
                            {
                                string fmi = Fts5TermDictionaries.Fmi(dir, name);
                                if (!System.IO.File.Exists(fmi)) throw new System.IO.FileNotFoundException("merged away", fmi);
                                OpenDictionary(Fts5TermDictionaries.Dat(dir, name), Fts5TermDictionaries.Db(dir, name), fmi);
                            }
                        }
                        break;
                    }
                    catch (Exception ex) when (attempt < 10 && (ex is System.IO.IOException || ex is SqliteException))
                    {
                        foreach (var d in _dictionaries) d.Dispose();
                        _dictionaries.Clear();
                        Thread.Sleep(50);
                    }
                }
                _termSegments = _dictionaries.ConvertAll(d => d.Handle).ToArray();
            }
            catch { Dispose(); throw; }
        }

        void OpenDictionary(string datPath, string dbPath, string fmiPath)
        {
            var d = new TermDictionary();
            _dictionaries.Add(d);
            d.Handle = new SegmentHandle(datPath, dbPath);
            if (System.IO.File.Exists(fmiPath)) d.Fm = new FmIndex.Reader(fmiPath);
            d.TermsByRowId = d.Handle.Conn.CreateCommand();
        }

        // ── Public API ────────────────────────────────────────────────

        /// <summary>Content batch size when the caller passes no plan of its own.</summary>
        const int DefaultContentBatchSize = 256;

        /// <summary>
        /// SUBSTRING candidates inside blocks of positions (a book's lines, or a run of them,
        /// are contiguous in the index's order). Every query word is cut into terms by the
        /// indexer's own tokenizer, and each term becomes the group of EVERY dictionary term
        /// that contains it, at any place and of any length — read off the FM-index, with
        /// none of the wildcard expanders' affix budgets or caps, so no line holding the
        /// substring can be missed. Lines holding all groups are returned as (range, position, line id),
        /// ascending. A superset: the caller confirms the exact substring on the text.
        ///
        /// Null when the index cannot answer exactly: a word with no indexable term, a
        /// dictionary without its FM-index, or a term contained in more than
        /// <paramref name="maxTermsPerFragment"/> dictionary terms (a fragment that common
        /// matches most of the book anyway — reading the book is the cheaper answer).
        /// </summary>
        public List<(int Range, long Position, int LineId)> FindSubstringCandidates(
            string query, IReadOnlyList<(long First, long Last)> ranges, int maxTermsPerFragment, CancellationToken ct)
        {
            var tokenizer = new Tokenization.Tokenizer();
            var groups = new List<IReadOnlyCollection<string>>();
            foreach (string word in query.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            {
                var fragments = tokenizer.Extract(word);
                if (fragments.Count == 0) return null;
                foreach (string fragment in fragments)
                {
                    ct.ThrowIfCancellationRequested();
                    var terms = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var d in _dictionaries)
                    {
                        if (d.Fm == null) return null;
                        foreach (var t in TermsByRowId(d, d.Fm.FindRowIds(fragment, false)))
                        {
                            terms.Add(t.Term);
                            if (terms.Count > maxTermsPerFragment) return null;
                        }
                    }
                    if (terms.Count == 0) return new List<(int, long, int)>();   // no line holds it
                    groups.Add(terms);
                }
            }
            if (groups.Count == 0) return null;

            // One MATCH per range: a rowid range is a seek FTS5 does natively, where an OR of
            // ranges in one statement would make it read the whole doclist.
            var found = new List<(int, long, int)>();
            using var cmd = _lines.CreateCommand();
            cmd.CommandText = "SELECT line_fts.rowid, p.line_id FROM line_fts JOIN line_position p ON p.position = line_fts.rowid " +
                              "WHERE line_fts MATCH $q AND line_fts.rowid BETWEEN $first AND $last ORDER BY line_fts.rowid";
            cmd.Parameters.AddWithValue("$q", BuildMatchExpression(groups));
            var pFirst = cmd.Parameters.Add("$first", SqliteType.Integer);
            var pLast = cmd.Parameters.Add("$last", SqliteType.Integer);
            for (int range = 0; range < ranges.Count; range++)
            {
                pFirst.Value = ranges[range].First;
                pLast.Value = ranges[range].Last;
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    found.Add((range, r.GetInt64(0), (int)r.GetInt64(1)));
                }
            }
            return found;
        }

        /// <summary>
        /// The given lines as matches of <paramref name="query"/> — the query parsed and
        /// expanded, each line's text read, NO full-text MATCH run — for building snippets of
        /// lines a search already returned (only the rows a page shows). Lines the query no
        /// longer expands for are simply absent from the result.
        /// </summary>
        public List<SearchResult> MatchesForLines(string query, IReadOnlyList<int> lineIds, CancellationToken ct)
        {
            var results = new List<SearchResult>(lineIds.Count);
            var plan = Plan(query, null, ct);
            if (plan == null || lineIds.Count == 0) return results;
            var prepared = PreparedQueryGroups.FromGroups(plan.MatchedGroups);
            using var corpus = _openCorpus();
            results.AddRange(Fetch(corpus, new List<int>(lineIds), plan, prepared, withPlacement: false, ct));
            return results;
        }

        /// <summary>Matching line ids in the index's order, streamed straight off the FTS5 cursor.</summary>
        public IEnumerable<int> SearchIds(string query,
                                          IEnumerable<int> filterIds = null, CancellationToken ct = default)
        {
            var plan = Plan(query, filterIds, ct);
            if (plan == null) yield break;
            foreach (int id in StreamIds(plan, ct)) yield return id;
        }

        /// <summary>
        /// Matching lines in the index's order, streamed: FTS5 produces the ids as the caller pulls, and
        /// content is fetched one batch at a time. Nothing is collected up front, so the first
        /// results cost one batch however broad the query.
        /// </summary>
        /// <param name="onStage">Optional progress hook, fired on the ENUMERATING thread with a
        /// stable ASCII stage key ("parse", "expand", "lookup", "fetch") as the pipeline moves.</param>
        /// <param name="batchSizeAt">The caller's batch plan: the number of ids to fetch as
        /// batch number n (0-based) — so a caller that processes results in batches of its
        /// own gets them cut at the same places. Null: <see cref="DefaultContentBatchSize"/>
        /// throughout.</param>
        /// <param name="withPlacement">True (default): an <see cref="IFtsPlacedCorpus"/> fills
        /// each result's <see cref="SearchResult.Placement"/> in the content read. False: the
        /// content read fetches the text only (what verification and snippets read), and the
        /// caller reads placement itself later, for the results it keeps.</param>
        public IEnumerable<SearchResult> Search(string query, int cap = 0,
                                                IEnumerable<int> filterIds = null, CancellationToken ct = default,
                                                Action<string> onStage = null, Func<int, int> batchSizeAt = null,
                                                bool withPlacement = true)
        {
            var plan = Plan(query, filterIds, ct, onStage);
            if (plan == null) yield break;
            onStage?.Invoke("lookup");
            var prepared = PreparedQueryGroups.FromGroups(plan.MatchedGroups);

            IFtsCorpus corpus = null;   // opened on the first hit
            try
            {
                int batchIndex = 0;
                int batchSize = Math.Max(1, batchSizeAt?.Invoke(batchIndex) ?? DefaultContentBatchSize);
                var batch = new List<int>(batchSize);
                int taken = 0;              // cap counts ids
                foreach (int id in StreamIds(plan, ct))
                {
                    batch.Add(id);
                    bool capReached = cap > 0 && ++taken >= cap;
                    if (batch.Count < batchSize && !capReached) continue;
                    if (corpus == null) onStage?.Invoke("fetch");
                    corpus ??= _openCorpus();
                    foreach (var result in Fetch(corpus, batch, plan, prepared, withPlacement, ct)) yield return result;
                    batch.Clear();
                    if (capReached) yield break;
                    batchSize = Math.Max(1, batchSizeAt?.Invoke(++batchIndex) ?? DefaultContentBatchSize);
                }
                if (batch.Count > 0)
                {
                    if (corpus == null) onStage?.Invoke("fetch");
                    corpus ??= _openCorpus();
                    foreach (var result in Fetch(corpus, batch, plan, prepared, withPlacement, ct)) yield return result;
                }
            }
            finally { corpus?.Dispose(); }
        }

        public static SnippetDescriptor LocateSnippetWindow(SearchResult result, bool requireOrdered = false,
                                                            int contextWords = SnippetDescriptorPipeline.DefaultContextWords)
        {
            if (result == null || result.MatchedGroups.Count == 0) return SnippetDescriptor.NoMatch;
            var prepared = result.Prepared ?? PreparedQueryGroups.FromGroups(result.MatchedGroups);
            return SnippetDescriptorPipeline.Locate(result.Content, prepared, requireOrdered, result.OriginalGroupCount, contextWords);
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
        QueryPlan Plan(string query, IEnumerable<int> filterIds, CancellationToken ct,
                       Action<string> onStage = null)
        {
            onStage?.Invoke("parse");
            var parsed = QueryParser.Parse(query);
            if (parsed.IsEmpty) return null;

            HashSet<int> filter = null;   // null = everything, empty = nothing, negatives ignored
            if (filterIds != null)
            {
                filter = new HashSet<int>();
                foreach (int id in filterIds) if (id >= 0) filter.Add(id);
                if (filter.Count == 0) return null;
            }

            onStage?.Invoke("expand");
            var groups = new List<IReadOnlyCollection<string>>(parsed.Groups.Count);
            foreach (var group in parsed.Groups)
            {
                ct.ThrowIfCancellationRequested();
                var groupTerms = ExpandGroup(group, ct, out bool hardMiss);
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
            // rowid is the line's position in result order (catalog order); line_position maps
            // it back to the line id, so the ids come out in that order.
            cmd.CommandText = "SELECT p.line_id FROM line_fts JOIN line_position p ON p.position = line_fts.rowid " +
                              "WHERE line_fts MATCH $q ORDER BY line_fts.rowid";
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
                                               PreparedQueryGroups prepared, bool withPlacement, CancellationToken ct)
        {
            if (ids.Count == 0) yield break;
            // A corpus may return a batch in its own order (by id); results go out in the
            // index's order, which is the order of ids.
            // A placed corpus gives each line's book / position / TOC entry in the same read,
            // unless the caller reads placement itself (withPlacement: false).
            var byId = new Dictionary<int, (string Content, string Title, FtsDocumentPlacement Placement)>(ids.Count);
            if (withPlacement && corpus is IFtsPlacedCorpus placed)
                foreach (var (lineId, content, placement) in placed.FetchPlacedDocuments(ids))
                    byId[lineId] = (content, "", placement);
            else
                foreach (var (lineId, content, bookTitle) in corpus.FetchDocuments(ids))
                    byId[lineId] = (content, bookTitle, default);
            foreach (int lineId in ids)
            {
                ct.ThrowIfCancellationRequested();
                if (byId.TryGetValue(lineId, out var row))
                    yield return new SearchResult(lineId, row.Title, row.Content, plan.MatchedGroups, plan.OriginalGroupCount, prepared, row.Placement);
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
        /// FuzzyExpander.Expand semantics: every term within edit distance d (a swap is one edit) (d clamped
        /// to 1..MaxAllowedDistance), capped at FuzzyExpander.MaxExpandedTerms keeping the
        /// closest (distance, then shorter, then higher document count, then ordinal).
        /// </summary>
        List<string> ExpandFuzzy(string pattern, int maxDistance)
        {
            if (maxDistance > FuzzyExpander.MaxAllowedDistance) maxDistance = FuzzyExpander.MaxAllowedDistance;
            if (maxDistance < 1) maxDistance = 1;
            // Union over the dictionaries: a term's distance is the same everywhere; its
            // document count is the sum, as FtsLib sums counts across segments.
            var found = new Dictionary<string, (int Distance, long Count)>(StringComparer.Ordinal);
            foreach (var d in _dictionaries)
            {
                if (d.Fm == null) continue;
                var hits = d.Fm.FindFuzzy(pattern, maxDistance);
                var distanceOf = new Dictionary<int, int>(hits.Count);
                foreach (var h in hits) distanceOf[h.RowId] = h.Distance;
                foreach (var t in TermsByRowId(d, distanceOf.Keys))
                {
                    found.TryGetValue(t.Term, out var prior);
                    found[t.Term] = (distanceOf[t.RowId], prior.Count + t.Count);
                }
            }
            var terms = new List<(string Term, int Distance, long Count)>(found.Count);
            foreach (var kv in found) terms.Add((kv.Key, kv.Value.Distance, kv.Value.Count));

            int cap = FuzzyExpander.MaxExpandedTerms;
            if (cap > 0 && terms.Count > cap)
            {
                terms.Sort((a, b) =>
                {
                    int c = a.Distance.CompareTo(b.Distance);
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
            foreach (var d in _dictionaries)
            {
                if (d.Fm == null) continue;
                foreach (var t in TermsByRowId(d, d.Fm.FindRowIds(core, suffixOnly)))
                    if (t.Term.Length - core.Length <= budget)
                    {
                        kept.TryGetValue(t.Term, out long prior);
                        kept[t.Term] = prior + t.Count;
                    }
            }

            int cap = HebrewWildcardExpander.MaxExpandedTerms;
            if (cap <= 0 || kept.Count <= cap) return new List<string>(kept.Keys);
            var entries = new List<KeyValuePair<string, long>>(kept);
            entries.Sort(HebrewWildcardExpander.CompareShortestFirst);
            return entries.GetRange(0, cap).ConvertAll(e => e.Key);
        }

        static List<(int RowId, string Term, long Count)> TermsByRowId(TermDictionary d, IEnumerable<int> rowIds)
        {
            var result = new List<(int, string, long)>();
            var batch = new List<int>(5000);
            foreach (int id in rowIds)
            {
                batch.Add(id);
                if (batch.Count == 5000) { ReadTerms(d, batch, result); batch.Clear(); }
            }
            if (batch.Count > 0) ReadTerms(d, batch, result);
            return result;
        }

        static void ReadTerms(TermDictionary d, List<int> batch, List<(int, string, long)> into)
        {
            d.TermsByRowId.CommandText = "SELECT rowid, term, count FROM term_index WHERE rowid IN (" + string.Join(",", batch) + ")";
            using var r = d.TermsByRowId.ExecuteReader();
            while (r.Read()) into.Add(((int)r.GetInt64(0), r.GetString(1), r.GetInt64(2)));
        }

        /// <summary>
        /// Expands one query group to literal terms with the production expanders over the
        /// term dictionaries (ported from the former segment engine's ExpandGroup).
        /// </summary>
        List<string> ExpandGroup(QueryGroup group, CancellationToken ct, out bool hardMiss)
        {
            hardMiss = false;

            if (group.IsSingle && !group.IsWildcard && !group.IsFuzzy && !group.IsGrammar && !group.IsUnindexed)
            {
                var single = new List<string> { group.Pattern };
                if (group.Alternatives[0].ExpandKetiv)
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

                // A literal too long to be indexed has no term and is no constraint: alone, its
                // group is skipped but still counts in OriginalGroupCount (it keeps its position).
                if (alt.IsUnindexed) continue;

                // Spelling variants come from the word's own '^' marker.
                bool ketiv = alt.ExpandKetiv;

                List<string> expanded;
                if (alt.IsFuzzy)
                {
                    anyRealConstraint = true;
                    expanded = ExpandFuzzy(alt.Pattern, alt.FuzzyDistance);
                }
                else if (alt.IsWildcard && alt.IsGrammar
                         && TrySplitStarCore(alt.Pattern, out string mixedLead, out string mixedCore, out _))
                {
                    // "*word%" / "%word*": '*' on one side, grammar on the other.
                    bool grammarIsPrefix = mixedLead.Length == 0;
                    expanded = ExpandStarGrammar(mixedCore, grammarIsPrefix, ref anyRealConstraint);
                    if (ketiv)
                        foreach (var variant in KetivExpander.Expand(mixedCore))
                            expanded.AddRange(ExpandStarGrammar(variant, grammarIsPrefix, ref anyRealConstraint));
                }
                else if (alt.IsWildcard)
                {
                    expanded = ExpandWildcard(alt.Pattern, ref anyRealConstraint);
                    // Spelling variants apply to the literal core too, or *word* would match
                    // FEWER lines than plain word (which gets its ketiv variants).
                    if (ketiv && TrySplitStarCore(alt.Pattern, out string lead, out string core, out string trail))
                        foreach (var variant in KetivExpander.Expand(core))
                            expanded.AddRange(ExpandWildcard(lead + variant + trail, ref anyRealConstraint));
                }
                else if (alt.IsGrammar)
                {
                    anyRealConstraint = true;
                    expanded = GrammarExpander.Expand(alt.Pattern, alt.GrammarExpandPrefixes, alt.GrammarExpandSuffixes, _termSegments);
                    // Same as wildcards: %word% must not lose the variants plain word gets.
                    if (ketiv)
                        foreach (var variant in KetivExpander.Expand(alt.Pattern))
                            expanded.AddRange(GrammarExpander.Expand(variant, alt.GrammarExpandPrefixes, alt.GrammarExpandSuffixes, _termSegments));
                }
                else
                {
                    anyRealConstraint = true;
                    expanded = new List<string> { alt.Pattern };
                    if (ketiv)
                        foreach (var variant in KetivExpander.Expand(alt.Pattern)) expanded.Add(variant);
                }

                foreach (var term in expanded)
                    if (seen.Add(term)) list.Add(term);
            }

            if (list.Count == 0 && anyRealConstraint) hardMiss = true;
            return list;
        }

        List<string> ExpandWildcard(string pattern, ref bool anyRealConstraint)
        {
            if (IsFmWildcard(pattern, out string core, out bool suffixOnly))
            {
                anyRealConstraint = true;
                return ExpandWildcardWithFm(core, suffixOnly);
            }
            var expanded = HebrewWildcardExpander.Expand(pattern, _termSegments, out bool rejected);
            if (!rejected) anyRealConstraint = true;
            return expanded;
        }

        /// <summary>
        /// "%word*" (<paramref name="grammarIsPrefix"/>) or "*word%": one '*' scan per grammatical
        /// form of the '%' side ("form*" per prefix form, "*form" per suffix form), so every term
        /// is exact and no unfiltered expansion can run into the cap.
        /// </summary>
        List<string> ExpandStarGrammar(string word, bool grammarIsPrefix, ref bool anyRealConstraint)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            var forms = grammarIsPrefix ? GrammarExpander.PrefixForms(word) : GrammarExpander.SuffixForms(word);
            foreach (var form in forms)
                foreach (var t in ExpandWildcard(grammarIsPrefix ? form + "*" : "*" + form, ref anyRealConstraint))
                    found.Add(t);
            return new List<string>(found);
        }

        /// <summary>
        /// Splits a wildcard whose only wildcards are leading/trailing '*' runs (no '?', no
        /// inner '*') into those runs and its literal core — the shape ketiv can vary.
        /// </summary>
        static bool TrySplitStarCore(string pattern, out string lead, out string core, out string trail)
        {
            lead = core = trail = null;
            if (pattern.IndexOf('?') >= 0) return false;
            int start = 0, end = pattern.Length;
            while (start < end && pattern[start] == '*') start++;
            while (end > start && pattern[end - 1] == '*') end--;
            core = pattern.Substring(start, end - start);
            if (core.Length == 0 || core.IndexOf('*') >= 0) return false;
            lead = pattern.Substring(0, start);
            trail = pattern.Substring(end);
            return true;
        }

        static string AlternativeKey(SubPattern alt)
        {
            if (alt.IsUnindexed) return "u:" + alt.Pattern;
            if (alt.IsFuzzy)    return "f" + alt.FuzzyDistance + ":" + alt.Pattern;
            if (alt.IsWildcard)
                return "w" + (alt.GrammarExpandPrefixes ? "p" : "") + (alt.GrammarExpandSuffixes ? "s" : "") + ":" + alt.Pattern;
            if (alt.IsGrammar)
                return "g" + (alt.GrammarExpandPrefixes ? "p" : "") + (alt.GrammarExpandSuffixes ? "s" : "") + ":" + alt.Pattern;
            return "l:" + alt.Pattern;
        }

        public void Dispose()
        {
            foreach (var d in _dictionaries) d.Dispose();
            _lines?.Dispose();
        }
    }
}
#endif
