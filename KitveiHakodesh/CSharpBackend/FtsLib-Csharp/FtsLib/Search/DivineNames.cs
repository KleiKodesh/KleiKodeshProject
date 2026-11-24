using System.Collections.Generic;

namespace FtsLib.Search
{
    /// <summary>
    /// Placeholder spellings of the divine names: a query word in one spelling of a name also
    /// searches its other spellings. Two classes:
    ///   - Tetragrammaton: יהוה (full), יקוק,
    ///     ידוד and the double yod יי.
    ///   - Elohim: אל[ו]Xי + suffix, X = ה (full) or ק, suffix in
    ///     <see cref="ElohimSuffixes"/>; the optional vav stays as typed.
    /// A whole word only, after up to two prefix letters (ו ב כ ל מ
    /// ש ה). Prefix + form pairs that are other real words are excluded both ways
    /// (<see cref="Excluded"/>). Always on, for every literal word (also inside a quoted
    /// phrase). Port of Otzaria's smart lexical rule (otzaria_search_engine 5babbce,
    /// smart_query.rs), made symmetric and extended from a seforim.db survey; same table as
    /// the Rust engine's hebrew-fts divine_names.rs (SeforimdbSerachEngine issue #39).
    /// The terms here are normalised (no marks), so no marks are carried.
    /// </summary>
    internal static class DivineNames
    {
        const string Prefixes = "ובכלמשה";

        // In output order.
        static readonly string[] Tetragrammaton =
            { "יהוה", "יקוק", "ידוד", "יי" };

        internal static readonly string[] ElohimSuffixes =
        {
            "", "ם", "ך", "נו", "כם",
            "הם", "ו", "ה", "הן", "כן",
        };

        // (prefix letter right before the name, spelling): another real word (survey):
        // shin + the dalet placeholder; he / kaf + the double yod.
        static readonly (char Prefix, string Form)[] Excluded =
        {
            ('ש', "ידוד"), ('ה', "יי"), ('כ', "יי"),
        };

        static bool IsExcluded(string prefix, string core)
        {
            if (prefix.Length == 0) return false;
            char p = prefix[prefix.Length - 1];
            foreach (var e in Excluded)
                if (e.Prefix == p && e.Form == core) return true;
            return false;
        }

        /// <summary>The other spellings of <paramref name="word"/>, the word itself left
        /// out; empty when it is no spelling of a name.</summary>
        public static List<string> Alternatives(string word)
        {
            for (int p = 0; p <= 2 && p <= word.Length; p++)
            {
                if (p > 0 && Prefixes.IndexOf(word[p - 1]) < 0) break;
                string prefix = word.Substring(0, p), core = word.Substring(p);
                var found = TetragrammatonAlternatives(prefix, core) ?? ElohimAlternatives(prefix, core);
                if (found != null) return found;
            }
            return new List<string>();
        }

        static List<string> TetragrammatonAlternatives(string prefix, string core)
        {
            if (System.Array.IndexOf(Tetragrammaton, core) < 0 || IsExcluded(prefix, core)) return null;
            var result = new List<string>();
            foreach (string form in Tetragrammaton)
                if (form != core && !IsExcluded(prefix, form)) result.Add(prefix + form);
            return result;
        }

        static List<string> ElohimAlternatives(string prefix, string core)
        {
            if (core.Length < 4 || core[0] != 'א' || core[1] != 'ל') return null;
            int x = core[2] == 'ו' ? 3 : 2;
            if (core.Length < x + 2 || core[x + 1] != 'י' || (core[x] != 'ה' && core[x] != 'ק'))
                return null;
            if (System.Array.IndexOf(ElohimSuffixes, core.Substring(x + 2)) < 0) return null;
            char other = core[x] == 'ה' ? 'ק' : 'ה';
            return new List<string> { prefix + core.Substring(0, x) + other + core.Substring(x + 1) };
        }
    }
}
