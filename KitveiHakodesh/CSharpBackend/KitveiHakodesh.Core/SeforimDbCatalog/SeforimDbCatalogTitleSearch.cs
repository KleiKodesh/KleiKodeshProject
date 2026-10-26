using Lucene.Net.Index;
using Lucene.Net.Search;

namespace KitveiHakodesh.Core.SeforimDbCatalog;

/// <summary>
/// Book-title search over the catalog index — the Lucene port of the former frontend in-memory
/// title matcher (book-catalog filter, full-text-search book filter, daily-learning
/// lookups). Level-0 docs only (one per book); every word must match (exact or spelling
/// variant) in title, catalog path or authors; the LAST word may also match as a prefix. The
/// ranking is the old frontend's: starts-with promotion for a one-word prefix, then title-hit
/// words, then fewer title tokens, then tree order.
/// </summary>
public sealed partial class SeforimDbCatalogIndex
{
    private (IndexReader Reader, Dictionary<long, (int BookId, List<string> TitleTokens)> Map)? _bookTitlesByRank;

    /// <summary>Cap on the partly typed multi-word abbreviation keys the last word is
    /// completed to (see <see cref="SeforimDbCatalogTextNormalizer.CompleteTrailingAbbreviationKeys"/>).</summary>
    private const int MaxKeyCompletions = 16;

    /// <summary>Book ids in rank order; empty when nothing matched or no reader is open.
    /// Uncapped.</summary>
    public List<int> SearchTitles(string query, CancellationToken ct = default)
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
            var exact = CollectTitleDocs(searcher, BuildTitleQuery(tokens, variants, lastPrefix: null), ct);
            var matched = exact;
            bool lastIsPrefixOnly = false;

            // The last typed word as a plain normalized word, for the prefix match.
            var chunks = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var plain = SeforimDbCatalogTextNormalizer.Tokenize(chunks[^1]);
            string? lastPrefix = plain.Count > 0 ? plain[^1] : null;

            // A partial last word that is also an abbreviation key "matches exactly" through its
            // expansion; that must not hide the books the word is the start of ("vayikr").
            bool lastIsKey = !tokens[^1].IsPlain && tokens[^1].PreferLiteral;
            if (lastPrefix != null && (exact.Count == 0 || lastIsKey))
            {
                matched = CollectTitleDocs(searcher, BuildTitleQuery(tokens, variants, lastPrefix), ct);
                lastIsPrefixOnly = true;
            }

            // A partly typed MULTI-WORD key ("pnei yehos"): the index holds such a title under
            // the key's canonical expansion, not its own words, so neither pass above can see it.
            // Complete the key and search the completed query exactly, like the index tokenized it.
            var completions = SeforimDbCatalogTextNormalizer.CompleteTrailingAbbreviationKeys(query, MaxKeyCompletions);
            if (completions.Count > 0)
            {
                var seenDocs = new HashSet<int>();
                foreach (var (doc, _) in matched) seenDocs.Add(doc);
                matched = new List<(int Doc, long Order)>(matched);
                foreach (var completed in completions)
                {
                    var completedTokens = SeforimDbCatalogTextNormalizer.TokenizeQuery(completed);
                    foreach (var hit in CollectTitleDocs(searcher, BuildTitleQuery(completedTokens, variants, lastPrefix: null), ct))
                        if (seenDocs.Add(hit.Doc)) matched.Add(hit);
                }
            }

            var words = new List<string>();
            foreach (var t in tokens) words.AddRange(t.RankingWords);
            return RankTitleMatches(matched, books, words, lastIsPrefixOnly && tokens.Count == 1 ? query.Trim() : null);
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

    /// <summary>The catalog query (<see cref="BuildQuery"/>, never fuzzy) restricted to level 0.
    /// With <paramref name="lastPrefix"/>, the last clause also accepts that word as a prefix on
    /// any field, he-prefixed both ways like the old frontend ("kuz" finds "ha-kuzari").</summary>
    private static BooleanQuery BuildTitleQuery(List<SeforimDbCatalogTextNormalizer.QueryToken> tokens, VariantIndex? variants, string? lastPrefix)
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
