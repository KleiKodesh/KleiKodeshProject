namespace FtsLib.Search
{
    /// <summary>
    /// The per-word markers of a query token: '^' (spelling variants) among its leading signs
    /// ("*^word" = "^*word"); '+' (inflections) and '=' (synonyms) among its trailing signs
    /// ("word%+", "word=", "word+="; a leading '+' / '=' is read too). Shared by the query parser
    /// and the host's related-forms rewrite, so both read a token the same way.
    ///
    /// Why '=': it is not a query operator, not an index-transparent char (so it never glues to a
    /// word), and not used by any other marker; '~' is fuzzy, '%' grammar, '*'/'?' wildcards,
    /// '|' OR, '^' spelling variants, '+' inflections.
    /// </summary>
    public static class WordMarkers
    {
        /// <summary>Splits <paramref name="token"/> into its markers and the token without them.</summary>
        public static void Split(string token, out bool ketiv, out bool inflections, out bool synonyms, out string rest)
        {
            int signs = 0;
            while (signs < token.Length && "*%^+=".IndexOf(token[signs]) >= 0) signs++;
            int trailing = TrailingSignsStart(token, signs);
            string leading = token.Substring(0, signs), trail = token.Substring(trailing);
            ketiv = leading.IndexOf('^') >= 0;
            inflections = leading.IndexOf('+') >= 0 || trail.IndexOf('+') >= 0;
            synonyms = leading.IndexOf('=') >= 0 || trail.IndexOf('=') >= 0;
            rest = leading.Replace("^", "").Replace("+", "").Replace("=", "")
                + token.Substring(signs, trailing - signs)
                + trail.Replace("+", "").Replace("=", "");
        }

        /// <summary>Where a token's trailing signs ('*', '%', '+', '=', '~' or '~N') start, not
        /// before <paramref name="min"/>.</summary>
        static int TrailingSignsStart(string token, int min)
        {
            int i = token.Length;
            while (i > min)
            {
                char c = token[i - 1];
                if (c == '*' || c == '%' || c == '+' || c == '=' || c == '~') i--;
                else if (c >= '1' && c <= '9' && i - 2 >= min && token[i - 2] == '~') i -= 2;
                else break;
            }
            return i;
        }
    }
}
