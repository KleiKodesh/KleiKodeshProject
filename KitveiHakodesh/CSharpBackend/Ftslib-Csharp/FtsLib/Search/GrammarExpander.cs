using FtsLib.Indexing;
using System;
using System.Collections.Generic;

namespace FtsLib.Search
{
    /// <summary>
    /// Expands a Hebrew word by prepending grammatical prefixes (קידומות דקדוקיות),
    /// appending grammatical suffixes (סיומות דקדוקיות), or both, then verifying
    /// each candidate against the segment term_index via exact lookup.
    ///
    /// Syntax (parsed by <see cref="QueryParser"/>):
    ///   %word   — prefix expansion only:  {prefix+word} ∪ {word}
    ///   word%   — suffix expansion only:  {word+suffix} ∪ {word}
    ///   %word%  — full expansion:         {prefix+word} ∪ {word+suffix} ∪ {prefix+word+suffix} ∪ {word}
    ///
    /// Interaction with other operators:
    ///   '?' — compatible: optional-char variants are generated first (by
    ///         <see cref="HebrewWildcardExpander"/>), then grammar expansion is
    ///         applied to each variant independently.
    ///   '*' — overrides '%': a token that contains '*' is treated as a plain
    ///         wildcard; any '%' markers are ignored.
    ///
    /// Lookup strategy (same as <see cref="KetivExpander"/>):
    ///   All candidate forms are generated in C#, then each is verified with an
    ///   exact SELECT against term_index.  Only forms that actually exist in the
    ///   index are returned.
    /// </summary>
    internal static class GrammarExpander
    {
        // ── Prefix table ──────────────────────────────────────────────
        //
        // All valid stacked Hebrew grammatical prefixes, ordered longest-first so
        // that the list is self-documenting and easy to extend.
        //
        // Sources: standard Biblical/Rabbinic Hebrew grammar.
        //   Single:   ו ב כ ל מ ש ה
        //   Double:   וב וכ ול ום וש וה מה שה לה כש מש לכ
        //   Triple:   ובש ולש וכש ומש ולכ ומה ושה ולה וכה מהש לכש
        //   Quad:     ולכש ומהש ובשה
        //
        // The definite article ה assimilates into the following prefix in speech
        // but is written separately in unvocalised text, so all ה-combinations
        // are included.
        private static readonly string[] Prefixes =
        {
            // ── quad ──────────────────────────────────────────────────
            "\u05D5\u05DC\u05DB\u05E9",   // ולכש
            "\u05D5\u05DE\u05D4\u05E9",   // ומהש
            "\u05D5\u05D1\u05E9\u05D4",   // ובשה
            // ── triple ────────────────────────────────────────────────
            "\u05D5\u05D1\u05E9",         // ובש
            "\u05D5\u05DC\u05E9",         // ולש
            "\u05D5\u05DB\u05E9",         // וכש
            "\u05D5\u05DE\u05E9",         // ומש
            "\u05D5\u05DC\u05DB",         // ולכ
            "\u05D5\u05DE\u05D4",         // ומה
            "\u05D5\u05E9\u05D4",         // ושה
            "\u05D5\u05DC\u05D4",         // ולה
            "\u05D5\u05DB\u05D4",         // וכה
            "\u05DE\u05D4\u05E9",         // מהש
            "\u05DC\u05DB\u05E9",         // לכש
            // ── double ────────────────────────────────────────────────
            "\u05D5\u05D1",               // וב
            "\u05D5\u05DB",               // וכ
            "\u05D5\u05DC",               // ול
            "\u05D5\u05DE",               // ום
            "\u05D5\u05E9",               // וש
            "\u05D5\u05D4",               // וה
            "\u05DE\u05D4",               // מה
            "\u05E9\u05D4",               // שה
            "\u05DC\u05D4",               // לה
            "\u05DB\u05E9",               // כש
            "\u05DE\u05E9",               // מש
            "\u05DC\u05DB",               // לכ
            // ── single ────────────────────────────────────────────────
            "\u05D5",                     // ו
            "\u05D1",                     // ב
            "\u05DB",                     // כ
            "\u05DC",                     // ל
            "\u05DE",                     // מ
            "\u05E9",                     // ש
            "\u05D4",                     // ה
        };

        // ── Suffix table ──────────────────────────────────────────────
        //
        // Pronominal suffixes, plural/gender endings, and construct forms.
        // Ordered longest-first.
        //
        // Pronominal (possessive / object):
        //   י  ך  ו  ה  נו  כם  כן  הם  הן  ךָ  יו  יה  יך  יכם  יכן  יהם  יהן
        // Plural / gender:
        //   ים  ות  ין  ות  ת
        // Plural + pronominal (common combinations):
        //   יהם  יהן  יכם  יכן  ינו  יו  יה  יך  יי
        // Verbal (common conjugation endings):
        //   תי  תה  תם  תן  נו  תי  ו  ה  י  ת
        private static readonly string[] Suffixes =
        {
            // ── length 4 ─────────────────────────────────────────────
            "\u05D9\u05DB\u05DD",         // יכם  (plural pronominal 2mp)
            "\u05D9\u05DB\u05DF",         // יכן  (plural pronominal 2fp)
            "\u05D9\u05D4\u05DD",         // יהם  (plural pronominal 3mp)
            "\u05D9\u05D4\u05DF",         // יהן  (plural pronominal 3fp)
            // ── length 3 ─────────────────────────────────────────────
            "\u05D9\u05DE\u05D5",         // ימו  (archaic 3mp)
            "\u05D9\u05E0\u05D5",         // ינו  (plural pronominal 1cp)
            "\u05D5\u05EA\u05D9",         // ותי  (plural + 1cs)
            "\u05D5\u05EA\u05DD",         // ותם  (plural + 2mp)
            "\u05D5\u05EA\u05DF",         // ותן  (plural + 2fp)
            "\u05D9\u05D5\u05EA",         // יות  (plural feminine)
            "\u05DB\u05DD",               // כם   (2mp pronominal) — 2 chars but listed here for ordering
            "\u05DB\u05DF",               // כן   (2fp pronominal)
            "\u05D4\u05DD",               // הם   (3mp pronominal)
            "\u05D4\u05DF",               // הן   (3fp pronominal)
            "\u05EA\u05DD",               // תם   (2mp verbal)
            "\u05EA\u05DF",               // תן   (2fp verbal)
            // ── length 2 ─────────────────────────────────────────────
            "\u05D9\u05DD",               // ים   (masculine plural)
            "\u05D5\u05EA",               // ות   (feminine plural)
            "\u05D9\u05DF",               // ין   (Aramaic / alternative plural)
            "\u05D9\u05EA",               // ית   (feminine singular / adjective)
            "\u05D5\u05DF",               // ון   (diminutive / Aramaic)
            "\u05D9\u05D5",               // יו   (3ms pronominal plural)
            "\u05D9\u05D4",               // יה   (3fs pronominal plural)
            "\u05D9\u05DA",               // יך   (2fs pronominal plural)
            "\u05D9\u05D9",               // יי   (1cs pronominal plural, poetic)
            "\u05EA\u05D9",               // תי   (1cs verbal)
            "\u05E0\u05D5",               // נו   (1cp verbal / pronominal)
            // ── length 1 ─────────────────────────────────────────────
            "\u05D9",                     // י    (1cs pronominal / construct)
            "\u05DA",                     // ך    (2fs/2ms pronominal)
            "\u05D5",                     // ו    (3ms pronominal)
            "\u05D4",                     // ה    (3fs pronominal / locale he)
            "\u05EA",                     // ת    (construct / 2fs verbal)
        };

        // ── Public entry point ────────────────────────────────────────

        /// <summary>
        /// Expands <paramref name="word"/> according to the grammar flags, verifies
        /// each candidate against the segment term_index, and returns the deduplicated
        /// list of terms that actually exist in the index.
        ///
        /// The bare <paramref name="word"/> itself is always included in the candidate
        /// set regardless of which flags are set.
        /// </summary>
        /// <param name="word">The base word (no '%' markers, already normalised).</param>
        /// <param name="expandPrefixes">Generate prefix+word forms.</param>
        /// <param name="expandSuffixes">Generate word+suffix forms.</param>
        /// <param name="segments">Live segment handles to verify against.</param>
        public static List<string> Expand(string                       word,
                                          bool                         expandPrefixes,
                                          bool                         expandSuffixes,
                                          IReadOnlyList<SegmentHandle> segments)
        {
            if (string.IsNullOrEmpty(word))
                return new List<string>();

            var candidates = BuildCandidates(word, expandPrefixes, expandSuffixes);
            return Verify(candidates, segments);
        }

        // ── Candidate generation ──────────────────────────────────────

        /// <summary>
        /// Generates all candidate forms without hitting the DB.
        /// Always includes the bare word itself.
        /// </summary>
        internal static HashSet<string> BuildCandidates(string word,
                                                         bool   expandPrefixes,
                                                         bool   expandSuffixes)
        {
            var candidates = new HashSet<string>(StringComparer.Ordinal) { word };

            if (expandPrefixes && !expandSuffixes)
            {
                // %word — prefix forms only
                foreach (var prefix in Prefixes)
                    candidates.Add(prefix + word);
            }
            else if (!expandPrefixes && expandSuffixes)
            {
                // word% — suffix forms only
                foreach (var suffix in Suffixes)
                    candidates.Add(word + suffix);
            }
            else if (expandPrefixes && expandSuffixes)
            {
                // %word% — prefix, suffix, and prefix+suffix forms
                foreach (var prefix in Prefixes)
                    candidates.Add(prefix + word);
                foreach (var suffix in Suffixes)
                    candidates.Add(word + suffix);
                foreach (var prefix in Prefixes)
                    foreach (var suffix in Suffixes)
                        candidates.Add(prefix + word + suffix);
            }

            return candidates;
        }

        // ── Index verification ────────────────────────────────────────

        /// <summary>
        /// Filters <paramref name="candidates"/> to those that exist in at least one
        /// segment's term_index, using exact equality (same strategy as KetivExpander).
        /// </summary>
        private static List<string> Verify(HashSet<string>              candidates,
                                            IReadOnlyList<SegmentHandle> segments)
        {
            // Build a working set — we remove hits as we find them so we don't
            // re-query segments for terms already confirmed.
            var remaining = new HashSet<string>(candidates, StringComparer.Ordinal);
            var results   = new List<string>(candidates.Count);

            foreach (var seg in segments)
            {
                if (remaining.Count == 0) break;

                using (var cmd = seg.Conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT term FROM term_index WHERE term = @t LIMIT 1";
                    cmd.Parameters.Add("@t", System.Data.DbType.String);

                    var confirmed = new List<string>();
                    foreach (var candidate in remaining)
                    {
                        cmd.Parameters["@t"].Value = candidate;
                        var scalar = cmd.ExecuteScalar();
                        if (scalar != null)
                            confirmed.Add(candidate);
                    }

                    foreach (var term in confirmed)
                    {
                        remaining.Remove(term);
                        results.Add(term);
                    }
                }
            }

            return results;
        }
    }
}
