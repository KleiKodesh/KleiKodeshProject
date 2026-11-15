using System.Collections.Generic;

namespace FtsLib.Search
{
    /// <summary>
    /// Placeholder spellings of the divine name: a query word in one spelling of the
    /// tetragrammaton also searches the others. The full spelling יהוה
    /// and the placeholders יקוק and ידוד are one
    /// class; a whole word only, after up to two prefix letters
    /// (ו ב כ ל מ ש ה). Always on, for every literal
    /// word (also inside a quoted phrase). Port of Otzaria's smart lexical rule
    /// (otzaria_search_engine 5babbce, smart_query.rs), made symmetric; same table as the
    /// Rust engine's hebrew-fts divine_names.rs (SeforimdbSerachEngine issue #39).
    /// The terms here are normalised (no marks), so no marks are carried.
    /// </summary>
    internal static class DivineNames
    {
        const string Prefixes = "ובכלמשה";
        // The letter repeated in the 2nd and 4th place of the name.
        static readonly char[] Class = { 'ה', 'ק', 'ד' };

        /// <summary>The other spellings of <paramref name="word"/>, the word itself left
        /// out; empty when it is no spelling of the name.</summary>
        public static List<string> Alternatives(string word)
        {
            var result = new List<string>();
            int n = word.Length;
            if (n < 4 || n > 6) return result;
            for (int i = 0; i < n - 4; i++)
                if (Prefixes.IndexOf(word[i]) < 0) return result;
            char x = word[n - 3];
            if (word[n - 4] != 'י' || word[n - 2] != 'ו' || word[n - 1] != x
                || System.Array.IndexOf(Class, x) < 0)
                return result;
            string prefix = word.Substring(0, n - 4);
            foreach (char y in Class)
                if (y != x) result.Add(prefix + "י" + y + "ו" + y);
            return result;
        }
    }
}
