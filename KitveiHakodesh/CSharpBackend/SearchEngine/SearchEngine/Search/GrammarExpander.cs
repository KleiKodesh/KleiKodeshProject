using System;
using System.Collections.Generic;

namespace SearchEngine.Search
{
    /// <summary>
    /// Expands a Hebrew word by prepending grammatical prefixes (קידומות דקדוקיות),
    /// appending grammatical suffixes (סיומות דקדוקיות), or both.
    ///
    /// All candidate forms are returned without index verification — Lucene handles
    /// non-existent terms gracefully (they simply match nothing).
    ///
    /// Ported from FtsLib.Search.GrammarExpander (candidate-generation logic only).
    ///
    /// Syntax (parsed by <see cref="HebrewQueryBuilder"/>):
    ///   %word   — prefix expansion only
    ///   word%   — suffix expansion only
    ///   %word%  — full expansion (prefix + suffix + prefix+suffix)
    /// </summary>
    internal static class GrammarExpander
    {
        // ── Prefix table ──────────────────────────────────────────────
        // All valid stacked Hebrew grammatical prefixes, ordered longest-first.
        private static readonly string[] Prefixes =
        {
            // quad
            "\u05D5\u05DC\u05DB\u05E9",   // ולכש
            "\u05D5\u05DE\u05D4\u05E9",   // ומהש
            "\u05D5\u05D1\u05E9\u05D4",   // ובשה
            // triple
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
            // double
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
            // single
            "\u05D5",                     // ו
            "\u05D1",                     // ב
            "\u05DB",                     // כ
            "\u05DC",                     // ל
            "\u05DE",                     // מ
            "\u05E9",                     // ש
            "\u05D4",                     // ה
        };

        // ── Suffix table ──────────────────────────────────────────────
        private static readonly string[] Suffixes =
        {
            // length 4
            "\u05D9\u05DB\u05DD",         // יכם
            "\u05D9\u05DB\u05DF",         // יכן
            "\u05D9\u05D4\u05DD",         // יהם
            "\u05D9\u05D4\u05DF",         // יהן
            // length 3
            "\u05D9\u05DE\u05D5",         // ימו
            "\u05D9\u05E0\u05D5",         // ינו
            "\u05D5\u05EA\u05D9",         // ותי
            "\u05D5\u05EA\u05DD",         // ותם
            "\u05D5\u05EA\u05DF",         // ותן
            "\u05D9\u05D5\u05EA",         // יות
            "\u05DB\u05DD",               // כם
            "\u05DB\u05DF",               // כן
            "\u05D4\u05DD",               // הם
            "\u05D4\u05DF",               // הן
            "\u05EA\u05DD",               // תם
            "\u05EA\u05DF",               // תן
            // length 2
            "\u05D9\u05DD",               // ים
            "\u05D5\u05EA",               // ות
            "\u05D9\u05DF",               // ין
            "\u05D9\u05EA",               // ית
            "\u05D5\u05DF",               // ון
            "\u05D9\u05D5",               // יו
            "\u05D9\u05D4",               // יה
            "\u05D9\u05DA",               // יך
            "\u05D9\u05D9",               // יי
            "\u05EA\u05D9",               // תי
            "\u05E0\u05D5",               // נו
            // length 1
            "\u05D9",                     // י
            "\u05DA",                     // ך
            "\u05D5",                     // ו
            "\u05D4",                     // ה
            "\u05EA",                     // ת
        };

        // ── Public entry point ────────────────────────────────────────

        /// <summary>
        /// Returns all candidate forms for <paramref name="word"/> based on the
        /// expansion flags. Always includes the bare word itself.
        /// No index verification — non-existent terms are harmless in Lucene.
        /// </summary>
        public static HashSet<string> Expand(
            string word,
            bool   expandPrefixes,
            bool   expandSuffixes)
        {
            var candidates = new HashSet<string>(StringComparer.Ordinal) { word };

            if (!expandPrefixes && !expandSuffixes)
                return candidates;

            if (expandPrefixes)
                foreach (var prefix in Prefixes)
                    candidates.Add(prefix + word);

            if (expandSuffixes)
                foreach (var suffix in Suffixes)
                    candidates.Add(word + suffix);

            if (expandPrefixes && expandSuffixes)
                foreach (var prefix in Prefixes)
                    foreach (var suffix in Suffixes)
                        candidates.Add(prefix + word + suffix);

            return candidates;
        }
    }
}
