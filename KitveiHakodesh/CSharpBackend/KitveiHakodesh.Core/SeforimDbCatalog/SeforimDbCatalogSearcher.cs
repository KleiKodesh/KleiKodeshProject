using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Data.Sqlite;
using KitveiHakodesh.Core.Common;
using KitveiHakodesh.Core.SeforimDb;

namespace KitveiHakodesh.Core.SeforimDbCatalog;

/// <summary>
/// The SEARCH half of <see cref="SeforimDbCatalogIndex"/> - and the reader lifecycle,
/// because a reader is not a job, it is how the searcher holds its resource. The build
/// half and the class doc live in SeforimDbCatalogIndexer.cs; one type, two files,
/// because both halves share one lock, one directory handle and one reader.
/// </summary>
public sealed partial class SeforimDbCatalogIndex
{
    // ── Open / refresh ──────────────────────────────────────────────────────────

    /// <summary>Open a reader on the committed on-disk index if one exists. Idempotent;
    /// the reader is opened once and reused. (During a build the reader is a near-real-
    /// time reader off the live writer instead — see <see cref="RefreshNrtLocked"/>.)</summary>
    public bool TryOpenActive()
    {
        lock (_lock)
        {
            if (_searcher is not null) return true;
            return OpenCommittedLocked();
        }
    }

    private bool OpenCommittedLocked()
    {
        FSDirectory? dir = null;
        try
        {
            if (!System.IO.Directory.Exists(rootPath)) return false;
            dir = _dir ?? FSDirectory.Open(rootPath);
            if (!DirectoryReader.IndexExists(dir))
            {
                if (!ReferenceEquals(dir, _dir)) dir.Dispose();
                return false;
            }
            var reader = DirectoryReader.Open(dir);
            SwapReaderLocked(dir, reader);
            return true;
        }
        catch
        {
            if (!ReferenceEquals(dir, _dir)) dir?.Dispose();
            return false;
        }
    }

    /// <summary>Open (or reopen) a near-real-time reader off the live build writer so
    /// documents added so far become searchable mid-build. Cheap when nothing changed
    /// (OpenIfChanged returns null and the current reader is kept). No-op if no build
    /// is running.</summary>
    private void RefreshNrtLocked()
    {
        if (_writer is null) return;
        try
        {
            DirectoryReader reader = _reader is not null
                ? DirectoryReader.OpenIfChanged(_reader, _writer, applyAllDeletes: true) ?? _reader
                : DirectoryReader.Open(_writer, applyAllDeletes: true);
            if (!ReferenceEquals(reader, _reader))
                SwapReaderLocked(_dir, reader);
        }
        catch { /* keep serving the current reader */ }
    }

    /// <summary>Install a new reader/searcher, disposing the previous reader. The
    /// directory handle is kept alive for the index's lifetime.</summary>
    private void SwapReaderLocked(FSDirectory? dir, DirectoryReader reader)
    {
        var old = _reader;
        _dir = dir;
        _reader = reader;
        _searcher = new IndexSearcher(reader);
        _variants = null; // stale — rebuilt lazily off the new reader on next use
        if (!ReferenceEquals(old, reader)) old?.Dispose();
    }

    /// <summary>
    /// ה-prefix and חסר/מלא skeleton variant lookup tables, built once per reader
    /// generation from the actual indexed vocabulary (TOC path + catalog + author
    /// fields) — the query-time-only port of the Vue frontend's book-catalog search
    /// (bookCatalogSearchNormalizer.ts): no index-time changes, no fuzzy/edit-distance
    /// matching, just the two symmetric normalization rules. Rebuilding is a single
    /// term-dictionary scan (cheap relative to a full build) and is skipped entirely
    /// while a build is in flight — the NRT reader refreshes too often mid-build for
    /// this to be worth redoing on every refresh tick, and the exact fallback still
    /// finds everything on the first search after a build (which invalidates and
    /// rebuilds it once).
    /// </summary>
    private VariantIndex? GetVariantsLocked()
    {
        if (_variants is not null) return _variants;
        if (_reader is null) return null;
        try
        {
            _variants = VariantIndex.Build(_reader, IndexedFields);
        }
        catch { /* best effort — searches still work via exact + fuzzy */ }
        return _variants;
    }

    /// <summary>Total docs in the open index (0 when none is open).</summary>
    public int DocCount()
    {
        TryOpenActive();
        lock (_lock) return _reader?.NumDocs ?? 0;
    }

    /// <summary>
    /// Drop the open reader/searcher so their retained state (segment term indexes,
    /// doc-values, materialized stored-field buffers) is freed while the service is idle
    /// — the catalog counterpart to clearing the SQLite pools. The next search reopens
    /// the committed reader lazily via <see cref="TryOpenActive"/>; the OS file cache
    /// still holds the hot index pages, so the reopen is cheap.
    ///
    /// A near-real-time reader off a LIVE build writer is left untouched — releasing it
    /// mid-build would abandon partial results and the writer is still growing. So this
    /// is a no-op while a build is in flight (the caller also gates on IsBusy). The
    /// directory handle is kept: it is a handful of bytes and avoids re-probing the FS.
    /// Returns true if a reader was actually released.
    /// </summary>
    public bool ReleaseIdleReader()
    {
        lock (_lock)
        {
            if (_writer is not null) return false; // build in flight — keep the NRT reader
            if (_reader is null) return false;     // nothing open
            _reader.Dispose();
            _reader = null;
            _searcher = null;
            // _variants is deliberately KEPT. It is a materialized vocab set + skeleton map
            // with no reference back to the reader, so it stays valid and correct after the
            // reader goes. Nulling it here looks like a memory win but costs correctness:
            // Search reads the searcher and the variants under two SEPARATE lock
            // acquisitions, so a trim landing between them hands the search a live searcher
            // and null variants — GetVariantsLocked cannot rebuild with _reader null — and
            // the query silently loses its prefix/spelling expansion and its literal-vs-
            // variant ranking. It would also re-scan the whole term dictionary inside _lock
            // on the first search after every trim. Only a vocabulary change invalidates
            // this, which is why SwapReaderLocked nulls it and we do not.
            return true;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _reader?.Dispose();
            _reader = null;
            _searcher = null;
            _writer?.Dispose();
            _writer = null;
            _dir?.Dispose();
            _dir = null;
        }
    }

    // ── Search ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Contains-all search: every query token (same normalization pipeline as indexing)
    /// must appear in ONE of the document's indexed fields — TOC path, catalog path, or
    /// author. Results are NEVER capped. Lucene relevance is ignored — ordering is
    /// accuracy first, then catalog position: IsLiteral descending (a hit that matched
    /// every word LITERALLY, i.e. without any כתיב/ה-prefix variant or fuzzy edit, ranks
    /// ahead of one that needed a variant to match), then Level ascending (book title =
    /// 0, then TOC depth), then TreeOrder ascending (catalog book position, then the
    /// original TOC order). Nothing else affects ordering.
    ///
    /// Query token order (final tie-breaker, never a relevance score): the test runs
    /// against the TOC-PATH FIELD ONLY — query tokens present among the path's tokens
    /// must appear there in typed order; tokens that aren't in the path (catalog terms,
    /// authors) don't participate by construction. When a (level, book) group contains
    /// BOTH an in-order and an out-of-order hit, the out-of-order hits are DISCARDED
    /// ("תנך בראשית ד יד" keeps פרק ד / פסוק יד and drops פרק יד / פסוק ד). Groups with
    /// no in-order hit are kept untouched, so title/catalog word order (משנה תורה vs
    /// תורה משנה) never filters anything.
    ///
    /// Per-structure level truncation (after the discard): within each book's each TOC
    /// structure (per literal block), only the shallowest level that matched is kept —
    /// its deeper-level hits are dropped. Scoped that narrowly on purpose: a structure
    /// whose sole matches are deep keeps them, and neither another book nor another
    /// structure of the SAME book (alt structures are independent address spaces) can
    /// truncate it.
    /// </summary>
    public List<CatalogTocHit> Search(string query, CancellationToken ct = default)
    {
        var tokens = SeforimDbCatalogTextNormalizer.TokenizeQuery(query);
        if (tokens.Count == 0) return [];

        IndexSearcher? searcher;
        lock (_lock) searcher = _searcher;
        if (searcher is null)
        {
            if (!TryOpenActive()) return [];
            lock (_lock) searcher = _searcher;
            if (searcher is null) return [];
        }

        VariantIndex? variants;
        lock (_lock) variants = GetVariantsLocked();

        // Accuracy-first ranking (see SortKeyCollector): literal (exact / non-variant)
        // matches rank ahead of variant/fuzzy-only ones. To tag each hit BEFORE the
        // materialization cap, run the strict literal query (no variants, no fuzzy) into
        // a doc-ID set; a hit is literal iff it is in that set. When variant expansion
        // adds nothing (no variants available), the two queries are identical and every
        // hit is literal, so the literal pass is skipped.
        HashSet<int>? literalDocIds = null;
        if (variants is not null)
        {
            var litCollector = new DocIdSetCollector(ct);
            searcher.Search(BuildQuery(tokens, fuzzy: false, variants: null), litCollector);
            literalDocIds = litCollector.Ids;
        }

        // Word list for the query-token-order tiebreak (see RunPass).
        var orderWords = new List<string>(tokens.Count);
        foreach (var t in tokens) orderWords.AddRange(t.Alternatives[0]);

        // Normal pass: exact + כתיב/ה-prefix variants (never fuzzy).
        var hits = RunPass(searcher, tokens, variants, fuzzy: false, literalDocIds, orderWords, ct);

        // SPARSE-FUZZY APPEND: when the normal result set is sparse (fewer than
        // SparseFuzzyThreshold hits — which subsumes the old "exact found nothing"
        // fallback), run a fuzzy pass and APPEND the hits it found that the normal pass
        // did not. Fuzzy edits are tried on the catalog and author fields ONLY — never
        // the TOC path, where a one-letter edit is a different chapter/verse — and only
        // for tokens of 3+ characters. The appended hits are fuzzy-only (never literal),
        // so they sit strictly AFTER every normal hit; ordered among themselves by
        // (Level, TreeOrder). This is a "did you mean" tail, not a reranking of the
        // confident results above it.
        if (hits.Count < SparseFuzzyThreshold && tokens.Any(HasFuzzyableWord))
        {
            var fuzzyHits = RunPass(searcher, tokens, variants, fuzzy: true, literalDocIds, orderWords, ct);
            var seen = new HashSet<(int, string)>(hits.Count);
            foreach (var h in hits) seen.Add((h.BookId, h.FullTocPath));
            foreach (var fh in fuzzyHits)
                if (seen.Add((fh.BookId, fh.FullTocPath)))
                    hits.Add(fh);
        }

        return hits;
    }

    /// <summary>Fewer than this many normal hits triggers the sparse-fuzzy append (also
    /// covers the count==0 case the old fuzzy fallback handled).</summary>
    private const int SparseFuzzyThreshold = 10;

    /// <summary>
    /// One search pass: run the query (optionally fuzzy), order by (IsLiteral desc,
    /// Level asc, TreeOrder asc), materialize the ordered top <see cref="MaterializeCap"/>,
    /// and apply the query-token-order discard and the per-structure level truncation
    /// (each book's each TOC structure keeps only its shallowest-level hits). Returns the
    /// resulting hit list (empty when nothing matched). Used for both the normal pass and
    /// the fuzzy append pass.
    /// </summary>
    private List<CatalogTocHit> RunPass(
        IndexSearcher searcher, List<SeforimDbCatalogTextNormalizer.QueryToken> tokens, VariantIndex? variants,
        bool fuzzy, HashSet<int>? literalDocIds, List<string> orderWords, CancellationToken ct)
    {
        var collector = new SortKeyCollector(ct, literalDocIds);
        searcher.Search(BuildQuery(tokens, fuzzy, variants), collector);
        if (collector.Count == 0) return [];

        // Order EVERY match by (IsLiteral, Level, TreeOrder): literal (exact / non-
        // variant) matches first, then catalog order within each block. The keys come
        // from column-stored doc-values + the literal doc-ID set captured during
        // collection — no stored-field decompression yet, so this stays cheap even for
        // tens of thousands of hits.
        var ordered = collector.Ordered();

        // Materialize (read stored fields — the expensive step) only the ordered top
        // MaterializeCap. The cap is a PERFORMANCE bound and nothing more: matching and
        // ordering above are uncapped, so this only limits how many already-ordered
        // documents are turned into full hit objects (no one scrolls past ~1000).
        int take = Math.Min(MaterializeCap, ordered.Count);
        var hits = new List<CatalogTocHit>(take);
        for (int i = 0; i < take; i++)
        {
            ct.ThrowIfCancellationRequested();
            int docId = ordered[i].DocId;
            var doc = searcher.Doc(docId);
            hits.Add(new CatalogTocHit
            {
                BookId = doc.GetField(FieldBookId)?.GetInt32Value() ?? 0,
                LineIndex = doc.GetField(FieldLineIndex)?.GetInt32Value() ?? -1,
                FullTocPath = doc.Get(FieldFullTocPath) ?? "",
                Level = ordered[i].Level,
                TreeOrder = ordered[i].TreeOrder,
                IsLiteral = ordered[i].IsLiteral,
                StructureId = ordered[i].StructureId,
            });
        }

        // Query-token-order tiebreak: within each (level, book) group that has at least
        // one in-order hit, drop the out-of-order ones. The order test runs on the flat
        // word sequence (an abbreviation contributes its first/canonical alternative's
        // words in order); ambiguity in an alternative doesn't change the typed order.
        if (orderWords.Count >= 2)
        {
            foreach (var h in hits)
                h.QueryInOrder = ContainsInQueryOrder(h.FullTocPath, orderWords);

            var groupsWithInOrder = new HashSet<(int Level, long Book)>();
            foreach (var h in hits)
                if (h.QueryInOrder) groupsWithInOrder.Add((h.Level, h.TreeOrder >> 24));

            if (groupsWithInOrder.Count > 0)
                hits.RemoveAll(h => !h.QueryInOrder && groupsWithInOrder.Contains((h.Level, h.TreeOrder >> 24)));
        }

        // Per-structure level truncation: within ONE book's ONE TOC structure, a hit at a
        // shallower level is the more accurate address for the query, so that structure's
        // deeper-level hits are dropped. A structure whose only hits are deep keeps them
        // all at its own shallowest level; nothing outside it ever truncates it.
        //
        // Scoped per structure, not per book: a book's alt structures (parshiot/aliyot,
        // dapim, …) are independent address spaces, so a shallow hit in the regular TOC
        // must not hide a deeper — and differently-addressed — hit in an alt structure.
        // Book-title docs and the generated Tanach verses belong to the regular structure.
        //
        // Grouped by IsLiteral too, so a literal deep hit is never discarded in favor of a
        // variant/fuzzy shallow one (accuracy-first, same as the sort).
        var minLevelByStructure = new Dictionary<(bool IsLiteral, int BookId, int StructureId), int>();
        foreach (var h in hits)
        {
            var key = (h.IsLiteral, h.BookId, h.StructureId);
            if (!minLevelByStructure.TryGetValue(key, out int min) || h.Level < min)
                minLevelByStructure[key] = h.Level;
        }
        hits.RemoveAll(h => h.Level > minLevelByStructure[(h.IsLiteral, h.BookId, h.StructureId)]);

        return hits;
    }

    /// <summary>
    /// Build the contains-all query from the structured query tokens.
    ///
    /// Plain word → MUST(word matched on path OR catalog OR author). An abbreviation
    /// carrying alternatives → MUST( OR over its alternatives ), where each alternative
    /// is the AND of its words, each word matched on (path OR catalog OR author). So
    /// מג"א → MUST( (מגן AND אברהם) OR (מגיני AND ארץ) ) and the two candidate books
    /// both qualify. A single-alternative abbreviation (או"ח → אורח חיים) reduces to a
    /// plain AND of its words, exactly as before.
    ///
    /// Fuzzy mode: a WORD of 3+ chars additionally tries fuzzy matches on catalog and
    /// author (edit distance 1, or 2 for words longer than 5 chars) — never on the TOC
    /// path. Applies inside alternatives too.
    /// </summary>
    private static BooleanQuery BuildQuery(List<SeforimDbCatalogTextNormalizer.QueryToken> tokens, bool fuzzy, VariantIndex? variants)
    {
        var bq = new BooleanQuery();
        foreach (var token in tokens)
        {
            if (token.IsPlain)
            {
                bq.Add(WordClause(token.Word, fuzzy, variants), Occur.MUST);
                continue;
            }

            // Abbreviation: MUST( OR over alternatives ). One alternative that fully
            // matches satisfies the clause.
            var anyAlt = new BooleanQuery();
            foreach (var alt in token.Alternatives)
            {
                // Alternative = AND of its words. A single-word alternative collapses to
                // one word clause; Lucene flattens the one-child BooleanQuery.
                var altAnd = new BooleanQuery();
                foreach (var word in alt)
                    altAnd.Add(WordClause(word, fuzzy, variants), Occur.MUST);
                anyAlt.Add(altAnd, Occur.SHOULD);
            }
            bq.Add(anyAlt, Occur.MUST);
        }
        return bq;
    }

    /// <summary>One word matched on any indexed field: (path OR catalog OR author), plus
    /// ה-prefix and חסר/מלא skeleton variants found in the actual index vocabulary (see
    /// <see cref="VariantIndex"/> — ported from the Vue frontend's book-catalog search,
    /// always active, not gated on the fuzzy fallback), plus fuzzy catalog/author when
    /// requested and the word is long enough.</summary>
    private static BooleanQuery WordClause(string word, bool fuzzy, VariantIndex? variants)
    {
        var perWord = new BooleanQuery();
        foreach (var field in IndexedFields)
            perWord.Add(new TermQuery(new Term(field, word)), Occur.SHOULD);

        if (variants is not null)
        {
            foreach (var variant in variants.Lookup(word))
                foreach (var field in IndexedFields)
                    perWord.Add(new TermQuery(new Term(field, variant)), Occur.SHOULD);
        }

        if (fuzzy && word.Length >= 3)
        {
            int maxEdits = word.Length <= 5 ? 1 : 2;
            perWord.Add(new FuzzyQuery(new Term(FieldCatalog, word), maxEdits), Occur.SHOULD);
            perWord.Add(new FuzzyQuery(new Term(FieldAuthor, word), maxEdits), Occur.SHOULD);
        }
        return perWord;
    }

    /// <summary>True when a query token has any word of 3+ chars — the threshold that
    /// makes the fuzzy fallback worthwhile.</summary>
    private static bool HasFuzzyableWord(SeforimDbCatalogTextNormalizer.QueryToken token)
    {
        foreach (var alt in token.Alternatives)
            foreach (var word in alt)
                if (word.Length >= 3) return true;
        return false;
    }

    /// <summary>
    /// The query-token-order test, defined by the TOC path alone: query tokens that
    /// exist among the path's tokens must appear there as an ordered subsequence in
    /// typed order. Tokens NOT present in the path (they matched via catalog/author)
    /// are excluded from the test by construction; fewer than two participating tokens
    /// means there is nothing to order — the hit counts as in order.
    /// </summary>
    private static bool ContainsInQueryOrder(string fullTocPath, List<string> queryTokens)
    {
        var pathTokens = SeforimDbCatalogTextNormalizer.Tokenize(fullTocPath);
        var pathSet = new HashSet<string>(pathTokens);

        var participating = new List<string>(queryTokens.Count);
        foreach (var t in queryTokens)
            if (pathSet.Contains(t)) participating.Add(t);
        if (participating.Count < 2) return true;

        int qi = 0;
        foreach (var tok in pathTokens)
        {
            if (tok == participating[qi] && ++qi == participating.Count) return true;
        }
        return false;
    }

    /// <summary>Collects just the global doc-IDs a query matched, into a set. Used to
    /// run the strict LITERAL query (no כתיב/ה-prefix variants, no fuzzy) alongside the
    /// full one, so each hit can be tagged literal-or-variant BEFORE the materialization
    /// cap is applied — see <see cref="SortKeyCollector"/> and the accuracy-first sort.</summary>
    private sealed class DocIdSetCollector(CancellationToken ct) : ICollector
    {
        private readonly HashSet<int> _ids = [];
        private int _docBase;

        public HashSet<int> Ids => _ids;

        public void SetScorer(Scorer scorer) { }
        public void SetNextReader(AtomicReaderContext context) => _docBase = context.DocBase;
        public bool AcceptsDocsOutOfOrder => true;

        public void Collect(int doc)
        {
            if ((_ids.Count & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
            _ids.Add(_docBase + doc);
        }
    }

    /// <summary>
    /// Collects every matching doc together with its (Level, TreeOrder) sort keys read
    /// from column-stored doc-values — no stored-field decompression, so it scales to
    /// tens of thousands of hits. <see cref="Ordered"/> returns the hits sorted by
    /// (IsLiteral desc, Level asc, TreeOrder asc): every literal (exact / non-variant)
    /// match ranks ahead of every variant/fuzzy-only match — the accuracy-first rule —
    /// and within each block the catalog order (Level, then TreeOrder) is preserved.
    /// Ranking happens BEFORE the materialization cap, so a literal hit deep in catalog
    /// order is still promoted (and materialized) ahead of earlier variant hits. No cap
    /// on matching/ordering, no relevance scores.
    /// </summary>
    private sealed class SortKeyCollector(CancellationToken ct, HashSet<int>? literalDocIds) : ICollector
    {
        public readonly struct Entry(int docId, int level, long treeOrder, bool isLiteral, int structureId)
        {
            public readonly int DocId = docId;
            public readonly int Level = level;
            public readonly long TreeOrder = treeOrder;
            public readonly bool IsLiteral = isLiteral;
            public readonly int StructureId = structureId;
        }

        private readonly List<Entry> _entries = [];
        private int _docBase;
        private NumericDocValues? _levels;
        private NumericDocValues? _treeOrders;
        private NumericDocValues? _structures;

        public int Count => _entries.Count;

        public void SetScorer(Scorer scorer) { /* scores are ignored by design */ }

        public void SetNextReader(AtomicReaderContext context)
        {
            _docBase = context.DocBase;
            _levels = context.AtomicReader.GetNumericDocValues(FieldLevelDv);
            _treeOrders = context.AtomicReader.GetNumericDocValues(FieldTreeOrderDv);
            _structures = context.AtomicReader.GetNumericDocValues(FieldStructureDv);
        }

        public bool AcceptsDocsOutOfOrder => true;

        public void Collect(int doc)
        {
            if ((_entries.Count & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
            int globalDoc = _docBase + doc;
            int level = (int)(_levels?.Get(doc) ?? 0);
            long treeOrder = _treeOrders?.Get(doc) ?? long.MaxValue;
            // Literal when there is no literal set (variant search never ran, so every
            // hit is by definition literal) or the doc is in it.
            bool isLiteral = literalDocIds is null || literalDocIds.Contains(globalDoc);
            int structureId = (int)(_structures?.Get(doc) ?? RegularTocStructureId);
            _entries.Add(new Entry(globalDoc, level, treeOrder, isLiteral, structureId));
        }

        public List<Entry> Ordered()
        {
            _entries.Sort(static (a, b) =>
            {
                // Accuracy-first: literal matches (exact / non-variant) ahead of variant-
                // or fuzzy-only matches. Then the existing catalog order within each block.
                if (a.IsLiteral != b.IsLiteral) return a.IsLiteral ? -1 : 1;
                int c = a.Level.CompareTo(b.Level);
                return c != 0 ? c : a.TreeOrder.CompareTo(b.TreeOrder);
            });
            return _entries;
        }
    }

    // ── Query-time variant lookup (ה-prefix + חסר/מלא skeleton) ──────────────────

    /// <summary>
    /// Query-time-only port of the Vue frontend's book-catalog search normalization
    /// (bookCatalogSearchNormalizer.ts) — NOT fuzzy/edit-distance matching. Built once per
    /// reader generation from the actual indexed vocabulary of <see cref="IndexedFields"/>:
    ///
    ///   - ה-prefix: a query word starting with ה also probes its stripped form, and a
    ///     query word without ה also probes its ה-prefixed form — either direction fires
    ///     as soon as THAT SPECIFIC indexed term exists (הרמבן ↔ רמבן; querying "רמבן"
    ///     finds a book indexed as "הרמבן" even though "רמבן" itself is never indexed).
    ///   - חסר/מלא skeleton: a query word's consonantal skeleton (mid-word י/ו stripped)
    ///     is matched against every indexed word sharing that skeleton with a compatible
    ///     (subset) vowel-set — נידה ↔ נדה, but not שבועות ↔ שביעית (incompatible vowel
    ///     sets). The query word itself need not be indexed anywhere.
    ///
    /// <see cref="Lookup"/> returns the extra literal terms (beyond the typed word itself)
    /// that should also be probed — always applied, on every search, not gated behind the
    /// fuzzy fallback (mirrors the frontend, where these are SCORE_EXACT tiers).
    /// </summary>
    private sealed class VariantIndex
    {
        /// <summary>Every distinct indexed word (across the three indexed fields).</summary>
        private readonly HashSet<string> _vocab;
        /// <summary>skeleton → every distinct indexed word sharing it, pre-decomposed.
        /// Mirrors the frontend's `skeleton` map, EXCEPT the frontend keys it by book —
        /// here it's by literal word, since Lucene terms (not per-book token lists) are
        /// what a TermQuery needs.</summary>
        private readonly Dictionary<string, List<(string Word, SeforimDbCatalogTextNormalizer.DecomposedWord Decomp)>> _bySkeleton;

        private VariantIndex(
            HashSet<string> vocab,
            Dictionary<string, List<(string Word, SeforimDbCatalogTextNormalizer.DecomposedWord Decomp)>> bySkeleton)
        {
            _vocab = vocab;
            _bySkeleton = bySkeleton;
        }

        /// <summary>
        /// Extra literal terms to also search for the given typed word (may be empty).
        /// Computed live against the prebuilt vocabulary/skeleton tables — mirrors the
        /// frontend's _lookupWord, which decomposes the QUERY word on every call and
        /// matches it against whatever is indexed, rather than requiring both spellings
        /// to already be paired up ahead of time (a word is reachable by its skeleton
        /// even when no other indexed word happens to share it — the query word itself
        /// supplies the other half of the pair).
        /// </summary>
        public IEnumerable<string> Lookup(string word)
        {
            HashSet<string>? extra = null;
            void Add(string term)
            {
                if (term == word) return;
                extra ??= new HashSet<string>(StringComparer.Ordinal);
                extra.Add(term);
            }

            // ה-prefix: word itself might BE a stripped form (query "רמבן" should also
            // probe "הרמבן" if that's indexed) — check every ה-prefixed vocab word whose
            // stripped form equals the query word. And the reverse: if the query word
            // itself starts with ה, its stripped form might be indexed directly.
            string? stripped = SeforimDbCatalogTextNormalizer.StripHePrefix(word);
            if (stripped is not null && _vocab.Contains(stripped)) Add(stripped);
            string withHe = "ה" + word;
            if (_vocab.Contains(withHe)) Add(withHe);

            // חסר/מלא skeleton: decompose the query word live (it need not itself be
            // indexed) and match against every indexed word sharing its skeleton.
            var decomp = SeforimDbCatalogTextNormalizer.DecomposeSkeleton(word);
            if (_bySkeleton.TryGetValue(decomp.Skeleton, out var group))
            {
                foreach (var (candidate, candidateDecomp) in group)
                    if (SeforimDbCatalogTextNormalizer.AreSkeletonVariants(decomp, candidateDecomp))
                        Add(candidate);
            }

            return (IEnumerable<string>?)extra ?? [];
        }

        /// <summary>Scan the term dictionary of every field in <paramref name="fields"/> and
        /// build the vocabulary + skeleton grouping. Cheap relative to a full index build
        /// (a single pass over already-sorted per-field term enums).</summary>
        public static VariantIndex Build(DirectoryReader reader, string[] fields)
        {
            var vocab = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                var terms = MultiFields.GetTerms(reader, field);
                if (terms is null) continue;
                var te = terms.GetEnumerator();
                while (te.MoveNext())
                    vocab.Add(te.Term.Utf8ToString());
            }

            var bySkeleton = new Dictionary<string, List<(string Word, SeforimDbCatalogTextNormalizer.DecomposedWord Decomp)>>(StringComparer.Ordinal);
            foreach (var word in vocab)
            {
                var decomp = SeforimDbCatalogTextNormalizer.DecomposeSkeleton(word);
                if (!bySkeleton.TryGetValue(decomp.Skeleton, out var list))
                    bySkeleton[decomp.Skeleton] = list = new List<(string, SeforimDbCatalogTextNormalizer.DecomposedWord)>();
                list.Add((word, decomp));
            }

            return new VariantIndex(vocab, bySkeleton);
        }
    }
}
