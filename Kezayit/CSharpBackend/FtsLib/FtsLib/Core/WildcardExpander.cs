using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace FtsLib.Core
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
    ///   MaxWildcardChars (3): each '*' in the pattern may match at most 3 characters.
    ///   After the DB query, any expanded term where the wildcard portion exceeds
    ///   this limit is discarded.  Data from the full index shows that genuine
    ///   Hebrew/Aramaic morphological prefixes and suffixes are 1–3 chars; anything
    ///   longer is a compound run-on (e.g. "תלמודתורה", "במלכיישראל") not an affix.
    ///   For infix patterns (*abc*) the budget is MaxWildcardChars per side (×2 total)
    ///   since both a prefix and a suffix can be present simultaneously.
    ///
    ///   MaxOptionalChars (4): a pattern may contain at most this many '?' operators.
    ///   Patterns with more are rejected to cap the 2^N combinatorial expansion.
    /// </summary>
    internal static class WildcardExpander
    {
        /// <summary>
        /// Minimum number of non-wildcard characters a pattern must contain.
        /// Patterns shorter than this are rejected before hitting the DB.
        /// </summary>
        public const int MinAnchorLength = 2;

        /// <summary>
        /// Maximum number of characters each '*' wildcard may match in an expanded term.
        /// Grounded in the actual index data and Hebrew/Aramaic morphology:
        ///   length 1–2: single-letter prefixes (ב, ו, ה, כ, ל, מ, ש) and two-letter
        ///               stacks (וב, וה, דב, etc.) — always real morphological forms.
        ///   length 3:   three-letter stacks (ובה, דמב, וכש, etc.) — still real.
        ///   length 4+:  compound run-ons (דכשה, במלכי, תלמוד+word) — not affixes.
        /// Cap at 3 keeps all genuine prefixes/suffixes and rejects compound noise.
        /// </summary>
        public const int MaxWildcardChars = 3;

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
        public static List<string> Expand(string pattern, IReadOnlyList<SegmentHandle> segments)
        {
            bool hasOptional = pattern.IndexOf('?') >= 0;
            bool hasStar     = pattern.IndexOf('*') >= 0;

            if (!hasOptional)
                return ExpandStar(pattern, segments);   // fast path — original behaviour

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
                    expanded = ExpandStar(sub, segments);
                else
                    expanded = LookupLiteral(sub, segments);

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
        public static List<string> ExpandStar(string pattern, IReadOnlyList<SegmentHandle> segments)
        {
            int anchorLen = AnchorLength(pattern);

            // Reject anchor-too-short patterns (includes bare "*" and "*" with 1 char).
            if (anchorLen < MinAnchorLength)
                return new List<string>();

            string likePattern = ToLikePattern(pattern);
            var    raw         = new HashSet<string>(StringComparer.Ordinal);

            foreach (var seg in segments)
            {
                using (var cmd = seg.Conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT term FROM term_index WHERE term LIKE @p ESCAPE '\\'";
                    cmd.Parameters.Add("@p", System.Data.DbType.String).Value = likePattern;

                    using (var reader = cmd.ExecuteReader())
                        while (reader.Read())
                            raw.Add(reader.GetString(0));
                }
            }

            // Determine the wildcard budget.
            // Prefix (abc*) or suffix (*abc): one wildcard slot → max MaxWildcardChars total.
            // Infix (*abc*): two wildcard slots → max MaxWildcardChars per side, so
            //   the total extra chars allowed is MaxWildcardChars * 2.
            bool hasLeadingStar  = pattern.StartsWith("*");
            bool hasTrailingStar = pattern.EndsWith("*");
            bool isInfix         = hasLeadingStar && hasTrailingStar;
            int  maxExtra        = isInfix ? MaxWildcardChars * 2 : MaxWildcardChars;

            var results = new List<string>(raw.Count);
            foreach (var term in raw)
            {
                if (term.Length - anchorLen <= maxExtra)
                    results.Add(term);
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
        /// </summary>
        private static List<string> LookupLiteral(string term, IReadOnlyList<SegmentHandle> segments)
        {
            if (AnchorLength(term) < MinAnchorLength)
                return new List<string>();

            foreach (var seg in segments)
            {
                using (var cmd = seg.Conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT 1 FROM term_index WHERE term = @t LIMIT 1";
                    cmd.Parameters.Add("@t", System.Data.DbType.String).Value = term;
                    var scalar = cmd.ExecuteScalar();
                    if (scalar != null)
                        return new List<string> { term };
                }
            }
            return new List<string>();
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
