using System.Security.Cryptography;
using System.Text;
using KitveiHakodeshService.SefroimDb;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.Catalog;

/// <summary>
/// Lucene.NET (4.8) full-text index over FULL TOC PATHS — the engine behind the catalog
/// "file-system" search, replacing the frontend's manual per-query TOC heuristics.
///
/// One document per TOC entry, carrying two indexed fields:
///   b — the book part: title + category path + authors (what the manual pipeline's
///       book matcher searched)
///   t — the TOC part: every segment of the entry's root→leaf path (what the manual
///       SegmentSearchTree scored), with title-variant roots stripped the same way
/// plus one document per book (title only) pointing at the book's first line.
///
/// Both fields store an EXPANDED token set (see CatalogTocTextRules.ExpandIndexTokens)
/// so an AND-of-prefix-queries over the plain query words is a superset of every manual
/// matcher tier (exact / prefix / ה-prefix / חסר-מלא skeleton / quote-splitting).
///
/// Search = for each query word a MUST clause matching (b OR t) by prefix, exact hits
/// boosted; results are never capped. Ancestry dedup (a matched entry suppresses its
/// matched descendants — manual Pass 3) is applied unless the caller opts out.
///
/// This class is deliberately host-agnostic (plain ctor args, synchronous Build) so the
/// parity test drives it directly; service concerns (background build, hash-triggered
/// rebuild, RPC) live in CatalogTocSearchService.
/// </summary>
public sealed class CatalogTocIndex(string indexPath, string dbPath) : IDisposable
{
    private const LuceneVersion Ver = LuceneVersion.LUCENE_48;

    // Stored/indexed field names — short on purpose (a million+ docs).
    private const string FieldBook = "b";        // indexed: book title + category path + authors
    private const string FieldToc = "t";         // indexed: full TOC path segments
    private const string FieldKindIndexed = "ki";// indexed: "b" = book title doc, "t" = toc entry doc
    private const string FieldKind = "k";        // stored: 1 = book title doc, 2 = toc entry doc
    private const string FieldBookId = "bid";    // stored
    private const string FieldTocEntryId = "tid";// stored (0 for book docs)
    private const string FieldLineId = "lid";    // stored (0 when the entry has no line)
    private const string FieldLineIndex = "lix"; // stored (-1 when unknown)
    private const string FieldTitle = "bt";      // stored: book title
    private const string FieldDisplay = "dp";    // stored: TOC display path within the book
    private const string FieldAncestors = "anc"; // stored: comma-joined ancestor tocEntry ids
    private const string FieldTreeOrder = "to";  // stored: the book's catalog tree order (rank tiebreak)

    private readonly object _readerLock = new();
    private DirectoryReader? _reader;
    private IndexSearcher? _searcher;

    public string IndexPath => indexPath;

    // ── Source-DB hash ──────────────────────────────────────────────────────────

    /// <summary>Bump when the index schema changes (new fields, token rules) so existing
    /// indexes rebuild — the version is folded into the ver-file content.</summary>
    public const string IndexFormatVersion = "v4";

    /// <summary>
    /// Fingerprint of the seforim DB the index answers for: path + size + mtime hashed,
    /// prefixed with the index format version. Changes whenever the DB file is replaced,
    /// updated, the user switches databases, or the index schema itself changes — the
    /// trigger for a full rebuild (mirrors the FTS index's source-DB versioning).
    /// </summary>
    public static string ComputeDbHash(string dbPath)
    {
        var info = new FileInfo(dbPath);
        string material = $"{dbPath.ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        return IndexFormatVersion + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    // ── Build ───────────────────────────────────────────────────────────────────

    private sealed class TocRow
    {
        public int Id;
        public int? ParentId;
        public int BookId;
        public int LineId;       // 0 = none
        public int LineIndex;    // -1 = none
        public string Text = "";
    }

    /// <summary>Rebuild the whole index from the seforim DB (synchronous; the service
    /// wrapper runs it on a background task). Returns the number of documents written.</summary>
    public int Build(Action<int, int>? onProgress = null, CancellationToken ct = default)
    {
        InvalidateReader();

        using var conn = OpenDb();
        var categoryList = LoadCategories(conn);
        var books = LoadBooks(conn);
        var firstLines = LoadFirstLines(conn);
        var treeOrders = ComputeTreeOrders(categoryList, books);

        var categories = new Dictionary<int, (int? ParentId, string Title)>(categoryList.Count);
        foreach (var c in categoryList) categories[c.Id] = (c.ParentId, c.Title);

        using var dir = FSDirectory.Open(indexPath);
        using var analyzer = new WhitespaceOnlyAnalyzer();
        var config = new IndexWriterConfig(Ver, analyzer)
        {
            OpenMode = OpenMode.CREATE,
            RAMBufferSizeMB = 48,
        };
        using var writer = new IndexWriter(dir, config);

        int docCount = 0, bookNo = 0;

        // Book-title docs — the title points at the book's first line.
        foreach (var (bookId, title, categoryId, authors) in books)
        {
            ct.ThrowIfCancellationRequested();
            var doc = new Document
            {
                new TextField(FieldBook, BookFieldText(title, categoryId, authors, categories), Field.Store.NO),
                new StringField(FieldKindIndexed, "b", Field.Store.NO),
                new StoredField(FieldKind, 1),
                new StoredField(FieldBookId, bookId),
                new StoredField(FieldTocEntryId, 0),
                new StoredField(FieldLineId, firstLines.TryGetValue(bookId, out var fl) ? fl.LineId : 0),
                new StoredField(FieldLineIndex, firstLines.TryGetValue(bookId, out var fl2) ? fl2.LineIndex : -1),
                new StoredField(FieldTitle, title),
                new StoredField(FieldDisplay, ""),
                new StoredField(FieldAncestors, ""),
                new StoredField(FieldTreeOrder, treeOrders.GetValueOrDefault(bookId, int.MaxValue)),
            };
            writer.AddDocument(doc);
            docCount++;
        }

        // TOC docs — stream all entries ordered by book, index each book's tree at once.
        var bookMeta = books.ToDictionary(b => b.Id, b => b);
        foreach (var group in StreamTocRowsByBook(conn))
        {
            ct.ThrowIfCancellationRequested();
            if (!bookMeta.TryGetValue(group.BookId, out var book)) continue;

            var rows = StripTitleRoots(group.Rows, book.Title, group.BookId);
            docCount += IndexBookToc(writer, rows, book, categories,
                treeOrders.GetValueOrDefault(group.BookId, int.MaxValue));

            if (++bookNo % 200 == 0) onProgress?.Invoke(bookNo, books.Count);
        }

        // Alt-TOC docs — alternative structures (parshiot/aliyot, dapim, …) indexed the
        // same way, one tree per structure, so "בראשית נח עליה א" resolves. The
        // structure's label tokens are folded into the toc field (queries may include
        // them) but stay out of the display path (the alt tree doesn't show them).
        var altStructures = LoadAltStructures(conn);
        foreach (var group in StreamAltTocRowsByStructure(conn))
        {
            ct.ThrowIfCancellationRequested();
            if (!altStructures.TryGetValue(group.StructureId, out var st)) continue;
            if (!bookMeta.TryGetValue(st.BookId, out var book)) continue;

            var rows = StripTitleRoots(group.Rows, book.Title, st.BookId);
            docCount += IndexBookToc(writer, rows, book, categories,
                treeOrders.GetValueOrDefault(st.BookId, int.MaxValue),
                altStructureLabel: string.IsNullOrWhiteSpace(st.HeTitle) ? st.Title : st.HeTitle);
        }

        writer.Commit();
        onProgress?.Invoke(books.Count, books.Count);
        return docCount;
    }

    /// <summary>Index one book's (root-stripped) TOC rows: build each entry's segment
    /// chain root→leaf, expand tokens per segment, and add one doc per entry.
    /// With <paramref name="altStructureLabel"/> set, the rows are an ALT-TOC structure:
    /// the label's tokens seed every chain and docs are stored as kind 3.</summary>
    private static int IndexBookToc(
        IndexWriter writer, List<TocRow> rows,
        (int Id, string Title, int CategoryId, string? Authors) book,
        Dictionary<int, (int? ParentId, string Title)> categories,
        int treeOrder,
        string? altStructureLabel = null)
    {
        var byId = rows.ToDictionary(r => r.Id);
        string bookField = BookFieldText(book.Title, book.CategoryId, book.Authors, categories);
        bool isAlt = altStructureLabel is not null;

        // Memoized per-entry chains (token text, display path, ancestor ids).
        var chainCache = new Dictionary<int, (string TocText, string Display, string Ancestors)>();
        (string TocText, string Display, string Ancestors) GetChain(TocRow row)
        {
            if (chainCache.TryGetValue(row.Id, out var cached)) return cached;

            var tokens = new HashSet<string>();
            string display, ancestors;
            if (row.ParentId is { } pid && byId.TryGetValue(pid, out var parent))
            {
                var p = GetChain(parent);
                foreach (var t in p.TocText.Split(' ', StringSplitOptions.RemoveEmptyEntries)) tokens.Add(t);
                CatalogTocTextRules.ExpandIndexTokens(row.Text, tokens);
                display = p.Display.Length > 0 ? p.Display + " / " + row.Text : row.Text;
                ancestors = p.Ancestors.Length > 0 ? p.Ancestors + "," + pid : pid.ToString();
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(altStructureLabel))
                    CatalogTocTextRules.ExpandIndexTokens(altStructureLabel, tokens);
                CatalogTocTextRules.ExpandIndexTokens(row.Text, tokens);
                display = row.Text;
                ancestors = "";
            }
            var result = (string.Join(' ', tokens), display, ancestors);
            chainCache[row.Id] = result;
            return result;
        }

        int added = 0;
        foreach (var row in rows)
        {
            var (tocText, display, ancestors) = GetChain(row);
            var doc = new Document
            {
                new TextField(FieldBook, bookField, Field.Store.NO),
                new TextField(FieldToc, tocText, Field.Store.NO),
                new StringField(FieldKindIndexed, "t", Field.Store.NO),
                new StoredField(FieldKind, isAlt ? 3 : 2),
                new StoredField(FieldBookId, book.Id), // alt rows carry no own bookId
                new StoredField(FieldTocEntryId, row.Id),
                new StoredField(FieldLineId, row.LineId),
                new StoredField(FieldLineIndex, row.LineIndex),
                new StoredField(FieldTitle, book.Title),
                new StoredField(FieldDisplay, display),
                new StoredField(FieldAncestors, ancestors),
                new StoredField(FieldTreeOrder, treeOrder),
            };
            writer.AddDocument(doc);
            added++;
        }
        return added;
    }

    /// <summary>The indexed "book part" of every doc: title + category path + authors,
    /// token-expanded — everything the manual book matcher (filterBooksByWords) indexed.</summary>
    private static string BookFieldText(
        string title, int categoryId, string? authors,
        Dictionary<int, (int? ParentId, string Title)> categories)
    {
        var tokens = new HashSet<string>();
        CatalogTocTextRules.ExpandIndexTokens(title, tokens);

        int? cat = categoryId;
        var guard = 0;
        while (cat is { } cid && categories.TryGetValue(cid, out var c) && guard++ < 32)
        {
            CatalogTocTextRules.ExpandIndexTokens(c.Title, tokens);
            cat = c.ParentId;
        }

        if (!string.IsNullOrWhiteSpace(authors))
            CatalogTocTextRules.ExpandIndexTokens(authors, tokens);

        return string.Join(' ', tokens);
    }

    /// <summary>Remove root TOC entries whose text is a title variant of the book title,
    /// re-parenting their children — port of tocSearchUtils.ts stripTocTitleRoots(), the
    /// same transformation the manual pipeline applies before scoring.</summary>
    private static List<TocRow> StripTitleRoots(List<TocRow> rows, string bookTitle, int bookId)
    {
        if (string.IsNullOrEmpty(bookTitle) || rows.Count == 0) return rows;
        bool forceStrip = CatalogTocTextRules.ForceStripBookIds.Contains(bookId);
        var rootIds = new HashSet<int>();
        foreach (var r in rows)
            if (r.ParentId is null && (forceStrip || CatalogTocTextRules.IsTitleVariant(bookTitle, r.Text)))
                rootIds.Add(r.Id);
        if (rootIds.Count == 0) return rows;

        var result = new List<TocRow>(rows.Count);
        foreach (var r in rows)
        {
            if (rootIds.Contains(r.Id)) continue;
            if (r.ParentId is { } pid && rootIds.Contains(pid)) r.ParentId = null;
            result.Add(r);
        }
        return result;
    }

    // ── DB loading ──────────────────────────────────────────────────────────────

    private SqliteConnection OpenDb()
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ConnectionString;
        var conn = new SqliteConnection(cs);
        conn.Open();
        return conn;
    }

    /// <summary>Categories in the SAME order the frontend loads them (level, then
    /// orderIndex when the column exists) — the tree-order computation depends on it.</summary>
    private static List<(int Id, int? ParentId, string Title)> LoadCategories(SqliteConnection conn)
    {
        bool hasOrderIndex = false;
        using (var probe = conn.CreateCommand())
        {
            probe.CommandText = "PRAGMA table_info(category)";
            using var pr = probe.ExecuteReader();
            while (pr.Read())
                if (string.Equals(pr.GetString(1), "orderIndex", StringComparison.Ordinal)) hasOrderIndex = true;
        }

        var list = new List<(int, int?, string)>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = hasOrderIndex
            ? "SELECT id, parentId, title FROM category ORDER BY level, orderIndex"
            : "SELECT id, parentId, title FROM category ORDER BY level";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetInt32(0), r.IsDBNull(1) ? null : r.GetInt32(1), r.IsDBNull(2) ? "" : r.GetString(2)));
        return list;
    }

    /// <summary>
    /// Each book's position in the catalog tree — identical to the frontend's
    /// buildTree + assignFullPaths (bookCatalogTree.ts): categories nested in load
    /// order, custom (negative-id) entries sorted last per level, orphaned books under
    /// a synthetic last root, then a DFS that numbers books as encountered. This is the
    /// order the manual pipeline effectively ranked tied results by, so it is the
    /// re-rank tiebreak.
    /// </summary>
    private static Dictionary<int, int> ComputeTreeOrders(
        List<(int Id, int? ParentId, string Title)> categories,
        List<(int Id, string Title, int CategoryId, string? Authors)> books)
    {
        var children = new Dictionary<int, List<int>>();   // categoryId → child category ids
        var catBooks = new Dictionary<int, List<int>>();   // categoryId → book ids
        var known = new HashSet<int>();
        foreach (var c in categories) known.Add(c.Id);

        var roots = new List<int>();
        foreach (var c in categories)
        {
            if (c.ParentId is { } pid && known.Contains(pid))
                (children.TryGetValue(pid, out var l) ? l : children[pid] = []).Add(c.Id);
            else roots.Add(c.Id);
        }

        var orphaned = new List<int>();
        foreach (var b in books)
        {
            if (known.Contains(b.CategoryId))
                (catBooks.TryGetValue(b.CategoryId, out var l) ? l : catBooks[b.CategoryId] = []).Add(b.Id);
            else orphaned.Add(b.Id);
        }

        static int CustomLast(int id) => id < 0 ? 1 : 0;
        foreach (var l in children.Values) StableSortByCustomLast(l);
        foreach (var l in catBooks.Values) StableSortByCustomLast(l);
        StableSortByCustomLast(roots);

        var orders = new Dictionary<int, int>(books.Count);
        int counter = 0;
        void Walk(int categoryId)
        {
            if (catBooks.TryGetValue(categoryId, out var bs))
                foreach (int bookId in bs) orders[bookId] = counter++;
            if (children.TryGetValue(categoryId, out var cs))
                foreach (int child in cs) Walk(child);
        }
        foreach (int root in roots) Walk(root);
        foreach (int bookId in orphaned) orders[bookId] = counter++; // synthetic last root

        return orders;

        static void StableSortByCustomLast(List<int> ids)
        {
            var sorted = ids.OrderBy(CustomLast).ToList();
            ids.Clear();
            ids.AddRange(sorted);
        }
    }

    private static List<(int Id, string Title, int CategoryId, string? Authors)> LoadBooks(SqliteConnection conn)
    {
        var list = new List<(int, string, int, string?)>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SeforimSql.GetAllBooks;
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetInt32(0), r.IsDBNull(2) ? "" : r.GetString(2), r.IsDBNull(1) ? 0 : r.GetInt32(1),
                r.IsDBNull(4) ? null : r.GetString(4)));
        return list;
    }

    private static Dictionary<int, (int LineId, int LineIndex)> LoadFirstLines(SqliteConnection conn)
    {
        // SQLite MIN() aggregate guarantees the bare columns come from the minimal row.
        var map = new Dictionary<int, (int, int)>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT bookId, id, MIN(lineIndex) FROM line GROUP BY bookId";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            map[r.GetInt32(0)] = (r.GetInt32(1), r.IsDBNull(2) ? -1 : r.GetInt32(2));
        return map;
    }

    /// <summary>All alt-TOC structures: structureId → (bookId, titles).</summary>
    private static Dictionary<int, (int BookId, string Title, string HeTitle)> LoadAltStructures(SqliteConnection conn)
    {
        var map = new Dictionary<int, (int, string, string)>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, bookId, title, heTitle FROM alt_toc_structure";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            map[r.GetInt32(0)] = (
                r.IsDBNull(1) ? 0 : r.GetInt32(1),
                r.IsDBNull(2) ? "" : r.GetString(2),
                r.IsDBNull(3) ? "" : r.GetString(3));
        return map;
    }

    private sealed class AltTocGroup
    {
        public int StructureId;
        public List<TocRow> Rows = [];
    }

    /// <summary>Stream all alt-TOC entries ordered by structure — one group per
    /// structure, mirroring StreamTocRowsByBook.</summary>
    private static IEnumerable<AltTocGroup> StreamAltTocRowsByStructure(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ae.structureId, ae.id, ae.parentId, ae.lineId, tt.text, l.lineIndex
            FROM alt_toc_entry ae
            JOIN tocText tt ON tt.id = ae.textId
            LEFT JOIN line l ON l.id = ae.lineId
            ORDER BY ae.structureId, ae.id";
        using var r = cmd.ExecuteReader();

        AltTocGroup? group = null;
        while (r.Read())
        {
            int structureId = r.GetInt32(0);
            if (group is null || group.StructureId != structureId)
            {
                if (group is not null) yield return group;
                group = new AltTocGroup { StructureId = structureId };
            }
            group.Rows.Add(new TocRow
            {
                Id = r.GetInt32(1),
                ParentId = r.IsDBNull(2) ? null : r.GetInt32(2),
                BookId = 0, // filled from the structure's book by the caller's doc writer
                LineId = r.IsDBNull(3) ? 0 : r.GetInt32(3),
                Text = r.IsDBNull(4) ? "" : r.GetString(4),
                LineIndex = r.IsDBNull(5) ? -1 : r.GetInt32(5),
            });
        }
        if (group is not null) yield return group;
    }

    private sealed class BookTocGroup
    {
        public int BookId;
        public List<TocRow> Rows = [];
    }

    /// <summary>Stream all TOC entries ordered by book — one group per book so each
    /// book's tree is materialized (and released) in turn instead of all at once.</summary>
    private static IEnumerable<BookTocGroup> StreamTocRowsByBook(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT te.bookId, te.id, te.parentId, te.lineId, tt.text, l.lineIndex
            FROM tocEntry te
            JOIN tocText tt ON tt.id = te.textId
            LEFT JOIN line l ON l.id = te.lineId
            ORDER BY te.bookId, te.id";
        using var r = cmd.ExecuteReader();

        BookTocGroup? group = null;
        while (r.Read())
        {
            int bookId = r.GetInt32(0);
            if (group is null || group.BookId != bookId)
            {
                if (group is not null) yield return group;
                group = new BookTocGroup { BookId = bookId };
            }
            group.Rows.Add(new TocRow
            {
                Id = r.GetInt32(1),
                ParentId = r.IsDBNull(2) ? null : r.GetInt32(2),
                BookId = bookId,
                LineId = r.IsDBNull(3) ? 0 : r.GetInt32(3),
                Text = r.IsDBNull(4) ? "" : r.GetString(4),
                LineIndex = r.IsDBNull(5) ? -1 : r.GetInt32(5),
            });
        }
        if (group is not null) yield return group;
    }

    // ── Search ──────────────────────────────────────────────────────────────────

    /// <summary>Search the index. Results are NEVER capped. <paramref name="dedupAncestors"/>
    /// mirrors the manual pipeline's Pass 3 (a matched entry suppresses matched descendants).
    ///
    /// Pipeline: one collector pass gathers every matching (doc, luceneScore) — no
    /// count-then-collect double execution and no priority queue. Stored fields are
    /// then read in docId order (compressed stored-field chunks decompress once per
    /// neighborhood instead of once per hit — this is what makes a 10k+-hit query fast).
    /// Finally the hits are re-ranked with the manual scorer's semantics (see Rerank)
    /// so the top of the list matches what the catalog page ranked highest today.
    ///
    /// SINGLE-WORD queries search book-title docs only. This is exact manual-pipeline
    /// behavior (both TOC split heuristics need ≥ 2 words, so the manual way never
    /// returns TOC items for one word) and it kills the pathology where a one-letter
    /// query prefix-matched a million TOC docs.</summary>
    public List<CatalogTocHit> Search(string query, bool dedupAncestors = true, CancellationToken ct = default)
    {
        var words = CatalogTocTextRules.QueryWords(query);
        if (words.Count == 0) return [];

        var searcher = GetSearcher();
        if (searcher is null) return [];

        bool booksOnly = words.Count == 1;
        var bq = new BooleanQuery();
        foreach (var word in words)
        {
            var perWord = new BooleanQuery();
            foreach (var form in WordForms(word))
            {
                perWord.Add(new PrefixQuery(new Term(FieldBook, form)), Occur.SHOULD);
                if (!booksOnly) perWord.Add(new PrefixQuery(new Term(FieldToc, form)), Occur.SHOULD);
            }
            bq.Add(perWord, Occur.MUST);
        }
        if (booksOnly) bq.Add(new TermQuery(new Term(FieldKindIndexed, "b")), Occur.MUST);

        // Single pass, uncapped, docId order.
        var collector = new AllHitsCollector(ct);
        searcher.Search(bq, collector);
        var scoreDocs = collector.Hits;
        if (scoreDocs.Count == 0) return [];

        var hits = new List<CatalogTocHit>(scoreDocs.Count);
        foreach (var (docId, luceneScore) in scoreDocs)
        {
            ct.ThrowIfCancellationRequested();
            var doc = searcher.Doc(docId);
            int kind = doc.GetField(FieldKind).GetInt32Value() ?? 2;
            hits.Add(new CatalogTocHit
            {
                Kind = kind == 1 ? "book" : kind == 3 ? "alttoc" : "toc",
                BookId = doc.GetField(FieldBookId).GetInt32Value() ?? 0,
                TocEntryId = doc.GetField(FieldTocEntryId).GetInt32Value() ?? 0,
                LineId = doc.GetField(FieldLineId).GetInt32Value() ?? 0,
                LineIndex = doc.GetField(FieldLineIndex).GetInt32Value() ?? -1,
                BookTitle = doc.Get(FieldTitle) ?? "",
                TocPath = doc.Get(FieldDisplay) ?? "",
                Score = luceneScore,
                AncestorIds = doc.Get(FieldAncestors) ?? "",
                TreeOrder = doc.GetField(FieldTreeOrder)?.GetInt32Value() ?? int.MaxValue,
            });
        }

        Rerank(hits, words);
        return dedupAncestors ? DedupAncestors(hits) : hits;
    }

    /// <summary>Collects every matching doc with its score, in docId order, no cap.
    /// Checks the cancellation token periodically so a superseded search stops early.</summary>
    private sealed class AllHitsCollector(CancellationToken ct) : ICollector
    {
        public readonly List<(int DocId, float Score)> Hits = [];
        private Scorer? _scorer;
        private int _docBase;

        public void SetScorer(Scorer scorer) => _scorer = scorer;
        public void SetNextReader(AtomicReaderContext context) => _docBase = context.DocBase;
        public bool AcceptsDocsOutOfOrder => true;

        public void Collect(int doc)
        {
            if ((Hits.Count & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
            Hits.Add((_docBase + doc, _scorer?.GetScore() ?? 0f));
        }
    }

    // ── Manual-style re-rank ────────────────────────────────────────────────────
    //
    // Raw Lucene TF-IDF ranks a short unrelated path above the exact section the user
    // addressed. The manual pipeline's scorer (segmentSearchTree.ts) got this right, so
    // its semantics are applied here as a re-RANK (never a filter — recall is untouched):
    //
    //   - each hit's segments = [book title] + its TOC display path segments, tokenized
    //     the same way the manual tree tokenized node texts
    //   - query words must appear as an ordered subsequence with prefix matching;
    //     score = intra-segment token distance + 10 per segment boundary crossed
    //     (lower = tighter = better), exactly the manual formula
    //   - two-attempt: hits where the LAST word matches a token exactly (Talmud page
    //     suffixes "י." / "י:" count as exact for "י") rank as a group above
    //     prefix-only hits — the manual "פרק ל before פרק לא" behavior
    //   - ties break on (catalog treeOrder, tocEntryId) — exactly the order the manual
    //     pipeline assembled tied results in (candidate books in tree order, rows in
    //     entry order), so רש"י/רמב"ן follow the chumash the way the catalog shelves them
    //   - hits the manual scorer can't score at all (they matched only via expanded
    //     token forms) sort by the same tree order at the bottom
    private static void Rerank(List<CatalogTocHit> hits, List<string> words)
    {
        var ranked = new (int Tier, int Score)[hits.Count];
        for (int i = 0; i < hits.Count; i++)
        {
            var segments = SegmentsForHit(hits[i]);
            int exactScore = ScorePath(segments, words, lastWordExact: true);
            int prefixScore = exactScore != int.MaxValue ? exactScore : ScorePath(segments, words, lastWordExact: false);
            int tier = exactScore != int.MaxValue ? 0 : prefixScore != int.MaxValue ? 1 : 2;
            ranked[i] = (tier, prefixScore);
        }

        var order = Enumerable.Range(0, hits.Count).ToArray();
        Array.Sort(order, (a, b) =>
        {
            int c = ranked[a].Tier.CompareTo(ranked[b].Tier);
            if (c != 0) return c;
            if (ranked[a].Tier < 2 && (c = ranked[a].Score.CompareTo(ranked[b].Score)) != 0) return c;
            c = hits[a].TreeOrder.CompareTo(hits[b].TreeOrder);
            if (c != 0) return c;
            c = hits[a].TocEntryId.CompareTo(hits[b].TocEntryId);
            return c != 0 ? c : a.CompareTo(b); // stable
        });

        var sorted = new List<CatalogTocHit>(hits.Count);
        foreach (int i in order) sorted.Add(hits[i]);
        hits.Clear();
        hits.AddRange(sorted);
    }

    /// <summary>Tokenized segment chain for a hit: book title first, then each TOC path
    /// segment — the shape the manual scorer walked.</summary>
    private static List<List<string>> SegmentsForHit(CatalogTocHit h)
    {
        var segments = new List<List<string>> { CatalogTocTextRules.TokenizeSegmentText(h.BookTitle) };
        if (h.TocPath.Length > 0)
            foreach (var part in h.TocPath.Split(" / "))
                segments.Add(CatalogTocTextRules.TokenizeSegmentText(part));
        return segments;
    }

    /// <summary>The manual scorer (segmentSearchTree.ts _score) adapted for UNSPLIT
    /// queries: ordered-subsequence prefix match of words across segments;
    /// int.MaxValue = no match.
    ///
    /// Segment 0 is the BOOK TITLE. In the manual pipeline, book words were split out
    /// of TOC scoring entirely, so here every pair anchored in the title is FREE:
    /// title-internal pairs cost 0 (the manual book matcher was order-free and
    /// positionless) and the title→TOC crossing costs 0 (the manual scorer only ever
    /// penalized between TOC words). Within non-title segments the next word must
    /// match FORWARD of the previous one — without this, a long TOC text containing
    /// "…יד…בראשית…" scores negative and beats a real "פרק יד" entry.</summary>
    private static int ScorePath(List<List<string>> segments, List<string> words, bool lastWordExact)
    {
        Span<int> segIndices = words.Count <= 16 ? stackalloc int[words.Count] : new int[words.Count];
        Span<int> tokenIndices = words.Count <= 16 ? stackalloc int[words.Count] : new int[words.Count];
        int segFrom = 0;

        for (int wi = 0; wi < words.Count; wi++)
        {
            string w = words[wi];
            bool requireExact = lastWordExact && wi == words.Count - 1;
            bool found = false;

            for (int si = segFrom; si < segments.Count && !found; si++)
            {
                var seg = segments[si];
                // Forward-only within the previous word's (non-title) segment; the
                // title segment stays order-free like the manual book matcher.
                int tiStart = wi > 0 && si == segFrom && si != 0 ? tokenIndices[wi - 1] + 1 : 0;
                for (int ti = tiStart; ti < seg.Count; ti++)
                {
                    string tok = seg[ti];
                    bool isTalmudSuffix = tok.Length == w.Length + 1
                        && (tok.EndsWith('.') || tok.EndsWith(':'))
                        && tok.StartsWith(w, StringComparison.Ordinal);
                    bool matches = requireExact
                        ? tok == w || isTalmudSuffix
                        : tok.StartsWith(w, StringComparison.Ordinal);
                    if (matches)
                    {
                        segIndices[wi] = si;
                        tokenIndices[wi] = ti;
                        segFrom = si;
                        found = true;
                        break;
                    }
                }
            }

            if (!found) return int.MaxValue;
        }

        const int SegmentCrossingPenalty = 10;
        int score = 0;
        for (int i = 1; i < words.Count; i++)
        {
            if (segIndices[i - 1] == 0) continue; // pair anchored in the title — free
            if (segIndices[i] == segIndices[i - 1]) score += tokenIndices[i] - tokenIndices[i - 1];
            else score += (segIndices[i] - segIndices[i - 1]) * SegmentCrossingPenalty;
        }
        return score;
    }

    /// <summary>The lookup forms tried for one query word — mirrors the manual matcher:
    /// the word itself, its ה-stripped form, and its חסר/מלא skeleton.</summary>
    private static IEnumerable<string> WordForms(string word)
    {
        yield return word;
        if (CatalogTocTextRules.StripHePrefix(word) is { } stripped) yield return stripped;
        string skel = CatalogTocTextRules.Skeleton(word);
        if (skel.Length > 0 && skel != word) yield return skel;
    }

    /// <summary>Manual Pass 3: drop any hit whose TOC ancestor (same book) also matched.
    /// Regular and alt TOC entries live in different id namespaces, so the kind is part
    /// of the key — a regular entry never suppresses an alt entry or vice versa.</summary>
    private static List<CatalogTocHit> DedupAncestors(List<CatalogTocHit> hits)
    {
        var matched = new HashSet<(string Kind, int BookId, int TocEntryId)>();
        foreach (var h in hits)
            if (h.TocEntryId != 0) matched.Add((h.Kind, h.BookId, h.TocEntryId));

        var result = new List<CatalogTocHit>(hits.Count);
        foreach (var h in hits)
        {
            bool suppressed = false;
            if (h.TocEntryId != 0 && h.AncestorIds.Length > 0)
            {
                foreach (var part in h.AncestorIds.Split(','))
                    if (int.TryParse(part, out int anc) && matched.Contains((h.Kind, h.BookId, anc)))
                    {
                        suppressed = true;
                        break;
                    }
            }
            if (!suppressed) result.Add(h);
        }
        return result;
    }

    // ── Reader lifecycle ────────────────────────────────────────────────────────

    private FSDirectory? _dir;

    private IndexSearcher? GetSearcher()
    {
        lock (_readerLock)
        {
            if (_searcher is not null) return _searcher;
            var dir = FSDirectory.Open(indexPath);
            if (!DirectoryReader.IndexExists(dir))
            {
                dir.Dispose();
                return null;
            }
            _dir = dir;
            _reader = DirectoryReader.Open(dir);
            _searcher = new IndexSearcher(_reader);
            return _searcher;
        }
    }

    /// <summary>Total docs in the index (0 when not yet built).</summary>
    public int DocCount()
    {
        GetSearcher();
        lock (_readerLock) return _reader?.NumDocs ?? 0;
    }

    private void InvalidateReader()
    {
        lock (_readerLock)
        {
            _reader?.Dispose();
            _reader = null;
            _searcher = null;
            _dir?.Dispose();
            _dir = null;
        }
    }

    public void Dispose() => InvalidateReader();

    /// <summary>Trivial whitespace analyzer — index text is pre-tokenized/expanded by
    /// CatalogTocTextRules, so tokens just split on spaces. No Analysis.Common needed.</summary>
    private sealed class WhitespaceOnlyAnalyzer : Analyzer
    {
        protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
            => new(new SpaceTokenizer(reader));
    }

    private sealed class SpaceTokenizer : Tokenizer
    {
        private readonly ICharTermAttribute _termAtt;
        private string[]? _tokens;
        private int _pos;

        public SpaceTokenizer(TextReader input) : base(input)
        {
            _termAtt = AddAttribute<ICharTermAttribute>();
        }

        public override bool IncrementToken()
        {
            _tokens ??= m_input.ReadToEnd().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (_pos >= _tokens.Length) return false;
            ClearAttributes();
            _termAtt.SetEmpty().Append(_tokens[_pos++]);
            return true;
        }

        public override void Reset()
        {
            base.Reset();
            _tokens = null;
            _pos = 0;
        }
    }
}

/// <summary>One catalog TOC search hit — a book title or a full TOC path.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class CatalogTocHit
{
    /// <summary>"book" (title doc → first line), "toc" (TOC entry doc), or "alttoc"
    /// (alternative-structure entry — its TocEntryId is an alt_toc_entry id).</summary>
    public string Kind { get; set; } = "toc";
    public int BookId { get; set; }
    /// <summary>0 for book-title hits. For "alttoc" hits this is the alt_toc_entry id
    /// (a different id namespace than tocEntry).</summary>
    public int TocEntryId { get; set; }
    /// <summary>The line the TOC path points to (book hits: the book's first line). 0 = none.</summary>
    public int LineId { get; set; }
    /// <summary>-1 when the entry has no resolved line.</summary>
    public int LineIndex { get; set; }
    public string BookTitle { get; set; } = "";
    /// <summary>TOC display path within the book ("פרק א / פסוק ד"); empty for book hits.</summary>
    public string TocPath { get; set; } = "";
    public float Score { get; set; }
    /// <summary>Comma-joined ancestor tocEntry ids (internal — used for ancestry dedup).</summary>
    [MessagePack.IgnoreMember]
    public string AncestorIds { get; set; } = "";
    /// <summary>The book's catalog tree order (internal — rank tiebreak).</summary>
    [MessagePack.IgnoreMember]
    public int TreeOrder { get; set; } = int.MaxValue;
}
