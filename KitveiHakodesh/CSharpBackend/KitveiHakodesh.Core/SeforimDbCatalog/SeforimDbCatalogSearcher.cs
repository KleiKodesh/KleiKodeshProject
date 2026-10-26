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
        _titleTokensByRank = null;
        if (!ReferenceEquals(old, reader)) old?.Dispose();
    }

    /// <summary>
    /// Every book's title tokens by its catalog rank (TreeOrder >> 24), read once per reader
    /// generation from the level-0 documents. Lets a search know, for every matching doc
    /// BEFORE the materialization cap, whether the query names its book — so a late-catalog
    /// book the user typed by name is never cut off behind a thousand earlier hits that
    /// merely share its words ("Biur Halacha seif 1" has every Shulchan Aruch commentary's
    /// seif 1 ahead of it in catalog order).
    /// </summary>
    private Dictionary<long, List<string>>? GetTitleTokensLocked()
    {
        if (_titleTokensByRank is not null) return _titleTokensByRank;
        if (_reader is null) return null;
        try
        {
            var map = new Dictionary<long, List<string>>(8192);
            foreach (var ctx in _reader.Leaves)
            {
                var levels = ctx.AtomicReader.GetNumericDocValues(FieldLevelDv);
                var orders = ctx.AtomicReader.GetNumericDocValues(FieldTreeOrderDv);
                if (levels is null || orders is null) continue;
                var live = ctx.AtomicReader.LiveDocs;
                int max = ctx.AtomicReader.MaxDoc;
                for (int d = 0; d < max; d++)
                {
                    if (live is not null && !live.Get(d)) continue;
                    if (levels.Get(d) != 0) continue;
                    long rank = orders.Get(d) >> 24;
                    if (map.ContainsKey(rank)) continue;
                    var doc = ctx.AtomicReader.Document(d);
                    map[rank] = SeforimDbCatalogTextNormalizer.Tokenize(doc.Get(FieldFullTocPath) ?? "");
                }
            }
            _titleTokensByRank = map;
        }
        catch { /* best effort — without it the cap falls back to catalog order alone */ }
        return _titleTokensByRank;
    }

    /// <summary>
    /// ה-prefix and חסר/מלא skeleton variant lookup tables, built once per reader
    /// generation from the actual indexed vocabulary (TOC path + catalog + author
    /// fields) — the query-time-only port of the former Vue frontend book-catalog
    /// normalizer: no index-time changes, no fuzzy/edit-distance
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

    /// <summary>What this index keeps resident right now (diagnostics).</summary>
    public string DescribeResidentState()
    {
        lock (_lock)
            return $"reader={(_reader is null ? "closed" : "open")} variants={(_variants is null ? "none" : "built")} writer={(_writer is null ? "none" : "open")}";
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
            // Dispose drops only the OWNER's reference: a search that took its own
            // reference (TryAcquireSearcher) keeps the reader open until it finishes.
            _reader.Dispose();
            _reader = null;
            _searcher = null;
            // The variant tables (vocab set + skeleton map, often the biggest survivor) are
            // dropped too. Safe because Search takes searcher + variants together in ONE
            // lock acquisition (TryAcquireSearcher): it can never see a live searcher with
            // null variants. They are rebuilt lazily (one term-dictionary scan) by the first
            // search after the reader reopens.
            _variants = null;
            _titleTokensByRank = null;
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

        // Searcher AND variants are taken together, in ONE lock acquisition, with a
        // reference held on the reader (see TryAcquireSearcher) — so an idle release or a
        // reader swap landing mid-search can neither close the reader under us nor hand
        // us a live searcher with mismatched/null variants.
        if (!TryAcquireSearcher(out var searcher, out var variants, out var titles))
        {
            if (!TryOpenActive() || !TryAcquireSearcher(out searcher, out variants, out titles)) return [];
        }
        try
        {
            return SearchOnSearcher(searcher, variants, titles, tokens, query, ct);
        }
        finally
        {
            searcher.IndexReader.DecRef();
        }
    }

    /// <summary>
    /// Snapshot the current searcher and its variant tables under one lock, and take a
    /// reference on the reader (<see cref="IndexReader.TryIncRef"/>) so it stays open until
    /// the caller's <see cref="IndexReader.DecRef"/> even if <see cref="ReleaseIdleReader"/>
    /// or a reader swap disposes it meanwhile (Dispose only drops the owner's reference).
    /// The variants are built from the SAME reader inside the same lock, so the pair is
    /// always consistent. False when no reader is open.
    /// </summary>
    private bool TryAcquireSearcher(out IndexSearcher searcher, out VariantIndex? variants, out Dictionary<long, List<string>>? titles)
    {
        lock (_lock)
        {
            searcher = _searcher!;
            variants = null;
            titles = null;
            if (_searcher is null || !_searcher.IndexReader.TryIncRef()) return false;
            variants = GetVariantsLocked();
            titles = GetTitleTokensLocked();
            return true;
        }
    }

    private List<CatalogTocHit> SearchOnSearcher(
        IndexSearcher searcher, VariantIndex? variants, Dictionary<long, List<string>>? titles,
        List<SeforimDbCatalogTextNormalizer.QueryToken> tokens, string query, CancellationToken ct)
    {
        // The words the ranking treats as typed, and the books those words name — both
        // decided once per search, the latter feeding the collector's pre-cap order.
        var queryWords = QueryWordSet(tokens, variants);
        var coveredRanks = new HashSet<long>();
        if (titles is not null)
            foreach (var (rank, titleTokens) in titles)
                if (IsCovered(titleTokens, queryWords)) coveredRanks.Add(rank);

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
        // The typed words, not an expansion: "bamidbar" typed is the word bamidbar, and
        // ordering on its expansion "Bamidbar Rabbah" put a second "rabbah" in the sequence
        // that no path has — every hit failed the order test, so word order decided nothing.
        foreach (var t in tokens) orderWords.AddRange(t.RankingWords);

        // The quoted acronyms the user actually typed, if any. Measured against each HIT's own
        // STORED text at materialization — the stored strings keep their quote glyphs, only the
        // indexed tokens were stripped — so no index-side field and no rebuild is involved.
        var queryQuotedKeys = SeforimDbCatalogTextNormalizer.GetQuotedAbbreviationKeys(query);

        // Normal pass: exact + כתיב/ה-prefix variants (never fuzzy).
        var hits = RunPass(searcher, tokens, variants, queryWords, coveredRanks, fuzzy: false, literalDocIds, orderWords, queryQuotedKeys, ct);

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
            var fuzzyHits = RunPass(searcher, tokens, variants, queryWords, coveredRanks, fuzzy: true, literalDocIds, orderWords, queryQuotedKeys, ct);
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
        HashSet<string> queryWords, HashSet<long> coveredRanks,
        bool fuzzy, HashSet<int>? literalDocIds, List<string> orderWords, HashSet<string>? queryQuotedKeys,
        CancellationToken ct)
    {
        var collector = new SortKeyCollector(ct, literalDocIds, coveredRanks);
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
        var queryCounts = QueryWordCounts(tokens);
        int queryWordCount = 0;
        foreach (var n in queryCounts.Values) queryWordCount += n;
        for (int i = 0; i < take; i++)
        {
            ct.ThrowIfCancellationRequested();
            int docId = ordered[i].DocId;
            var doc = searcher.Doc(docId);
            string fullTocPath = doc.Get(FieldFullTocPath) ?? "";
            // Tokenize the path ONCE; title and leaf are short re-tokenizations of its ends.
            var pathTokens = SeforimDbCatalogTextNormalizer.Tokenize(fullTocPath);
            var titleTokens = ordered[i].Level == 0 ? pathTokens : SeforimDbCatalogTextNormalizer.Tokenize(TitleOf(doc, fullTocPath, ordered[i].Level));
            string tocPath = PathWithoutTitle(doc, fullTocPath, ordered[i].Level);
            var leafTokens = ordered[i].Level == 0 ? titleTokens : SeforimDbCatalogTextNormalizer.Tokenize(LastSegment(tocPath));
            hits.Add(new CatalogTocHit
            {
                BookId = doc.GetField(FieldBookId)?.GetInt32Value() ?? 0,
                LineIndex = doc.GetField(FieldLineIndex)?.GetInt32Value() ?? -1,
                FullTocPath = fullTocPath,
                PathTokens = pathTokens,
                TocPath = tocPath,
                Level = ordered[i].Level,
                TreeOrder = ordered[i].TreeOrder,
                IsLiteral = ordered[i].IsLiteral,
                StructureId = ordered[i].StructureId,
                QuotedFormMatch = ordered[i].Level == 0 && queryQuotedKeys != null && HitCarriesQuotedKey(doc, queryQuotedKeys),
                MatchesTitleOrAuthor = MatchesOnTitleOrAuthor(doc, pathTokens, tokens, variants),
                TitleCovered = IsCovered(titleTokens, queryWords),
                TitleDistinctWords = DistinctFound(titleTokens, queryWords),
                TitleLengthGap = Math.Abs(titleTokens.Count - queryWordCount),
                LeafCovered = IsCovered(leafTokens, queryWords),
                LeafAddsWord = ordered[i].Level > 0 && IsCovered(leafTokens, queryWords) && leafTokens.Exists(w => !titleTokens.Contains(w)),
                WordsMatched = CountMatched(pathTokens, queryCounts, variants),
            });
        }

        // Query-token-order tiebreak: within each (level, book) group that has at least
        // one in-order hit, drop the out-of-order ones. The order test runs on the flat
        // word sequence (an abbreviation contributes its first/canonical alternative's
        // words in order); ambiguity in an alternative doesn't change the typed order.
        if (orderWords.Count >= 2)
        {
            // One tokenization per hit serves BOTH the order test and the proximity
            // measurement; measuring them separately tokenized every path twice, on every
            // keystroke.
            var scratch = new PathScratch(orderWords.Count);
            foreach (var h in hits) scratch.Measure(h.PathTokens, orderWords, h);

            var groupsWithInOrder = new HashSet<(int Level, long Book)>();
            foreach (var h in hits)
                if (h.QueryInOrder) groupsWithInOrder.Add((h.Level, h.TreeOrder >> 24));

            if (groupsWithInOrder.Count > 0)
                hits.RemoveAll(h => !h.QueryInOrder && groupsWithInOrder.Contains((h.Level, h.TreeOrder >> 24)));
        }

        // Field-accuracy rank over the materialized window, on keys that can only be read from
        // STORED text (so SortKeyCollector, which sees only doc-values, cannot carry them):
        //   MatchesTitleOrAuthor - every query word landed in the title/path or author, not
        //                          merely in a category name shared by every book filed under it
        //   TitleCovered         - the query names the book: every title word is a query word.
        //                          "Tur" is the book Tur before "Kapei Aharon al Tur"; "Tur Orach
        //                          Chaim" is Tur's own section before that commentary. Among
        //                          covered titles the one explaining more of the query wins, then
        //                          the shorter ("X" before "X (Y)").
        //   LeafCovered /        - the typed address IS this entry's own segment: "Shemini" before
        //   WordsMatched           "Shemini Atzeret"; "mishnah 5" before a "perek 5" sharing one word
        //   QuotedFormMatch      - a book title written the way the query was typed, quote and
        //                          all, rather than being the ordinary word the acronym strips to
        //                          (below TitleCovered: the book the acronym NAMES comes first)
        //
        // Unconditional, unlike the proximity sort below which guards on a 2+ word query: an
        // acronym is typically ONE word, and a category collision needs no particular length
        // either. Stable, and below IsLiteral throughout, so a variant match never overtakes a
        // literal one.
        StableSortByFieldAccuracy(hits);

        // Word-proximity rank: how tightly the query's words sit together in the path (keys
        // already measured in the fused pass above). Reordering the materialized window rather
        // than the full match set is deliberate: the accuracy-first block order from
        // SortKeyCollector already decided WHICH hits are worth showing, and proximity only
        // reorders within that.
        //
        // Sorted with a STABLE comparison chain, so hits equal on every key keep the
        // (Level, TreeOrder) order they arrived in. Proximity sits BELOW IsLiteral — a variant
        // match never overtakes a literal one however tightly its words cluster — and ABOVE
        // Level/TreeOrder, which would otherwise let catalog position bury the tighter address.
        // WordsFound outranks WordSpan: finding more of the query in the path beats packing
        // fewer words closer together.
        if (orderWords.Count >= 2)
            StableSortByProximity(hits);

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
        //
        // "Shallower" is judged among the entries whose path holds the most of the query's
        // words, repetitions included (WordsMatched): for "Magen Avot mishnah 5" the level-1
        // "perek 5" shares only the "5" with the query, while "perek 1 / mishnah 5" holds both
        // words — the deeper one is the address typed, and keeping the shallower one would
        // have hidden it. A plain title query ties every entry with the book (each holds the
        // title words once), and the book, shallowest, is what stays.
        var bestByStructure = new Dictionary<(bool IsLiteral, int BookId, int StructureId), (int Matched, int Level)>();
        foreach (var h in hits)
        {
            var key = (h.IsLiteral, h.BookId, h.StructureId);
            if (!bestByStructure.TryGetValue(key, out var best)
                || h.WordsMatched > best.Matched
                || (h.WordsMatched == best.Matched && h.Level < best.Level))
                bestByStructure[key] = (h.WordsMatched, h.Level);
        }
        // An entry whose OWN name holds a typed word beyond the title (LeafAddsWord) is never trimmed: a book with
        // "daf 8" in its first edition and again under "second edition" must show both.
        hits.RemoveAll(h =>
        {
            if (h.LeafAddsWord) return false;
            var best = bestByStructure[(h.IsLiteral, h.BookId, h.StructureId)];
            return h.WordsMatched < best.Matched || h.Level > best.Level;
        });

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
    /// Stable re-sort of the materialized window on the FIELD-ACCURACY keys — the ones that can
    /// only be read from stored text, which the collector never sees.
    /// </summary>
    private static void StableSortByFieldAccuracy(List<CatalogTocHit> hits)
    {
        int n = hits.Count;
        var arrival = new int[n];
        var items = new CatalogTocHit[n];
        for (int i = 0; i < n; i++) { items[i] = hits[i]; arrival[i] = i; }

        Array.Sort(arrival, (x, y) =>
        {
            var a = items[x];
            var b = items[y];
            if (a.IsLiteral != b.IsLiteral) return a.IsLiteral ? -1 : 1;
            if (a.MatchesTitleOrAuthor != b.MatchesTitleOrAuthor) return a.MatchesTitleOrAuthor ? -1 : 1;
            if (a.TitleCovered != b.TitleCovered) return a.TitleCovered ? -1 : 1;
            if (a.TitleCovered && a.TitleDistinctWords != b.TitleDistinctWords) return b.TitleDistinctWords.CompareTo(a.TitleDistinctWords);
            if (a.TitleCovered && a.TitleLengthGap != b.TitleLengthGap) return a.TitleLengthGap.CompareTo(b.TitleLengthGap);
            if (a.LeafCovered != b.LeafCovered) return a.LeafCovered ? -1 : 1;
            if (a.WordsMatched != b.WordsMatched) return b.WordsMatched.CompareTo(a.WordsMatched);
            if (a.QuotedFormMatch != b.QuotedFormMatch) return a.QuotedFormMatch ? -1 : 1;
            return x.CompareTo(y);
        });

        hits.Clear();
        for (int i = 0; i < n; i++) hits.Add(items[arrival[i]]);
    }

    /// <summary>
    /// Stable re-sort of the materialized window by how tightly the query's words sit together
    /// in the path. Carries the field-accuracy keys too: this pass re-sorts the same window, so
    /// a key it did not carry would be undone here — proximity must not float an ordinary-word
    /// hit above the book actually written the way the query was typed.
    /// </summary>
    private static void StableSortByProximity(List<CatalogTocHit> hits)
    {
        int n = hits.Count;
        var arrival = new int[n];
        var items = new CatalogTocHit[n];
        for (int i = 0; i < n; i++) { items[i] = hits[i]; arrival[i] = i; }

        // Sort an index permutation so the arrival position stays attached to each hit without
        // a per-hit dictionary lookup in the comparer.
        Array.Sort(arrival, (x, y) =>
        {
            var a = items[x];
            var b = items[y];
            if (a.IsLiteral != b.IsLiteral) return a.IsLiteral ? -1 : 1;
            if (a.MatchesTitleOrAuthor != b.MatchesTitleOrAuthor) return a.MatchesTitleOrAuthor ? -1 : 1;
            if (a.TitleCovered != b.TitleCovered) return a.TitleCovered ? -1 : 1;
            if (a.TitleCovered && a.TitleDistinctWords != b.TitleDistinctWords) return b.TitleDistinctWords.CompareTo(a.TitleDistinctWords);
            if (a.TitleCovered && a.TitleLengthGap != b.TitleLengthGap) return a.TitleLengthGap.CompareTo(b.TitleLengthGap);
            if (a.LeafCovered != b.LeafCovered) return a.LeafCovered ? -1 : 1;
            if (a.WordsMatched != b.WordsMatched) return b.WordsMatched.CompareTo(a.WordsMatched);
            if (a.QuotedFormMatch != b.QuotedFormMatch) return a.QuotedFormMatch ? -1 : 1;
            int c = b.WordsFound.CompareTo(a.WordsFound);   // more words found first
            if (c != 0) return c;
            // Words in the typed order first: "Shmuel 2 perek 1" is not "Shmuel 1 / perek 2",
            // though both carry the same four words equally tightly.
            if (a.QueryInOrder != b.QueryInOrder) return a.QueryInOrder ? -1 : 1;
            c = a.WordSpan.CompareTo(b.WordSpan);           // tighter cluster first
            return c != 0 ? c : x.CompareTo(y);             // else keep arrival order
        });

        hits.Clear();
        for (int i = 0; i < n; i++) hits.Add(items[arrival[i]]);
    }

    /// <summary>True when this hit's own stored text carries any of the quoted acronyms the
    /// query was typed with.</summary>
    private static bool HitCarriesQuotedKey(Document doc, HashSet<string> queryQuotedKeys)
    {
        // IndexedFields is the same three stored fields, reused so this allocates no array per
        // hit. Their ORDER does not matter — any one match answers the question — though
        // path-first means the common case returns soonest.
        foreach (var field in IndexedFields)
        {
            var text = doc.Get(field);
            if (string.IsNullOrEmpty(text)) continue;
            var keys = SeforimDbCatalogTextNormalizer.GetQuotedAbbreviationKeys(text);
            if (keys == null) continue;
            foreach (var key in queryQuotedKeys) if (keys.Contains(key)) return true;
        }
        return false;
    }

    /// <summary>The stored path minus its book-title head (and the " / " after it). A pre-v21
    /// segment has no title-length field; it splits at the first " / " instead, which is only
    /// wrong for a title that itself contains " / ", and only until the rebuild lands.</summary>
    private static string PathWithoutTitle(Document doc, string fullTocPath, int level)
    {
        if (level == 0) return "";
        int titleLength = doc.GetField(FieldTitleLength)?.GetInt32Value()
            ?? fullTocPath.IndexOf(" / ", StringComparison.Ordinal);
        int cut = titleLength + 3;
        return titleLength >= 0 && cut <= fullTocPath.Length ? fullTocPath.Substring(cut) : fullTocPath;
    }

    /// <summary>Every word the ranking counts as typed: each alternative's words and their
    /// variants — except for an unquoted word that is also a key, which counts only as itself
    /// (<see cref="SeforimDbCatalogTextNormalizer.QueryToken.PreferLiteral"/>): "bamidbar"
    /// must not make Bamidbar Rabbah's title look fully typed. Built once per pass;
    /// <see cref="IsCovered"/> tests each hit's title against it.</summary>
    private static HashSet<string> QueryWordSet(List<SeforimDbCatalogTextNormalizer.QueryToken> tokens, VariantIndex? variants)
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
    private static Dictionary<string, int> QueryWordCounts(List<SeforimDbCatalogTextNormalizer.QueryToken> tokens)
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
    private static int CountMatched(List<string> pathTokens, Dictionary<string, int> queryCounts, VariantIndex? variants)
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
    private static int DistinctFound(List<string> tokens, HashSet<string> queryWords)
    {
        if (tokens.Count == 0) return 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in tokens)
            if (queryWords.Contains(word)) seen.Add(word);
        return seen.Count;
    }

    /// <summary>The last " / "-separated segment of a path without its title.</summary>
    private static string LastSegment(string tocPath)
    {
        int i = tocPath.LastIndexOf(" / ", StringComparison.Ordinal);
        return i < 0 ? tocPath : tocPath.Substring(i + 3);
    }

    /// <summary>True when EVERY title word is a query word — the query names the book. An
    /// abbreviation query counts through its expansions, so a quoted acronym for a two-word
    /// title covers that title.</summary>
    private static bool IsCovered(List<string> titleTokens, HashSet<string> queryWords)
    {
        if (titleTokens.Count == 0) return false;
        foreach (var word in titleTokens)
            if (!queryWords.Contains(word)) return false;
        return true;
    }

    /// <summary>The book title at the head of a stored path (the whole path for a level-0
    /// hit); same split as <see cref="PathWithoutTitle"/>.</summary>
    private static string TitleOf(Document doc, string fullTocPath, int level)
    {
        if (level == 0) return fullTocPath;
        int titleLength = doc.GetField(FieldTitleLength)?.GetInt32Value()
            ?? fullTocPath.IndexOf(" / ", StringComparison.Ordinal);
        return titleLength > 0 && titleLength <= fullTocPath.Length ? fullTocPath.Substring(0, titleLength) : fullTocPath;
    }

    /// <summary>
    /// True when EVERY query token landed in the book's own title/TOC path or its author,
    /// rather than only in the category path.
    ///
    /// The membership test MIRRORS <see cref="BuildQuery"/>: per token, satisfied when SOME
    /// alternative is fully present, each word by its own form or any variant. Testing only the
    /// canonical alternative demotes exactly the hits this key exists to promote — many
    /// abbreviation keys are ambiguous, and some carry their self-alternative in a later slot.
    /// </summary>
    private static bool MatchesOnTitleOrAuthor(
        Document doc, List<string> pathTokens, List<SeforimDbCatalogTextNormalizer.QueryToken> tokens, VariantIndex? variants)
    {
        if (tokens.Count == 0) return false;

        var authorText = doc.Get(FieldAuthor);
        var authorTokens = string.IsNullOrEmpty(authorText)
            ? null
            : SeforimDbCatalogTextNormalizer.Tokenize(authorText);

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
    private sealed class PathScratch
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
    private sealed class SortKeyCollector(CancellationToken ct, HashSet<int>? literalDocIds, HashSet<long> coveredRanks) : ICollector
    {
        public readonly struct Entry(int docId, int level, long treeOrder, bool isLiteral, int structureId, bool titleCovered)
        {
            public readonly int DocId = docId;
            public readonly int Level = level;
            public readonly long TreeOrder = treeOrder;
            public readonly bool IsLiteral = isLiteral;
            public readonly int StructureId = structureId;
            /// <summary>The query names this doc's book (see GetTitleTokensLocked) — known
            /// here, before the cap, from the book rank alone.</summary>
            public readonly bool TitleCovered = titleCovered;
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
            bool titleCovered = coveredRanks.Count > 0 && coveredRanks.Contains(treeOrder >> 24);
            _entries.Add(new Entry(globalDoc, level, treeOrder, isLiteral, structureId, titleCovered));
        }

        public List<Entry> Ordered()
        {
            _entries.Sort(static (a, b) =>
            {
                // Accuracy-first: literal matches (exact / non-variant) ahead of variant-
                // or fuzzy-only matches. Then the existing catalog order within each block.
                if (a.IsLiteral != b.IsLiteral) return a.IsLiteral ? -1 : 1;
                // The books the query NAMES come next, whatever their catalog position: the
                // materialization cap below must never cut them off behind earlier books
                // that merely share the words.
                if (a.TitleCovered != b.TitleCovered) return a.TitleCovered ? -1 : 1;
                int c = a.Level.CompareTo(b.Level);
                return c != 0 ? c : a.TreeOrder.CompareTo(b.TreeOrder);
            });
            return _entries;
        }
    }

    // ── Query-time variant lookup (ה-prefix + חסר/מלא skeleton) ──────────────────

    /// <summary>
    /// Query-time-only port of the Vue frontend's book-catalog search normalization
    /// (ported from the former frontend normalizer) — NOT fuzzy/edit-distance matching. Built once per
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
