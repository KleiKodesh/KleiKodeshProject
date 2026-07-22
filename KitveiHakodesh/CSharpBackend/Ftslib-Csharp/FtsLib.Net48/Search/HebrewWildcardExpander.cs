using FtsLib.Indexing;
using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace FtsLib.Search
{
    /// <summary>
    /// Expands wildcard patterns into the set of concrete terms that exist in the
    /// index by querying each segment's <c>term_index</c> table.
    ///
    /// Supported wildcards:
    ///   '*'  — matches zero or more characters (prefix / suffix / infix)
    ///   '?'  — makes the immediately preceding character optional
    ///          e.g. שלו?ם → {שלום, שלם}  (with or without ו)
    ///          A '?' with no preceding letter (at position 0, or after another '?'
    ///          or after '*') is silently dropped.
    ///
    /// Pattern rules for '*':
    ///   שלו*   → prefix  → LIKE 'שלו%'
    ///   *לום   → suffix  → LIKE '%לום'
    ///   *לו*   → infix   → LIKE '%לו%'
    ///
    /// Expansion limits (both enforced before/after the DB query):
    ///
    ///   MinAnchorLength (2): the non-wildcard anchor must be at least 2 chars.
    ///   Patterns like "*ל" or "מ*" are rejected immediately — they would expand
    ///   to tens of thousands of terms.  Rejection is reported via the
    ///   <c>out bool rejected</c> overload of <see cref="Expand(string,IReadOnlyList{SegmentHandle},TermChunkCache,out bool)"/>:
    ///   the caller skips a REJECTED pattern's group, but treats a supported
    ///   pattern that matched nothing as an unsatisfiable constraint.
    ///
    ///   MaxPrefixWildcardChars (3) / MaxSuffixWildcardChars (4):
    ///   After the DB query, expanded terms are filtered by how many characters the
    ///   wildcard portion actually matched:
    ///     *abc  (suffix wildcard) — leading '*' capped at 3 chars (max Hebrew prefix)
    ///     abc*  (prefix wildcard) — trailing '*' capped at 4 chars (max Hebrew suffix)
    ///     *abc* (infix wildcard)  — leading capped at 3, trailing at 4 (7 total)
    ///   Research basis: Hebrew stacked prefixes max at 3 (וּמִבְּ); pronominal suffixes
    ///   max at 4 (יהֶם, יכֶם).  Anything longer is a compound run-on, not an affix.
    ///
    ///   MaxOptionalChars (4): a pattern may contain at most this many '?' operators.
    ///   Patterns with more are rejected to cap the 2^N combinatorial expansion.
    ///
    ///   MaxExpandedTerms: when a pattern still expands to more concrete terms than
    ///   this cap (broad infix patterns like *כי*), the expansion is trimmed
    ///   shortest-term-first — shorter terms sit closest to the anchor and carry the
    ///   most matches per term, so they sacrifice the fewest results. Ties prefer
    ///   the higher document count, then ordinal order (deterministic across runs
    ///   and runtimes).
    /// </summary>
    internal static class HebrewWildcardExpander
    {
        /// <summary>
        /// Minimum number of non-wildcard characters a pattern must contain.
        /// Patterns shorter than this are rejected before hitting the DB.
        /// </summary>
        public const int MinAnchorLength = 2;

        /// <summary>
        /// Maximum characters the leading '*' of a suffix wildcard (*abc) may match.
        /// Hebrew/Aramaic prefixes stack to at most 3 chars (e.g. וּמִבְּ = vav+mem+bet).
        /// </summary>
        public const int MaxPrefixWildcardChars = 3;

        /// <summary>
        /// Maximum characters the trailing '*' of a prefix wildcard (abc*) may match.
        /// Hebrew pronominal suffixes reach at most 4 chars (e.g. יהֶם, יכֶם, יהֶן).
        /// Verb conjugation suffixes top out at 3 chars (תֶּם, תֶּן), so 4 is the safe cap.
        /// </summary>
        public const int MaxSuffixWildcardChars = 4;

        /// <summary>
        /// Maximum number of '?' operators allowed in a single pattern.
        /// Caps the 2^N combinatorial expansion at 2^4 = 16 variants.
        /// </summary>
        public const int MaxOptionalChars = 4;

        /// <summary>
        /// Tuned default for <see cref="MaxExpandedTerms"/> — the chosen sweet spot.
        /// Measured with FtsLibTest capsweep on the FULL tier (2026-07-17):
        /// *כי* expands to 27,543 terms uncapped; results kept sit on a flat
        /// plateau (~87-88%) across caps 1000-3000 — 2000 is its middle, keeping
        /// 88.3% of the 2.39M matching lines — then cliff below 1000 (75% → 52%).
        /// 5000 reaches 95.9% but more than doubles the capped posting volume for
        /// a marginal recall gain. Snippet cost no longer depends on term count
        /// (the term→group map is prepared once per query — see
        /// Snippets.PreparedQueryGroups), so the cap bounds expansion-scan and
        /// posting-resolution work on pathological patterns, not snippets.
        /// Normal patterns (*יצח* = 199 terms) are untouched.
        ///
        /// Lucene reference (verified against lucene/main source): wildcard-style
        /// MultiTermQueries there are NOT term-capped — CONSTANT_SCORE_BLENDED_REWRITE
        /// enumerates every matching term, pre-merging postings with docFreq ≤ 512
        /// into one bitset and keeping only ~16 high-frequency iterators live
        /// (AbstractMultiTermQueryConstantScoreWrapper.BOOLEAN_REWRITE_TERM_COUNT_THRESHOLD).
        /// Only its SCORING boolean rewrites cap, via IndexSearcher.maxClauseCount = 1024.
        /// Our cap is therefore a deliberate recall/latency trade the caller controls,
        /// not an emulation of Lucene.
        /// </summary>
        public const int DefaultMaxExpandedTerms = 2000;

        /// <summary>
        /// Maximum number of concrete terms a single wildcard pattern may expand to.
        /// 0 = unlimited. When the expansion exceeds the cap, terms are kept
        /// shortest-first (ties: higher doc count, then ordinal).
        /// Runtime-settable so hosts and test rigs can tune it.
        /// </summary>
        public static int MaxExpandedTerms = DefaultMaxExpandedTerms;

        // ── Public entry point ────────────────────────────────────────

        /// <summary>
        /// Expands a pattern that may contain '*', '?', or both.
        ///
        /// '?' patterns are first unrolled into up to 2^N concrete sub-patterns
        /// (each with/without the optional char), then each sub-pattern is either
        /// looked up as a literal or expanded via the '*' LIKE query.
        ///
        /// Returns an empty list when the anchor is too short, the '?' count
        /// exceeds <see cref="MaxOptionalChars"/>, or nothing survives the filter.
        /// </summary>
        public static List<string> Expand(string pattern, IReadOnlyList<SegmentHandle> segments,
                                          TermChunkCache cache = null)
            => Expand(pattern, segments, cache, out _);

        /// <summary>
        /// As <see cref="Expand(string,IReadOnlyList{SegmentHandle},TermChunkCache)"/>,
        /// additionally reporting via <paramref name="rejected"/> whether the pattern
        /// was UNSUPPORTED (anchor shorter than <see cref="MinAnchorLength"/>, or more
        /// than <see cref="MaxOptionalChars"/> '?' operators) — the documented
        /// "skip this AND slot" cases — as opposed to a supported pattern that simply
        /// matched nothing, which is a real constraint the caller must treat as an
        /// unsatisfiable group (whole query returns empty), never as a slot to drop.
        /// </summary>
        public static List<string> Expand(string pattern, IReadOnlyList<SegmentHandle> segments,
                                          TermChunkCache cache, out bool rejected)
        {
            rejected = false;
            bool hasOptional = pattern.IndexOf('?') >= 0;

            if (!hasOptional)
            {
                // Fast path — original behaviour, with the short-anchor rejection
                // surfaced instead of buried inside ExpandStarCore's empty result.
                if (AnchorLength(pattern) < MinAnchorLength)
                {
                    rejected = true;
                    return new List<string>();
                }
                return ExpandStar(pattern, segments, cache);
            }

            // Count '?' operators (after normalising away no-op ones).
            // We count positions where '?' has a real preceding letter.
            int optCount = CountEffectiveOptionals(pattern);
            if (optCount > MaxOptionalChars)
            {
                rejected = true;
                return new List<string>();
            }

            // Generate all sub-patterns by including/excluding each optional char.
            var subPatterns = new HashSet<string>(StringComparer.Ordinal);
            ExpandOptionals(pattern, 0, new System.Text.StringBuilder(pattern.Length), subPatterns);

            // Collect results across all sub-patterns, deduplicating. Counts are
            // set-once: a term found via two sub-patterns comes from the same
            // term_index rows, so its total is identical either way.
            var merged = new Dictionary<string, long>(StringComparer.Ordinal);

            bool anyEligible = false;
            foreach (var sub in subPatterns)
            {
                // A variant that falls under the anchor floor is individually
                // unsupported — skip it without letting it decide the outcome.
                if (AnchorLength(sub) < MinAnchorLength) continue;
                anyEligible = true;

                if (sub.IndexOf('*') >= 0)
                {
                    foreach (var kv in ExpandStarCore(sub, segments, cache))
                        if (!merged.ContainsKey(kv.Key))
                            merged[kv.Key] = kv.Value;
                }
                else if (!merged.ContainsKey(sub) &&
                         TryLookupLiteral(sub, segments, cache, out long count))
                {
                    merged[sub] = count;
                }
            }

            // Every generated variant was under the anchor floor — the pattern as a
            // whole is unsupported, same as the '*'-only short-anchor case above.
            if (!anyEligible)
            {
                rejected = true;
                return new List<string>();
            }

            // The cap is applied once on the merged set (not per sub-pattern) so
            // shortest-first competes across all '?' variants together.
            return CapTerms(merged);
        }

        // ── '*'-only expansion (original logic) ───────────────────────

        /// <summary>
        /// Queries every segment for terms matching <paramref name="pattern"/>
        /// (which must contain only '*' wildcards, no '?'), filters out any
        /// result where the wildcard portion exceeds the affix budgets, then
        /// applies the <see cref="MaxExpandedTerms"/> shortest-first cap.
        ///
        /// Returns an empty list when the anchor is too short or nothing survives
        /// the filter.
        /// </summary>
        public static List<string> ExpandStar(string pattern, IReadOnlyList<SegmentHandle> segments,
                                              TermChunkCache cache = null)
            => CapTerms(ExpandStarCore(pattern, segments, cache));

        /// <summary>
        /// Uncapped '*' expansion. Returns each surviving term with its total
        /// document count (summed across segments) so <see cref="CapTerms"/> can
        /// break length ties in favour of the more frequent term.
        /// </summary>
        private static Dictionary<string, long> ExpandStarCore(string pattern,
                                                               IReadOnlyList<SegmentHandle> segments,
                                                               TermChunkCache cache)
        {
            int anchorLen = AnchorLength(pattern);

            // Reject anchor-too-short patterns (includes bare "*" and "*" with 1 char).
            if (anchorLen < MinAnchorLength)
                return new Dictionary<string, long>(StringComparer.Ordinal);

            // F06: pure prefix patterns (abc*) whose semantics a binary range can
            // reproduce exactly are served via idx_term instead of a full LIKE
            // table scan (LIKE never uses the index — measured 75-120x slower).
            // Ineligible patterns keep the LIKE path, byte-identical to before.
            bool useRange = TryGetPrefixRange(pattern, out string rangeLo, out string rangeHi);

            string likePattern = useRange ? null : ToLikePattern(pattern);
            var    raw         = new Dictionary<string, long>(StringComparer.Ordinal);

            // Trigram anchor for the leading-star (infix/suffix) LIKE case: the literal core with
            // no internal wildcard. When a segment has a trigram sidecar and the anchor is a
            // single ≥3-char run, we pre-filter candidate rowids from the sidecar and let SQLite
            // confirm with its OWN LIKE on just those rows (identical semantics, no full scan).
            string trgAnchor = null;
            if (!useRange)
            {
                string core = pattern.Trim('*');
                if (core.Length >= TrigramIndex.MinRun && core.IndexOf('*') < 0 && core.IndexOf('?') < 0)
                    trgAnchor = core;
            }

            foreach (var seg in segments)
            {
                using (var cmd = seg.Conn.CreateCommand())
                {
                    // The scan piggybacks the chunk metadata so resolve never has to
                    // re-fetch these rows with per-term point SELECTs (F01).
                    if (useRange)
                    {
                        cmd.CommandText =
                            "SELECT term, skip_offset, skip_count, offset, length, count " +
                            "FROM term_index WHERE term >= @lo AND term < @hi";
                        cmd.Parameters.Add("@lo", System.Data.DbType.String).Value = rangeLo;
                        cmd.Parameters.Add("@hi", System.Data.DbType.String).Value = rangeHi;
                    }
                    else if (trgAnchor != null && seg.Trigram != null &&
                             TrySetupTrigramConfirm(seg.Trigram, trgAnchor, likePattern, cmd))
                    {
                        // cmd is now a rowid-restricted LIKE confirm — falls through to the
                        // identical consume loop below. (Routing: selective candidates only;
                        // TrySetupTrigramConfirm returns false to use the full scan instead.)
                    }
                    else
                    {
                        cmd.CommandText =
                            "SELECT term, skip_offset, skip_count, offset, length, count " +
                            "FROM term_index WHERE term LIKE @p ESCAPE '\\'";
                        cmd.Parameters.Add("@p", System.Data.DbType.String).Value = likePattern;
                    }

                    using (var reader = cmd.ExecuteReader())
                        while (reader.Read())
                        {
                            string term  = reader.GetString(0);
                            int    count = reader.GetInt32(5);
                            raw.TryGetValue(term, out long total);
                            raw[term] = total + count;
                            cache?.Add(term, new SegmentChunk(seg,
                                reader.GetInt64(1),   // skip_offset
                                reader.GetInt32(2),   // skip_count
                                reader.GetInt64(3),   // offset
                                reader.GetInt32(4),   // length
                                count));              // count
                        }
                }
            }

            // Determine the wildcard budget per shape:
            //   *abc  (suffix wildcard): leading '*' may match at most MaxPrefixWildcardChars
            //   abc*  (prefix wildcard): trailing '*' may match at most MaxSuffixWildcardChars
            //   *abc* (infix wildcard):  leading capped at MaxPrefixWildcardChars,
            //                            trailing capped at MaxSuffixWildcardChars
            bool hasLeadingStar  = pattern.StartsWith("*");
            bool hasTrailingStar = pattern.EndsWith("*");

            var results = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var kv in raw)
            {
                int extra = kv.Key.Length - anchorLen; // total wildcard chars matched

                if (hasLeadingStar && hasTrailingStar)
                {
                    // Infix: we don't know the exact split, but the total extra chars
                    // cannot exceed the combined budget.
                    if (extra <= MaxPrefixWildcardChars + MaxSuffixWildcardChars)
                        results.Add(kv.Key, kv.Value);
                }
                else if (hasLeadingStar)
                {
                    // Suffix wildcard (*abc): extra chars are all prefix.
                    if (extra <= MaxPrefixWildcardChars)
                        results.Add(kv.Key, kv.Value);
                }
                else
                {
                    // Prefix wildcard (abc*): extra chars are all suffix.
                    if (extra <= MaxSuffixWildcardChars)
                        results.Add(kv.Key, kv.Value);
                }
            }

            return results;
        }

        // ── Trigram pre-filter (sidecar) ──────────────────────────────

        /// <summary>Above this many trigram candidates a full LIKE scan is comparable, so we
        /// skip the sidecar and let the scan run (the routing "not selective enough" case).</summary>
        private const int TrigramCandidateCap = 8000;

        /// <summary>
        /// Sets <paramref name="cmd"/> to a rowid-restricted LIKE confirm over the trigram
        /// candidates for <paramref name="anchor"/>, or returns false to use the full scan.
        /// Candidates are a superset of the LIKE matches (a term matching %anchor% contains all
        /// of the anchor's trigrams), and SQLite's own LIKE confirms — so results are identical.
        /// </summary>
        private static bool TrySetupTrigramConfirm(TrigramIndex.Reader tr, string anchor,
                                                   string likePattern, System.Data.SQLite.SQLiteCommand cmd)
        {
            var grams = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            TrigramIndex.AddTrigrams(anchor, grams, seen);
            if (grams.Count == 0) return false;

            int[] acc = null;
            foreach (var g in grams)
            {
                int[] l = tr.Lookup(g);
                if (l.Length == 0) { acc = new int[0]; break; }             // gram absent ⇒ no match
                acc = acc == null ? l : Inter(acc, l);
                if (acc.Length == 0) break;
            }
            if (acc == null) return false;
            if (acc.Length > TrigramCandidateCap) return false;             // not selective enough → full scan

            const string cols = "SELECT term, skip_offset, skip_count, offset, length, count FROM term_index WHERE ";
            if (acc.Length == 0) { cmd.CommandText = cols + "0"; return true; } // no candidates

            var sb = new System.Text.StringBuilder(cols, cols.Length + acc.Length * 7);
            sb.Append("rowid IN (");
            for (int i = 0; i < acc.Length; i++) { if (i > 0) sb.Append(','); sb.Append(acc[i]); }
            sb.Append(") AND term LIKE @p ESCAPE '\\'");
            cmd.CommandText = sb.ToString();
            cmd.Parameters.Add("@p", System.Data.DbType.String).Value = likePattern;
            return true;
        }

        private static int[] Inter(int[] a, int[] b)
        {
            var r = new List<int>(System.Math.Min(a.Length, b.Length)); int i = 0, j = 0;
            while (i < a.Length && j < b.Length) { int x = a[i], y = b[j]; if (x == y) { r.Add(x); i++; j++; } else if (x < y) i++; else j++; }
            return r.ToArray();
        }

        // ── Expansion cap ─────────────────────────────────────────────

        /// <summary>
        /// Applies <see cref="MaxExpandedTerms"/> to an expansion, keeping the
        /// shortest terms (ties: higher doc count, then ordinal). Under the cap
        /// the terms are returned unsorted — the intersection does not care.
        /// </summary>
        private static List<string> CapTerms(Dictionary<string, long> terms)
        {
            int cap = MaxExpandedTerms;
            if (cap <= 0 || terms.Count <= cap)
                return new List<string>(terms.Keys);

            var entries = new List<KeyValuePair<string, long>>(terms);
            entries.Sort(CompareShortestFirst);

            var kept = new List<string>(cap);
            for (int i = 0; i < cap; i++)
                kept.Add(entries[i].Key);
            return kept;
        }

        /// <summary>
        /// Orders expansion candidates: shortest term first, then higher document
        /// count, then ordinal — fully deterministic across runs and runtimes.
        /// </summary>
        internal static int CompareShortestFirst(KeyValuePair<string, long> a,
                                                 KeyValuePair<string, long> b)
        {
            int c = a.Key.Length.CompareTo(b.Key.Length);
            if (c != 0) return c;
            c = b.Value.CompareTo(a.Value); // higher count first
            if (c != 0) return c;
            return string.CompareOrdinal(a.Key, b.Key);
        }

        // ── '?' expansion helpers ─────────────────────────────────────

        /// <summary>
        /// Recursively generates all sub-patterns by including or excluding each
        /// optional character (the char immediately before a '?').
        ///
        /// A '?' is a no-op (silently dropped) when:
        ///   - it appears at position 0 (nothing before it), or
        ///   - the character immediately before it is another '?' or a '*'
        ///     (wildcards cannot themselves be made optional).
        /// </summary>
        private static void ExpandOptionals(
            string                      pattern,
            int                         pos,
            System.Text.StringBuilder   current,
            HashSet<string>             results)
        {
            if (pos == pattern.Length)
            {
                results.Add(current.ToString());
                return;
            }

            char c = pattern[pos];

            if (c != '?')
            {
                current.Append(c);
                ExpandOptionals(pattern, pos + 1, current, results);
                current.Length--;
                return;
            }

            // c == '?'
            // A '?' is effective only when the PATTERN character immediately before
            // it is a real letter — not another '?' (a run of '?'s collapses to one
            // toggle) and not '*' (wildcards cannot be made optional). This mirrors
            // CountEffectiveOptionals exactly. Deciding from the BUILT buffer here
            // (as this used to) made a '?' after an effective '?' toggle the SAME
            // letter again — producing sub-patterns the user never wrote (e.g.
            // "אבגד??" also generated bare "אב") and branching once per raw '?',
            // which bypassed the MaxOptionalChars gate (it counts a '?' run as one)
            // and let a long pattern with many '?'s explode into millions of
            // recursive calls, hanging the search thread.
            bool hasOptionalTarget =
                pos > 0 &&
                pattern[pos - 1] != '?' &&
                pattern[pos - 1] != '*' &&
                current.Length > 0;

            if (!hasOptionalTarget)
            {
                // No-op '?' — just skip it and continue.
                ExpandOptionals(pattern, pos + 1, current, results);
                return;
            }

            // Branch 1: include the optional char (do nothing — it's already in `current`).
            ExpandOptionals(pattern, pos + 1, current, results);

            // Branch 2: exclude the optional char (remove the last char from `current`).
            char saved = current[current.Length - 1];
            current.Length--;
            ExpandOptionals(pattern, pos + 1, current, results);
            current.Append(saved); // restore for the caller
        }

        /// <summary>
        /// Counts the number of '?' operators that have a real (non-wildcard)
        /// preceding character — i.e. the ones that will actually produce two branches.
        /// </summary>
        private static int CountEffectiveOptionals(string pattern)
        {
            int count = 0;
            for (int i = 0; i < pattern.Length; i++)
            {
                if (pattern[i] != '?') continue;
                if (i == 0) continue;                    // no preceding char
                char prev = pattern[i - 1];
                if (prev == '*' || prev == '?') continue; // wildcard before '?' is a no-op
                count++;
            }
            return count;
        }

        // ── Literal lookup ────────────────────────────────────────────

        /// <summary>
        /// Looks up an exact term across all segments, reporting its total document
        /// count. Probes EVERY segment (no early exit) so the cached chunk list is
        /// complete — a cache entry missing a segment would silently drop that
        /// segment's postings at resolve time.
        /// </summary>
        private static bool TryLookupLiteral(string term, IReadOnlyList<SegmentHandle> segments,
                                             TermChunkCache cache, out long totalCount)
        {
            totalCount = 0;

            if (AnchorLength(term) < MinAnchorLength)
                return false;

            bool found = false;
            foreach (var seg in segments)
            {
                using (var cmd = seg.Conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT skip_offset, skip_count, offset, length, count " +
                        "FROM term_index WHERE term = @t";
                    cmd.Parameters.Add("@t", System.Data.DbType.String).Value = term;
                    using (var reader = cmd.ExecuteReader())
                        if (reader.Read())
                        {
                            found = true;
                            int count = reader.GetInt32(4);
                            totalCount += count;
                            cache?.Add(term, new SegmentChunk(seg,
                                reader.GetInt64(0),
                                reader.GetInt32(1),
                                reader.GetInt64(2),
                                reader.GetInt32(3),
                                count));
                        }
                }
            }
            return found;
        }

        // ── Prefix range scan (F06) ───────────────────────────────────

        /// <summary>
        /// Decides whether <paramref name="pattern"/> can be served by an indexed
        /// binary range scan (<c>term &gt;= lo AND term &lt; hi</c>) with results
        /// EXACTLY equal to the LIKE scan it replaces, and computes the bounds.
        ///
        /// Eligible: a pure prefix pattern — exactly one '*', at the very end,
        /// no '?' — whose anchor contains no ASCII letters. The ASCII-letter
        /// restriction is what guarantees equality: SQLite's LIKE is
        /// case-insensitive for A-Z/a-z only, and a case-insensitive prefix cannot
        /// be expressed as one contiguous BINARY-collation range. Hebrew, digits,
        /// and punctuation have no case folding, so for them
        /// <c>LIKE 'anchor%'</c> ≡ <c>term &gt;= anchor AND term &lt; successor</c>
        /// under SQLite's default BINARY (UTF-8 memcmp) collation, because UTF-8
        /// byte order equals code-point order.
        ///
        /// The upper bound is the anchor with its last char incremented. If the
        /// incremented char would land in the surrogate range (invalid to encode)
        /// or overflow, the pattern is declared ineligible and LIKE is used.
        /// </summary>
        internal static bool TryGetPrefixRange(string pattern, out string lo, out string hi)
        {
            lo = null;
            hi = null;

            // Exactly one '*', at the end, and no '?' (ExpandStar only ever sees
            // '?'-free patterns, but guard anyway so the helper is safe on its own).
            int star = pattern.IndexOf('*');
            if (star < 0 || star != pattern.Length - 1) return false;
            if (pattern.IndexOf('?') >= 0) return false;

            string anchor = pattern.Substring(0, pattern.Length - 1);
            if (anchor.Length == 0) return false;

            foreach (char c in anchor)
            {
                // ASCII letters would engage LIKE's case folding — not range-safe.
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) return false;
                // LIKE treats '%' and '_' as (escaped) literals; range comparison
                // treats them literally too, so they are fine to allow.
            }

            char last = anchor[anchor.Length - 1];
            char next = (char)(last + 1);
            // Successor must be a directly encodable BMP code point: no overflow,
            // no landing in the surrogate block, and the anchor itself must not
            // end mid surrogate pair.
            if (last >= 0xFFFF) return false;
            if (last >= 0xD800 && last <= 0xDFFF) return false;
            if (next >= 0xD800 && next <= 0xDFFF) return false;

            lo = anchor;
            hi = anchor.Substring(0, anchor.Length - 1) + next;
            return true;
        }

        // ── Pattern translation ───────────────────────────────────────

        /// <summary>
        /// Converts a user wildcard pattern (using '*') to a SQLite LIKE pattern
        /// (using '%'). Literal '%' and '_' in the input are escaped with '\'.
        /// '?' characters must have been removed before calling this method.
        /// </summary>
        internal static string ToLikePattern(string pattern)
        {
            var sb = new System.Text.StringBuilder(pattern.Length + 4);
            foreach (char c in pattern)
            {
                switch (c)
                {
                    case '%':  sb.Append("\\%"); break;
                    case '_':  sb.Append("\\_"); break;
                    case '*':  sb.Append('%');   break;
                    default:   sb.Append(c);     break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Returns the pattern with all '*' and '?' characters removed — used as the
        /// fallback literal when expansion yields no results.
        /// </summary>
        public static string StripWildcard(string pattern)
            => pattern.Replace("*", string.Empty).Replace("?", string.Empty);

        // ── Helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Returns the number of non-wildcard ('*' or '?') characters in
        /// <paramref name="pattern"/>.
        /// </summary>
        internal static int AnchorLength(string pattern)
        {
            int n = 0;
            foreach (char c in pattern)
                if (c != '*' && c != '?') n++;
            return n;
        }
    }
}
