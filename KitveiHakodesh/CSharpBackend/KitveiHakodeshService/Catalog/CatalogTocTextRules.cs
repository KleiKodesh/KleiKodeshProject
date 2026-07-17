namespace KitveiHakodeshService.Catalog;

/// <summary>
/// Text normalization + tokenization rules for the catalog TOC search index.
///
/// Every rule here is a C# port of the frontend's manual catalog-search pipeline so the
/// Lucene index matches at least everything the manual way matches:
///   - Normalize            → normalizeText.ts normalize() (lowercase, strip quotes)
///   - ApplyBookVariants    → bookCatalogSearchNormalizer.ts normalizeBookPath()
///   - TokenizeSegmentText  → segmentSearchTree.ts tokenizeSegmentText() (keeps Talmud
///                            page suffixes "י." / "י:" as single tokens)
///   - StripHePrefix        → bookCatalogSearchNormalizer.ts stripHePrefix()
///   - Skeleton             → bookCatalogSearchNormalizer.ts decomposeHebrewWord() skeleton
///   - IsTitleVariant       → tocSearchUtils.ts isTitleVariant() (root stripping)
///
/// Index docs store an EXPANDED token set (raw + normalized + ה-stripped + חסר/מלא
/// skeleton forms) so a prefix query over the plain query word forms is a superset of
/// the manual matcher's exact/prefix/ה/skeleton tiers.
/// </summary>
public static class CatalogTocTextRules
{
    // ── normalize() ──────────────────────────────────────────────────────────────

    /// <summary>Lowercase + strip quote characters (" ' ״ ׳) — normalizeText.ts.</summary>
    public static string Normalize(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s.ToLowerInvariant())
            if (c is not ('"' or '\'' or '״' or '׳')) sb.Append(c);
        return sb.ToString();
    }

    // ── normalizeBookPath() ─────────────────────────────────────────────────────

    /// <summary>Canonicalize known title variants. Input must already be Normalize()'d
    /// (quotes stripped), matching the frontend call order — so the שו"ע regex reduces
    /// to a plain שוע replacement here.</summary>
    public static string ApplyBookVariants(string normalized) =>
        normalized.Replace("שוע", "שלחן ערוך").Replace("שולחן", "שלחן");

    // ── tokenizeSegmentText() ───────────────────────────────────────────────────

    /// <summary>
    /// Tokenize one text into lowercase tokens: letters/digits are word chars; '.' and ':'
    /// stay attached to a preceding word char (Talmud "דף י." / "דף י:"); everything else
    /// separates. Port of segmentSearchTree.ts tokenizeSegmentText().
    /// </summary>
    public static List<string> TokenizeSegmentText(string text)
    {
        string s = text.ToLowerInvariant();
        var tokens = new List<string>();
        var token = new System.Text.StringBuilder();
        bool prevIsWord = false;

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];

            // Surrogate pair — classify the full code point; never a "previous word char"
            // for a following '.'/':' (mirrors the JS regex behavior).
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                int cp = char.ConvertToUtf32(c, s[i + 1]);
                var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(cp);
                if (IsLetterOrNumberCategory(cat)) { token.Append(c).Append(s[i + 1]); }
                else if (token.Length > 0) { tokens.Add(token.ToString()); token.Clear(); }
                prevIsWord = false;
                i++;
                continue;
            }

            bool isWord = char.IsLetter(c) || char.IsNumber(c);
            if (isWord || ((c == '.' || c == ':') && prevIsWord))
            {
                token.Append(c);
            }
            else if (token.Length > 0)
            {
                tokens.Add(token.ToString());
                token.Clear();
            }
            prevIsWord = isWord;
        }
        if (token.Length > 0) tokens.Add(token.ToString());
        return tokens;
    }

    private static bool IsLetterOrNumberCategory(System.Globalization.UnicodeCategory cat) => cat
        is System.Globalization.UnicodeCategory.UppercaseLetter
        or System.Globalization.UnicodeCategory.LowercaseLetter
        or System.Globalization.UnicodeCategory.TitlecaseLetter
        or System.Globalization.UnicodeCategory.ModifierLetter
        or System.Globalization.UnicodeCategory.OtherLetter
        or System.Globalization.UnicodeCategory.DecimalDigitNumber
        or System.Globalization.UnicodeCategory.LetterNumber
        or System.Globalization.UnicodeCategory.OtherNumber;

    // ── stripHePrefix() ─────────────────────────────────────────────────────────

    /// <summary>Strip a leading ה when the remainder keeps ≥ 2 chars, else null.</summary>
    public static string? StripHePrefix(string word) =>
        word.Length >= 3 && word[0] == 'ה' ? word[1..] : null;

    // ── decomposeHebrewWord() skeleton ──────────────────────────────────────────

    private static bool IsHebrewLetter(char c) => c is >= 'א' and <= 'ת';

    /// <summary>Consonantal skeleton: drop yod/vav that sit between two Hebrew letters
    /// (matres lectionis). נידה → נדה, שבועות → שבעת.</summary>
    public static string Skeleton(string word)
    {
        var sb = new System.Text.StringBuilder(word.Length);
        for (int i = 0; i < word.Length; i++)
        {
            char c = word[i];
            bool midVowel = (c == 'י' || c == 'ו')
                && i > 0 && i < word.Length - 1
                && IsHebrewLetter(word[i - 1]) && IsHebrewLetter(word[i + 1]);
            if (!midVowel) sb.Append(c);
        }
        return sb.ToString();
    }

    // ── Query words ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A raw user query → the flat normalized word list the manual pipeline searches with:
    /// normalizeBookPath(normalize(query)) split on whitespace, then TOC-tokenized so
    /// punctuation splits the same way indexed path tokens did.
    /// </summary>
    public static List<string> QueryWords(string rawQuery)
    {
        string normalized = ApplyBookVariants(Normalize(rawQuery.Trim()));
        var words = new List<string>();
        foreach (var part in normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            words.AddRange(TokenizeSegmentText(part));
        return words;
    }

    // ── Index-side token expansion ──────────────────────────────────────────────

    /// <summary>
    /// Expand one text into every token form the index should answer for:
    /// raw segment tokens (quotes split — ל"ב → ל, ב), normalized tokens (quotes
    /// stripped — ל"ב → לב), book-variant tokens (שוע → שלחן ערוך), plus the
    /// ה-stripped and skeleton form of each. Prefix search over the plain query word
    /// forms then covers all manual matcher tiers.
    /// </summary>
    public static void ExpandIndexTokens(string text, HashSet<string> into)
    {
        string normalized = Normalize(text);
        AddTokenForms(TokenizeSegmentText(text), into);
        AddTokenForms(TokenizeSegmentText(normalized), into);
        AddTokenForms(TokenizeSegmentText(ApplyBookVariants(normalized)), into);
    }

    private static void AddTokenForms(List<string> tokens, HashSet<string> into)
    {
        foreach (var tok in tokens)
        {
            into.Add(tok);
            if (StripHePrefix(tok) is { } stripped) into.Add(stripped);
            string skel = Skeleton(tok);
            if (skel.Length > 0 && skel != tok) into.Add(skel);
        }
    }

    // ── Title-variant root stripping (tocSearchUtils.ts) ────────────────────────

    /// <summary>Book ids whose root TOC entry is a known title variant the fuzzy rule
    /// misses — same list as tocSearchUtils.ts FORCE_STRIP_BOOK_IDS.</summary>
    public static readonly HashSet<int> ForceStripBookIds = [6036, 6037, 6042, 6043, 6044];

    private const double TitleRatio = 0.6;

    private static List<string> NormTitleWords(string s)
    {
        // TITLE_STRIP_RE: Hebrew geresh/gershayim, ASCII/curly quotes, maqaf, hyphen
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s)
            if (c is not ('"' or '״' or '׳' or '“' or '”' or '‘' or '’' or '־' or '-'))
                sb.Append(c);
        var words = new List<string>();
        foreach (var w in sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            words.Add(w);
        return words;
    }

    /// <summary>Fuzzy title comparison for root stripping: shorter word set must be a
    /// subset of the longer with ≥ 0.6 length ratio. Port of isTitleVariant().</summary>
    public static bool IsTitleVariant(string bookTitle, string rootText)
    {
        var bt = NormTitleWords(bookTitle);
        var rt = NormTitleWords(rootText);
        if (bt.Count == 0 || rt.Count == 0) return false;
        var (shorter, longer) = bt.Count <= rt.Count ? (bt, rt) : (rt, bt);
        if (shorter.Count < longer.Count * TitleRatio) return false;
        foreach (var w in shorter)
            if (!longer.Contains(w)) return false;
        return true;
    }
}
