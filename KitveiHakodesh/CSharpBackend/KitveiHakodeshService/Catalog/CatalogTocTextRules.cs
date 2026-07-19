namespace KitveiHakodeshService.Catalog;

/// <summary>
/// The catalog TOC search normalization pipeline — applied IDENTICALLY at index time
/// and query time (the whole point: both sides meet at the same tokens).
///
/// Pipeline, in this exact order:
///   1. Canonical normalization (token-based) — variant spellings map to one canonical
///      token. This MUST run before punctuation stripping: שו"ע contains a quote, and
///      once stripped it would become שוע and could no longer be recognized.
///   2. Talmud page (amud) normalization — a token following דף that ends with the
///      amud mark expands: "דף יד." → "דף יד עמוד א", "דף יד:" → "דף יד עמוד ב".
///      This too MUST run before punctuation stripping (the mark IS the information).
///   3. Strip all non-word characters (anything that is not a letter or digit).
///   4. Tokenization (whitespace-separated; empty tokens dropped).
/// </summary>
public static class CatalogTocTextRules
{
    /// <summary>Canonical token map. Key = the exact token as typed/stored (after
    /// whitespace splitting, before any character stripping); value = canonical form.</summary>
    private static readonly Dictionary<string, string> Canonical = new(StringComparer.Ordinal)
    {
        ["שלחן"] = "שולחן",
        ["שו\"ע"] = "שולחן",   // ASCII quote
        ["שו״ע"] = "שולחן",    // Hebrew gershayim
        ["שו''ע"] = "שולחן",   // doubled ASCII apostrophe
        ["ש\"ע"] = "שולחן",    // short form, ASCII quote
        ["ש״ע"] = "שולחן",     // short form, gershayim
        ["ש''ע"] = "שולחן",    // short form, doubled ASCII apostrophe
    };

    /// <summary>
    /// Run the full pipeline on a text (a query, or a document's search text) and
    /// return its tokens. Lowercases (Hebrew is unaffected; Latin becomes uniform).
    /// </summary>
    public static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        foreach (var raw in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            // 1. Canonical normalization — token-based, tried on the raw token and on
            //    the token with edge punctuation trimmed (so "(שו"ע)" still maps).
            string tok = raw;
            if (!Canonical.TryGetValue(tok, out var canonical))
            {
                string trimmed = TrimEdgeNonWord(tok);
                if (trimmed.Length > 0 && Canonical.TryGetValue(trimmed, out canonical)) tok = canonical;
                else canonical = null;
            }
            if (canonical is not null) tok = canonical;

            // 2. Amud normalization — right after a דף token, a trailing "." means
            //    עמוד א and a trailing ":" means עמוד ב. Applies to any דף TOC (and to
            //    queries typed the same way), and must see the mark before stripping.
            string? amud = null;
            if (tokens.Count > 0 && tokens[^1] == "דף" && tok.Length > 1)
            {
                if (tok.EndsWith('.')) amud = "א";
                else if (tok.EndsWith(':')) amud = "ב";
                if (amud is not null) tok = tok[..^1];
            }

            // 3. Strip all non-word characters. 4. Emit the token(s).
            var sb = new System.Text.StringBuilder(tok.Length);
            foreach (char c in tok)
                if (char.IsLetter(c) || char.IsNumber(c))
                    sb.Append(char.ToLowerInvariant(c));
            if (sb.Length > 0) tokens.Add(sb.ToString());
            else amud = null; // the mark stood alone — nothing to attach an amud to

            if (amud is not null)
            {
                tokens.Add("עמוד");
                tokens.Add(amud);
            }
        }
        return tokens;
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
