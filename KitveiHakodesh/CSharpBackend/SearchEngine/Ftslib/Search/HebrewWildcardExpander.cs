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
    ///
    /// Pattern rules for '*':
    ///   שלו*   → prefix  → LIKE 'שלו%'
    ///   *לום   → suffix  → LIKE '%לום'
    ///   *לו*   → infix   → LIKE '%לו%'
    ///
    /// Expansion limits (enforced before/after the DB query):
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

        // ── Public entry point ────────────────────────────────────────

        /// <summary>
        /// Expands a pattern that may contain '*' wildcards.
        ///
        /// Returns an empty list when the anchor is too short or nothing survives the filter.
        /// </summary>
        public static List<string> Expand(string pattern, IReadOnlyList<SegmentHandle> segments)
        {
            return ExpandStar(pattern, segments);
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

        // ── Pattern translation ───────────────────────────────────────

        /// <summary>
        /// Converts a user wildcard pattern (using '*') to a SQLite LIKE pattern
        /// (using '%'). Literal '%' and '_' in the input are escaped with '\'.
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
        /// Returns the pattern with all '*' characters removed — used as the
        /// fallback literal when expansion yields no results.
        /// </summary>
        public static string StripWildcard(string pattern)
            => pattern.Replace("*", string.Empty);

        // ── Helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Returns the number of non-wildcard ('*') characters in <paramref name="pattern"/>.
        /// </summary>
        internal static int AnchorLength(string pattern)
        {
            int n = 0;
            foreach (char c in pattern)
                if (c != '*') n++;
            return n;
        }
    }
}
