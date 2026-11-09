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
        if (!ReferenceEquals(old, reader)) old?.Dispose();
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
            return $"reader={(_reader is null ? "closed" : "open")} writer={(_writer is null ? "none" : "open")}";
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
    /// author. Matching and ranking are NEVER capped. Lucene relevance is ignored: every
    /// match is collected with its ranking keys read from index data alone (postings,
    /// positions, doc-values — see <see cref="RankCollector"/>), ordered by
    /// <see cref="SeforimDbCatalogRankingKeys.Compare"/> and then catalog position (Level,
    /// then TreeOrder), and only the ordered top <see cref="MaterializeCap"/> are read from
    /// stored fields. No key is ever measured on stored text.
    ///
    /// Query token order: the test runs against the TOC-PATH FIELD ONLY — query tokens present
    /// among the path's tokens must appear there in typed order; tokens that aren't in the
    /// path (catalog terms, authors) don't participate by construction. When a (level, book)
    /// group contains BOTH an in-order and an out-of-order hit, the out-of-order hits are
    /// DISCARDED ("תנך בראשית ד יד" keeps פרק ד / פסוק יד and drops פרק יד / פסוק ד). Groups
    /// with no in-order hit are kept untouched, so title/catalog word order (משנה תורה vs
    /// תורה משנה) never filters anything.
    ///
    /// Per-structure level truncation (after the discard): within each book's each TOC
    /// structure (per literal block), only the shallowest level that matched is kept —
    /// its deeper-level hits are dropped. Scoped that narrowly on purpose: a structure
    /// whose sole matches are deep keeps them, and neither another book nor another
    /// structure of the SAME book (alt structures are independent address spaces) can
    /// truncate it — except that a book whose own title row is its best match collapses
    /// whole, every structure, to that row.
    /// </summary>
    public List<CatalogTocHit> Search(string query, CancellationToken ct = default)
        => Search(query, null, ct);

    /// <summary><see cref="Search(string, CancellationToken)"/> limited to books filed (at any
    /// depth) under one of <paramref name="categoryIds"/> — a FILTER clause on every query the
    /// search runs, so the ranking is the catalog's own over the scoped set. Null = unscoped;
    /// an empty set matches nothing. Scoped hits carry
    /// <see cref="CatalogTocHit.CategoryAncestors"/>.</summary>
    public List<CatalogTocHit> Search(string query, IReadOnlyCollection<int>? categoryIds, CancellationToken ct = default)
    {
        if (categoryIds is { Count: 0 }) return [];
        var tokens = SeforimDbCatalogTextNormalizer.TokenizeQuery(query);
        if (tokens.Count == 0) return [];

        // A reference is held on the reader (see TryAcquireSearcher), so an idle release or
        // a reader swap landing mid-search cannot close it under us. Variants are looked up
        // in that same reader's term dictionary — nothing is built per reader.
        if (!TryAcquireSearcher(out var searcher))
        {
            if (!TryOpenActive() || !TryAcquireSearcher(out searcher)) return [];
        }
        try
        {
            return SearchOnSearcher(searcher, new VariantLookup(searcher.IndexReader), tokens, query, CategoryFilter(categoryIds), ct);
        }
        finally
        {
            searcher.IndexReader.DecRef();
        }
    }

    /// <summary>
    /// Snapshot the current searcher under the lock and take a reference on the reader
    /// (<see cref="IndexReader.TryIncRef"/>) so it stays open until the caller's
    /// <see cref="IndexReader.DecRef"/> even if <see cref="ReleaseIdleReader"/> or a reader
    /// swap disposes it meanwhile (Dispose only drops the owner's reference). False when no
    /// reader is open.
    /// </summary>
    private bool TryAcquireSearcher(out IndexSearcher searcher)
    {
        lock (_lock)
        {
            searcher = _searcher!;
            return _searcher is not null && _searcher.IndexReader.TryIncRef();
        }
    }

    private List<CatalogTocHit> SearchOnSearcher(
        IndexSearcher searcher, VariantLookup variants,
        List<SeforimDbCatalogTextNormalizer.QueryToken> tokens, string query, Query? scope, CancellationToken ct)
    {
        // The query-side word sets every key is measured against, decided once per search.
        var queryWords = SeforimDbCatalogRankingKeys.QueryWordSet(tokens, variants);
        var queryCounts = SeforimDbCatalogRankingKeys.QueryWordCounts(tokens);
        int queryWordCount = 0;
        foreach (var n in queryCounts.Values) queryWordCount += n;
        // The typed words, not an expansion: "bamidbar" typed is the word bamidbar, and
        // ordering on its expansion "Bamidbar Rabbah" put a second "rabbah" in the sequence
        // that no path has — every hit failed the order test, so word order decided nothing.
        var orderWords = SeforimDbCatalogRankingKeys.OrderWords(tokens);
        // The quoted acronyms the user actually typed, if any: matched against the keys the
        // index holds per book (FieldQuotedKey).
        var queryQuotedKeys = SeforimDbCatalogTextNormalizer.GetQuotedAbbreviationKeys(query);
        // The books the query names — CoveredTitles stays unscoped on purpose: it only marks
        // books, and a book outside the scope never reaches the collector to be marked.
        var coveredTitles = CoveredTitles(searcher.IndexReader, queryWords, queryWordCount);

        // Accuracy-first ranking: literal (exact / non-variant) matches rank ahead of
        // variant/fuzzy-only ones. To tag each hit, run the strict literal query (no
        // variants, no fuzzy) into a doc-ID set; a hit is literal iff it is in that set.
        var litCollector = new DocIdSetCollector(ct, searcher.IndexReader.MaxDoc);
        searcher.Search(Scoped(BuildQuery(tokens, fuzzy: false, variants: null), scope), litCollector);
        FixedBitSet? literalDocIds = litCollector.Ids;

        var pass = new PassInputs(tokens, variants, literalDocIds, coveredTitles, queryWords, queryCounts, orderWords, queryQuotedKeys, scope);

        // Normal pass: exact + כתיב/ה-prefix variants (never fuzzy).
        var hits = RunPass(searcher, pass, fuzzy: false, ct);

        // SPARSE-FUZZY APPEND: when the normal result set is sparse (fewer than
        // SparseFuzzyThreshold hits — which subsumes the old "exact found nothing"
        // fallback), run a fuzzy pass and APPEND the hits it found that the normal pass
        // did not. Fuzzy edits are tried on the catalog and author fields ONLY — never
        // the TOC path, where a one-letter edit is a different chapter/verse — and only
        // for tokens of 3+ characters. The appended hits are fuzzy-only (never literal),
        // so they sit strictly AFTER every normal hit; ordered among themselves by
        // the same rules. This is a "did you mean" tail, not a reranking of the
        // confident results above it.
        if (hits.Count < SparseFuzzyThreshold && tokens.Any(HasFuzzyableWord))
        {
            var fuzzyHits = RunPass(searcher, pass, fuzzy: true, ct);
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

    /// <summary>What both passes of one search share: the query-side sets and the per-search
    /// index tables, built once.</summary>
    private sealed record PassInputs(
        List<SeforimDbCatalogTextNormalizer.QueryToken> Tokens, VariantLookup Variants, FixedBitSet? LiteralDocIds,
        Dictionary<int, CoveredTitle> CoveredTitles, HashSet<string> QueryWords, Dictionary<string, int> QueryCounts,
        List<string> OrderWords, HashSet<string>? QueryQuotedKeys, Query? Scope);

    /// <summary>
    /// One search pass: run the query (optionally fuzzy) through <see cref="RankCollector"/>,
    /// which hands back EVERY match with its full ranking keys (index data only — nothing is
    /// capped and no stored field is read); apply the two per-book set rules — the query-token-
    /// order discard and the per-structure level truncation — over that whole set; order it by
    /// the ranking keys, then catalog order; and materialize (read stored fields — the only
    /// expensive step) the ordered top <see cref="MaterializeCap"/>. The cap is a PERFORMANCE
    /// bound and nothing more: matching, the set rules and the ordering above are uncapped, so
    /// it only limits how many already-ordered documents are turned into full hit objects (no
    /// one scrolls past ~1000). Used for both the normal pass and the fuzzy append pass.
    /// </summary>
    private List<CatalogTocHit> RunPass(IndexSearcher searcher, PassInputs pass, bool fuzzy, CancellationToken ct)
    {
        var collector = new RankCollector(ct, pass.Tokens, pass.Variants, pass.LiteralDocIds, pass.CoveredTitles,
            pass.QueryWords, pass.QueryCounts, pass.OrderWords, pass.QueryQuotedKeys);
        searcher.Search(Scoped(BuildQuery(pass.Tokens, fuzzy, pass.Variants), pass.Scope), collector);
        var entries = collector.Entries;
        if (entries.Count == 0) return [];

        // Query-token-order discard: within each (level, book) group that has at least one
        // in-order hit, drop the out-of-order ones. Groups with no in-order hit are kept
        // untouched, so title/catalog word order (משנה תורה vs תורה משנה) never filters anything.
        if (pass.OrderWords.Count >= 2)
        {
            var groupsWithInOrder = new HashSet<(int Level, int Book)>();
            foreach (var e in entries)
                if (e.Keys.QueryInOrder) groupsWithInOrder.Add((e.Level, e.BookId));
            if (groupsWithInOrder.Count > 0)
                entries.RemoveAll(e => !e.Keys.QueryInOrder && groupsWithInOrder.Contains((e.Level, e.BookId)));
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
        //
        // "Shallower" is judged among the entries whose path holds the most of the query's
        // words, repetitions included (WordsMatched): for "Magen Avot mishnah 5" the level-1
        // "perek 5" shares only the "5" with the query, while "perek 1 / mishnah 5" holds both
        // words — the deeper one is the address typed, and keeping the shallower one would
        // have hidden it. A plain title query ties every entry with the book (each holds the
        // title words once), and the book, shallowest, is what stays.
        //
        // The book's TITLE row, when it is the book's best match, is the shallowest address of
        // the WHOLE book: every structure collapses to it, also an alt structure that has no row
        // of its own at level 0 ("Tur" is the book, not six hundred of its hilchot headings).
        var bestByStructure = new Dictionary<(bool IsLiteral, int Book, int StructureId), (int Matched, int Level)>();
        var titleMatched = new Dictionary<(bool IsLiteral, int Book), int>();
        foreach (var e in entries)
        {
            var key = (e.Keys.IsLiteral, e.BookId, e.StructureId);
            if (!bestByStructure.TryGetValue(key, out var best)
                || e.Keys.WordsMatched > best.Matched
                || (e.Keys.WordsMatched == best.Matched && e.Level < best.Level))
                bestByStructure[key] = (e.Keys.WordsMatched, e.Level);
            if (e.Level == 0) titleMatched[(e.Keys.IsLiteral, e.BookId)] = e.Keys.WordsMatched;
        }
        // An entry whose OWN name holds a typed word beyond the title (LeafAddsWord) is never trimmed: a book with
        // "daf 8" in its first edition and again under "second edition" must show both.
        entries.RemoveAll(e =>
        {
            if (e.Keys.LeafAddsWord) return false;
            var best = bestByStructure[(e.Keys.IsLiteral, e.BookId, e.StructureId)];
            if (e.Keys.WordsMatched < best.Matched || e.Level > best.Level) return true;
            return e.Level > 0 && titleMatched.TryGetValue((e.Keys.IsLiteral, e.BookId), out int byTitle) && byTitle >= e.Keys.WordsMatched;
        });

        // The order: the ranking keys (proximity included for a 2+ word query — how tightly
        // the query's words sit together in the path, below the accuracy keys so a variant
        // match never overtakes a literal one however tightly its words cluster), then the
        // catalog's own order (Level, then TreeOrder) among hits that tie on every key.
        bool proximity = pass.OrderWords.Count >= 2;
        entries.Sort((a, b) =>
        {
            int c = SeforimDbCatalogRankingKeys.Compare(a.Keys, b.Keys, proximity);
            if (c != 0) return c;
            c = a.Level.CompareTo(b.Level);   // the keys rank title-or-not only; depth is catalog order
            return c != 0 ? c : a.TreeOrder.CompareTo(b.TreeOrder);
        });

        int take = Math.Min(MaterializeCap, entries.Count);
        var hits = new List<CatalogTocHit>(take);
        for (int i = 0; i < take; i++)
        {
            ct.ThrowIfCancellationRequested();
            var e = entries[i];
            var doc = searcher.Doc(e.DocId);
            string fullTocPath = doc.Get(FieldFullTocPath) ?? "";
            hits.Add(new CatalogTocHit
            {
                BookId = doc.GetField(FieldBookId)?.GetInt32Value() ?? 0,
                LineIndex = doc.GetField(FieldLineIndex)?.GetInt32Value() ?? -1,
                FullTocPath = fullTocPath,
                TocPath = PathWithoutTitle(doc, fullTocPath, e.Level),
                Level = e.Level,
                TreeOrder = e.TreeOrder,
                StructureId = e.StructureId,
                Keys = e.Keys,
                // Read only for a scoped search (UserSpace needs it to find the hit's root).
                CategoryAncestors = pass.Scope is null ? [] : CategoryAncestorsOf(doc),
            });
        }
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
    private static BooleanQuery BuildQuery(List<SeforimDbCatalogTextNormalizer.QueryToken> tokens, bool fuzzy, VariantLookup? variants)
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
    /// <see cref="VariantLookup"/> — ported from the Vue frontend's book-catalog search,
    /// always active, not gated on the fuzzy fallback), plus fuzzy catalog/author when
    /// requested and the word is long enough.</summary>
    private static BooleanQuery WordClause(string word, bool fuzzy, VariantLookup? variants)
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

    /// <summary>The any-of category clause for a scoped search; null when unscoped.
    /// <see cref="CoveredTitles"/> stays unscoped on purpose: it only marks books the query
    /// names, and a book outside the scope never reaches the collector to be marked.</summary>
    private static Query? CategoryFilter(IReadOnlyCollection<int>? categoryIds)
    {
        if (categoryIds is null) return null;
        var anyOf = new BooleanQuery();
        foreach (int id in categoryIds)
            anyOf.Add(new TermQuery(new Term(FieldCategoryAncestor, id.ToString(System.Globalization.CultureInfo.InvariantCulture))), Occur.SHOULD);
        return anyOf;
    }

    /// <summary>The words query AND the scope: the scope is a MUST that only filters — scores
    /// are ignored by every collector here anyway.</summary>
    private static Query Scoped(BooleanQuery query, Query? scope)
    {
        if (scope is null) return query;
        var scoped = new BooleanQuery { { query, Occur.MUST }, { scope, Occur.MUST } };
        return scoped;
    }

    /// <summary>The stored category ancestors of a hit's book, own category first.</summary>
    private static int[] CategoryAncestorsOf(Document doc)
    {
        var values = doc.GetValues(FieldCategoryAncestor);
        var ids = new int[values.Length];
        for (int i = 0; i < values.Length; i++)
            ids[i] = int.Parse(values[i], System.Globalization.CultureInfo.InvariantCulture);
        return ids;
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

    /// <summary>Collects just the global doc-IDs a query matched, into a set. Used to
    /// run the strict LITERAL query (no כתיב/ה-prefix variants, no fuzzy) alongside the
    /// full one, so each hit can be tagged literal-or-variant BEFORE the materialization
    /// cap is applied — see <see cref="RankCollector"/>.</summary>
    private sealed class DocIdSetCollector(CancellationToken ct, int maxDoc) : ICollector
    {
        private readonly FixedBitSet _ids = new(maxDoc);
        private int _count;
        private int _docBase;

        public FixedBitSet Ids => _ids;

        public void SetScorer(Scorer scorer) { }
        public void SetNextReader(AtomicReaderContext context) => _docBase = context.DocBase;
        public bool AcceptsDocsOutOfOrder => true;

        public void Collect(int doc)
        {
            if ((_count++ & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
            _ids.Set(_docBase + doc);
        }
    }

    // ── Query-time variant lookup (ה-prefix + חסר/מלא skeleton) ──────────────────

    /// <summary>
    /// Query-time port of the Vue frontend's book-catalog search normalization (ported from
    /// the former frontend normalizer) — NOT fuzzy/edit-distance matching. Answered from the
    /// index's own term dictionary, per search, with nothing built per reader:
    ///
    ///   - ה-prefix: a query word starting with ה also probes its stripped form, and a
    ///     query word without ה also probes its ה-prefixed form — either direction fires
    ///     as soon as THAT SPECIFIC indexed term exists in any of <see cref="IndexedFields"/>
    ///     (הרמבן ↔ רמבן).
    ///   - חסר/מלא skeleton: every indexed word sharing the query word's consonantal skeleton
    ///     with a compatible (subset) vowel-set — נידה ↔ נדה, but not שבועות ↔ שביעית. The
    ///     skeleton field indexes each word as "skeleton \u0001 word", so one term-range seek
    ///     lists the skeleton's group. The query word itself need not be indexed anywhere.
    ///
    /// <see cref="Lookup"/> returns the extra literal terms (beyond the typed word itself)
    /// that should also be probed — always applied, not gated behind the fuzzy fallback.
    /// Memoized for the one search it serves.
    /// </summary>
    internal sealed class VariantLookup(IndexReader reader)
    {
        private readonly Dictionary<string, List<string>> _memo = new(StringComparer.Ordinal);
        private TermsEnum? _skeletons;

        public List<string> Lookup(string word)
        {
            if (_memo.TryGetValue(word, out var known)) return known;
            var extra = new List<string>();
            void Add(string term)
            {
                if (term != word && !extra.Contains(term)) extra.Add(term);
            }

            string? stripped = SeforimDbCatalogTextNormalizer.StripHePrefix(word);
            if (stripped is not null && IsIndexed(stripped)) Add(stripped);
            string withHe = "ה" + word;
            if (IsIndexed(withHe)) Add(withHe);

            var decomp = SeforimDbCatalogTextNormalizer.DecomposeSkeleton(word);
            if (_skeletons is null)
            {
                var terms = MultiFields.GetTerms(reader, FieldVariantSkeleton);
                _skeletons = terms?.GetEnumerator();
            }
            if (_skeletons is not null)
            {
                string prefix = decomp.Skeleton + SkeletonSeparator;
                var prefixBytes = new BytesRef(prefix);
                if (_skeletons.SeekCeil(prefixBytes) != TermsEnum.SeekStatus.END)
                {
                    do
                    {
                        if (!StringHelper.StartsWith(_skeletons.Term, prefixBytes)) break;
                        string candidate = _skeletons.Term.Utf8ToString().Substring(prefix.Length);
                        if (SeforimDbCatalogTextNormalizer.AreSkeletonVariants(decomp, SeforimDbCatalogTextNormalizer.DecomposeSkeleton(candidate)))
                            Add(candidate);
                    } while (_skeletons.MoveNext());
                }
            }

            _memo[word] = extra;
            return extra;
        }

        private bool IsIndexed(string term)
        {
            foreach (var field in IndexedFields)
                if (reader.DocFreq(new Term(field, term)) > 0) return true;
            return false;
        }
    }
}
