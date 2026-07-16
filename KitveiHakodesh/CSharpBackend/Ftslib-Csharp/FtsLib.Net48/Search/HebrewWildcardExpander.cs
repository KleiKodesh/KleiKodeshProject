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
    ///   to tens of thousands of terms.  The caller receives an empty list and
    ///   should skip the group rather than killing the whole query.
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
        {
            bool hasOptional = pattern.IndexOf('?') >= 0;
            bool hasStar     = pattern.IndexOf('*') >= 0;

            if (!hasOptional)
                return ExpandStar(pattern, segments, cache);   // fast path — original behaviour

            // Count '?' operators (after normalising away no-op ones).
            // We count positions where '?' has a real preceding letter.
            int optCount = CountEffectiveOptionals(pattern);
            if (optCount > MaxOptionalChars)
                return new List<string>();

            // Generate all sub-patterns by including/excluding each optional char.
            var subPatterns = new HashSet<string>(StringComparer.Ordinal);
            ExpandOptionals(pattern, 0, new System.Text.StringBuilder(pattern.Length), subPatterns);

            // Collect results across all sub-patterns, deduplicating.
            var seen    = new HashSet<string>(StringComparer.Ordinal);
            var results = new List<string>();

            foreach (var sub in subPatterns)
            {
                List<string> expanded;
                if (sub.IndexOf('*') >= 0)
                    expanded = ExpandStar(sub, segments, cache);
                else
                    expanded = LookupLiteral(sub, segments, cache);

                foreach (var term in expanded)
                    if (seen.Add(term))
                        results.Add(term);
            }

            return results;
        }

        // ── '*'-only expansion (original logic) ───────────────────────

        /// <summary>
        /// Queries every segment for terms matching <paramref name="pattern"/>
        /// (which must contain only '*' wildcards, no '?'), then filters out any
        /// result where the wildcard portion exceeds <see cref="MaxWildcardChars"/>.
        ///
        /// Returns an empty list when the anchor is too short or nothing survives
        /// the filter.
        /// </summary>
        public static List<string> ExpandStar(string pattern, IReadOnlyList<SegmentHandle> segments,
                                              TermChunkCache cache = null)
        {
            int anchorLen = AnchorLength(pattern);

            // Reject anchor-too-short patterns (includes bare "*" and "*" with 1 char).
            if (anchorLen < MinAnchorLength)
                return new List<string>();

            // F06: pure prefix patterns (abc*) whose semantics a binary range can
            // reproduce exactly are served via idx_term instead of a full LIKE
            // table scan (LIKE never uses the index — measured 75-120x slower).
            // Ineligible patterns keep the LIKE path, byte-identical to before.
            bool useRange = TryGetPrefixRange(pattern, out string rangeLo, out string rangeHi);

            string likePattern = useRange ? null : ToLikePattern(pattern);
            var    raw         = new HashSet<string>(StringComparer.Ordinal);

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
                            string term = reader.GetString(0);
                            raw.Add(term);
                            cache?.Add(term, new SegmentChunk(seg,
                                reader.GetInt64(1),   // skip_offset
                                reader.GetInt32(2),   // skip_count
                                reader.GetInt64(3),   // offset
                                reader.GetInt32(4),   // length
                                reader.GetInt32(5))); // count
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

            var results = new List<string>(raw.Count);
            foreach (var term in raw)
            {
                int extra = term.Length - anchorLen; // total wildcard chars matched

                if (hasLeadingStar && hasTrailingStar)
                {
                    // Infix: we don't know the exact split, but the total extra chars
                    // cannot exceed the combined budget.
                    if (extra <= MaxPrefixWildcardChars + MaxSuffixWildcardChars)
                        results.Add(term);
                }
                else if (hasLeadingStar)
                {
                    // Suffix wildcard (*abc): extra chars are all prefix.
                    if (extra <= MaxPrefixWildcardChars)
                        results.Add(term);
                }
                else
                {
                    // Prefix wildcard (abc*): extra chars are all suffix.
                    if (extra <= MaxSuffixWildcardChars)
                        results.Add(term);
                }
            }

            return results;
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
            // Determine whether the preceding character in `current` is a real letter
            // (not a wildcard) that can be made optional.
            bool hasOptionalTarget =
                current.Length > 0 &&
                current[current.Length - 1] != '*';
            // (A preceding '?' was already consumed as a letter or dropped, so the
            //  last char in `current` at this point is always a real letter or '*'.)

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
        /// Looks up an exact term across all segments.
        /// Returns a single-element list if found, empty list otherwise.
        /// Probes EVERY segment (no early exit) so the cached chunk list is
        /// complete — a cache entry missing a segment would silently drop that
        /// segment's postings at resolve time.
        /// </summary>
        private static List<string> LookupLiteral(string term, IReadOnlyList<SegmentHandle> segments,
                                                  TermChunkCache cache = null)
        {
            if (AnchorLength(term) < MinAnchorLength)
                return new List<string>();

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
                            cache?.Add(term, new SegmentChunk(seg,
                                reader.GetInt64(0),
                                reader.GetInt32(1),
                                reader.GetInt64(2),
                                reader.GetInt32(3),
                                reader.GetInt32(4)));
                        }
                }
            }
            return found ? new List<string> { term } : new List<string>();
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
