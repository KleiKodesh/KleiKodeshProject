using System.Collections.Concurrent;
using System.Text;
using Microsoft.Data.Sqlite;

namespace AssocBuilder;

/// <summary>
/// Pass 1: token frequencies -> vocabulary + Hebrew prefix folding.
///
/// Folding is the single largest measured quality win (2.9x P@20, FINDINGS.md
/// §9): Hebrew grammatical particles split one concept across dozens of tokens,
/// and pooling them thickens every profile downstream.
/// </summary>
internal static class Vocabulary
{
    // Two-letter stacks come FIRST — longest match wins, or `וה` strips as bare
    // `ו` and leaves the article attached.
    private static readonly string[] Prefixes =
    [
        "וכש", "ולכ", "ובכ",
        "וה", "ול", "וב", "וכ", "ומ", "וש",
        "כש", "לכ", "בכ", "מה", "שה", "הל", "הב",
        "ו", "ה", "ב", "ל", "כ", "מ", "ש",
    ];

    /// <summary>High-frequency function words whose own first letter coincides
    /// with a particle. The frequency guard alone will not catch these, because
    /// the remainder is also a real word (של -> ל, מה -> ה).</summary>
    private static readonly HashSet<string> NeverStrip =
    [
        "ואת", "ויהי", "והוא", "והיה", "כי", "כל", "כה", "כן", "כאשר", "כמו",
        "לא", "לו", "לה", "לכ", "למה", "מה", "מי", "מנ", "משה", "מאד", "מאה",
        "בנ", "בת", "בית", "בא", "בו", "הוא", "היא", "הנה", "הימ", "המ", "הנ",
        "של", "שמ", "שנה", "שני", "שמע", "שר", "שלמה", "ולא", "ואמר",
        "ו", "ה", "ב", "ל", "כ", "מ", "ש",
    ];

    private const int MinStemLen = 3;

    internal static (string[] Vocab, Dictionary<string, int> WordId, int[] Counts,
                     Dictionary<string, string> PrefixMap, long Tokens, long Units)
        Build(string db, List<int> books, int minCount, bool stripPrefixes,
              int minStemFreq, double stemRatio, int workers,
              bool lemmatize = false, string? bridgeCsv = null)
    {
        // Shard the scan: each worker counts its own books, then the partials
        // are summed. Counting is additive, so this is exact.
        var shards = Shard(books, workers);
        var partials = new ConcurrentBag<(Dictionary<string, int> F, long Tok, long Units)>();

        Parallel.ForEach(shards, new ParallelOptions { MaxDegreeOfParallelism = workers },
            shard =>
            {
                var freq = new Dictionary<string, int>(1 << 16, StringComparer.Ordinal);
                long tok = 0, units = 0;
                var toks = new List<string>(256);
                var buf  = new StringBuilder(64);
                foreach (var line in Corpus.ReadLines(db, shard))
                {
                    HebrewTokenizer.Tokenize(line, toks, buf);
                    if (toks.Count == 0) continue;
                    units++;
                    tok += toks.Count;
                    foreach (var w in toks)
                        freq[w] = freq.TryGetValue(w, out int c) ? c + 1 : 1;
                }
                partials.Add((freq, tok, units));
            });

        var total = new Dictionary<string, int>(1 << 20, StringComparer.Ordinal);
        long nTok = 0, nUnits = 0;
        foreach (var (f, tk, u) in partials)
        {
            nTok += tk; nUnits += u;
            foreach (var (w, c) in f)
                total[w] = total.TryGetValue(w, out int e) ? e + c : c;
        }

        var prefixMap = stripPrefixes
            ? BuildPrefixMap(total, minStemFreq, stemRatio)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        // Lemmatization runs AFTER prefix folding and is composed into the same
        // map, so a token is rewritten exactly once at count time.
        //
        // Order matters: the prefix heuristic is cheap and corpus-derived, the
        // lexicon is curated and reaches suffixes the heuristic cannot. Applying
        // the lexicon to the ALREADY-prefix-stripped form means `דמלכא` needs only
        // `מלכא -> מלכ` from the lexicon rather than an entry for every
        // prefix+suffix combination.
        if (lemmatize)
        {
            var raw = Lexicon.LoadLemmas();
            if (bridgeCsv is not null)
                Lexicon.MergeBridge(raw, bridgeCsv, 0.5);
            if (raw.Count > 0)
            {
                // Frequencies as they stand after prefix folding — that is the
                // vocabulary the lemma targets have to exist in.
                var afterPrefix = new Dictionary<string, int>(total.Count, StringComparer.Ordinal);
                foreach (var (w, c) in total)
                {
                    string k = prefixMap.TryGetValue(w, out var s) ? s : w;
                    afterPrefix[k] = afterPrefix.TryGetValue(k, out int e) ? e + c : c;
                }
                var vocabNow = afterPrefix.Keys.ToArray();
                var countsNow = vocabNow.Select(w => afterPrefix[w]).ToArray();
                var lemmas = Lexicon.RestrictToCorpus(raw, vocabNow, countsNow, minCount);

                // Compose: surface -> (prefix-stripped) -> lemma.
                foreach (var w in total.Keys)
                {
                    string mid = prefixMap.TryGetValue(w, out var s) ? s : w;
                    if (lemmas.TryGetValue(mid, out var lem) && lem != w)
                        prefixMap[w] = lem;
                }
            }
        }

        // Fold the counts so the vocabulary reflects post-fold frequency.
        Dictionary<string, int> folded;
        if (prefixMap.Count > 0)
        {
            folded = new Dictionary<string, int>(total.Count, StringComparer.Ordinal);
            foreach (var (w, c) in total)
            {
                string key = prefixMap.TryGetValue(w, out var s) ? s : w;
                folded[key] = folded.TryGetValue(key, out int e) ? e + c : c;
            }
        }
        else folded = total;

        // Ids in descending-frequency order: the hottest words get the smallest
        // ids, so their rows sit together at the front of the table.
        var kept = folded.Where(kv => kv.Value >= minCount)
                         .OrderByDescending(kv => kv.Value)
                         .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                         .ToArray();

        var vocab  = new string[kept.Length];
        var counts = new int[kept.Length];
        var wordId = new Dictionary<string, int>(kept.Length, StringComparer.Ordinal);
        for (int i = 0; i < kept.Length; i++)
        {
            vocab[i]  = kept[i].Key;
            counts[i] = kept[i].Value;
            wordId[kept[i].Key] = i;
        }
        return (vocab, wordId, counts, prefixMap, nTok, nUnits);
    }

    /// <summary>
    /// Surface form -> stripped lexeme, only where stripping is safe.
    ///
    /// The guard that matters: the remainder must itself be a corpus word above
    /// a frequency floor. Without it, `משה` becomes `שה`. Measured on the Tanach,
    /// the frequency floor (minStemFreq) is the knob that governs quality; the
    /// `stemRatio` direction test is flat across its whole range and is kept only
    /// as a cheap guard (FINDINGS.md §9).
    /// </summary>
    private static Dictionary<string, string> BuildPrefixMap(
        Dictionary<string, int> freq, int minStemFreq, double stemRatio)
    {
        var map = new Dictionary<string, string>(1 << 16, StringComparer.Ordinal);
        foreach (var (w, n) in freq)
        {
            if (w.Length < MinStemLen + 1 || NeverStrip.Contains(w)) continue;
            foreach (var p in Prefixes)
            {
                if (!w.StartsWith(p, StringComparison.Ordinal)) continue;
                string stem = w[p.Length..];
                if (stem.Length < MinStemLen || NeverStrip.Contains(stem)) continue;
                if (freq.TryGetValue(stem, out int sf) &&
                    sf >= minStemFreq && sf >= stemRatio * n)
                {
                    map[w] = stem;
                    break;
                }
            }
        }
        return map;
    }

    internal static List<List<int>> Shard(List<int> books, int workers)
    {
        int n = Math.Max(1, workers);
        var shards = new List<List<int>>(n);
        for (int i = 0; i < n; i++) shards.Add([]);
        // Interleave so no single worker inherits all of one large category.
        for (int i = 0; i < books.Count; i++) shards[i % n].Add(books[i]);
        return shards.Where(s => s.Count > 0).ToList();
    }
}

/// <summary>Streams line content for a set of books. Never materializes the corpus.</summary>
internal static class Corpus
{
    internal static IEnumerable<string> ReadLines(string db, List<int> books)
    {
        using var con = new SqliteConnection($"Data Source={db};Mode=ReadOnly;Cache=Shared");
        con.Open();
        using (var pragma = con.CreateCommand())
        {
            pragma.CommandText = "pragma mmap_size=1073741824; pragma temp_store=MEMORY";
            pragma.ExecuteNonQuery();
        }

        // Chunk the IN list: SQLite caps expression tree depth, and a 7,000-id
        // list blows it.
        const int Chunk = 400;
        for (int off = 0; off < books.Count; off += Chunk)
        {
            var slice = books.GetRange(off, Math.Min(Chunk, books.Count - off));
            using var cmd = con.CreateCommand();
            cmd.CommandText =
                $"select content from line where bookId in ({string.Join(",", slice)}) "
                + "and heRef is not null";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (!r.IsDBNull(0))
                    yield return r.GetString(0);
        }
    }
}
