using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace FtsLib.Core
{
    /// <summary>
    /// Expands a wildcard pattern (containing '*') into the set of concrete terms
    /// that exist in the index by querying each segment's <c>term_index</c> table.
    ///
    /// Pattern rules:
    ///   שלו*   → prefix  → LIKE 'שלו%'
    ///   *לום   → suffix  → LIKE '%לום'
    ///   *לו*   → infix   → LIKE '%לו%'
    ///
    /// Multiple '*' characters are collapsed: the text between the first and last
    /// '*' is treated as the infix anchor.
    ///
    /// If no terms match, the caller should fall back to using the stripped literal
    /// (pattern with '*' removed) as a plain AND term.
    /// </summary>
    internal static class WildcardExpander
    {
        /// <summary>
        /// Queries every segment for terms matching <paramref name="pattern"/> and
        /// returns the union as a deduplicated list.
        ///
        /// Returns an empty list when nothing matches — the caller decides the fallback.
        /// </summary>
        public static List<string> Expand(string pattern, IReadOnlyList<SegmentHandle> segments)
        {
            string likePattern = ToLikePattern(pattern);
            var    results     = new HashSet<string>(StringComparer.Ordinal);

            foreach (var seg in segments)
            {
                using (var cmd = seg.Conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT term FROM term_index WHERE term LIKE @p ESCAPE '\\'";
                    cmd.Parameters.Add("@p", System.Data.DbType.String).Value = likePattern;

                    using (var reader = cmd.ExecuteReader())
                        while (reader.Read())
                            results.Add(reader.GetString(0));
                }
            }

            return new List<string>(results);
        }

        // ── Pattern translation ───────────────────────────────────────

        /// <summary>
        /// Converts a user wildcard pattern (using '*') to a SQLite LIKE pattern
        /// (using '%'). Literal '%' and '_' in the input are escaped with '\'.
        /// </summary>
        internal static string ToLikePattern(string pattern)
        {
            // Escape any literal SQLite LIKE special chars in the non-wildcard parts
            // then replace '*' with '%'.
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
    }
}
