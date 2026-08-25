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
/// Lucene.NET (4.8) disk index over FULL TOC PATHS — the engine behind the catalog
/// "file-system" search.
///
/// DESIGN (deliberately simple):
///   - One document per TOC entry (regular + alt structures) and one per book title
///     (pointing at the book's first line).
///   - THREE indexed fields, all analyzed by the shared normalization pipeline
///     (SeforimDbCatalogTextNormalizer): the full TOC path (also stored — display + order-rule
///     text), the catalog (category) path, and the author names. The rest is
///     stored-only metadata.
///   - Search is contains-all: per query token, a MUST over (path OR catalog OR
///     author), plus ה-prefix and חסר/מלא skeleton variants found in the actual
///     indexed vocabulary (see VariantIndex — ported from the Vue frontend's
///     book-catalog search, always active) and, only as a last-resort fallback when
///     the exact search finds nothing, Lucene FuzzyQuery on catalog/author. No
///     scoring, no boosting, no phrase/proximity/wildcard queries, and results are
///     NEVER capped.
///   - Ordering ignores Lucene relevance entirely: Level ascending (book title = 0,
///     then TOC depth), then TreeOrder ascending (catalog tree order, then the
///     original TOC order within the book). Nothing else affects ordering.
///   - After ordering, each book's each TOC structure (per literal block) is truncated
///     to its shallowest matched level: a shallower hit is the more accurate address,
///     so the deeper ones under it are dropped (see Search).
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
///
/// ONE CLASS, TWO FILES (the SeforimDbQueries / SeforimDbConnection precedent): the
/// indexer half (this file) BUILDS and owns provenance; SeforimDbCatalogSearcher.cs is
/// the search half and the reader lifecycle. They stay one type because they share one
/// lock, one directory handle and one reader, and the build refreshes that reader on
/// every progress tick - splitting the type would put that machinery across a seam.
/// </summary>
public sealed partial class SeforimDbCatalogIndex : IDisposable
{
    // Field-style rather than a primary constructor: primary-constructor parameters are
    // not in scope in the other partial file, and both halves read these.
    private readonly string rootPath;
    private readonly string dbPath;

    /// <param name="rootPath">The index directory.</param>
    /// <param name="dbPath">The seforim database the index is built from.</param>
    public SeforimDbCatalogIndex(string rootPath, string dbPath)
    {
        this.rootPath = rootPath;
        this.dbPath = dbPath;
    }

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
    private const string FieldLevelDv = "vdv";     // numeric doc-values (sort key)
    private const string FieldTreeOrderDv = "odv"; // numeric doc-values (sort key)
    private const string FieldStructureDv = "sdv"; // numeric doc-values (truncation group)

    /// <summary>Structure id of the regular TOC (and of book-title docs) — the alt
    /// structures carry their own positive ids from the DB, so 0 never collides.</summary>
    private const int RegularTocStructureId = 0;

    /// <summary>Every indexed field, in the order the per-token OR probes them.</summary>
    private static readonly string[] IndexedFields = [FieldFullTocPath, FieldCatalog, FieldAuthor];

    /// <summary>Materialization cap: after matching (uncapped) and ordering, only this
    /// many documents have their stored fields read (the expensive step — see the
    /// --bench probe). The match COUNT stays exact and uncapped; only the returned,
    /// fully-built hit list is bounded. Generous enough that every real query's useful
    /// results fit; broad one-word queries (tens of thousands of hits) stop after the
    /// ordered top slice instead of materializing everything (~10s → tens of ms).</summary>
    private const int MaterializeCap = 1000;

    private readonly object _lock = new();
    private FSDirectory? _dir;
    private DirectoryReader? _reader;
    private IndexSearcher? _searcher;
    private IndexWriter? _writer;   // non-null only while a build is in flight
    private VariantIndex? _variants; // lazily (re)built off the current reader — see GetVariantsLocked

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
    /// composite (no pointless SHA-256 over 60 bytes of metadata).
    /// v13: Level + TreeOrder also stored as numeric doc-values so ordering happens
    /// before (and independently of) stored-field materialization, which is now capped.
    /// v14: Talmud "דף X." / "דף X:" entries restructured into a parent "דף X"
    /// (→ עמוד א line) with עמוד א/עמוד ב children one level deeper.
    /// v15: abbreviation map moved to the generated CatalogAbbreviations table (author/
    /// book/tractate acronyms from the Otzaria set), and the query side OR-expands
    /// ambiguous abbreviations (מג"א → מגן אברהם / מגיני ארץ). Index-side tokenization is
    /// unchanged in practice (real titles carry no abbreviations), but the format bump
    /// forces a clean rebuild so any stray abbreviation-shaped indexed text re-tokenizes
    /// under the new table.
    /// v16: query-time ה-prefix and חסר/מלא skeleton variant matching (ported from the
    /// Vue frontend's book-catalog search — see VariantIndex), and the abbreviation
    /// matcher now also tries a candidate window's leading word with its ה stripped
    /// (so "היד החזקה" resolves through the same key as "יד החזקה"). "משנה תורה" is no
    /// longer its own abbreviation row (dropped — was colliding with "יד החזקה" via a
    /// shared רמבם target and breaking title word-order independence); "יד החזקה" now
    /// expands to "משנה תורה" instead of "רמבם". No index-side tokenization changed,
    /// but the format bump forces a rebuild so results reflect the corrected mapping.
    /// v17: abbreviation keys are QUOTE-STRIPPED and lookups strip quote glyphs off the
    /// candidate first (SeforimDbCatalogTextNormalizer.StripQuoteGlyphs), so ט"ז / ט״ז / ט''ז and the
    /// bare טז all resolve through one entry — previously the generator enumerated quote
    /// flavours and the bare form matched nothing. The map also grew from 153 to 286 keys
    /// with hand-authored AUTHOR acronyms mapping to the full names the seforim DB actually
    /// stores (חידא → חיים דוד אזולאי, יעבץ → יעקב עמדין, תפאי → ישראל ליפשיץ) plus book-title
    /// acronyms; every added alternative was validated against the DB's author/title text.
    /// Index-side tokenization DOES change here (unlike v15/v16): real titles carry
    /// abbreviations — הגהות יעב"ץ, חידושי רידב"ז — which now normalize, so a full rebuild
    /// is required, not merely forced.
    /// v18: dropped two keys that HIJACKED ordinary title words — מת (collided with
    /// הלכות טומאת מת, 16 titles, forcing משנה תורה into any corpse-tumah query) and אדרת
    /// (the first word of the real title אדרת אליהו, so the key rewrote the token before it
    /// could match and the book became unfindable). Acronyms that appear verbatim in titles
    /// (רש"ש על בבא בתרא, הגהות יעב"ץ) also carry themselves as an extra alternative, so both
    /// the acronym and the expanded author name match.
    ///
    /// NOTE for future edits: the change stamp covers the seforim DB + this version string
    /// only — it does NOT fingerprint catalog_abbreviations.json. Editing the map therefore
    /// does NOT invalidate an existing index; bump this version or the stale index keeps
    /// serving tokens normalized under the OLD map.
    /// v19: audited every entry against the DB's raw vocabulary and fixed what the audit
    /// found, rather than tolerating entries that only worked by accident:
    ///   - Removed expansions whose target text appears in NO title/author/category, so they
    ///     could never match: ספרא → תורת כהנים (0 titles; ספרא is itself the title of six
    ///     books, so the key rewrote an existing word into a non-existent one and "worked"
    ///     only because the index side normalized identically), הגהות מיימוניות, משבצות זהב,
    ///     רעיא מהימנא. תוכ now resolves to ספרא (the title this library actually uses).
    ///   - Removed keys that are not abbreviations of their target at all: דבר → דברים רבה
    ///     (דבר is an ordinary word in 20 unrelated titles — דבר אברהם, העמק דבר, משיב דבר —
    ///     and only resolved because כתיב variant matching masked the wrong expansion), and
    ///     משנת → משנה תורה (construct form of משנה; first word of משנת דרבי אליעזר and six
    ///     other titles unrelated to the Rambam).
    ///   - Split כרתי ופלתי into (כרתי | פלתי): they are two separate books here, and the
    ///     joint form demanded "ופלתי", which exists nowhere.
    ///   - Keys that also appear verbatim in real titles (יוד, יוט, שע, מוהרן, מוהרנת) now
    ///     carry the literal form as an extra alternative, so those titles stay reachable.
    /// All 309 alternatives now resolve against the raw vocabulary; the --abbrev mode fails
    /// the build on any future entry that does not.
    /// v20: each doc records the TOC STRUCTURE it belongs to (numeric doc-values; 0 = the
    /// regular TOC, alt structures carry their DB id), which scopes the per-book level
    /// truncation in Search to one structure at a time. A new field on every document, so
    /// this bump schedules a full rebuild. Note what that means for the rebuild WINDOW:
    /// a pre-v20 index keeps serving until the new one is built (the deliberate no-
    /// downtime design — see the orchestrator's ensure-index), and its segments have
    /// no structure field, so the doc-values read falls back to 0 for every doc and the
    /// truncation collapses to the old per-book behavior meanwhile. Old results, never
    /// wrong paths — and OpenMode.CREATE means a reader is all-v19 or all-v20, never a
    /// mix.</summary>
    public const string IndexFormatVersion = "v20";

    /// <summary>Fingerprint of the seforim DB the index answers for — the shared
    /// <see cref="SeforimDbContentFingerprint"/>, prefixed with this index's format version so a
    /// schema/pipeline change also forces a rebuild. Any change to the DB's ROWS (or a
    /// format bump) changes the value. Human-readable in the ver file.
    ///
    /// Content, not file metadata: this used to use DbChangeStamp, which reports any write
    /// to the file even when no row changed. Because that stamp includes the per-file USN
    /// and file id — values that never return to a previous state — a single stray write
    /// (a `PRAGMA journal_mode` on open, another reader's WAL checkpoint, a copy or
    /// restore) made the recorded hash unmatchable and rebuilt this index on every launch
    /// thereafter.</summary>
    public static string ComputeDbHash(string dbPath)
        => SeforimDbContentFingerprint.Compute(dbPath, IndexFormatVersion);

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

        var analyzer = new SeforimDbCatalogAnalyzer();
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
                treeOrder: TreeOrder(b.Id),
                structureId: RegularTocStructureId));
            docCount++;
        }

        // Regular TOC docs — one per entry, full root→leaf path prefixed by the title.
        foreach (var group in StreamTocRowsByBook(conn))
        {
            ct.ThrowIfCancellationRequested();
            if (!bookMeta.TryGetValue(group.BookId, out var book)) continue;
            docCount += IndexTocTree(writer, group.Rows, book, TreeOrder,
                catalogPathByBook.GetValueOrDefault(group.BookId, ""), RegularTocStructureId);
            if (++bookNo % NrtRefreshEveryBooks == 0) Progress(bookNo, books.Count);
        }

        // Alt-TOC docs — alternative structures (parshiot/aliyot, dapim, …), same shape.
        foreach (var group in StreamAltTocRowsByStructure(conn))
        {
            ct.ThrowIfCancellationRequested();
            if (!altStructures.TryGetValue(group.StructureId, out var st)) continue;
            if (!bookMeta.TryGetValue(st.BookId, out var book)) continue;
            docCount += IndexTocTree(writer, group.Rows, book, TreeOrder,
                catalogPathByBook.GetValueOrDefault(st.BookId, ""), group.StructureId);
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
    private static readonly HashSet<string> TanachBookTitles = new(StringComparer.Ordinal)
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
            cmd.CommandText = SeforimDbSqlStrings.CatalogIndexBookLines;
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
                        treeOrder: treeOrder(book.Id),
                        // Generated from the regular TOC's chapters, so they truncate
                        // together with it — a chapter hit outranks its verse hits.
                        structureId: RegularTocStructureId));
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
        cmd.CommandText = SeforimDbSqlStrings.CatalogIndexTocRowsForBook;
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
        string catalogPath, string? authors, int bookId, int lineIndex, string fullTocPath, int level, long treeOrder,
        int structureId)
    {
        var doc = new Document
        {
            new TextField(FieldFullTocPath, fullTocPath, Field.Store.YES),
            new StoredField(FieldBookId, bookId),
            new StoredField(FieldLineIndex, lineIndex),
            new StoredField(FieldLevel, level),
            new StoredField(FieldTreeOrder, treeOrder),
            // Level + TreeOrder ALSO as numeric doc-values: column-stored, so the sort
            // that decides which docs to materialize can read them per-doc without
            // decompressing the (expensive) stored-fields blob. This is what lets the
            // fetch be bounded — see Search().
            new NumericDocValuesField(FieldLevelDv, level),
            new NumericDocValuesField(FieldTreeOrderDv, treeOrder),
            // Which TOC structure this entry belongs to (0 = the regular TOC). Read
            // alongside the sort keys so the per-book level truncation can scope itself
            // to one structure without materializing stored fields.
            new NumericDocValuesField(FieldStructureDv, structureId),
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
        Func<int, long> treeOrder, string catalogPath, int structureId)
    {
        rows = StripTitleRoots(rows, book.Title, book.Id);
        rows = ExpandDafAmudim(rows);
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
                treeOrder: treeOrder(book.Id),
                structureId: structureId));
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
            if (r.ParentId is null && SeforimDbCatalogTextNormalizer.IsTitleVariant(bookTitle, r.Text))
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

    /// <summary>
    /// Restructure Talmud page entries: sibling "דף X." / "דף X:" entries become a
    /// synthetic parent "דף X" (navigating to the עמוד א line — the "." member) with
    /// "עמוד א" / "עמוד ב" children one level deeper.
    ///
    /// Why: the amud punctuation used to be flattened into the daf entry's own tokens,
    /// so the injected amud letters (א/ב) collided with real daf/siman/verse letters —
    /// "שבת ב" matched every "דף X:" through its עמוד-ב token AT THE SAME LEVEL as the
    /// real דף ב. With amudim as children, the bare "דף X" parent carries no amud token
    /// and sits a level above; amud hits can only rank below the daf level. The default
    /// navigation for "דף X" is עמוד א.
    /// </summary>
    private static List<TocRow> ExpandDafAmudim(List<TocRow> rows)
    {
        List<TocRow>? result = null;
        Dictionary<(int? ParentId, string Core), TocRow>? parents = null;
        int syntheticId = int.MinValue / 2;

        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (!SeforimDbCatalogTextNormalizer.TryParseDafText(r.Text, out string core, out bool isAmudB))
            {
                result?.Add(r);
                continue;
            }

            if (result is null)
            {
                // First daf entry found — start rewriting from here.
                result = new List<TocRow>(rows.Count + 64);
                for (int j = 0; j < i; j++) result.Add(rows[j]);
                parents = [];
            }

            var key = (r.ParentId, core);
            if (!parents!.TryGetValue(key, out var parent))
            {
                parent = new TocRow
                {
                    Id = syntheticId++,
                    ParentId = r.ParentId,
                    BookId = r.BookId,
                    Text = core,
                    LineIndex = r.LineIndex, // provisional — the "." member overrides below
                };
                parents[key] = parent;
                result.Add(parent); // the parent takes its first member's position
            }
            if (!isAmudB) parent.LineIndex = r.LineIndex; // default navigation → עמוד א

            // The original entry becomes the amud child (keeps its id so any children
            // of the original entry stay attached beneath it).
            result.Add(new TocRow
            {
                Id = r.Id,
                ParentId = parent.Id,
                BookId = r.BookId,
                Text = isAmudB ? "עמוד ב" : "עמוד א",
                LineIndex = r.LineIndex,
            });
        }

        return result ?? rows;
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

    private SqliteConnection OpenDb() => SqliteConnectionFactory.OpenCorpusRead(dbPath);

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
        cmd.CommandText = SeforimDbSqlStrings.CatalogIndexCategories(hasOrderIndex);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetInt32(0), r.IsDBNull(1) ? null : r.GetInt32(1), r.IsDBNull(2) ? "" : r.GetString(2)));
        return list;
    }

    private static List<(int Id, string Title, int CategoryId, string? Authors)> LoadBooks(SqliteConnection conn)
    {
        var list = new List<(int, string, int, string?)>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SeforimDbSqlStrings.CatalogIndexBooks;
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
        cmd.CommandText = SeforimDbSqlStrings.CatalogIndexFirstLines;
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
        cmd.CommandText = SeforimDbSqlStrings.CatalogIndexAltStructures;
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
        cmd.CommandText = SeforimDbSqlStrings.CatalogIndexTocRowsAllBooks;
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
        cmd.CommandText = SeforimDbSqlStrings.CatalogIndexAltTocRowsAllStructures;
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
}
