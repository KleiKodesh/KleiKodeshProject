using System;
using System.Collections.Generic;

namespace KitveiHakodesh.Core.SeforimDbCatalog
{
    /// <summary>
    /// The two invariants the retired code generator used to enforce on the abbreviation table,
    /// kept now that the table is hand-edited.
    ///
    /// Both failures are SILENT at runtime — a bad entry does not throw, it just never matches,
    /// or quietly shadows another — which is exactly why they were a build-time guard before and
    /// need to stay checkable after.
    ///
    ///   1. NO QUOTE GLYPH IN A KEY. The lookup strips quote glyphs from a candidate before
    ///      probing, so a key that still contains one can never be matched by anything.
    ///   2. NO COLLIDING KEYS. Two source keys that normalise to the same lookup key mean one
    ///      of them silently wins and the other's alternatives are unreachable.
    ///
    /// It also rejects an entry with no alternatives, which the generator could not produce
    /// but a hand edit can: such a key matches and then expands to nothing.
    ///
    /// Pure data: no database, no files, no environment. Call it from any harness.
    /// </summary>
    public static class SeforimDbCatalogAbbreviationsCheck
    {
        /// <summary>
        /// Checks the shipped table and returns one message per problem — empty when it is
        /// sound. Returning rather than throwing so a caller can report every problem at once
        /// instead of only the first.
        /// </summary>
        public static IReadOnlyList<string> Verify()
        {
            var problems = new List<string>();
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var entry in SeforimDbCatalogTextNormalizer.AbbreviationEntries)
            {
                string key = entry.Key;

                string stripped = SeforimDbCatalogTextNormalizer.StripQuoteGlyphs(key);
                if (!string.Equals(stripped, key, StringComparison.Ordinal))
                {
                    problems.Add(
                        "key contains a quote glyph and can never be matched: '" + key
                        + "' (strips to '" + stripped + "')");
                }

                if (seen.TryGetValue(stripped, out var first))
                {
                    problems.Add(
                        "keys collide after stripping: '" + first + "' and '" + key
                        + "' both reduce to '" + stripped + "', so one shadows the other");
                }
                else
                {
                    seen[stripped] = key;
                }

                if (entry.Value == null || entry.Value.Length == 0)
                    problems.Add("key has no alternatives: '" + key + "'");
            }

            return problems;
        }
    }
}
