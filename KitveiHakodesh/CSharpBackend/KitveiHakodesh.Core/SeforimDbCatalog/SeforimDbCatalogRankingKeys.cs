namespace KitveiHakodesh.Core.SeforimDbCatalog;

/// <summary>
/// The catalog search's RANKING KEYS and comparators, shared by the catalog searcher and by
/// anything that ranks its own hits as if they were catalog hits (the UserSpace search ranks
/// local files this way). One copy of the rules: a hit, catalog or not, is measured by these
/// functions and ordered by <see cref="CompareFieldAccuracy"/> / <see cref="CompareProximity"/>.
/// </summary>
internal static class SeforimDbCatalogRankingKeys
{
    /// <summary>
    /// The FIELD-ACCURACY keys — the ones that can only be read from stored text, which the
    /// collector never sees. 0 when the two hits tie on every key (the caller decides the tie).
    /// </summary>
    internal static int CompareFieldAccuracy(CatalogTocHit a, CatalogTocHit b)
    {
        if (a.IsLiteral != b.IsLiteral) return a.IsLiteral ? -1 : 1;
        if (a.MatchesTitleOrAuthor != b.MatchesTitleOrAuthor) return a.MatchesTitleOrAuthor ? -1 : 1;
        if (a.TitleCovered != b.TitleCovered) return a.TitleCovered ? -1 : 1;
        if (a.TitleCovered && a.TitleDistinctWords != b.TitleDistinctWords) return b.TitleDistinctWords.CompareTo(a.TitleDistinctWords);
        if (a.TitleCovered && a.TitleLengthGap != b.TitleLengthGap) return a.TitleLengthGap.CompareTo(b.TitleLengthGap);
        if (a.LeafCovered != b.LeafCovered) return a.LeafCovered ? -1 : 1;
        if (a.WordsMatched != b.WordsMatched) return b.WordsMatched.CompareTo(a.WordsMatched);
        if (a.QuotedFormMatch != b.QuotedFormMatch) return a.QuotedFormMatch ? -1 : 1;
        return 0;
    }

    /// <summary>
    /// The field-accuracy keys, then how tightly the query's words sit together in the path.
    /// Carries the field-accuracy keys too: the proximity pass re-sorts the same window, so a
    /// key it did not carry would be undone there — proximity must not float an ordinary-word
    /// hit above the book actually written the way the query was typed. Only meaningful for a
    /// 2+ word query (the proximity keys are measured only then).
    /// </summary>
    internal static int CompareProximity(CatalogTocHit a, CatalogTocHit b)
    {
        int c = CompareFieldAccuracy(a, b);
        if (c != 0) return c;
        c = b.WordsFound.CompareTo(a.WordsFound);   // more words found first
        if (c != 0) return c;
        // Words in the typed order first: "Shmuel 2 perek 1" is not "Shmuel 1 / perek 2",
        // though both carry the same four words equally tightly.
        if (a.QueryInOrder != b.QueryInOrder) return a.QueryInOrder ? -1 : 1;
        return a.WordSpan.CompareTo(b.WordSpan);    // tighter cluster first
    }

    /// <summary>Stable re-sort of the materialized window on <see cref="CompareFieldAccuracy"/>.</summary>
    internal static void StableSortByFieldAccuracy(List<CatalogTocHit> hits) => StableSort(hits, CompareFieldAccuracy);

    /// <summary>Stable re-sort of the materialized window on <see cref="CompareProximity"/>.</summary>
    internal static void StableSortByProximity(List<CatalogTocHit> hits) => StableSort(hits, CompareProximity);

    /// <summary>Sort an index permutation so the arrival position stays attached to each hit
    /// without a per-hit dictionary lookup in the comparer; ties keep arrival order.</summary>
    private static void StableSort(List<CatalogTocHit> hits, Comparison<CatalogTocHit> compare)
    {
        int n = hits.Count;
        var arrival = new int[n];
        var items = new CatalogTocHit[n];
        for (int i = 0; i < n; i++) { items[i] = hits[i]; arrival[i] = i; }

        Array.Sort(arrival, (x, y) =>
        {
            int c = compare(items[x], items[y]);
            return c != 0 ? c : x.CompareTo(y);   // else keep arrival order
        });

        hits.Clear();
        for (int i = 0; i < n; i++) hits.Add(items[arrival[i]]);
    }

    /// <summary>Every word the ranking counts as typed: each alternative's words and their
    /// variants — except for an unquoted word that is also a key, which counts only as itself
    /// (<see cref="SeforimDbCatalogTextNormalizer.QueryToken.PreferLiteral"/>): "bamidbar"
    /// must not make Bamidbar Rabbah's title look fully typed. Built once per pass;
    /// <see cref="IsCovered"/> tests each hit's title against it.</summary>
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

    /// <summary>How many of the query's words, with repetition, <paramref name="pathTokens"/>
    /// holds: per query word, min(times in query, times in path), a path occurrence of any
    /// variant of the word counting for it.</summary>
    internal static int CountMatched(List<string> pathTokens, Dictionary<string, int> queryCounts, SeforimDbCatalogIndex.VariantLookup? variants)
    {
        if (queryCounts.Count == 0 || pathTokens.Count == 0) return 0;
        var pathCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in pathTokens) pathCounts[t] = pathCounts.GetValueOrDefault(t) + 1;

        int matched = 0;
        foreach (var (word, wanted) in queryCounts)
        {
            int have = pathCounts.GetValueOrDefault(word);
            if (have < wanted && variants != null)
                foreach (var variant in variants.Lookup(word))
                    have += pathCounts.GetValueOrDefault(variant);
            matched += Math.Min(have, wanted);
        }
        return matched;
    }

    /// <summary>How many DISTINCT words of <paramref name="tokens"/> the query can match.</summary>
    internal static int DistinctFound(List<string> tokens, HashSet<string> queryWords)
    {
        if (tokens.Count == 0) return 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in tokens)
            if (queryWords.Contains(word)) seen.Add(word);
        return seen.Count;
    }

    /// <summary>The last " / "-separated segment of a path without its title.</summary>
    internal static string LastSegment(string tocPath)
    {
        int i = tocPath.LastIndexOf(" / ", StringComparison.Ordinal);
        return i < 0 ? tocPath : tocPath.Substring(i + 3);
    }

    /// <summary>True when EVERY title word is a query word — the query names the book. An
    /// abbreviation query counts through its expansions, so a quoted acronym for a two-word
    /// title covers that title.</summary>
    internal static bool IsCovered(List<string> titleTokens, HashSet<string> queryWords)
    {
        if (titleTokens.Count == 0) return false;
        foreach (var word in titleTokens)
            if (!queryWords.Contains(word)) return false;
        return true;
    }

    /// <summary>
    /// True when EVERY query token is present among <paramref name="pathTokens"/> or
    /// <paramref name="authorTokens"/> (the hit's own title/TOC path or author), rather than
    /// only in the category path.
    ///
    /// The membership test MIRRORS the searcher's BuildQuery: per token, satisfied when SOME
    /// alternative is fully present, each word by its own form or any variant. Testing only the
    /// canonical alternative demotes exactly the hits this key exists to promote — many
    /// abbreviation keys are ambiguous, and some carry their self-alternative in a later slot.
    /// </summary>
    internal static bool EveryTokenPresent(
        List<SeforimDbCatalogTextNormalizer.QueryToken> tokens, List<string> pathTokens, List<string>? authorTokens,
        SeforimDbCatalogIndex.VariantLookup? variants)
    {
        if (tokens.Count == 0) return false;

        bool Present(string word)
        {
            if (pathTokens.Contains(word)) return true;
            if (authorTokens != null && authorTokens.Contains(word)) return true;
            if (variants != null)
                foreach (var variant in variants.Lookup(word))
                {
                    if (pathTokens.Contains(variant)) return true;
                    if (authorTokens != null && authorTokens.Contains(variant)) return true;
                }
            return false;
        }

        foreach (var token in tokens)
        {
            bool anyAlt = false;
            foreach (var alt in token.Alternatives)
            {
                bool allWords = true;
                foreach (var word in alt)
                    if (!Present(word)) { allWords = false; break; }
                if (allWords) { anyAlt = true; break; }
            }
            if (!anyAlt) return false; // this token only matched the category path
        }
        return true;
    }

    /// <summary>
    /// Reusable scratch for measuring one hit's path against the query words.
    ///
    /// Both measurements this makes — the query-token-ORDER test and the word-PROXIMITY span —
    /// need the path's tokens, so they come off ONE tokenization with buffers reused across
    /// hits. Measuring them separately tokenized every hit's path twice, per keystroke.
    /// </summary>
    internal sealed class PathScratch
    {
        private readonly List<string> _ordered;   // participating, WITH duplicates
        private readonly List<string> _distinct;  // participating, distinct
        private readonly List<int> _positions;    // flattened, per-word ranges
        private readonly int[] _starts;
        private readonly int[] _cursor;

        public PathScratch(int queryWordCount)
        {
            _ordered = new List<string>(queryWordCount);
            _distinct = new List<string>(queryWordCount);
            _positions = new List<int>(64);
            _starts = new int[queryWordCount + 1];
            _cursor = new int[queryWordCount];
        }

        /// <summary>Measure one hit and write QueryInOrder / WordsFound / WordSpan onto it.</summary>
        public void Measure(List<string> pathTokens, List<string> queryWords, CatalogTocHit hit)
        {

            // Participation, both forms, in ONE sweep over the query words. Membership is
            // tested against the path token list directly: it is short (a TOC path), and a scan
            // beats allocating a HashSet per hit.
            //
            // _ordered KEEPS duplicates and _distinct does not, and that difference matters:
            // the order test must match a twice-typed word twice, while the span must not
            // demand two positions for it.
            _ordered.Clear();
            _distinct.Clear();
            foreach (var t in queryWords)
            {
                bool inPath = false;
                for (int i = 0; i < pathTokens.Count; i++)
                    if (string.Equals(pathTokens[i], t, StringComparison.Ordinal)) { inPath = true; break; }
                if (!inPath) continue;
                _ordered.Add(t);
                if (!_distinct.Contains(t)) _distinct.Add(t);
            }

            hit.QueryInOrder = TestOrder(pathTokens);
            var span = MeasureSpan(pathTokens);
            hit.WordsFound = span.Found;
            hit.WordSpan = span.Span;
        }

        /// <summary>Participating query words must appear in the path as an ordered subsequence
        /// in typed order. Fewer than two participating words means there is nothing to order —
        /// the hit counts as in order.</summary>
        private bool TestOrder(List<string> pathTokens)
        {
            if (_ordered.Count < 2) return true;
            int qi = 0;
            foreach (var tok in pathTokens)
                if (string.Equals(tok, _ordered[qi], StringComparison.Ordinal) && ++qi == _ordered.Count)
                    return true;
            return false;
        }

        /// <summary>
        /// Width of the SMALLEST window of the path's token sequence containing every distinct
        /// participating word. Adjacent words give a span equal to the word count (the tightest
        /// possible); words scattered across TOC levels give a wide one.
        ///
        /// Returns the participating count too, because a span only compares between hits that
        /// found the same number of words in the path — "2 words within 2 slots" is tighter than
        /// "2 within 5", but says nothing against a hit where only 1 word was in the path at
        /// all. Fewer than two leaves nothing to measure: span 0, and the lower count already
        /// sorts the hit below every measured one.
        /// </summary>
        private (int Found, int Span) MeasureSpan(List<string> pathTokens)
        {
            int n = _distinct.Count;
            if (n < 2) return (n, 0);

            // Positions flattened into one list with per-word ranges — no dictionary and no
            // per-word list allocation.
            _positions.Clear();
            for (int w = 0; w < n; w++)
            {
                _starts[w] = _positions.Count;
                string word = _distinct[w];
                for (int i = 0; i < pathTokens.Count; i++)
                    if (string.Equals(pathTokens[i], word, StringComparison.Ordinal)) _positions.Add(i);
            }
            _starts[n] = _positions.Count;

            // Minimum-window sweep: advance the cursor of the word sitting leftmost — the only
            // move that can shrink the window — and record the narrowest span seen.
            Array.Clear(_cursor, 0, n);
            int best = int.MaxValue;
            while (true)
            {
                int min = int.MaxValue, max = int.MinValue, minWord = -1;
                for (int w = 0; w < n; w++)
                {
                    int lo = _starts[w] + _cursor[w];
                    if (lo >= _starts[w + 1]) return (n, best);  // exhausted — no narrower window left
                    int pos = _positions[lo];
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
