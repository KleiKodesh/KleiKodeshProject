namespace KitveiHakodesh.Core.SeforimDbCatalog;

/// <summary>
/// One hit's ranking keys, whatever index it came from: the catalog collector and the local
/// file collector both fill this from INDEX DATA (postings, positions, doc-values) while
/// collecting, and <see cref="SeforimDbCatalogRankingKeys.CompareFieldAccuracy"/> /
/// <see cref="SeforimDbCatalogRankingKeys.CompareProximity"/> order it. Nothing is measured on
/// stored text anywhere.
/// </summary>
internal struct RankKeys
{
    /// <summary>Every query word matched LITERALLY (no כתיב/ה-prefix variant, no fuzzy edit).
    /// The PRIMARY key: accuracy first.</summary>
    public bool IsLiteral;
    /// <summary>EVERY query token landed in the hit's own title/path or author, not only in
    /// the category path (which every book filed under it shares).</summary>
    public bool MatchesTitleOrAuthor;
    /// <summary>The query NAMES the hit's book: every title word is a query word. "Tur" for
    /// the query "Tur", not "Kapei Aharon al Tur".</summary>
    public bool TitleCovered;
    /// <summary>DISTINCT title words, meaningful when <see cref="TitleCovered"/>: the title that
    /// explains more of the query wins.</summary>
    public int TitleDistinctWords;
    /// <summary>|title word count - query word count|, meaningful when <see cref="TitleCovered"/>:
    /// among covered titles explaining equally much, the one sized like the query wins.</summary>
    public int TitleLengthGap;
    /// <summary>Every word of the hit's own LEAF segment (the title for a level-0 hit) is a
    /// query word — the address typed IS this entry: "Shemini" before "Shemini Atzeret".</summary>
    public bool LeafCovered;
    /// <summary><see cref="LeafCovered"/> AND the leaf holds a typed word the title lacks — an
    /// address the user typed, not a TOC line that merely repeats the title.</summary>
    public bool LeafAddsWord;
    /// <summary>How many of the query's words, COUNTED WITH REPETITION, the path holds.</summary>
    public int WordsMatched;
    /// <summary>0 = a book title (a file is always one), 1+ = TOC depth. Ranked only as TITLE OR
    /// NOT: below <see cref="WordsMatched"/> — "perek 1 / mishnah 5" (2 words) beats "perek 5"
    /// (1) — and above <see cref="LeafCovered"/>: a book whose title holds the word beats a
    /// dictionary line named exactly that word. Depth among TOC lines decides nothing here; the
    /// catalog order does, after every key.</summary>
    public int Level;
    /// <summary>A level-0 hit whose stored title carries the quoted acronym the query was typed
    /// with (an author acronym vs the ordinary word it strips to).</summary>
    public bool QuotedFormMatch;
    /// <summary>How many DISTINCT query words the path holds (proximity, 2+ word queries).</summary>
    public int WordsFound;
    /// <summary>The query's words appear in the path in typed order (proximity, 2+ word queries).</summary>
    public bool QueryInOrder;
    /// <summary>Width of the smallest run of path words holding every word counted in
    /// <see cref="WordsFound"/>; smaller is tighter.</summary>
    public int WordSpan;
}

/// <summary>
/// The catalog search's RANKING RULES: the comparators over <see cref="RankKeys"/>, the
/// query-side word sets every collector measures against, and the proximity measure. One copy
/// of the rules, used by the catalog collector, the local file collector and the UserSpace
/// merge of both.
/// </summary>
internal static class SeforimDbCatalogRankingKeys
{
    /// <summary>The accuracy keys alone (a 1-word query): see <see cref="Compare"/>.</summary>
    internal static int CompareFieldAccuracy(in RankKeys a, in RankKeys b) => Compare(a, b, proximity: false);

    /// <summary>The accuracy keys with proximity (a 2+ word query): see <see cref="Compare"/>.</summary>
    internal static int CompareProximity(in RankKeys a, in RankKeys b) => Compare(a, b, proximity: true);

    /// <summary>
    /// The ranking, most significant key first. 0 when the two hits tie on every key (the
    /// caller decides the tie, by catalog order).
    ///
    ///   literal match; every word in the hit's own title/path or author; the query names the
    ///   book (then the title explaining more of the query, then the one sized like it); more
    ///   query words in the path, repeats counted; the title written as the typed acronym;
    ///   for a 2+ word query (<paramref name="proximity"/>): more of the query's words found,
    ///   in typed order, closest together - the typed address "hilchot 3" is the TOC line
    ///   "hilchot 3", not a book titled "... volume 3" that merely shares its words;
    ///   a book title before a TOC line - typing one word that names books wants the books,
    ///   not a glossary line spelled exactly that way; the leaf typed in full.
    ///
    /// Proximity sits below the literal and title keys: it must not float an ordinary-word hit
    /// above the book actually written the way the query was typed.
    /// </summary>
    internal static int Compare(in RankKeys a, in RankKeys b, bool proximity)
    {
        if (a.IsLiteral != b.IsLiteral) return a.IsLiteral ? -1 : 1;
        if (a.MatchesTitleOrAuthor != b.MatchesTitleOrAuthor) return a.MatchesTitleOrAuthor ? -1 : 1;
        if (a.TitleCovered != b.TitleCovered) return a.TitleCovered ? -1 : 1;
        if (a.TitleCovered && a.TitleDistinctWords != b.TitleDistinctWords) return b.TitleDistinctWords.CompareTo(a.TitleDistinctWords);
        if (a.TitleCovered && a.TitleLengthGap != b.TitleLengthGap) return a.TitleLengthGap.CompareTo(b.TitleLengthGap);
        if (a.WordsMatched != b.WordsMatched) return b.WordsMatched.CompareTo(a.WordsMatched);
        // The book written as the typed acronym, quote and all, before lines that merely cite
        // the acronym however tightly: above proximity.
        if (a.QuotedFormMatch != b.QuotedFormMatch) return a.QuotedFormMatch ? -1 : 1;
        if (proximity)
        {
            if (a.WordsFound != b.WordsFound) return b.WordsFound.CompareTo(a.WordsFound);   // more words found first
            // Words in the typed order first: "Shmuel 2 perek 1" is not "Shmuel 1 / perek 2",
            // though both carry the same four words equally tightly.
            if (a.QueryInOrder != b.QueryInOrder) return a.QueryInOrder ? -1 : 1;
            if (a.WordSpan != b.WordSpan) return a.WordSpan.CompareTo(b.WordSpan);           // tighter cluster first
        }
        if ((a.Level == 0) != (b.Level == 0)) return a.Level == 0 ? -1 : 1;
        if (a.LeafCovered != b.LeafCovered) return a.LeafCovered ? -1 : 1;
        return 0;
    }

    /// <summary>Every word the ranking counts as typed: each alternative's words and their
    /// variants — except for an unquoted word that is also a key, which counts only as itself
    /// (<see cref="SeforimDbCatalogTextNormalizer.QueryToken.PreferLiteral"/>): "bamidbar"
    /// must not make Bamidbar Rabbah's title look fully typed.</summary>
    internal static HashSet<string> QueryWordSet(List<SeforimDbCatalogTextNormalizer.QueryToken> tokens, SeforimDbCatalogIndex.VariantLookup? variants)
    {
        var queryWords = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            IEnumerable<string[]> alts = token.PreferLiteral ? [token.RankingWords] : token.Alternatives;
            foreach (var alt in alts)
                foreach (var word in alt)
                {
                    queryWords.Add(word);
                    if (variants != null)
                        foreach (var variant in variants.Lookup(word)) queryWords.Add(variant);
                }
        }
        return queryWords;
    }

    /// <summary>The query's words with their multiplicity, one alternative per token
    /// (<see cref="SeforimDbCatalogTextNormalizer.QueryToken.RankingWords"/>).</summary>
    internal static Dictionary<string, int> QueryWordCounts(List<SeforimDbCatalogTextNormalizer.QueryToken> tokens)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var token in tokens)
            foreach (var word in token.RankingWords)
                counts[word] = counts.GetValueOrDefault(word) + 1;
        return counts;
    }

    /// <summary>The typed words in typed order, with repeats: the sequence the proximity keys
    /// measure. An abbreviation contributes its ranking alternative's words in order.</summary>
    internal static List<string> OrderWords(List<SeforimDbCatalogTextNormalizer.QueryToken> tokens)
    {
        var words = new List<string>(tokens.Count);
        foreach (var t in tokens) words.AddRange(t.RankingWords);
        return words;
    }

    /// <summary>The last " / "-separated segment of a path.</summary>
    internal static string LastSegment(string tocPath)
    {
        int i = tocPath.LastIndexOf(" / ", StringComparison.Ordinal);
        return i < 0 ? tocPath : tocPath.Substring(i + 3);
    }

    /// <summary>
    /// True when EVERY query token is present: per token, SOME alternative is fully present,
    /// each word by its own form or any variant — mirroring the searcher's query, so a hit the
    /// query matched through an ambiguous abbreviation is not demoted by this key.
    /// <paramref name="present"/> answers for one word (own form or variant: the caller folds
    /// variants in).
    /// </summary>
    internal static bool EveryTokenPresent(List<SeforimDbCatalogTextNormalizer.QueryToken> tokens, Func<string, bool> present)
    {
        if (tokens.Count == 0) return false;
        foreach (var token in tokens)
        {
            bool anyAlt = false;
            foreach (var alt in token.Alternatives)
            {
                bool allWords = true;
                foreach (var word in alt)
                    if (!present(word)) { allWords = false; break; }
                if (allWords) { anyAlt = true; break; }
            }
            if (!anyAlt) return false;
        }
        return true;
    }

    /// <summary>
    /// The proximity keys of one hit from the POSITIONS of the query's words in its path (the
    /// path field's postings), reusing its buffers across hits.
    ///
    /// Both measurements — the query-token-ORDER test and the word-PROXIMITY span — read the
    /// same per-word position lists, each sorted ascending as Lucene yields them.
    /// </summary>
    internal sealed class ProximityScratch
    {
        private readonly List<string> _orderWords;      // the typed sequence, with repeats
        private readonly List<int> _ordered = [];       // indexes into _orderWords: participating, WITH duplicates
        private readonly List<int> _distinct = [];      // indexes into _orderWords: participating, distinct words (first occurrence)
        private readonly int[] _cursor;

        public ProximityScratch(List<string> orderWords)
        {
            _orderWords = orderWords;
            _cursor = new int[orderWords.Count];
        }

        /// <summary>Measure one hit and write QueryInOrder / WordsFound / WordSpan onto its keys.
        /// <paramref name="positionsOf"/>(i) is the sorted positions of order word i in the path
        /// (empty when absent); equal words share one list.</summary>
        public void Measure(Func<int, List<int>> positionsOf, ref RankKeys keys)
        {
            // Participation, both forms, in ONE sweep over the query words. _ordered KEEPS
            // duplicates and _distinct does not, and that difference matters: the order test
            // must match a twice-typed word twice, while the span must not demand two
            // positions for it.
            _ordered.Clear();
            _distinct.Clear();
            for (int i = 0; i < _orderWords.Count; i++)
            {
                if (positionsOf(i).Count == 0) continue;
                _ordered.Add(i);
                bool seen = false;
                foreach (int d in _distinct)
                    if (string.Equals(_orderWords[d], _orderWords[i], StringComparison.Ordinal)) { seen = true; break; }
                if (!seen) _distinct.Add(i);
            }

            keys.QueryInOrder = TestOrder(positionsOf);
            var span = MeasureSpan(positionsOf);
            keys.WordsFound = span.Found;
            keys.WordSpan = span.Span;
        }

        /// <summary>Participating query words must appear in the path as an ordered subsequence
        /// in typed order: each word at a position after the previous word's. Fewer than two
        /// participating words means there is nothing to order — the hit counts as in order.</summary>
        private bool TestOrder(Func<int, List<int>> positionsOf)
        {
            if (_ordered.Count < 2) return true;
            int after = -1;
            foreach (int w in _ordered)
            {
                var positions = positionsOf(w);
                int next = -1;
                foreach (int p in positions)
                    if (p > after) { next = p; break; }
                if (next < 0) return false;
                after = next;
            }
            return true;
        }

        /// <summary>
        /// Width of the SMALLEST window of the path's word positions containing every distinct
        /// participating word. Adjacent words give a span equal to the word count (the tightest
        /// possible); words scattered across TOC levels give a wide one.
        ///
        /// Returns the participating count too, because a span only compares between hits that
        /// found the same number of words — "2 words within 2 slots" is tighter than "2 within
        /// 5", but says nothing against a hit where only 1 word was in the path at all. Fewer
        /// than two leaves nothing to measure: span 0, and the lower count already sorts the hit
        /// below every measured one.
        /// </summary>
        private (int Found, int Span) MeasureSpan(Func<int, List<int>> positionsOf)
        {
            int n = _distinct.Count;
            if (n < 2) return (n, 0);

            // Minimum-window sweep: advance the cursor of the word sitting leftmost — the only
            // move that can shrink the window — and record the narrowest span seen.
            Array.Clear(_cursor, 0, n);
            int best = int.MaxValue;
            while (true)
            {
                int min = int.MaxValue, max = int.MinValue, minWord = -1;
                for (int w = 0; w < n; w++)
                {
                    var positions = positionsOf(_distinct[w]);
                    if (_cursor[w] >= positions.Count) return (n, best);  // exhausted — no narrower window left
                    int pos = positions[_cursor[w]];
                    if (pos < min) { min = pos; minWord = w; }
                    if (pos > max) max = pos;
                }
                int span = max - min + 1;
                if (span < best) best = span;
                if (best == n) return (n, best);                 // tightest possible — stop early
                _cursor[minWord]++;
            }
        }
    }
}
