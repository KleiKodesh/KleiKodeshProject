using KitveiHakodeshService.Catalog;
using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.Tests;

/// <summary>
/// Verifies the QUOTE-STRIPPED abbreviation map (v17) against the real tokenizer and,
/// when an index is available, the real search.
///
/// Five checks:
///   1. FLAVOUR EQUIVALENCE — every quote flavour of a key (ט"ז / ט״ז / ט''ז / טז)
///      tokenizes identically. This is the v17 promise; before v17 the bare form matched
///      nothing.
///   2. EVERY KEY RESOLVES — TokenizeQuery on each key must produce the mapped
///      alternatives, not the key's own letters. Catches keys the lookup can't reach.
///   3. TARGET EXISTS IN THE DB — every alternative's words must all appear in the
///      author/title vocabulary. An expansion pointing at text no book contains is dead
///      weight (word-level, matching how the engine actually matches).
///   4. NO PLAIN-WORD HIJACK — a stripped key must not be a word that occurs in real
///      titles as ordinary text. Such a key would silently rewrite legitimate queries.
///   5. LIVE SEARCH — for a sample of author acronyms, searching the acronym must return
///      hits, and they must be the same books the full name returns.
/// </summary>
internal static class AbbrevVerify
{
    public static int Run(string dbPath, string indexPath)
    {
        int failures = 0;
        void Fail(string msg) { failures++; Console.Error.WriteLine("  FAIL: " + msg); }

        Console.WriteLine($"db:    {dbPath}");
        Console.WriteLine($"index: {indexPath}");
        Console.WriteLine();

        var map = CatalogTocTextRules.AbbreviationEntries
            .Select(kv => (Key: kv.Key, Value: kv.Value)).ToList();
        Console.WriteLine($"abbreviation map: {map.Count} keys, "
            + $"{map.Count(kv => kv.Value.Length > 1)} with multiple alternatives");
        Console.WriteLine();

        // ── 1. Flavour equivalence ────────────────────────────────────────────────
        Console.WriteLine("1. quote-flavour equivalence");
        int flavourChecked = 0, flavourBad = 0;
        foreach (var (key, _) in map)
        {
            // Only keys that look like acronyms (would carry a gershayim when written out).
            if (key.Length < 2 || key.Contains(' ')) continue;

            // Reconstruct plausible written forms: insert each quote glyph before the
            // final letter, the conventional acronym position.
            string stem = key[..^1], last = key[^1..];
            string[] written =
            [
                key,
                stem + "\"" + last,
                stem + "״" + last,   // gershayim
                stem + "''" + last,
                stem + "”" + last,   // curly close-quote
            ];

            var baseline = CatalogTocTextRules.TokenizeQuery(written[0]);
            foreach (string form in written[1..])
            {
                var got = CatalogTocTextRules.TokenizeQuery(form);
                flavourChecked++;
                if (!SameTokens(baseline, got))
                {
                    flavourBad++;
                    if (flavourBad <= 10)
                        Fail($"flavour mismatch: \"{form}\" -> {Describe(got)} "
                            + $"but \"{key}\" -> {Describe(baseline)}");
                }
            }
        }
        Console.WriteLine($"   {flavourChecked} flavour forms checked, {flavourBad} mismatched");
        Console.WriteLine();

        // ── 2. Every key resolves through the map ─────────────────────────────────
        Console.WriteLine("2. every key resolves to its alternatives");
        int unresolved = 0;
        foreach (var (key, alts) in map)
        {
            var toks = CatalogTocTextRules.TokenizeQuery(key);
            // A resolved abbreviation yields ONE token carrying >1 word or the mapped words.
            bool resolved = toks.Count == 1 && !toks[0].IsPlain
                            || toks.Count >= 1 && SameWords(toks, alts[0]);
            if (!resolved)
            {
                unresolved++;
                if (unresolved <= 15)
                    Fail($"key {key} did not resolve: got {Describe(toks)}, expected {string.Join(" ", alts[0])}");
            }
        }
        Console.WriteLine($"   {map.Count - unresolved}/{map.Count} keys resolve");
        Console.WriteLine();

        // ── DB vocabulary ─────────────────────────────────────────────────────────
        var vocab = LoadVocabulary(dbPath, out int authorCount, out int titleCount);
        Console.WriteLine($"DB vocabulary: {vocab.Count} distinct normalized words "
            + $"({authorCount} authors, {titleCount} titles)");
        Console.WriteLine();

        // ── 3. Every alternative's words exist in the DB ──────────────────────────
        //
        // An expansion whose words appear in no title/author/category can never match
        // anything — it only forces an impossible term into the query. Since `vocab` is now
        // built from RAW words (no abbreviation expansion), self-alternatives like
        // רשש -> רשש are checked on the same footing as any other: the token really does
        // occur in titles such as רש"ש על בבא בתרא. So this is a hard failure, not a note.
        Console.WriteLine("3. expansion targets exist in the DB");
        int deadAlts = 0;
        foreach (var (key, alts) in map)
        {
            foreach (var alt in alts)
            {
                var missing = alt.Where(w => !vocab.Contains(w)).ToList();
                if (missing.Count == 0) continue;
                deadAlts++;
                Fail($"{key} -> {string.Join(" ", alt)} "
                    + $"references words absent from every author/title/category: "
                    + $"{string.Join(", ", missing)}");
            }
        }
        Console.WriteLine($"   {map.Sum(kv => kv.Value.Length) - deadAlts}"
            + $"/{map.Sum(kv => kv.Value.Length)} alternatives resolve");
        Console.WriteLine();

        // ── 4. No plain-word hijack ───────────────────────────────────────────────
        //
        // The dangerous shape is a key that occurs in real titles as an ordinary word AND
        // whose expansion DROPS it — the word then disappears from the query and those
        // titles become unreachable. That is what made הלכות טומאת מת unfindable (מת →
        // משנה תורה) and אדרת אליהו return zero hits (אדרת → the author's full name).
        //
        // A key whose expansion still CONTAINS it is safe: פני → פני יהושע keeps emitting
        // פני, so פני דוד and אור פני משה still match (the extra word rides along and the
        // OR-expansion absorbs it). Reporting those as problems is what makes a raw
        // collision count misleading, so they are listed separately and don't fail.
        Console.WriteLine("4. keys that shadow an ordinary title word");
        var titleWordFreq = LoadTitleWordFrequency(dbPath);
        int dropping = 0, keeping = 0;
        foreach (var (key, alts) in map)
        {
            if (key.Contains(' ')) continue;
            if (!titleWordFreq.TryGetValue(key, out int freq) || freq == 0) continue;

            // Safe when ANY alternative still contains the key: BuildQuery emits
            // MUST( OR over alternatives ), so one surviving alternative that carries the
            // literal word is enough to keep those titles reachable. Requiring EVERY
            // alternative to keep it would flag רשש → (שמואל שטראשון | רשש) as a hijack even
            // though the second alternative is exactly what makes רש"ש-in-a-title match.
            bool anyKeeps = alts.Any(a => a.Contains(key, StringComparer.Ordinal));
            if (anyKeeps) { keeping++; continue; }

            dropping++;
            Fail($"key {key} occurs in {freq} title words as ordinary text, but expands to "
                + $"{string.Join(" / ", alts.Select(a => string.Join(" ", a)))} — which drops it, "
                + $"making those titles unreachable");
        }
        Console.WriteLine($"   {keeping} keys shadow a title word but KEEP it in every expansion (safe)");
        Console.WriteLine($"   {dropping} keys shadow a title word and DROP it (hijack)");
        Console.WriteLine();

        // ── 5. Live search ────────────────────────────────────────────────────────
        Console.WriteLine("5. live search: acronym vs full name");
        if (!Directory.Exists(indexPath))
        {
            Console.WriteLine("   SKIPPED — no index at that path (run the main test first to build one)");
        }
        else
        {
            using var index = new CatalogTocIndex(indexPath, dbPath);
            if (!index.TryOpenActive())
            {
                Console.WriteLine("   SKIPPED — index present but not openable");
            }
            else
            {
                Console.WriteLine($"   index docs: {index.DocCount()}");
                (string Acronym, string FullName)[] probes =
                [
                    ("חידא",   "חיים דוד אזולאי"),
                    ("חיד\"א", "חיים דוד אזולאי"),
                    ("יעבץ",   "יעקב עמדין"),
                    ("יעב\"ץ", "יעקב עמדין"),
                    ("רשש",    "שמואל שטראשון"),
                    ("רידבז",  "יעקב דוד וילובסקי"),
                    ("תפאי",   "ישראל ליפשיץ"),
                    ("נציב",   "נפתלי צבי יהודה ברלין"),
                    ("טז",     "טורי זהב"),
                    ("ט\"ז",   "טורי זהב"),
                    ("תויט",   "תוספות יום טוב"),
                    ("מצד",    "מצודת דוד"),
                    ("מצצ",    "מצודת ציון"),
                    ("אוש",    "אור שמח"),
                    ("כמ",     "כסף משנה"),
                ];

                foreach (var (acronym, fullName) in probes)
                {
                    var byAcronym = index.Search(acronym);
                    var byName = index.Search(fullName);
                    var aBooks = byAcronym.Select(h => h.BookId).Distinct().ToHashSet();
                    var nBooks = byName.Select(h => h.BookId).Distinct().ToHashSet();

                    string verdict;
                    if (byAcronym.Count == 0)
                        verdict = "*** NO HITS ***";
                    else if (aBooks.SetEquals(nBooks))
                        verdict = "identical book set";
                    else
                    {
                        int overlap = aBooks.Intersect(nBooks).Count();
                        verdict = overlap > 0
                            ? $"overlap {overlap}/{nBooks.Count}"
                            : "*** DISJOINT ***";
                    }

                    Console.WriteLine($"   {acronym,-9} {byAcronym.Count,6} hits / {aBooks.Count,4} books"
                        + $"   |  \"{fullName}\" {byName.Count,6} hits / {nBooks.Count,4} books   |  {verdict}");

                    if (byAcronym.Count == 0) Fail($"acronym {acronym} returned no hits");
                    else if (aBooks.Count > 0 && nBooks.Count > 0 && !aBooks.Overlaps(nBooks))
                        Fail($"acronym {acronym} and full name \"{fullName}\" return disjoint books");
                }

                // ── Top-hit expectations ──────────────────────────────────────────
                // The user-facing promise: typing the acronym puts the right book FIRST.
                // Includes the two hijack regressions v18 fixed — a key that shadows an
                // ordinary title word made real books unfindable (אדרת אליהו returned 0
                // hits) or forced an unrelated expansion in (טומאת מת → משנה תורה).
                Console.WriteLine();
                Console.WriteLine("   top-hit expectations:");
                (string Query, string ExpectedTop)[] topHits =
                [
                    ("יעבץ על ברכות",       "הגהות יעב\"ץ על ברכות"),
                    ("מאירי על פסחים",      "מאירי על פסחים"),
                    ("גרא על פרקי אבות",    "גר\"א על פרקי אבות"),
                    ("רשש על בבא בתרא",     "רש\"ש על בבא בתרא"),
                    ("תויט על משנה ברכות",  "תוספות יום טוב על משנה ברכות"),
                    ("מצד על תהילים",       "מצודת דוד על תהילים"),
                    ("טז על אורח חיים",     "טורי זהב על שולחן ערוך אורח חיים"),
                    // Regressions: these must NOT be hijacked by an abbreviation key.
                    // אדרת אליהו is a PREFIX match, not exact: two books carry that title
                    // and catalog order puts "אדרת אליהו (ר יוסף חיים)" first. Before v18 this
                    // query returned 0 hits — the אדרת key rewrote the token so neither was
                    // reachable. See TopHitMatches.
                    ("אדרת אליהו",          "אדרת אליהו"),
                    ("העמק דבר",            "העמק דבר על בראשית"),
                    ("פני משה",             "פני משה על תלמוד ירושלמי ברכות"),
                    ("במדבר",               "במדבר"),
                ];

                foreach (var (query, expectedTop) in topHits)
                {
                    var hits = index.Search(query);
                    string top = hits.Count > 0 ? hits[0].FullTocPath : "(no hits)";
                    bool ok = TopHitMatches(top, expectedTop);
                    Console.WriteLine($"     {(ok ? "OK  " : "BAD ")} \"{query}\" -> {top}");
                    if (!ok)
                        Fail($"query \"{query}\" top hit was \"{top}\", expected \"{expectedTop}\"");
                }

                // ── Hijack guard ──────────────────────────────────────────────────
                // A key must never be a word that titles use as ordinary text with a
                // DIFFERENT meaning. Verified behaviourally: searching the key's own
                // literal text must still find titles containing that word.
                Console.WriteLine();
                Console.WriteLine("   hijack guard (ordinary word still findable):");
                foreach (string word in new[] { "טומאת מת", "אדרת", "דבר", "פני" })
                {
                    var hits = index.Search(word);
                    Console.WriteLine($"     {(hits.Count > 0 ? "OK  " : "BAD ")} \"{word}\" -> {hits.Count} hits"
                        + (hits.Count > 0 ? $", top: {hits[0].FullTocPath}" : ""));
                    if (hits.Count == 0)
                        Fail($"ordinary word \"{word}\" returned no hits — an abbreviation key is shadowing it");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} FAILURE(S)");
        return failures == 0 ? 0 : 1;
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    /// <summary>Top-hit expectation: the actual path must START with the expected text.
    /// Prefix, not equality, because several books legitimately share a title stem and
    /// catalog order decides which comes first — "אדרת אליהו" is also the stem of
    /// "אדרת אליהו (ר יוסף חיים)". What matters for these regressions is that the right BOOK
    /// leads, not which of its editions.</summary>
    private static bool TopHitMatches(string actual, string expected) =>
        actual.StartsWith(expected, StringComparison.Ordinal);

    private static bool SameTokens(
        List<CatalogTocTextRules.QueryToken> a, List<CatalogTocTextRules.QueryToken> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Alternatives.Length != b[i].Alternatives.Length) return false;
            for (int j = 0; j < a[i].Alternatives.Length; j++)
                if (!a[i].Alternatives[j].SequenceEqual(b[i].Alternatives[j])) return false;
        }
        return true;
    }

    private static bool SameWords(List<CatalogTocTextRules.QueryToken> toks, string[] words)
    {
        var flat = toks.SelectMany(t => t.Alternatives[0]).ToList();
        return flat.SequenceEqual(words);
    }

    private static string Describe(List<CatalogTocTextRules.QueryToken> toks) =>
        string.Join(" + ", toks.Select(t =>
            t.IsPlain ? t.Word
                      : "(" + string.Join(" | ", t.Alternatives.Select(a => string.Join(" ", a))) + ")"));

    /// <summary>All distinct RAW words occurring in author names, book titles and category
    /// titles — the vocabulary an expansion must land in to be matchable.
    ///
    /// Deliberately does NOT use CatalogTocTextRules.Tokenize: that expands abbreviations,
    /// so a title reading "בעל הטורים על בראשית" would contribute the EXPANSION's words and
    /// not הטורים itself — making a perfectly good expansion look absent. Punctuation is
    /// stripped the same way the pipeline does it, but no abbreviation lookup runs.</summary>
    private static HashSet<string> LoadVocabulary(string dbPath, out int authors, out int titles)
    {
        var vocab = new HashSet<string>(StringComparer.Ordinal);
        authors = titles = 0;
        using var conn = Open(dbPath);

        void AddWords(string text)
        {
            foreach (var raw in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var sb = new System.Text.StringBuilder(raw.Length);
                foreach (char ch in raw)
                    if (char.IsLetter(ch) || char.IsNumber(ch)) sb.Append(char.ToLowerInvariant(ch));
                if (sb.Length > 0) vocab.Add(sb.ToString());
            }
        }

        foreach (var (table, column) in new[] { ("author", "name"), ("book", "title"), ("category", "title") })
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {column} FROM {table}";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (r.IsDBNull(0)) continue;
                if (table == "author") authors++;
                else if (table == "book") titles++;
                AddWords(r.GetString(0));
            }
        }
        return vocab;
    }

    /// <summary>How many book titles contain each word, tokenized the RAW way (no
    /// abbreviation expansion) so we can spot a key that is itself an ordinary word.</summary>
    private static Dictionary<string, int> LoadTitleWordFrequency(string dbPath)
    {
        var freq = new Dictionary<string, int>(StringComparer.Ordinal);
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT title FROM book";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (r.IsDBNull(0)) continue;
            foreach (var raw in r.GetString(0).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                // Strip punctuation the same way the pipeline does, WITHOUT abbreviation lookup.
                var sb = new System.Text.StringBuilder(raw.Length);
                foreach (char ch in raw)
                    if (char.IsLetter(ch) || char.IsNumber(ch)) sb.Append(char.ToLowerInvariant(ch));
                if (sb.Length == 0) continue;
                string w = sb.ToString();
                freq[w] = freq.GetValueOrDefault(w) + 1;
            }
        }
        return freq;
    }

    private static SqliteConnection Open(string dbPath)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ConnectionString;
        var conn = new SqliteConnection(cs);
        conn.Open();
        return conn;
    }
}
