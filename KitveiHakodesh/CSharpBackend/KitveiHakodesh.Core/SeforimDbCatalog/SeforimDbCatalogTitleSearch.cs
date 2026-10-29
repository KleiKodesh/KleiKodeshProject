using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Search.Similarities;
using Lucene.Net.Util;

namespace KitveiHakodesh.Core.SeforimDbCatalog;

/// <summary>
/// Book-title search over the catalog index — the Lucene port of the former frontend in-memory
/// title matcher (book-catalog filter, full-text-search book filter, daily-learning
/// lookups). Level-0 docs only (one per book); every word must match (exact or spelling
/// variant) in title, catalog path or authors; the LAST word may also match as a prefix. The
/// ranking is the old frontend's: starts-with promotion for a one-word prefix, then title-hit
/// words, then fewer title tokens, then tree order — all of it one Lucene query sorted on
/// score and doc-values (see <see cref="RankedTitleQuery"/>).
/// </summary>
public sealed partial class SeforimDbCatalogIndex
{
    /// <summary>Cap on the partly typed multi-word abbreviation keys the last word is
    /// completed to (see <see cref="SeforimDbCatalogTextNormalizer.CompleteTrailingAbbreviationKeys"/>).</summary>
    private const int MaxKeyCompletions = 16;

    /// <summary>Score of the starts-with promotion; above any count of title-hit words.</summary>
    private const float StartsWithBoost = 1_000_000f;

    /// <summary>Scores are exact clause counts: no query normalization.</summary>
    private static readonly Similarity CountingSimilarity = new UnnormalizedSimilarity();

    /// <summary>Book ids in rank order; empty when nothing matched or no reader is open.
    /// Uncapped.</summary>
    public List<int> SearchTitles(string query, CancellationToken ct = default)
    {
        var tokens = SeforimDbCatalogTextNormalizer.TokenizeQuery(query);
        if (tokens.Count == 0) return [];
        if (!TryAcquireSearcher(out var shared))
        {
            if (!TryOpenActive() || !TryAcquireSearcher(out shared)) return [];
        }
        try
        {
            var reader = shared.IndexReader;
            var searcher = new IndexSearcher(reader) { Similarity = CountingSimilarity };
            var variants = new VariantLookup(reader);
            Query matched = BuildTitleQuery(tokens, variants, lastPrefix: null);
            bool lastIsPrefixOnly = false;

            // The last typed word as a plain normalized word, for the prefix match.
            var chunks = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var plain = SeforimDbCatalogTextNormalizer.Tokenize(chunks[^1]);
            string? lastPrefix = plain.Count > 0 ? plain[^1] : null;

            // A partial last word that is also an abbreviation key "matches exactly" through its
            // expansion; that must not hide the books the word is the start of ("vayikr").
            bool lastIsKey = !tokens[^1].IsPlain && tokens[^1].PreferLiteral;
            if (lastPrefix != null && (lastIsKey || !AnyMatch(searcher, matched)))
            {
                matched = BuildTitleQuery(tokens, variants, lastPrefix);
                lastIsPrefixOnly = true;
            }

            // A partly typed MULTI-WORD key ("pnei yehos"): the index holds such a title under
            // the key's canonical expansion, not its own words, so neither pass above can see it.
            // Complete the key and search the completed query exactly, like the index tokenized it.
            var completions = SeforimDbCatalogTextNormalizer.CompleteTrailingAbbreviationKeys(query, MaxKeyCompletions);
            if (completions.Count > 0)
            {
                var any = new BooleanQuery { { matched, Occur.SHOULD } };
                foreach (var completed in completions)
                    any.Add(BuildTitleQuery(SeforimDbCatalogTextNormalizer.TokenizeQuery(completed), variants, lastPrefix: null), Occur.SHOULD);
                matched = any;
            }

            var words = new List<string>();
            foreach (var t in tokens) words.AddRange(t.RankingWords);
            return RankedBookIds(searcher, RankedTitleQuery(matched, words, lastIsPrefixOnly && tokens.Count == 1 ? query.Trim() : null), ct);
        }
        finally
        {
            shared.IndexReader.DecRef();
        }
    }

    /// <summary>
    /// The match query, scored for the ranking: the match itself scores 0; each ranking word
    /// that starts some title word adds 1 (one per word, repeats included); a title that
    /// starts with <paramref name="startsWithPromotion"/> adds <see cref="StartsWithBoost"/>.
    /// Sorted (<see cref="TitleRankSort"/>) by that score, then fewer distinct title words,
    /// then tree order.
    /// </summary>
    private static Query RankedTitleQuery(Query matched, List<string> words, string? startsWithPromotion)
    {
        var ranked = new BooleanQuery(disableCoord: true) { { new ConstantScoreQuery(matched) { Boost = 0f }, Occur.MUST } };
        foreach (var w in words)
            ranked.Add(new ConstantScoreQuery(new PrefixQuery(new Term(FieldBookTitle, w))) { Boost = 1f }, Occur.SHOULD);
        if (startsWithPromotion != null)
            ranked.Add(new ConstantScoreQuery(new PrefixQuery(new Term(FieldBookTitleJoined, startsWithPromotion))) { Boost = StartsWithBoost }, Occur.SHOULD);
        return ranked;
    }

    private static readonly Sort TitleRankSort = new(
        SortField.FIELD_SCORE,
        new SortField(FieldBookTitleWordsDv, SortFieldType.INT64),
        new SortField(FieldTreeOrderDv, SortFieldType.INT64));

    private static List<int> RankedBookIds(IndexSearcher searcher, Query query, CancellationToken ct)
    {
        var reader = searcher.IndexReader;
        // Every book can match; the level-0 doc count bounds the queue (results stay uncapped).
        var levelZero = new BytesRef();
        NumericUtils.Int32ToPrefixCoded(0, 0, levelZero);
        int books = Math.Max(1, reader.DocFreq(new Term(FieldLevel, levelZero)));
        ct.ThrowIfCancellationRequested();
        var top = searcher.Search(query, null, books, TitleRankSort, doDocScores: false, doMaxScore: false);
        var leaves = reader.Leaves;
        var ids = new List<int>(top.ScoreDocs.Length);
        foreach (var sd in top.ScoreDocs)
        {
            var leaf = leaves[ReaderUtil.SubIndex(sd.Doc, leaves)];
            var bookIds = leaf.AtomicReader.GetNumericDocValues(FieldBookIdDv);
            ids.Add((int)(bookIds?.Get(sd.Doc - leaf.DocBase) ?? 0));
        }
        return ids;
    }

    private static bool AnyMatch(IndexSearcher searcher, Query query) => searcher.Search(query, 1).TotalHits > 0;

    /// <summary>The catalog query (<see cref="BuildQuery"/>, never fuzzy) restricted to level 0.
    /// With <paramref name="lastPrefix"/>, the last clause also accepts that word as a prefix on
    /// any field, he-prefixed both ways like the old frontend ("kuz" finds "ha-kuzari").</summary>
    private static BooleanQuery BuildTitleQuery(List<SeforimDbCatalogTextNormalizer.QueryToken> tokens, VariantLookup? variants, string? lastPrefix)
    {
        var bq = BuildQuery(tokens, fuzzy: false, variants);
        if (lastPrefix != null)
        {
            var either = new BooleanQuery { { bq.Clauses[^1].Query, Occur.SHOULD } };
            var stems = new List<string> { lastPrefix, "ה" + lastPrefix };
            if (SeforimDbCatalogTextNormalizer.StripHePrefix(lastPrefix) is { } stripped) stems.Add(stripped);
            foreach (var field in IndexedFields)
                foreach (var stem in stems)
                    either.Add(new PrefixQuery(new Term(field, stem)), Occur.SHOULD);
            bq.Clauses[^1].Query = either;
        }
        bq.Add(NumericRangeQuery.NewInt32Range(FieldLevel, 0, 0, true, true), Occur.MUST);
        return bq;
    }

    /// <summary>Default similarity without query normalization, so a constant-score clause
    /// scores exactly its boost and a sum of them is an exact count.</summary>
    private sealed class UnnormalizedSimilarity : DefaultSimilarity
    {
        public override float QueryNorm(float sumOfSquaredWeights) => 1f;
    }
}
