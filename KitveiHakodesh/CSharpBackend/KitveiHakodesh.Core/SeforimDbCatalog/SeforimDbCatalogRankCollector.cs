using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using KitveiHakodesh.Core.Common;

namespace KitveiHakodesh.Core.SeforimDbCatalog;

/// <summary>
/// The catalog search's collector: every match comes out with its FULL <see cref="RankKeys"/>,
/// read while collecting from index data alone — the path field's postings (presence,
/// frequency, positions), the author / leaf-word / quoted-key postings, the doc-values and
/// the per-search book-title table. Nothing is capped here and no stored field is read; the
/// searcher orders the whole entry list and materializes only the top of it.
/// </summary>
public sealed partial class SeforimDbCatalogIndex
{
    /// <summary>One collected match: its doc, the catalog-order keys and the ranking keys.</summary>
    private struct RankedEntry
    {
        public int DocId;
        /// <summary>The book: the grouping key of the per-book passes (the catalog rank is not
        /// one - every book the tree order does not know shares the same rank).</summary>
        public int BookId;
        public int Level;
        public long TreeOrder;
        public int StructureId;
        public RankKeys Keys;
    }

    /// <summary>What the query's words make of one book's title: filled for the books the
    /// query NAMES (every title word a query word), looked up by book id for every hit of
    /// that book, so a TOC hit carries its book's title keys without reading its title.</summary>
    private readonly record struct CoveredTitle(int DistinctWords, int LengthGap);

    /// <summary>
    /// The books the query names, by book id: the level-0 docs whose every distinct title word
    /// is a query word. Read straight off the index — the title-only field's postings count
    /// each book's matched title words, and the book's distinct-title-word doc-value says how
    /// many it needs. Lets a search know, before anything is capped, whether the query names a
    /// hit's book: a late-catalog book typed by name is never cut off behind a thousand earlier
    /// hits that merely share its words.
    /// </summary>
    private static Dictionary<int, CoveredTitle> CoveredTitles(IndexReader reader, HashSet<string> queryWords, int queryWordCount)
    {
        var covered = new Dictionary<int, CoveredTitle>();
        var counts = new Dictionary<int, int>();
        foreach (var leaf in reader.Leaves)
        {
            var atomic = leaf.AtomicReader;
            var terms = atomic.GetTerms(FieldBookTitle);
            if (terms is null) continue;
            counts.Clear();
            var te = terms.GetEnumerator();
            DocsEnum? docs = null;
            foreach (var word in queryWords)
            {
                if (!te.SeekExact(new BytesRef(word))) continue;
                docs = te.Docs(atomic.LiveDocs, docs, DocsFlags.NONE);
                int d;
                while ((d = docs.NextDoc()) != DocIdSetIterator.NO_MORE_DOCS)
                    counts[d] = counts.GetValueOrDefault(d) + 1;
            }
            if (counts.Count == 0) continue;
            var needed = atomic.GetNumericDocValues(FieldBookTitleWordsDv);
            var wordCounts = atomic.GetNumericDocValues(FieldBookTitleWordCountDv);
            var bookIds = atomic.GetNumericDocValues(FieldBookIdDv);
            if (needed is null || wordCounts is null || bookIds is null) continue;
            foreach (var (doc, found) in counts)
                if (found == needed.Get(doc))
                    covered[(int)bookIds.Get(doc)] = new CoveredTitle(found, Math.Abs((int)wordCounts.Get(doc) - queryWordCount));
        }
        return covered;
    }

    private sealed class RankCollector : ICollector
    {
        // ── Per search ─────────────────────────────────────────────────────────────
        private readonly CancellationToken _ct;
        private readonly List<SeforimDbCatalogTextNormalizer.QueryToken> _tokens;
        private readonly FixedBitSet? _literalDocIds;
        private readonly Dictionary<int, CoveredTitle> _coveredTitles;
        private readonly HashSet<string> _queryWords;
        private readonly List<string> _quotedKeys;
        private readonly SeforimDbCatalogRankingKeys.ProximityScratch? _proximity;

        // The vocabulary: every word any key looks up, each once. A word's index is its slot
        // in every per-segment array below.
        private readonly List<string> _words = [];
        private readonly Dictionary<string, int> _wordIndex = new(StringComparer.Ordinal);
        private readonly int[][] _variantsOf;          // per word: the slots of its variants
        private readonly int[] _countWord;             // per QueryWordCounts entry: the slot
        private readonly int[] _countWanted;           //   ...and how many times it was typed
        private readonly int[] _orderWord;             // per order word: the slot
        private readonly int[] _leafWord;              // the slots of the query words (leaf coverage)
        private readonly Func<string, bool> _present;
        private readonly Func<int, List<int>> _positionsOfOrderWord;

        // ── Per segment ────────────────────────────────────────────────────────────
        private int _docBase;
        private DocsEnum?[] _path = [];                // positions-capable when the field has them
        private DocsEnum?[] _author = [];
        private DocsEnum?[] _leaf = [];
        private DocsEnum?[] _quoted = [];
        private NumericDocValues? _bookIds, _levels, _treeOrders, _structures, _leafDistinct, _leafAdds;

        // ── Per doc ────────────────────────────────────────────────────────────────
        private readonly bool[] _onPath, _onAuthor, _onLeaf;
        private readonly int[] _pathFreq;
        private readonly List<int>[] _positions;       // per word: its positions in this doc's path
        private static readonly List<int> NoPositions = [];

        public readonly List<RankedEntry> Entries = [];

        public RankCollector(
            CancellationToken ct, List<SeforimDbCatalogTextNormalizer.QueryToken> tokens, VariantLookup? variants,
            FixedBitSet? literalDocIds, Dictionary<int, CoveredTitle> coveredTitles,
            HashSet<string> queryWords, Dictionary<string, int> queryCounts, List<string> orderWords, HashSet<string>? queryQuotedKeys)
        {
            _ct = ct;
            _tokens = tokens;
            _literalDocIds = literalDocIds;
            _coveredTitles = coveredTitles;
            _queryWords = queryWords;
            _quotedKeys = queryQuotedKeys is null ? [] : [.. queryQuotedKeys];

            // Vocabulary: the TYPED words first (every alternative's words, the counted words,
            // the order words), then each typed word's variants (a variant's own variants are
            // not looked up - the query does not either), then the query word set, which holds
            // nothing beyond those. Gathered in full BEFORE the per-word arrays are sized: Slot
            // grows the vocabulary.
            foreach (var token in tokens)
                foreach (var alt in token.Alternatives)
                    foreach (var word in alt) Slot(word);
            foreach (var word in orderWords) Slot(word);
            foreach (var word in queryCounts.Keys) Slot(word);
            int typedWords = _words.Count;
            var variantSlots = new int[typedWords][];
            for (int i = 0; i < typedWords; i++)
            {
                var found = variants?.Lookup(_words[i]);
                variantSlots[i] = found is null || found.Count == 0 ? [] : found.Select(Slot).ToArray();
            }
            foreach (var word in queryWords) Slot(word);
            _variantsOf = new int[_words.Count][];
            for (int i = 0; i < _words.Count; i++) _variantsOf[i] = i < typedWords ? variantSlots[i] : [];
            _countWord = new int[queryCounts.Count];
            _countWanted = new int[queryCounts.Count];
            int c = 0;
            foreach (var (word, wanted) in queryCounts) { _countWord[c] = Slot(word); _countWanted[c++] = wanted; }
            _orderWord = orderWords.Select(Slot).ToArray();
            _leafWord = queryWords.Select(Slot).ToArray();
            if (orderWords.Count >= 2) _proximity = new SeforimDbCatalogRankingKeys.ProximityScratch(orderWords);

            _onPath = new bool[_words.Count];
            _onAuthor = new bool[_words.Count];
            _onLeaf = new bool[_words.Count];
            _pathFreq = new int[_words.Count];
            _positions = new List<int>[_words.Count];
            for (int i = 0; i < _positions.Length; i++) _positions[i] = [];
            _present = PresentOnPathOrAuthor;
            _positionsOfOrderWord = i => _positions[_orderWord[i]];
        }

        private int Slot(string word)
        {
            if (_wordIndex.TryGetValue(word, out int i)) return i;
            _wordIndex[word] = _words.Count;
            _words.Add(word);
            return _words.Count - 1;
        }

        public void SetScorer(Scorer scorer) { /* scores are ignored by design */ }

        /// <summary>In doc order: the postings enums below are forward-only.</summary>
        public bool AcceptsDocsOutOfOrder => false;

        public void SetNextReader(AtomicReaderContext context)
        {
            var reader = context.AtomicReader;
            _docBase = context.DocBase;
            _path = LucenePostings.Open(reader, FieldFullTocPath, _words, positions: true);
            _author = LucenePostings.Open(reader, FieldAuthor, _words, positions: false);
            _leaf = LucenePostings.Open(reader, FieldLeafWords, _words, positions: false);
            _quoted = LucenePostings.Open(reader, FieldQuotedKey, _quotedKeys, positions: false);
            _bookIds = reader.GetNumericDocValues(FieldBookIdDv);
            _levels = reader.GetNumericDocValues(FieldLevelDv);
            _treeOrders = reader.GetNumericDocValues(FieldTreeOrderDv);
            _structures = reader.GetNumericDocValues(FieldStructureDv);
            _leafDistinct = reader.GetNumericDocValues(FieldLeafDistinctWordsDv);
            _leafAdds = reader.GetNumericDocValues(FieldLeafAddsTitleWordDv);
        }

        public void Collect(int doc)
        {
            if ((Entries.Count & 0x3FFF) == 0) _ct.ThrowIfCancellationRequested();

            // Where each vocabulary word is in this doc, and the path positions of the order words.
            for (int i = 0; i < _words.Count; i++)
            {
                _onAuthor[i] = LucenePostings.At(_author[i], doc);
                _onLeaf[i] = LucenePostings.At(_leaf[i], doc);
                _pathFreq[i] = LucenePostings.FreqAndPositions(_path[i], doc, _positions[i]);
                _onPath[i] = _pathFreq[i] > 0;
            }

            int level = (int)(_levels?.Get(doc) ?? 0);
            long treeOrder = _treeOrders?.Get(doc) ?? long.MaxValue;
            int bookId = (int)(_bookIds?.Get(doc) ?? 0);
            int globalDoc = _docBase + doc;
            var keys = new RankKeys
            {
                // Literal when there is no literal set (variant search never ran, so every hit
                // is by definition literal) or the doc is in it.
                IsLiteral = _literalDocIds is null || _literalDocIds.Get(globalDoc),
                MatchesTitleOrAuthor = SeforimDbCatalogRankingKeys.EveryTokenPresent(_tokens, _present),
                Level = level,
            };
            if (_coveredTitles.TryGetValue(bookId, out var title))
            {
                keys.TitleCovered = true;
                keys.TitleDistinctWords = title.DistinctWords;
                keys.TitleLengthGap = title.LengthGap;
            }
            if (level == 0)
            {
                keys.LeafCovered = keys.TitleCovered;   // the leaf IS the title
                foreach (var q in _quoted)
                    if (LucenePostings.At(q, doc)) { keys.QuotedFormMatch = true; break; }
            }
            else
            {
                keys.LeafCovered = LeafCovered(doc);
                keys.LeafAddsWord = keys.LeafCovered && (_leafAdds?.Get(doc) ?? 0) != 0;
            }
            keys.WordsMatched = WordsMatched();
            _proximity?.Measure(_positionsOfOrderWord, ref keys);

            Entries.Add(new RankedEntry
            {
                DocId = globalDoc,
                BookId = bookId,
                Level = level,
                TreeOrder = treeOrder,
                StructureId = (int)(_structures?.Get(doc) ?? RegularTocStructureId),
                Keys = keys,
            });
        }

        /// <summary>The word, by its own form or any variant, is in the path or the author.</summary>
        private bool PresentOnPathOrAuthor(string word)
        {
            int i = _wordIndex[word];
            if (_onPath[i] || _onAuthor[i]) return true;
            foreach (int v in _variantsOf[i])
                if (_onPath[v] || _onAuthor[v]) return true;
            return false;
        }

        /// <summary>Every distinct leaf word is a query word: as many distinct query words on
        /// the leaf as the leaf has distinct words.</summary>
        private bool LeafCovered(int doc)
        {
            int needed = (int)(_leafDistinct?.Get(doc) ?? 0);
            if (needed == 0) return false;
            int found = 0;
            foreach (int w in _leafWord)
                if (_onLeaf[w] && ++found == needed) return true;
            return false;
        }

        /// <summary>Per typed word, min(times typed, times in the path), a path occurrence of
        /// any variant of the word counting for it.</summary>
        private int WordsMatched()
        {
            int matched = 0;
            for (int c = 0; c < _countWord.Length; c++)
            {
                int w = _countWord[c], wanted = _countWanted[c];
                int have = _pathFreq[w];
                if (have < wanted)
                    foreach (int v in _variantsOf[w]) have += _pathFreq[v];
                matched += Math.Min(have, wanted);
            }
            return matched;
        }
    }
}
