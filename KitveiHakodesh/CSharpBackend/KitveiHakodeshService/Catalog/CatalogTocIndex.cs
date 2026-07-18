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
/// Lucene.NET (4.8) disk index over FULL TOC PATHS — the engine behind the catalog
/// "file-system" search.
///
/// DESIGN (deliberately simple):
///   - One document per TOC entry (regular + alt structures) and one per book title
///     (pointing at the book's first line).
///   - THREE indexed fields, all analyzed by the shared normalization pipeline
///     (CatalogTocTextRules): the full TOC path (also stored — display + order-rule
///     text), the catalog (category) path, and the author names. The rest is
///     stored-only metadata.
///   - Search is contains-all: per query token, a MUST over (path OR catalog OR
///     author). No scoring, no boosting, no fuzzy/phrase/proximity/wildcard queries,
///     and results are NEVER capped.
///   - Ordering ignores Lucene relevance entirely: Level ascending (book title = 0,
///     then TOC depth), then TreeOrder ascending (catalog tree order, then the
///     original TOC order within the book). Nothing else affects ordering.
///
/// SEARCH-WHILE-BUILDING (near-real-time): there is ONE in-place index directory. A
/// rebuild writes into it with the writer kept open, and a near-real-time reader is
/// opened off that live writer (DirectoryReader.Open(writer, …)) and refreshed on an
/// interval — so documents become searchable AS they are indexed, and partial results
/// appear immediately instead of waiting for the whole build to finish. When the build
/// completes the writer commits, the ver file (source-DB hash) is written, and the
/// reader is refreshed one last time. When no build is running the reader is a plain
/// DirectoryReader on the committed index. Either way the reader is reused across
/// queries and only reopened to pick up new documents.
/// </summary>
public sealed class CatalogTocIndex(string rootPath, string dbPath) : IDisposable
{
    private const LuceneVersion Ver = LuceneVersion.LUCENE_48;

    // Field names. THREE indexed fields: the full TOC path (also stored — it is the
    // display path AND the text the query-token-order rule runs against), the catalog
    // (category) path, and the author names — both matchable but order-exempt.
    private const string FieldFullTocPath = "p";  // indexed + stored
    private const string FieldCatalog = "c";      // indexed + stored
    private const string FieldAuthor = "a";       // indexed + stored
    private const string FieldBookId = "b";
    private const string FieldLineIndex = "l";
    private const string FieldLevel = "v";
    private const string FieldTreeOrder = "o";

    /// <summary>Every indexed field, in the order the per-token OR probes them.</summary>
    private static readonly string[] IndexedFields = [FieldFullTocPath, FieldCatalog, FieldAuthor];

    private readonly object _lock = new();
    private FSDirectory? _dir;
    private DirectoryReader? _reader;
    private IndexSearcher? _searcher;
    private IndexWriter? _writer;   // non-null only while a build is in flight

    public string RootPath => rootPath;

    // ── Source-DB hash + ver file ───────────────────────────────────────────────

    /// <summary>Bump when the index schema or normalization changes so existing
    /// indexes rebuild — folded into the hash, which is stored in the ver file.
    /// v9: single in-place index + NRT reader (dropped the a/b slot directories and the
    /// two-line ver file), so pre-v9 on-disk layouts are discarded and rebuilt.
    /// v10: SearchText split into three fields — TOC path (indexed+stored), catalog
    /// path (indexed+stored), author (indexed+stored); order rule scoped to the path.
    /// v11: title-variant root strip also folds ASCII apostrophes (ש''ע/ר' roots) and
    /// the stale hardcoded force-strip book-id list is gone.
    /// v12: ש"ע/ש''ע/שו''ע canonical variants; the DB stamp is a plain readable
    /// composite (no pointless SHA-256 over 60 bytes of metadata).</summary>
    public const string IndexFormatVersion = "v12";

    /// <summary>
    /// Fingerprint of the seforim DB the index answers for: format version + path +
    /// file size + last-write time, as one readable string. Cheap (one stat call, no
    /// file content read) and reliable: any change to the DB file — content written
    /// (size/mtime move), file replaced, or the user switching databases (path) —
    /// changes the stamp and triggers a full rebuild. Human-readable in the ver file.
    /// </summary>
    public static string ComputeDbHash(string dbPath)
    {
        var info = new FileInfo(dbPath);
        return $"{IndexFormatVersion}|{dbPath.ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
    }

    private string VerFile => Path.Combine(rootPath, "catalogtoc.ver");

    /// <summary>Ver file: a single line = the DB hash the committed index was built from.
    /// Written only after a build fully completes, so a hash match means the on-disk
    /// index is complete and fresh (a build interrupted mid-way leaves no/stale ver).</summary>
    private string? ReadVer()
    {
        try
        {
            if (!File.Exists(VerFile)) return null;
            var lines = File.ReadAllLines(VerFile);
            return lines.Length >= 1 && !string.IsNullOrWhiteSpace(lines[0]) ? lines[0].Trim() : null;
        }
        catch { return null; }
    }

    /// <summary>The DB hash the committed on-disk index was built from, or null when no
    /// complete index exists.</summary>
    public string? ActiveHash => ReadVer();

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

    // ── Build (in-place, searchable while building) ─────────────────────────────

    /// <summary>How often (in indexed books) to refresh the near-real-time reader so
    /// partial results appear during a build. Cheap when nothing changed.</summary>
    private const int NrtRefreshEveryBooks = 200;

    /// <summary>
    /// Rebuild the whole index from the seforim DB IN PLACE, keeping it searchable the
    /// whole time via a near-real-time reader that refreshes as documents are added.
    /// On completion the writer commits, the ver file (DB hash) is written, and the
    /// reader is refreshed one last time. Returns the number of documents written.
    /// (Name kept for API compatibility; there is no longer a slot swap.)
    /// </summary>
    public int BuildAndSwitch(string dbHash, Action<int, int>? onProgress = null, CancellationToken ct = default)
    {
        System.IO.Directory.CreateDirectory(rootPath);

        // A completed index is only claimed by the ver file; clear it up front so a
        // build interrupted midway is never mistaken for fresh on the next run.
        try { if (File.Exists(VerFile)) File.Delete(VerFile); } catch { /* best effort */ }

        // Sweep any pre-v9 slot directories (a/b) left by the old two-slot layout.
        foreach (var slot in new[] { "a", "b" })
        {
            string old = Path.Combine(rootPath, slot);
            try { if (System.IO.Directory.Exists(old)) System.IO.Directory.Delete(old, recursive: true); }
            catch { /* harmless leftovers */ }
        }

        try
        {
            int docCount = BuildInPlace(onProgress, ct);

            // Build finished: publish the hash, then refresh once more so the final
            // documents (the Tanach verse pass) are visible on the NRT reader.
            File.WriteAllText(VerFile, dbHash);
            lock (_lock) RefreshNrtLocked();
            return docCount;
        }
        finally
        {
            // Always release the writer (and its lock). Whatever it committed stays on
            // disk; the NRT reader opened off it remains valid after the writer closes.
            lock (_lock)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }
    }

    private int BuildInPlace(Action<int, int>? onProgress, CancellationToken ct)
    {
        using var conn = OpenDb();
        var categoryList = LoadCategories(conn);
        var books = LoadBooks(conn);
        var firstLines = LoadFirstLines(conn);
        var bookRank = ComputeTreeOrders(categoryList, books);
        var altStructures = LoadAltStructures(conn);

        // Full catalog (category) path per book — part of SearchText so a query can
        // address a book through its shelf: "תנך בראשית", "שלחן ערוך אורח חיים סימן ב"
        // for a book like פרי מגדים whose title carries neither שלחן nor ערוך.
        var catalogPathByBook = ComputeCatalogPaths(categoryList, books);

        // Reuse the already-open directory handle if one exists (a committed reader may
        // be serving off it); otherwise open one. A single handle per index path avoids
        // write-lock contention from two FSDirectory instances on the same folder.
        FSDirectory fsDir;
        lock (_lock) fsDir = _dir ??= FSDirectory.Open(rootPath);

        var analyzer = new PipelineAnalyzer();
        var config = new IndexWriterConfig(Ver, analyzer)
        {
            OpenMode = OpenMode.CREATE,
            RAMBufferSizeMB = 48,
        };
        var writer = new IndexWriter(fsDir, config);
        // Publish the writer, drop any committed reader, and open the first NRT reader so
        // searches hit the (empty, then filling) index immediately. OpenMode.CREATE has
        // marked the old segments for deletion; disposing the old reader releases them.
        lock (_lock)
        {
            _reader?.Dispose();
            _reader = null;
            _searcher = null;
            _writer = writer;
            RefreshNrtLocked();
        }

        // The NRT reader refreshes on the same cadence as the progress callback.
        void Progress(int done, int total)
        {
            onProgress?.Invoke(done, total);
            lock (_lock) RefreshNrtLocked();
        }

        var bookMeta = books.ToDictionary(b => b.Id, b => b);
        // Per-book doc sequence: 0 = the book-title doc, then TOC entries in original
        // order. TreeOrder = (catalog book rank << 24) | seq — one long that sorts by
        // catalog position first, original TOC order within the book second.
        var nextSeq = new Dictionary<int, int>(books.Count);
        long TreeOrder(int bookId)
        {
            int seq = nextSeq.GetValueOrDefault(bookId);
            nextSeq[bookId] = seq + 1;
            return ((long)bookRank.GetValueOrDefault(bookId, int.MaxValue) << 24) | (uint)seq;
        }

        int docCount = 0, bookNo = 0;

        // Book-title docs — Level 0, pointing at the book's first line.
        foreach (var b in books)
        {
            ct.ThrowIfCancellationRequested();
            writer.AddDocument(MakeDoc(
                catalogPath: catalogPathByBook.GetValueOrDefault(b.Id, ""),
                authors: b.Authors,
                bookId: b.Id,
                lineIndex: firstLines.TryGetValue(b.Id, out var fl) ? fl.LineIndex : -1,
                fullTocPath: b.Title,
                level: 0,
                treeOrder: TreeOrder(b.Id)));
            docCount++;
        }

        // Regular TOC docs — one per entry, full root→leaf path prefixed by the title.
        foreach (var group in StreamTocRowsByBook(conn))
        {
            ct.ThrowIfCancellationRequested();
            if (!bookMeta.TryGetValue(group.BookId, out var book)) continue;
            docCount += IndexTocTree(writer, group.Rows, book, TreeOrder,
                catalogPathByBook.GetValueOrDefault(group.BookId, ""));
            if (++bookNo % NrtRefreshEveryBooks == 0) Progress(bookNo, books.Count);
        }

        // Alt-TOC docs — alternative structures (parshiot/aliyot, dapim, …), same shape.
        foreach (var group in StreamAltTocRowsByStructure(conn))
        {
            ct.ThrowIfCancellationRequested();
            if (!altStructures.TryGetValue(group.StructureId, out var st)) continue;
            if (!bookMeta.TryGetValue(st.BookId, out var book)) continue;
            docCount += IndexTocTree(writer, group.Rows, book, TreeOrder,
                catalogPathByBook.GetValueOrDefault(st.BookId, ""));
        }

        // 1+2. The normal index is complete — commit it before the slower verse pass,
        // and refresh so every book/TOC entry is searchable before verses stream in.
        writer.Commit();
        Progress(books.Count, books.Count);

        // 3-5. Second pass, Tanach only: the seforim DB's Tanach TOCs stop at chapters,
        // so verse-level entries are generated from the LINE TEXT and added as ordinary
        // documents (same fields, analyzer, ordering). 6. Final commit here. The writer
        // is left OPEN — the caller writes the ver file, does the final NRT refresh (so
        // the verses become visible), then disposes it.
        docCount += IndexTanachVerses(writer, conn, books, TreeOrder, catalogPathByBook, ct);
        writer.Commit();

        return docCount;
    }

    // ── Tanach verse extraction (compensates for missing verse-level TOC) ─────────

    /// <summary>The Tanach base-text books, titled exactly as in the seforim DB — the
    /// traditional 24 books as the DB stores them (שמואל/מלכים/דברי הימים split in two,
    /// תרי עשר as twelve). Only these get the verse-extraction pass.</summary>
    public static readonly HashSet<string> TanachBookTitles = new(StringComparer.Ordinal)
    {
        // תורה
        "בראשית", "שמות", "ויקרא", "במדבר", "דברים",
        // נביאים
        "יהושע", "שופטים", "שמואל א", "שמואל ב", "מלכים א", "מלכים ב",
        "ישעיהו", "ירמיהו", "יחזקאל",
        "הושע", "יואל", "עמוס", "עובדיה", "יונה", "מיכה", "נחום", "חבקוק", "צפניה", "חגי", "זכריה", "מלאכי",
        // כתובים
        "תהילים", "משלי", "איוב", "שיר השירים", "רות", "איכה", "קהלת", "אסתר",
        "דניאל", "עזרא", "נחמיה", "דברי הימים א", "דברי הימים ב",
    };

    private static readonly System.Text.RegularExpressions.Regex VerseMarkerRe =
        new(@"\(([א-ת]{1,3})\)", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex HtmlTagRe =
        new("<[^>]*>", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Generate verse-level TOC documents for the Tanach books by scanning their line
    /// text. A verse begins at a marker like <c>(א)</c>; a marker only counts when its
    /// gematria value is exactly the NEXT expected verse number for the current chapter
    /// (resets at every TOC entry) — this rejects parasha markers (פ)/(ס) and any other
    /// parenthesized text. Each verse becomes a normal document: path =
    /// "&lt;chapter path&gt; / פסוק &lt;letters&gt;", level = chapter level + 1,
    /// LineIndex = the marker's line.
    /// </summary>
    private static int IndexTanachVerses(
        IndexWriter writer, SqliteConnection conn,
        List<(int Id, string Title, int CategoryId, string? Authors)> books,
        Func<int, long> treeOrder, Dictionary<int, string> catalogPathByBook, CancellationToken ct)
    {
        int added = 0;
        foreach (var book in books)
        {
            if (!TanachBookTitles.Contains(book.Title)) continue;
            ct.ThrowIfCancellationRequested();
            string catalogPath = catalogPathByBook.GetValueOrDefault(book.Id, "");

            // The book's TOC entries (chapters), root-stripped, with resolved paths.
            var rows = StripTitleRoots(LoadTocRowsForBook(conn, book.Id), book.Title, book.Id);
            var byId = rows.ToDictionary(r => r.Id);
            var chainCache = new Dictionary<int, (string Path, int Level)>();
            (string Path, int Level) GetChain(TocRow row)
            {
                if (chainCache.TryGetValue(row.Id, out var cached)) return cached;
                var result = row.ParentId is { } pid && byId.TryGetValue(pid, out var parent)
                    ? (GetChain(parent).Path + " / " + row.Text, GetChain(parent).Level + 1)
                    : (book.Title + " / " + row.Text, 1);
                chainCache[row.Id] = result;
                return result;
            }

            // Entries in line order — each owns the lines up to the next entry.
            var entries = rows.Where(r => r.LineIndex >= 0).OrderBy(r => r.LineIndex).ToList();
            if (entries.Count == 0) continue;

            int entryIdx = -1;
            int expectedVerse = 1;
            string chapterPath = "";
            int chapterLevel = 0;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT lineIndex, content FROM line WHERE bookId = @b ORDER BY lineIndex";
            cmd.Parameters.AddWithValue("@b", book.Id);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                int lineIndex = reader.IsDBNull(0) ? -1 : reader.GetInt32(0);
                string content = reader.IsDBNull(1) ? "" : reader.GetString(1);

                // Advance to the TOC entry this line belongs to (reset the verse counter).
                while (entryIdx + 1 < entries.Count && entries[entryIdx + 1].LineIndex <= lineIndex)
                {
                    entryIdx++;
                    var (path, level) = GetChain(entries[entryIdx]);
                    chapterPath = path;
                    chapterLevel = level;
                    expectedVerse = 1;
                }
                if (entryIdx < 0) continue; // front matter before the first entry

                foreach (System.Text.RegularExpressions.Match m in
                         VerseMarkerRe.Matches(HtmlTagRe.Replace(content, " ")))
                {
                    string letters = m.Groups[1].Value;
                    if (Gematria(letters) != expectedVerse) continue; // (פ)/(ס)/quotes etc.

                    string path = chapterPath + " / פסוק " + letters;
                    writer.AddDocument(MakeDoc(
                        catalogPath: catalogPath,
                        authors: book.Authors,
                        bookId: book.Id,
                        lineIndex: lineIndex,
                        fullTocPath: path,
                        level: chapterLevel + 1,
                        treeOrder: treeOrder(book.Id)));
                    added++;
                    expectedVerse++;
                }
            }
        }
        return added;
    }

    private static List<TocRow> LoadTocRowsForBook(SqliteConnection conn, int bookId)
    {
        var rows = new List<TocRow>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT te.id, te.parentId, tt.text, l.lineIndex
            FROM tocEntry te
            JOIN tocText tt ON tt.id = te.textId
            LEFT JOIN line l ON l.id = te.lineId
            WHERE te.bookId = @b
            ORDER BY te.id";
        cmd.Parameters.AddWithValue("@b", bookId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new TocRow
            {
                Id = r.GetInt32(0),
                ParentId = r.IsDBNull(1) ? null : r.GetInt32(1),
                BookId = bookId,
                Text = r.IsDBNull(2) ? "" : r.GetString(2),
                LineIndex = r.IsDBNull(3) ? -1 : r.GetInt32(3),
            });
        return rows;
    }

    /// <summary>Standard Hebrew numeral value (א=1 … ת=400, finals folded); -1 when a
    /// character is not a Hebrew letter. Sum-based, so both טו and יה style forms work.</summary>
    private static int Gematria(string letters)
    {
        int value = 0;
        foreach (char c in letters)
        {
            int v = c switch
            {
                >= 'א' and <= 'ט' => c - 'א' + 1,
                'י' => 10, 'כ' or 'ך' => 20, 'ל' => 30, 'מ' or 'ם' => 40, 'נ' or 'ן' => 50,
                'ס' => 60, 'ע' => 70, 'פ' or 'ף' => 80, 'צ' or 'ץ' => 90,
                'ק' => 100, 'ר' => 200, 'ש' => 300, 'ת' => 400,
                _ => -1,
            };
            if (v < 0) return -1;
            value += v;
        }
        return value;
    }

    /// <summary>The TOC path is one field doing double duty: indexed (searchable, and
    /// the order rule's reference text) and stored (the display path). Catalog path and
    /// author are separate fields, each indexed (matchable, order-exempt) + stored.</summary>
    private static Document MakeDoc(
        string catalogPath, string? authors, int bookId, int lineIndex, string fullTocPath, int level, long treeOrder)
    {
        var doc = new Document
        {
            new TextField(FieldFullTocPath, fullTocPath, Field.Store.YES),
            new StoredField(FieldBookId, bookId),
            new StoredField(FieldLineIndex, lineIndex),
            new StoredField(FieldLevel, level),
            new StoredField(FieldTreeOrder, treeOrder),
        };
        if (catalogPath.Length > 0)
            doc.Add(new TextField(FieldCatalog, catalogPath, Field.Store.YES));
        if (!string.IsNullOrWhiteSpace(authors))
            doc.Add(new TextField(FieldAuthor, authors, Field.Store.YES));
        return doc;
    }

    /// <summary>Index one TOC tree (regular or alt): title-variant roots are dropped
    /// from the PATHS (display only — "בראשית / בראשית / פרק א" would be noise), then
    /// each entry gets a doc with its full path, its depth as Level, and the next
    /// per-book sequence number as TreeOrder. <paramref name="catalogPath"/> is folded
    /// into SearchText only — never into the display path.</summary>
    private int IndexTocTree(
        IndexWriter writer, List<TocRow> rows,
        (int Id, string Title, int CategoryId, string? Authors) book,
        Func<int, long> treeOrder, string catalogPath)
    {
        rows = StripTitleRoots(rows, book.Title, book.Id);
        var byId = rows.ToDictionary(r => r.Id);

        // Memoized (path, level) per entry, root→leaf.
        var chainCache = new Dictionary<int, (string Path, int Level)>();
        (string Path, int Level) GetChain(TocRow row)
        {
            if (chainCache.TryGetValue(row.Id, out var cached)) return cached;
            (string Path, int Level) result;
            if (row.ParentId is { } pid && byId.TryGetValue(pid, out var parent))
            {
                var p = GetChain(parent);
                result = (p.Path + " / " + row.Text, p.Level + 1);
            }
            else
            {
                result = (book.Title + " / " + row.Text, 1);
            }
            chainCache[row.Id] = result;
            return result;
        }

        int added = 0;
        foreach (var row in rows)
        {
            var (path, level) = GetChain(row);
            writer.AddDocument(MakeDoc(
                catalogPath: catalogPath,
                authors: book.Authors,
                bookId: book.Id,
                lineIndex: row.LineIndex,
                fullTocPath: path,
                level: level,
                treeOrder: treeOrder(book.Id)));
            added++;
        }
        return added;
    }

    /// <summary>Each book's full catalog (category) path, root→leaf, space-joined —
    /// SearchText material only. Orphaned books (unknown category) get "".</summary>
    private static Dictionary<int, string> ComputeCatalogPaths(
        List<(int Id, int? ParentId, string Title)> categories,
        List<(int Id, string Title, int CategoryId, string? Authors)> books)
    {
        var catById = new Dictionary<int, (int? ParentId, string Title)>(categories.Count);
        foreach (var c in categories) catById[c.Id] = (c.ParentId, c.Title);

        var pathCache = new Dictionary<int, string>(categories.Count);
        string CatPath(int id)
        {
            if (pathCache.TryGetValue(id, out var cached)) return cached;
            if (!catById.TryGetValue(id, out var c)) return "";
            string result = c.ParentId is { } pid ? JoinNonEmpty(CatPath(pid), c.Title) : c.Title;
            pathCache[id] = result;
            return result;
        }

        var map = new Dictionary<int, string>(books.Count);
        foreach (var b in books) map[b.Id] = CatPath(b.CategoryId);
        return map;
    }

    private static string JoinNonEmpty(string a, string? b) =>
        string.IsNullOrWhiteSpace(b) ? a : a + " " + b;

    /// <summary>Remove root TOC entries whose text is a title variant of the book title,
    /// re-parenting their children (the paths would just repeat the title).</summary>
    private static List<TocRow> StripTitleRoots(List<TocRow> rows, string bookTitle, int bookId)
    {
        if (string.IsNullOrEmpty(bookTitle) || rows.Count == 0) return rows;
        var rootIds = new HashSet<int>();
        foreach (var r in rows)
            if (r.ParentId is null && CatalogTocTextRules.IsTitleVariant(bookTitle, r.Text))
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

    private sealed class TocRow
    {
        public int Id;
        public int? ParentId;
        public int BookId;
        public int LineIndex; // -1 = none
        public string Text = "";
    }

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

    private static List<(int Id, string Title, int CategoryId, string? Authors)> LoadBooks(SqliteConnection conn)
    {
        var list = new List<(int, string, int, string?)>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT b.id, b.categoryId, b.title, group_concat(a.name, ', ') AS authors
            FROM book b
            LEFT JOIN book_author ba ON ba.bookId = b.id
            LEFT JOIN author a ON a.id = ba.authorId
            GROUP BY b.id
            ORDER BY b.orderIndex";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetInt32(0), r.IsDBNull(2) ? "" : r.GetString(2), r.IsDBNull(1) ? 0 : r.GetInt32(1),
                r.IsDBNull(3) ? null : r.GetString(3)));
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

    /// <summary>
    /// Each book's position in the catalog tree — identical to the frontend's
    /// buildTree + assignFullPaths (bookCatalogTree.ts): categories nested in load
    /// order, custom (negative-id) entries sorted last per level, orphaned books under
    /// a synthetic last root, then a DFS that numbers books as encountered.
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

    /// <summary>All alt-TOC structures: structureId → owning bookId.</summary>
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

    private sealed class TocGroup
    {
        public int BookId;
        public int StructureId;
        public List<TocRow> Rows = [];
    }

    /// <summary>Stream all TOC entries ordered by book — one group per book so each
    /// book's tree is materialized (and released) in turn instead of all at once.</summary>
    private static IEnumerable<TocGroup> StreamTocRowsByBook(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT te.bookId, te.id, te.parentId, tt.text, l.lineIndex
            FROM tocEntry te
            JOIN tocText tt ON tt.id = te.textId
            LEFT JOIN line l ON l.id = te.lineId
            ORDER BY te.bookId, te.id";
        using var r = cmd.ExecuteReader();

        TocGroup? group = null;
        while (r.Read())
        {
            int bookId = r.GetInt32(0);
            if (group is null || group.BookId != bookId)
            {
                if (group is not null) yield return group;
                group = new TocGroup { BookId = bookId };
            }
            group.Rows.Add(new TocRow
            {
                Id = r.GetInt32(1),
                ParentId = r.IsDBNull(2) ? null : r.GetInt32(2),
                BookId = bookId,
                Text = r.IsDBNull(3) ? "" : r.GetString(3),
                LineIndex = r.IsDBNull(4) ? -1 : r.GetInt32(4),
            });
        }
        if (group is not null) yield return group;
    }

    /// <summary>Stream all alt-TOC entries ordered by structure — one group per structure.</summary>
    private static IEnumerable<TocGroup> StreamAltTocRowsByStructure(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ae.structureId, ae.id, ae.parentId, tt.text, l.lineIndex
            FROM alt_toc_entry ae
            JOIN tocText tt ON tt.id = ae.textId
            LEFT JOIN line l ON l.id = ae.lineId
            ORDER BY ae.structureId, ae.id";
        using var r = cmd.ExecuteReader();

        TocGroup? group = null;
        while (r.Read())
        {
            int structureId = r.GetInt32(0);
            if (group is null || group.StructureId != structureId)
            {
                if (group is not null) yield return group;
                group = new TocGroup { StructureId = structureId };
            }
            group.Rows.Add(new TocRow
            {
                Id = r.GetInt32(1),
                ParentId = r.IsDBNull(2) ? null : r.GetInt32(2),
                Text = r.IsDBNull(3) ? "" : r.GetString(3),
                LineIndex = r.IsDBNull(4) ? -1 : r.GetInt32(4),
            });
        }
        if (group is not null) yield return group;
    }

    // ── Search ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Contains-all search: every query token (same normalization pipeline as indexing)
    /// must appear in ONE of the document's indexed fields — TOC path, catalog path, or
    /// author. Results are NEVER capped. Lucene relevance is ignored — ordering is
    /// Level ascending (book title = 0, then TOC depth), then TreeOrder ascending
    /// (catalog book position, then the original TOC order). Nothing else affects
    /// ordering.
    ///
    /// Query token order (final tie-breaker, never a relevance score): the test runs
    /// against the TOC-PATH FIELD ONLY — query tokens present among the path's tokens
    /// must appear there in typed order; tokens that aren't in the path (catalog terms,
    /// authors) don't participate by construction. When a (level, book) group contains
    /// BOTH an in-order and an out-of-order hit, the out-of-order hits are DISCARDED
    /// ("תנך בראשית ד יד" keeps פרק ד / פסוק יד and drops פרק יד / פסוק ד). Groups with
    /// no in-order hit are kept untouched, so title/catalog word order (משנה תורה vs
    /// תורה משנה) never filters anything.
    /// </summary>
    public List<CatalogTocHit> Search(string query, CancellationToken ct = default)
    {
        var tokens = CatalogTocTextRules.Tokenize(query);
        if (tokens.Count == 0) return [];

        IndexSearcher? searcher;
        lock (_lock) searcher = _searcher;
        if (searcher is null)
        {
            if (!TryOpenActive()) return [];
            lock (_lock) searcher = _searcher;
            if (searcher is null) return [];
        }

        var bq = new BooleanQuery();
        foreach (var token in tokens.Distinct())
        {
            var perToken = new BooleanQuery();
            foreach (var field in IndexedFields)
                perToken.Add(new TermQuery(new Term(field, token)), Occur.SHOULD);
            bq.Add(perToken, Occur.MUST);
        }

        var collector = new AllDocsCollector(ct);
        searcher.Search(bq, collector);
        if (collector.DocIds.Count == 0) return [];

        // Stored fields are read in docId order (collector order) — compressed
        // stored-field chunks decompress once per neighborhood, not once per hit.
        var hits = new List<CatalogTocHit>(collector.DocIds.Count);
        foreach (int docId in collector.DocIds)
        {
            ct.ThrowIfCancellationRequested();
            var doc = searcher.Doc(docId);
            hits.Add(new CatalogTocHit
            {
                BookId = doc.GetField(FieldBookId)?.GetInt32Value() ?? 0,
                LineIndex = doc.GetField(FieldLineIndex)?.GetInt32Value() ?? -1,
                FullTocPath = doc.Get(FieldFullTocPath) ?? "",
                Level = doc.GetField(FieldLevel)?.GetInt32Value() ?? 0,
                TreeOrder = doc.GetField(FieldTreeOrder)?.GetInt64Value() ?? long.MaxValue,
            });
        }

        // Query-token-order tiebreak: only meaningful for multi-token queries (a single
        // token is trivially in order). Within each (level, book) group that has at
        // least one in-order hit, the out-of-order hits are discarded.
        if (tokens.Count >= 2)
        {
            foreach (var h in hits)
                h.QueryInOrder = ContainsInQueryOrder(h.FullTocPath, tokens);

            var groupsWithInOrder = new HashSet<(int Level, long Book)>();
            foreach (var h in hits)
                if (h.QueryInOrder) groupsWithInOrder.Add((h.Level, h.TreeOrder >> 24));

            if (groupsWithInOrder.Count > 0)
                hits.RemoveAll(h => !h.QueryInOrder && groupsWithInOrder.Contains((h.Level, h.TreeOrder >> 24)));
        }

        hits.Sort(static (a, b) =>
        {
            int c = a.Level.CompareTo(b.Level);
            return c != 0 ? c : a.TreeOrder.CompareTo(b.TreeOrder);
        });
        return hits;
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
        var pathTokens = CatalogTocTextRules.Tokenize(fullTocPath);
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

    /// <summary>Collects every matching docId, in order, no cap, no scores.</summary>
    private sealed class AllDocsCollector(CancellationToken ct) : ICollector
    {
        public readonly List<int> DocIds = [];
        private int _docBase;

        public void SetScorer(Scorer scorer) { /* scores are ignored by design */ }
        public void SetNextReader(AtomicReaderContext context) => _docBase = context.DocBase;
        public bool AcceptsDocsOutOfOrder => true;

        public void Collect(int doc)
        {
            if ((DocIds.Count & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
            DocIds.Add(_docBase + doc);
        }
    }

    // ── Analyzer ────────────────────────────────────────────────────────────────

    /// <summary>The shared normalization pipeline as a Lucene analyzer — indexing runs
    /// text through CatalogTocTextRules.Tokenize, exactly like query parsing does.</summary>
    private sealed class PipelineAnalyzer : Analyzer
    {
        protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
            => new(new PipelineTokenizer(reader));
    }

    private sealed class PipelineTokenizer : Tokenizer
    {
        private readonly ICharTermAttribute _termAtt;
        private List<string>? _tokens;
        private int _pos;

        public PipelineTokenizer(TextReader input) : base(input)
        {
            _termAtt = AddAttribute<ICharTermAttribute>();
        }

        public override bool IncrementToken()
        {
            _tokens ??= CatalogTocTextRules.Tokenize(m_input.ReadToEnd());
            if (_pos >= _tokens.Count) return false;
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

/// <summary>One catalog TOC search hit. Level 0 = a book-title hit (LineIndex is the
/// book's first line); Level ≥ 1 = a TOC entry at that depth.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class CatalogTocHit
{
    public int BookId { get; set; }
    /// <summary>-1 when the entry has no resolved line.</summary>
    public int LineIndex { get; set; }
    /// <summary>Display path: the book title, then " / "-joined TOC segments.</summary>
    public string FullTocPath { get; set; } = "";
    /// <summary>0 = book title, 1+ = TOC depth. First sort key.</summary>
    public int Level { get; set; }
    /// <summary>Catalog tree position + original TOC order. Second sort key.</summary>
    public long TreeOrder { get; set; }
    /// <summary>Internal (not on the wire): query tokens appear in typed order in the
    /// path — the last tiebreak within a (book, level) group.</summary>
    [MessagePack.IgnoreMember]
    public bool QueryInOrder { get; set; }
}
