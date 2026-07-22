namespace KitveiHakodeshService.Catalog;

/// <summary>
/// The catalog TOC search normalization pipeline — applied IDENTICALLY at index time
/// and query time (the whole point: both sides meet at the same tokens).
///
/// Pipeline, in this exact order:
///   1. Canonical normalization (token-based) — variant spellings and abbreviations map
///      to one or more canonical tokens (שו"ע → שולחן; the Shulchan Aruch section
///      abbreviations או"ח / חו"מ / יו"ד / אבהע"ז expand to their full two-word names).
///      This MUST run before punctuation stripping: the abbreviations contain a quote,
///      and once stripped they could no longer be recognized.
///   2. Talmud page (amud) normalization — a token following דף that ends with the
///      amud mark expands: "דף יד." → "דף יד עמוד א", "דף יד:" → "דף יד עמוד ב".
///      This too MUST run before punctuation stripping (the mark IS the information).
///   3. Strip all non-word characters (anything that is not a letter or digit).
///   4. Tokenization (whitespace-separated; empty tokens dropped).
/// </summary>
public static class CatalogTocTextRules
{
    /// <summary>Non-abbreviation spelling normalizations (כתיב חסר → מלא): a bare word
    /// that the DB and queries may spell either way, folded to one canonical spelling so
    /// index and query meet. Kept tiny and separate from the abbreviation map — these
    /// carry no quote mark and are not acronyms.</summary>
    private static readonly Dictionary<string, string> Spelling = new(StringComparer.Ordinal)
    {
        ["שלחן"] = "שולחן",
    };

    /// <summary>The abbreviation map (generated from Catalog/catalog_abbreviations.json).
    /// Key = a typed abbreviation in one quote flavour (single word like שו"ע, or a
    /// multi-word phrase like משנה תורה / שו"ע הגר"ז); value = alternatives, each an
    /// ordered word list that is AND-matched. More than one alternative = an ambiguous
    /// abbreviation that OR-expands (only used on the QUERY side — see
    /// <see cref="TokenizeQuery"/>).</summary>
    private static readonly Dictionary<string, string[][]> Abbrev = CatalogAbbreviations.Map;

    /// <summary>Largest key word-count in <see cref="Abbrev"/> — the lookahead window
    /// for greedy multi-word matching. Computed once from the generated map.</summary>
    private static readonly int MaxKeyWords = ComputeMaxKeyWords();

    private static int ComputeMaxKeyWords()
    {
        int max = 1;
        foreach (var key in Abbrev.Keys)
        {
            int words = 1;
            foreach (char c in key) if (c == ' ') words++;
            if (words > max) max = words;
        }
        return max;
    }

    /// <summary>
    /// Run the full pipeline on a text (a document's search text, or the order-rule
    /// reference) and return its tokens. This is the INDEX-side / plain tokenizer: an
    /// abbreviation expands to its FIRST alternative's words (indexed text is real book
    /// titles that virtually never contain abbreviations, and the first alternative is
    /// the canonical reading). The query side uses <see cref="TokenizeQuery"/>, which
    /// preserves all alternatives for OR-expansion. Lowercases (Hebrew is unaffected;
    /// Latin becomes uniform).
    /// </summary>
    public static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var raws = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < raws.Length; )
        {
            if (TryMatchAbbrev(raws, i, out var alts, out int consumed))
            {
                // Abbreviation words are already clean Hebrew (no punctuation) and never
                // carry a daf/amud mark, so they bypass the amud and strip steps. The
                // index/plain tokenizer takes the first (canonical) alternative.
                tokens.AddRange(alts[0]);
                i += consumed;
                continue;
            }

            EmitNormalizedToken(raws[i], tokens);
            i++;
        }
        return tokens;
    }

    /// <summary>
    /// The QUERY-side tokenizer. Identical to <see cref="Tokenize"/> except an
    /// abbreviation is emitted as an <see cref="QueryToken"/> carrying ALL its
    /// alternatives, so an ambiguous abbreviation (מג"א → מגן אברהם / מגיני ארץ) can be
    /// OR-expanded by the query builder. Plain words become single-alternative,
    /// single-word tokens.
    /// </summary>
    public static List<QueryToken> TokenizeQuery(string text)
    {
        var result = new List<QueryToken>();
        // A scratch list reused for the daf/amud lookback: the amud rule keys off the
        // PREVIOUS emitted plain token being "דף".
        var flat = new List<string>();
        var raws = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < raws.Length; )
        {
            if (TryMatchAbbrev(raws, i, out var alts, out int consumed))
            {
                result.Add(new QueryToken(alts));
                // An abbreviation's expansion is never "דף", so the amud lookback below
                // simply sees a non-"דף" previous token.
                flat.Add(alts[0].Length > 0 ? alts[0][^1] : "");
                i += consumed;
                continue;
            }

            int before = flat.Count;
            EmitNormalizedToken(raws[i], flat);
            for (int j = before; j < flat.Count; j++)
                result.Add(new QueryToken(flat[j]));
            i++;
        }
        return result;
    }

    /// <summary>Greedy longest-match abbreviation lookup starting at raw token
    /// <paramref name="start"/>. Tries the longest window first (up to
    /// <see cref="MaxKeyWords"/>), so a multi-word key (משנה תורה, שו"ע הגר"ז) wins over
    /// its first word alone. Each window is tried as typed, edge-trimmed (so "(שו"ע)"
    /// still maps), and — if the window's FIRST word carries a leading ה — with that ה
    /// stripped too (so a key like "יד החזקה" also matches when typed "היד החזקה"; ה is
    /// the definite article and only ever attaches to the leading word of a phrase, so
    /// only that position is tried). On a hit, <paramref name="consumed"/> is the number
    /// of raw tokens the key spans.</summary>
    private static bool TryMatchAbbrev(string[] raws, int start, out string[][] alts, out int consumed)
    {
        int maxWindow = Math.Min(MaxKeyWords, raws.Length - start);
        for (int w = maxWindow; w >= 1; w--)
        {
            string candidate = w == 1 ? raws[start] : string.Join(' ', raws, start, w);
            if (Abbrev.TryGetValue(candidate, out alts!))
            {
                consumed = w;
                return true;
            }
            string trimmed = TrimEdgeNonWord(candidate);
            if (trimmed.Length > 0 && trimmed.Length != candidate.Length && Abbrev.TryGetValue(trimmed, out alts!))
            {
                consumed = w;
                return true;
            }

            string? heStripped = StripHePrefix(raws[start]);
            if (heStripped is not null)
            {
                string heCandidate = w == 1 ? heStripped : heStripped + " " + string.Join(' ', raws, start + 1, w - 1);
                if (Abbrev.TryGetValue(heCandidate, out alts!))
                {
                    consumed = w;
                    return true;
                }
                string heTrimmed = TrimEdgeNonWord(heCandidate);
                if (heTrimmed.Length > 0 && heTrimmed.Length != heCandidate.Length && Abbrev.TryGetValue(heTrimmed, out alts!))
                {
                    consumed = w;
                    return true;
                }
            }
        }
        alts = null!;
        consumed = 0;
        return false;
    }

    /// <summary>The amud + strip + spelling-fold steps for a single non-abbreviation
    /// token, appending the resulting token(s) to <paramref name="tokens"/>.</summary>
    private static void EmitNormalizedToken(string raw, List<string> tokens)
    {
        string tok = raw;

        // Amud normalization — right after a דף token, a trailing "." means עמוד א and a
        // trailing ":" means עמוד ב. Applies to any דף TOC (and to queries typed the same
        // way), and must see the mark before stripping.
        string? amud = null;
        if (tokens.Count > 0 && tokens[^1] == "דף" && tok.Length > 1)
        {
            if (tok.EndsWith('.')) amud = "א";
            else if (tok.EndsWith(':')) amud = "ב";
            if (amud is not null) tok = tok[..^1];
        }

        // Strip all non-word characters, then fold known spelling variants.
        var sb = new System.Text.StringBuilder(tok.Length);
        foreach (char c in tok)
            if (char.IsLetter(c) || char.IsNumber(c))
                sb.Append(char.ToLowerInvariant(c));
        if (sb.Length > 0)
        {
            string clean = sb.ToString();
            tokens.Add(Spelling.TryGetValue(clean, out var folded) ? folded : clean);
        }
        else
        {
            amud = null; // the mark stood alone — nothing to attach an amud to
        }

        if (amud is not null)
        {
            tokens.Add("עמוד");
            tokens.Add(amud);
        }
    }

    /// <summary>One query token: either a plain word, or an abbreviation carrying its
    /// alternatives. Each alternative is an ordered word list that is AND-matched; more
    /// than one alternative means the abbreviation OR-expands.</summary>
    public readonly struct QueryToken
    {
        /// <summary>Alternatives; each is an ordered AND-group of words. A plain word is
        /// a single alternative of a single word.</summary>
        public readonly string[][] Alternatives;

        /// <summary>True when the token is a plain single word (exactly one alternative
        /// of one word) — the common case, matched like any indexed term.</summary>
        public bool IsPlain => Alternatives.Length == 1 && Alternatives[0].Length == 1;

        /// <summary>The plain word (valid only when <see cref="IsPlain"/>).</summary>
        public string Word => Alternatives[0][0];

        public QueryToken(string word) => Alternatives = [[word]];
        public QueryToken(string[][] alternatives) => Alternatives = alternatives;
    }

    private static string TrimEdgeNonWord(string s)
    {
        int start = 0, end = s.Length - 1;
        while (start <= end && !char.IsLetter(s[start]) && !char.IsNumber(s[start])) start++;
        while (end >= start && !char.IsLetter(s[end]) && !char.IsNumber(s[end])) end--;
        return start == 0 && end == s.Length - 1 ? s : s[start..(end + 1)];
    }

    // ── Talmud daf entry parsing (TOC restructuring) ─────────────────────────────

    /// <summary>
    /// Recognize a Talmud page TOC entry text: "דף X." (amud א) or "דף X:" (amud ב).
    /// Returns the core ("דף X") and which amud the mark denotes. Used by the indexer
    /// to restructure such entries into a parent daf with עמוד א / עמוד ב children —
    /// putting the amud level BELOW the daf level so the injected amud tokens (א/ב)
    /// can never outrank a real daf/siman/verse match for those letters.
    /// </summary>
    public static bool TryParseDafText(string text, out string core, out bool isAmudB)
    {
        core = "";
        isAmudB = false;
        string t = text.Trim();
        if (t.Length < 4 || !t.StartsWith("דף ", StringComparison.Ordinal)) return false;
        char last = t[^1];
        if (last != '.' && last != ':') return false;
        core = t[..^1].TrimEnd();
        if (core.Length <= 3) return false; // "דף" alone — no page designation
        isAmudB = last == ':';
        return true;
    }

    // ── ה-prefix stripping (ported from the Vue frontend's bookCatalogSearchNormalizer) ──
    // Symmetric definite-article folding so הרמבן and רמבן resolve to the same match:
    // the index adds the stripped form as an extra token at the same position, and the
    // query side probes both the typed word and its stripped form (see WordClause).

    /// <summary>Strip the definite article ה from the start of a word, if present. Returns
    /// null when the word doesn't start with ה or the remainder is under 2 characters (too
    /// short to strip meaningfully). Does not check whether the remainder is a real word —
    /// the index carries both forms, so הלכה stays findable as הלכה; a query for לכה
    /// simply finds nothing, which is correct.</summary>
    public static string? StripHePrefix(string word)
    {
        if (word.Length == 0 || word[0] != 'ה') return null;
        string remainder = word[1..];
        return remainder.Length >= 2 ? remainder : null;
    }

    // ── חסר/מלא skeleton decomposition (ported from decomposeHebrewWord) ─────────
    // A mid-word י/ו (between two Hebrew letters) is a vowel letter (mater lectionis) that
    // may or may not be written out — נידה vs נדה. Stripping it to a bare consonantal
    // skeleton plus a positional vowel-set lets two spellings of the same word compare
    // equal when one vowel-set is a subset of the other (see AreSkeletonVariants).

    private static bool IsHebrewLetter(char c) => c is >= 'א' and <= 'ת';

    /// <summary>A word decomposed into its consonantal skeleton and the set of
    /// "position:letter" keys for the mid-word mater-lectionis (י/ו) characters removed.</summary>
    public readonly struct DecomposedWord
    {
        public readonly string Skeleton;
        public readonly HashSet<string> VowelSet;
        public DecomposedWord(string skeleton, HashSet<string> vowelSet) { Skeleton = skeleton; VowelSet = vowelSet; }
    }

    /// <summary>Decompose a word into its skeleton (מלא letters י/ו stripped when they sit
    /// strictly between two Hebrew consonants) and the stripped positions' letters.</summary>
    public static DecomposedWord DecomposeSkeleton(string word)
    {
        var skeleton = new System.Text.StringBuilder(word.Length);
        var vowelSet = new HashSet<string>();
        int skelIndex = 0;
        for (int i = 0; i < word.Length; i++)
        {
            char c = word[i];
            bool isMater = (c == 'י' || c == 'ו')
                && i > 0 && i < word.Length - 1
                && IsHebrewLetter(word[i - 1]) && IsHebrewLetter(word[i + 1]);
            if (isMater)
            {
                vowelSet.Add(skelIndex + ":" + c);
            }
            else
            {
                skeleton.Append(c);
                skelIndex++;
            }
        }
        return new DecomposedWord(skeleton.ToString(), vowelSet);
    }

    /// <summary>True when two decomposed words are חסר/מלא spelling variants: same
    /// consonantal skeleton, and one's vowel-set is a subset of the other's.</summary>
    public static bool AreSkeletonVariants(DecomposedWord a, DecomposedWord b)
    {
        if (a.Skeleton != b.Skeleton) return false;
        var (smaller, larger) = a.VowelSet.Count <= b.VowelSet.Count ? (a.VowelSet, b.VowelSet) : (b.VowelSet, a.VowelSet);
        foreach (var key in smaller)
            if (!larger.Contains(key)) return false;
        return true;
    }

    // ── Title-variant root stripping (display-path construction only) ────────────
    // A book whose root TOC entry just repeats the book title would render as
    // "בראשית / בראשית / פרק א" — the root is dropped from the path like the catalog
    // page always did. This affects the DISPLAY PATH construction, never result
    // ordering. On the current DB the rule strips ~90% of roots (6,943 exact title
    // duplicates + fuzzy variants); genuinely structural roots (חלק א, ספר names)
    // are kept. No hardcoded book-id exception list — ids shift between DB versions.

    private const double TitleRatio = 0.6;

    private static List<string> NormTitleWords(string s)
    {
        // Strip quote-like chars before comparing: Hebrew geresh/gershayim, ASCII
        // quote AND apostrophe (titles write ש"ע as ש''ע / ר' with plain apostrophes),
        // curly quotes, maqaf, hyphen.
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s)
            if (c is not ('"' or '\'' or '״' or '׳' or '“' or '”' or '‘' or '’' or '־' or '-'))
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
