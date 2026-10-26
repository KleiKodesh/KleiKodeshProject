namespace KitveiHakodesh.Core.SeforimDbCatalog;

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
public static class SeforimDbCatalogTextNormalizer
{
    /// <summary>Non-abbreviation spelling normalizations (כתיב חסר → מלא): a bare word
    /// that the DB and queries may spell either way, folded to one canonical spelling so
    /// index and query meet. Kept tiny and separate from the abbreviation map — these
    /// carry no quote mark and are not acronyms.</summary>
    private static readonly Dictionary<string, string> Spelling = new(StringComparer.Ordinal)
    {
        ["שלחן"] = "שולחן",
    };

    /// <summary>The abbreviation map (hand-edited, in SeforimDbCatalogAbbreviations).
    /// Key = an abbreviation with EVERY QUOTE GLYPH STRIPPED (ט"ז is keyed as טז); value =
    /// alternatives, each an ordered word list that is AND-matched. More than one
    /// alternative = an ambiguous abbreviation that OR-expands (only used on the QUERY
    /// side — see <see cref="TokenizeQuery"/>).
    ///
    /// Lookups strip the same glyphs off the candidate before probing (see
    /// <see cref="StripQuoteGlyphs"/>), so ט"ז, ט״ז, ט''ז and the bare טז all resolve
    /// through the one entry — the map never has to enumerate quote flavours.</summary>
    private static readonly Dictionary<string, string[][]> Abbrev = SeforimDbCatalogAbbreviations.Map;

    /// <summary>Every quote-ish glyph an abbreviation may be written with: ASCII quote and
    /// apostrophe, Hebrew gershayim/geresh, and the curly forms. Stripped from both the
    /// map keys (by hand, enforced by SeforimDbCatalogAbbreviationsCheck) and every lookup
    /// candidate, so a token matches regardless of which mark — if any — was typed.</summary>
    private static bool IsQuoteGlyph(char c) =>
        c is '"' or '\'' or '״' or '׳' or '“' or '”' or '‘' or '’';

    /// <summary>Remove every quote glyph from a token. Returns the input unchanged (same
    /// reference) when it carries none, so the common case allocates nothing.</summary>
    public static string StripQuoteGlyphs(string s)
    {
        int i = 0;
        while (i < s.Length && !IsQuoteGlyph(s[i])) i++;
        if (i == s.Length) return s;

        var sb = new System.Text.StringBuilder(s.Length);
        sb.Append(s, 0, i);
        for (; i < s.Length; i++)
            if (!IsQuoteGlyph(s[i])) sb.Append(s[i]);
        return sb.ToString();
    }

    /// <summary>
    /// The abbreviation key this word carries IF it is written as a quoted acronym, else null.
    ///
    /// The quote matters because a few acronyms are spelled identically to an ordinary Hebrew
    /// word once the quote is gone. Those keys are necessarily identity entries in the map
    /// (they expand to themselves), so after normalization an author acronym and a common noun
    /// are the SAME token and nothing downstream can tell them apart. The quote — stripped by
    /// the pipeline before matching — is the only thing that separates them, so it is recovered
    /// here, per key.
    ///
    /// The caller compares the query's keys against each HIT's own stored raw text at result
    /// time: the stored strings keep their glyphs, only the indexed tokens were stripped, so no
    /// index-side field is involved. See SeforimDbCatalogIndex.HitCarriesQuotedKey.
    /// </summary>
    public static string? GetQuotedAbbreviationKey(string word)
    {
        // Trim edge punctuation FIRST: a quote at the word boundary is quotation punctuation,
        // not an acronym mark, and must not register — a common word wrapped in quotes in a
        // title (or a phrase-quoted query) is not an acronym. Only a glyph INSIDE the trimmed
        // word counts.
        int a = 0, b = word.Length;
        while (a < b && !char.IsLetterOrDigit(word[a])) a++;
        while (b > a && !char.IsLetterOrDigit(word[b - 1])) b--;
        if (b <= a) return null;

        bool hasGlyph = false;
        for (int i = a; i < b; i++) if (IsQuoteGlyph(word[i])) { hasGlyph = true; break; }
        if (!hasGlyph) return null;

        string candidate = StripQuoteGlyphs(word.Substring(a, b - a));
        return Abbrev.ContainsKey(candidate) ? candidate : null;
    }

    /// <summary>Every distinct quoted-acronym key in <paramref name="text"/> (see
    /// <see cref="GetQuotedAbbreviationKey"/>), or null when there are none — the common case,
    /// which then allocates nothing.</summary>
    public static HashSet<string>? GetQuotedAbbreviationKeys(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        HashSet<string>? keys = null;
        foreach (var word in text!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var key = GetQuotedAbbreviationKey(word);
            if (key != null) (keys ?? (keys = new HashSet<string>(StringComparer.Ordinal))).Add(key);
        }
        return keys;
    }

    /// <summary>Largest key word-count in <see cref="Abbrev"/> — the lookahead window
    /// for greedy multi-word matching. Computed once from the map.</summary>
    private static readonly int MaxKeyWords = ComputeMaxKeyWords();

    /// <summary>The abbreviation map as (key, alternatives) pairs — exposed for
    /// verification and diagnostics (see the Tests project's --abbrev mode), which needs to
    /// enumerate every entry to prove each one resolves and points at text the DB actually
    /// contains. Read-only view; the map itself stays private.</summary>
    public static IEnumerable<KeyValuePair<string, string[][]>> AbbreviationEntries => Abbrev;

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
            if (!IsNumeralAfterCounter(raws, i) && TryMatchAbbrev(raws, i, out var alts, out int consumed, indexSide: true))
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
            if (!IsNumeralAfterCounter(raws, i) && TryMatchAbbrev(raws, i, out var alts, out int consumed, indexSide: false))
            {
                // A lone UNQUOTED word that is also a key keeps its literal form as one more
                // alternative: the index never expands such a word (see TryMatchAbbrev), so
                // "bamidbar" must still find the book Bamidbar and "md" the section md, while
                // the expansion still finds Bamidbar Rabbah and Midrash. Quoted forms are
                // unambiguous acronyms and get the expansion only.
                if (consumed == 1 && !HasQuoteGlyph(raws[i]))
                {
                    var literal = new List<string>();
                    EmitNormalizedToken(raws[i], literal);
                    if (literal.Count == 1)
                    {
                        var withLiteral = new string[alts.Length + 1][];
                        Array.Copy(alts, withLiteral, alts.Length);
                        withLiteral[alts.Length] = [literal[0]];
                        result.Add(new QueryToken(withLiteral, preferLiteral: true));
                        flat.Add(literal[0]);
                        i += consumed;
                        continue;
                    }
                }
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

    /// <summary>
    /// Counter words after which a SHORT Hebrew token is a numeral, never an abbreviation:
    /// "perek mb" is chapter 42, not Mishnah Berurah; "siman sq" is siman 160, not seif katan.
    /// Quote glyphs do not decide it — the library writes numerals both as mb and m"b. The
    /// seif-katan key itself (quote-stripped) and "katan" count as counters, so "s"q b" keeps
    /// its numeral. Unicode escapes keep this block ASCII-safe.
    /// </summary>
    private static readonly HashSet<string> CounterWords = new(StringComparer.Ordinal)
    {
        "\u05E4\u05E8\u05E7",        // perek
        "\u05E1\u05D9\u05DE\u05DF",  // siman
        "\u05D3\u05E3",              // daf
        "\u05D4\u05DC\u05DB\u05D4",  // halacha
        "\u05E1\u05E2\u05D9\u05E3",  // seif
        "\u05E2\u05DE\u05D5\u05D3",  // amud
        "\u05D0\u05D5\u05EA",        // ot
        "\u05E4\u05E1\u05D5\u05E7",  // pasuk
        "\u05DE\u05E9\u05E0\u05D4",  // mishnah
        "\u05E1\u05E7",              // s"q, quote-stripped
        "\u05E7\u05D8\u05DF",        // katan
        "\u05DB\u05DC\u05DC",        // klal
        "\u05E9\u05E2\u05E8",        // shaar
        "\u05D7\u05DC\u05E7",        // chelek
        "\u05DE\u05E6\u05D5\u05D4",  // mitzvah
        "\u05DE\u05D0\u05DE\u05E8",  // maamar
        "\u05E8\u05DE\u05D6",        // remez
        "\u05D3\u05D9\u05DF",        // din
        "\u05D3\u05E8\u05D5\u05E9",  // derush
        "\u05DE\u05D6\u05DE\u05D5\u05E8",  // mizmor
        "\u05E4\u05E1\u05E7\u05D0",  // piska
        "\u05E4\u05E1\u05E7\u05D4",  // piska, spelled with he
        "\u05EA\u05DC\u05DE\u05D5\u05D3",  // talmud (Meiri: "talmud mb." = daf 42)
        // NOT "pirush" / "bayit": "pirush shu"a" is a commentary on an acronym-named book.
    };

    /// <summary>True when raw token <paramref name="i"/> is a numeral position: it follows a
    /// <see cref="CounterWords"/> word (with or without a leading he) and is 1-4 Hebrew letters
    /// once quote glyphs and edge punctuation are stripped. Such a token is emitted as a plain
    /// token, never looked up as an abbreviation, in BOTH the index and the query tokenizer.</summary>
    private static bool IsNumeralAfterCounter(string[] raws, int i)
    {
        string raw = raws[i];
        string tok = StripQuoteGlyphs(TrimEdgeNonWord(raw));
        if (tok.Length is 0 or > 4) return false;
        foreach (char c in tok)
            if (!IsHebrewLetter(c)) return false;

        // A daf mark ("mb." / "mb:") or parenthesised reference ("(ma mb)") is a numeral
        // whatever precedes it — TOC paths cite pages and verses that way. Up to three letters
        // unquoted; quoted only up to two (m"b. is a page, but shu"a: is an acronym with a colon).
        bool quoted = StripQuoteGlyphs(raw).Length != raw.Length;
        if (tok.Length <= (quoted ? 2 : 3)
            && (raw.EndsWith('.') || raw.EndsWith(':') || raw.StartsWith('(') || raw.EndsWith(')')))
            return true;

        if (i == 0) return false;
        string prev = StripQuoteGlyphs(TrimEdgeNonWord(raws[i - 1]));
        if (prev.Length > 1 && prev[0] == '\u05D4' && CounterWords.Contains(prev.Substring(1)))
            prev = prev.Substring(1);
        return CounterWords.Contains(prev);
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
    private static bool TryMatchAbbrev(string[] raws, int start, out string[][] alts, out int consumed, bool indexSide)
    {
        // A key never contains the path separator, so no window may cross one. Without this
        // the two-raw window "/ mb" edge-trims to "mb" and bypasses the single-word guard below.
        if (raws[start] == PathSeparator) { alts = null!; consumed = 0; return false; }
        int maxWindow = Math.Min(MaxKeyWords, raws.Length - start);
        for (int i = 1; i < maxWindow; i++)
            if (raws[start + i] == PathSeparator) { maxWindow = i; break; }

        for (int w = maxWindow; w >= 1; w--)
        {
            // INDEX side: a lone word is an abbreviation only when it carries a quote glyph.
            // Unquoted it is what it says — a numeral in a TOC path ("perush she" = perush
            // 375, "chadar mb" = room 42) or a plain word that happens to be a key ("bamidbar"
            // is the Chumash, not Bamidbar Rabbah) — and the counter list above cannot name
            // every such case. The QUERY side stays lenient: a user typing "shve" or "mb"
            // without the glyph gets the expansion AND the literal (see TokenizeQuery).
            if (w == 1 && indexSide && !HasQuoteGlyph(raws[start])) continue;
            string candidate = w == 1 ? raws[start] : string.Join(" ", raws, start, w);
            if (TryLookup(candidate, out alts!))
            {
                consumed = w;
                return true;
            }
            string trimmed = TrimEdgeNonWord(candidate);
            if (trimmed.Length > 0 && trimmed.Length != candidate.Length && TryLookup(trimmed, out alts!))
            {
                consumed = w;
                return true;
            }

            string? heStripped = StripHePrefix(raws[start]);
            if (heStripped is not null)
            {
                string heCandidate = w == 1 ? heStripped : heStripped + " " + string.Join(" ", raws, start + 1, w - 1);
                if (TryLookup(heCandidate, out alts!))
                {
                    consumed = w;
                    return true;
                }
                string heTrimmed = TrimEdgeNonWord(heCandidate);
                if (heTrimmed.Length > 0 && heTrimmed.Length != heCandidate.Length && TryLookup(heTrimmed, out alts!))
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

    /// <summary>The " / " joiner of a stored TOC path, as the whitespace split sees it.</summary>
    private const string PathSeparator = "/";

    private static bool HasQuoteGlyph(string raw) => StripQuoteGlyphs(raw).Length != raw.Length;

    /// <summary>Probe the abbreviation map with quote glyphs stripped off the candidate —
    /// the map's keys are stored stripped, so this is what lets ט"ז / ט״ז / ט''ז / טז all
    /// hit the same entry. A candidate that carries no glyph is probed as-is.</summary>
    private static bool TryLookup(string candidate, out string[][] alts)
        => Abbrev.TryGetValue(StripQuoteGlyphs(candidate), out alts!);

    /// <summary>The amud + strip + spelling-fold steps for a single non-abbreviation
    /// token, appending the resulting token(s) to <paramref name="tokens"/>.</summary>
    private static void EmitNormalizedToken(string raw, List<string> tokens)
    {
        string tok = raw;

        // Amud normalization — right after a דף token, a trailing "." means עמוד א and a
        // trailing ":" means עמוד ב. Applies to any דף TOC (and to queries typed the same
        // way), and must see the mark before stripping.
        string? amud = null;
        // "talmud X." is the Meiri's way of writing "daf X." — same amud marks.
        if (tokens.Count > 0 && (tokens[^1] == "דף" || tokens[^1] == "\u05EA\u05DC\u05DE\u05D5\u05D3") && tok.Length > 1)
        {
            if (tok.EndsWith(".", StringComparison.Ordinal)) amud = "א";
            else if (tok.EndsWith(":", StringComparison.Ordinal)) amud = "ב";
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

        /// <summary>True for an UNQUOTED word that is also an abbreviation key: the last
        /// alternative is the word itself, and it is what the user most likely meant
        /// ("bamidbar" is the Chumash before it is Bamidbar Rabbah). Matching still tries
        /// every alternative; the ranking's notion of "the query's words" takes the literal.</summary>
        public readonly bool PreferLiteral;

        /// <summary>The alternative the ranking treats as the typed words: the literal one
        /// when <see cref="PreferLiteral"/>, else the first (canonical) expansion.</summary>
        public string[] RankingWords => PreferLiteral ? Alternatives[^1] : Alternatives[0];

        public QueryToken(string word) { Alternatives = [[word]]; PreferLiteral = false; }
        public QueryToken(string[][] alternatives, bool preferLiteral = false) { Alternatives = alternatives; PreferLiteral = preferLiteral; }
    }

    /// <summary>
    /// The query with its trailing words completed to each multi-word abbreviation key they
    /// begin, the LAST word read as a prefix ("pnei yehos" -> "pnei yehoshua"). Needed by
    /// a search that matches the last word as a prefix: the INDEX expands a multi-word key
    /// to its canonical reading even unquoted (see <see cref="Tokenize"/>), so a book titled
    /// by such a key does not carry the key's own words, and a partly typed key matches
    /// nothing until it is completed. Each returned string is a whole query, to be searched
    /// exactly through <see cref="TokenizeQuery"/>. Keys typed in full are not returned —
    /// the exact query already expands them. At most <paramref name="max"/> completions.
    /// </summary>
    public static List<string> CompleteTrailingAbbreviationKeys(string query, int max)
    {
        var completions = new List<string>();
        var raws = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (int w = Math.Min(MaxKeyWords, raws.Length); w >= 2 && completions.Count < max; w--)
        {
            int start = raws.Length - w;
            var words = new string[w];
            for (int i = 0; i < w; i++) words[i] = StripQuoteGlyphs(TrimEdgeNonWord(raws[start + i]));
            if (Array.IndexOf(words, "") >= 0 || Array.IndexOf(words, PathSeparator) >= 0) continue;
            string typed = string.Join(" ", words);
            int lastWordStart = typed.Length - words[^1].Length;
            string head = start == 0 ? "" : string.Join(" ", raws, 0, start) + " ";
            foreach (var key in Abbrev.Keys)
            {
                // Same word count: the typed last word is a prefix of the key's last word.
                if (key.Length <= typed.Length || !key.StartsWith(typed, StringComparison.Ordinal)
                    || key.IndexOf(' ', lastWordStart) >= 0) continue;
                completions.Add(head + key);
                if (completions.Count == max) break;
            }
        }
        return completions;
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

    // ── ה-prefix stripping (ported from the former frontend normalizer) ──
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

    /// <summary>True when a root TOC entry only repeats the book title: a title variant that
    /// adds no word of its own. "Rosh Yosef al Chullin, chelek 1" is a variant of "Rosh Yosef
    /// al Chullin" but carries "chelek 1", so it is NOT a repeat and must stay in the index.</summary>
    public static bool IsTitleRepeat(string bookTitle, string rootText)
    {
        if (!IsTitleVariant(bookTitle, rootText)) return false;
        var bt = NormTitleWords(bookTitle);
        foreach (var w in NormTitleWords(rootText))
            if (!bt.Contains(w)) return false;
        return true;
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
