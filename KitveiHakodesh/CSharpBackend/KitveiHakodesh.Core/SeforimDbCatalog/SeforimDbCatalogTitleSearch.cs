using Lucene.Net.Index;
using Lucene.Net.Search;

namespace KitveiHakodesh.Core.SeforimDbCatalog;

/// <summary>
/// PROTOTYPE (test only): book-title search over the catalog index - the Lucene port of the
/// frontend's filterBooksByWords. Level-0 docs only (one per book), every word must match
/// (exact or spelling variant) in title/catalog path/authors, the LAST word may match as a
/// prefix, and the ranking is the frontend's: exact-tier filter, then title-hit words, then
/// fewer title tokens, then tree order.
/// </summary>
public sealed partial class SeforimDbCatalogIndex
{
    private (IndexReader Reader, Dictionary<long, (int BookId, List<string> TitleTokens)> Map)? _bookTitlesByRank;

    /// <summary>Book ids in rank order. <paramref name="lastWordPrefix"/>: the last typed word
    /// also matches as a prefix. <paramref name="fuzzy"/>: append the sparse-fuzzy tail like
    /// <see cref="Search"/> does.</summary>
    public List<int> SearchTitles(string query, bool lastWordPrefix = true, bool fuzzy = false, CancellationToken ct = default)
    {
        var tokens = SeforimDbCatalogTextNormalizer.TokenizeQuery(query);
        if (tokens.Count == 0) return [];
        if (!TryAcquireSearcher(out var searcher, out var variants, out _))
        {
            if (!TryOpenActive() || !TryAcquireSearcher(out searcher, out variants, out _)) return [];
        }
        try
        {
            var books = GetBookTitles(searcher.IndexReader);
            var exact = CollectTitleDocs(searcher, BuildTitleQuery(tokens, variants, prefix: false, fuzzy: false), ct);
            List<(int Doc, long Order)> matched = exact;
            bool lastIsPrefixOnly = false;
            if (exact.Count == 0 && lastWordPrefix && tokens[^1].IsPlain)
            {
                matched = CollectTitleDocs(searcher, BuildTitleQuery(tokens, variants, prefix: true, fuzzy: false), ct);
                lastIsPrefixOnly = true;
            }

            var words = new List<string>();
            foreach (var t in tokens) words.AddRange(t.RankingWords);
            var ranked = RankTitleMatches(matched, books, words, lastIsPrefixOnly && tokens.Count == 1 ? query.Trim() : null);

            if (fuzzy && ranked.Count < SparseFuzzyThreshold && tokens.Any(HasFuzzyableWord))
            {
                var seen = new HashSet<int>(ranked);
                var fuzzyDocs = CollectTitleDocs(searcher, BuildTitleQuery(tokens, variants, prefix: lastWordPrefix && tokens[^1].IsPlain, fuzzy: true), ct);
                foreach (var id in RankTitleMatches(fuzzyDocs, books, words, null))
                    if (seen.Add(id)) ranked.Add(id);
            }
            return ranked;
        }
        finally
        {
            searcher.IndexReader.DecRef();
        }
    }

    private static List<int> RankTitleMatches(
        List<(int Doc, long Order)> matched, Dictionary<long, (int BookId, List<string> TitleTokens)> books,
        List<string> words, string? startsWithPromotion)
    {
        var rows = new List<(int BookId, int TitleHits, int TitleCount, long Order, bool Starts)>(matched.Count);
        foreach (var (_, order) in matched)
        {
            long rank = order >> 24;
            if (!books.TryGetValue(rank, out var book)) continue;
            int hits = 0;
            foreach (var w in words)
                foreach (var tok in book.TitleTokens)
                    if (tok.StartsWith(w, StringComparison.Ordinal)) { hits++; break; }
            bool starts = startsWithPromotion != null && book.TitleTokens.Count > 0
                && string.Join(" ", book.TitleTokens).StartsWith(startsWithPromotion, StringComparison.Ordinal);
            rows.Add((book.BookId, hits, new HashSet<string>(book.TitleTokens, StringComparer.Ordinal).Count, order, starts));
        }
        rows.Sort((a, b) =>
        {
            if (a.Starts != b.Starts) return a.Starts ? -1 : 1;
            if (a.TitleHits != b.TitleHits) return b.TitleHits.CompareTo(a.TitleHits);
            if (a.TitleCount != b.TitleCount) return a.TitleCount.CompareTo(b.TitleCount);
            return a.Order.CompareTo(b.Order);
        });
        var ids = new List<int>(rows.Count);
        foreach (var r in rows) ids.Add(r.BookId);
        return ids;
    }

    private static BooleanQuery BuildTitleQuery(List<SeforimDbCatalogTextNormalizer.QueryToken> tokens, VariantIndex? variants, bool prefix, bool fuzzy)
    {
        var bq = BuildQuery(tokens, fuzzy, variants);
        if (prefix)
        {
            // Replace the last clause with (exact/variants OR prefix on any field).
            var last = bq.Clauses[^1];
            var either = new BooleanQuery { { last.Query, Occur.SHOULD } };
            foreach (var field in IndexedFields)
                either.Add(new PrefixQuery(new Term(field, tokens[^1].Word)), Occur.SHOULD);
            bq.Clauses[^1].Query = either;
        }
        bq.Add(NumericRangeQuery.NewInt32Range(FieldLevel, 0, 0, true, true), Occur.MUST);
        return bq;
    }

    private static List<(int Doc, long Order)> CollectTitleDocs(IndexSearcher searcher, Query query, CancellationToken ct)
    {
        var c = new TitleDocCollector(ct);
        searcher.Search(query, c);
        return c.Docs;
    }

    private Dictionary<long, (int BookId, List<string> TitleTokens)> GetBookTitles(IndexReader reader)
    {
        lock (_lock)
        {
            if (_bookTitlesByRank is { } cached && ReferenceEquals(cached.Reader, reader)) return cached.Map;
        }
        var map = new Dictionary<long, (int, List<string>)>(16384);
        foreach (var ctx in reader.Leaves)
        {
            var levels = ctx.AtomicReader.GetNumericDocValues(FieldLevelDv);
            var orders = ctx.AtomicReader.GetNumericDocValues(FieldTreeOrderDv);
            if (levels is null || orders is null) continue;
            var live = ctx.AtomicReader.LiveDocs;
            for (int d = 0; d < ctx.AtomicReader.MaxDoc; d++)
            {
                if (live is not null && !live.Get(d)) continue;
                if (levels.Get(d) != 0) continue;
                long rank = orders.Get(d) >> 24;
                if (map.ContainsKey(rank)) continue;
                var doc = ctx.AtomicReader.Document(d);
                map[rank] = (doc.GetField(FieldBookId)?.GetInt32Value() ?? 0,
                    SeforimDbCatalogTextNormalizer.Tokenize(doc.Get(FieldFullTocPath) ?? ""));
            }
        }
        lock (_lock) _bookTitlesByRank = (reader, map);
        return map;
    }

    private sealed class TitleDocCollector(CancellationToken ct) : ICollector
    {
        public readonly List<(int Doc, long Order)> Docs = [];
        private int _docBase;
        private NumericDocValues? _orders;
        public void SetScorer(Scorer scorer) { }
        public void SetNextReader(AtomicReaderContext context)
        {
            _docBase = context.DocBase;
            _orders = context.AtomicReader.GetNumericDocValues(FieldTreeOrderDv);
        }
        public bool AcceptsDocsOutOfOrder => true;
        public void Collect(int doc)
        {
            if ((Docs.Count & 1023) == 0) ct.ThrowIfCancellationRequested();
            Docs.Add((_docBase + doc, _orders?.Get(doc) ?? long.MaxValue));
        }
    }
}
