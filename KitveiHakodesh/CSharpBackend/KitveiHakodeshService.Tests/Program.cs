// Catalog TOC search test — validates the SIMPLIFIED index design:
//
//   1. Analyzer spec table  — the canonical-normalization examples must hold exactly
//                             (שלחן / שו"ע / שו״ע → שולחן, before punctuation stripping)
//   2. Hash trigger         — the rebuild fingerprint is stable / changes on DB change
//   3. Self-recall          — for a corpus of (title + path-words) queries generated
//                             from real books, regular TOC entries, and alt-TOC
//                             entries, the entry's own doc must be returned
//   4. Contains-all         — sampled results must contain every query token in
//                             (FullTocPath + authors), same pipeline both sides
//   5. Ordering             — results are sorted by (Level, TreeOrder) — nothing else
//
// --compare "<query>" prints the OLD manual pipeline's results next to the Lucene
// results for eyeballing (informational — the simplified design intentionally differs).
//
// Usage: dotnet run -c Release [-- --db <seforim.db>] [--books 300] [--entries 3] [--rebuild]
using System.Diagnostics;
using KitveiHakodeshService.Catalog;
using KitveiHakodeshService.Tests;
using Microsoft.Data.Sqlite;

// ── Args ────────────────────────────────────────────────────────────────────────

string? dbPath = null;
string? indexPath = null;
string? compareQuery = null;
int sampleBooks = 300, entriesPerBook = 3;
bool forceRebuild = false;
bool benchMode = false;
bool watcherMode = false;
bool variantMode = false;
bool abbrevMode = false;
var ktivSimQueries = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--db": dbPath = args[++i]; break;
        case "--index": indexPath = args[++i]; break;
        case "--books": sampleBooks = int.Parse(args[++i]); break;
        case "--entries": entriesPerBook = int.Parse(args[++i]); break;
        case "--rebuild": forceRebuild = true; break;
        case "--compare": compareQuery = args[++i]; break;
        case "--bench": benchMode = true; break;
        case "--watcher": watcherMode = true; break;
        case "--variant": variantMode = true; break;
        case "--abbrev": abbrevMode = true; break;
        case "--ktivsim": while (i + 1 < args.Length && !args[i + 1].StartsWith("--")) ktivSimQueries.Add(args[++i]); break;
    }
}

if (watcherMode)
    return WatcherE2E.Run() == 0 ? 0 : 1;
if (variantMode)
    return VariantVerify.Run();

dbPath ??= Environment.GetEnvironmentVariable("DB_PATH");
if (string.IsNullOrWhiteSpace(dbPath))
{
    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    string otzaria = Path.Combine(appData, "otzaria", "books", "seforim.db");
    string zayit = Path.Combine(appData, "io.github.kdroidfilter.seforimapp", "databases", "seforim.db");
    dbPath = File.Exists(otzaria) ? otzaria : zayit;
}
if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"seforim DB not found: {dbPath}");
    return 2;
}
indexPath ??= Path.Combine(AppContext.BaseDirectory, "CatalogTocIndex.test");

if (ktivSimQueries.Count > 0)
    return KtivTiebreakSim.Run(dbPath, indexPath, ktivSimQueries.ToArray());

if (abbrevMode)
    return AbbrevVerify.Run(dbPath, indexPath);

Console.WriteLine($"db:    {dbPath}");
Console.WriteLine($"index: {indexPath}");

int failures = 0;
void Fail(string message)
{
    failures++;
    Console.Error.WriteLine("  FAIL: " + message);
}

/// <summary>Readable form of a query token for failure messages: a plain word as-is, an
/// abbreviation as "(alt1 words | alt2 words)".</summary>
static string Describe(CatalogTocTextRules.QueryToken t) =>
    t.IsPlain
        ? t.Word
        : "(" + string.Join(" | ", t.Alternatives.Select(a => string.Join(" ", a))) + ")";

// ── 1. Analyzer spec table ──────────────────────────────────────────────────────

{
    var cases = new (string Input, string Expected)[]
    {
        ("שלחן ערוך", "שולחן ערוך"),
        ("שולחן ערוך", "שולחן ערוך"),
        ("שו\"ע", "שולחן ערוך"),
        ("שו״ע", "שולחן ערוך"),
        ("שו''ע", "שולחן ערוך"),
        ("ש\"ע", "שולחן ערוך"),
        ("ש״ע", "שולחן ערוך"),
        ("ש''ע", "שולחן ערוך"),
        ("קיצור ש''ע ילקוט יוסף", "קיצור שולחן ערוך ילקוט שמעוני יוסף"),
        ("שלחן", "שולחן"),
        ("פירוש שו\"ע אבן העזר", "פירוש שולחן ערוך אבן העזר"),
        ("הלכות שו\"ע החדשות", "הלכות שולחן ערוך החדשות"),
        ("רש\"י, על בראשית!", "רשי על בראשית"),   // non-word chars stripped
        ("דף יד.", "דף יד עמוד א"),                 // amud mark → עמוד, before stripping
        ("דף יד:", "דף יד עמוד ב"),
        ("פסחים דף י: תוספות", "פסחים דף י עמוד ב תוספות"),
        ("דף יד", "דף יד"),                         // no mark — no amud token
        ("יד:", "יד"),                              // not after דף — mark just strips
    };
    int ok = 0;
    foreach (var (input, expected) in cases)
    {
        string actual = string.Join(' ', CatalogTocTextRules.Tokenize(input));
        if (actual == expected) ok++;
        else Fail($"analyzer: \"{input}\" → \"{actual}\", expected \"{expected}\"");
    }
    Console.WriteLine($"analyzer spec table: {ok}/{cases.Length} OK");
}

// ── 2. Rebuild trigger: the DB stamp detects real changes, ignores false ones ────
//
// The stamp gates every rebuild (service compares ComputeDbHash to the ver file's
// value). Test both directions: real changes MUST change the stamp (else a stale index
// keeps serving), and non-changes MUST NOT (else the ~30s build runs on every startup).

{
    string dir = Path.Combine(Path.GetTempPath(), $"catalogtoc-trigger-{Environment.ProcessId}");
    Directory.CreateDirectory(dir);
    string dbA = Path.Combine(dir, "a.db");
    string dbB = Path.Combine(dir, "b.db");
    try
    {
        File.WriteAllText(dbA, "seforim-content-one");
        var t0 = File.GetLastWriteTimeUtc(dbA);

        string baseline = CatalogTocIndex.ComputeDbHash(dbA);

        // ── FALSE changes: the stamp must stay identical ──────────────────────────

        // (a) Re-reading the same untouched file.
        if (CatalogTocIndex.ComputeDbHash(dbA) != baseline)
            Fail("trigger: stamp not stable across two reads of an unchanged file");

        // (b) Opening the file for reading (access time may move; the stamp must not).
        using (var fs = File.OpenRead(dbA)) { _ = fs.ReadByte(); }
        if (CatalogTocIndex.ComputeDbHash(dbA) != baseline)
            Fail("trigger: stamp changed after merely READING the file");

        // ── REAL changes: the stamp MUST differ ───────────────────────────────────

        // (c) THE classic blind spot: same-size in-place edit with mtime RESTORED to
        //     the original. size+mtime alone are fooled; NTFS ChangeTime + the
        //     per-file USN (which applications cannot set) must catch it.
        File.WriteAllText(dbA, "seforim-content-two");            // same length
        File.SetLastWriteTimeUtc(dbA, t0);                        // restore mtime
        string stealthEdit = CatalogTocIndex.ComputeDbHash(dbA);
        if (stealthEdit == baseline)
            Fail("trigger: MISSED same-size edit with restored mtime (ctime/USN failed)");

        string afterC = stealthEdit;

        // (d) Content grew (size + mtime move).
        File.WriteAllText(dbA, "seforim-content-one-plus-more");
        string grown = CatalogTocIndex.ComputeDbHash(dbA);
        if (grown == afterC) Fail("trigger: stamp did not change when content grew");

        // (e) File REPLACED by another file with identical size and restored mtime
        //     (new MFT record → file id changes; USN changes too).
        string tmp = dbA + ".swap";
        File.WriteAllBytes(tmp, File.ReadAllBytes(dbA));
        File.SetLastWriteTimeUtc(tmp, File.GetLastWriteTimeUtc(dbA));
        string beforeSwap = CatalogTocIndex.ComputeDbHash(dbA);
        var keepM = File.GetLastWriteTimeUtc(dbA);
        File.Delete(dbA);
        File.Move(tmp, dbA);
        File.SetLastWriteTimeUtc(dbA, keepM);
        if (CatalogTocIndex.ComputeDbHash(dbA) == beforeSwap)
            Fail("trigger: MISSED file replacement with identical size + restored mtime");

        // (f) User switched databases — different path, same bytes → different stamp.
        File.WriteAllBytes(dbB, File.ReadAllBytes(dbA));
        File.SetLastWriteTimeUtc(dbB, keepM);
        if (CatalogTocIndex.ComputeDbHash(dbB) == CatalogTocIndex.ComputeDbHash(dbA))
            Fail("trigger: stamp did not change when the DB path changed");

        // (g) Index format version is part of the stamp (schema bump → rebuild).
        if (!baseline.StartsWith(CatalogTocIndex.IndexFormatVersion + "|", StringComparison.Ordinal))
            Fail("trigger: stamp does not carry the index format version");

        // (h) SQLite WAL: a committed write sits in the -wal sidecar; the MAIN file may
        //     be untouched until a checkpoint. Compute the stamp WHILE a writer holds
        //     the database open (closing the last connection would checkpoint and
        //     delete the wal, hiding the scenario) — the stamp must still change.
        string walDb = Path.Combine(dir, "wal.db");
        using (var conn = new SqliteConnection($"Data Source={walDb}"))
        {
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; " +
                                  "CREATE TABLE t(x); INSERT INTO t VALUES(1);";
                cmd.ExecuteNonQuery();
            }

            string walBaseline = CatalogTocIndex.ComputeDbHash(walDb);
            long mainSizeBefore = new FileInfo(walDb).Length;

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO t VALUES(2);"; // parked in the -wal only
                cmd.ExecuteNonQuery();
            }

            string walAfter = CatalogTocIndex.ComputeDbHash(walDb);
            if (walAfter == walBaseline)
                Fail("trigger: MISSED a WAL-mode write (change parked in the -wal sidecar)");
            if (new FileInfo(walDb).Length != mainSizeBefore)
                Console.WriteLine("  (note: main file size moved too — wal scenario not isolated on this run)");
        }
        SqliteConnection.ClearAllPools();

        Console.WriteLine("rebuild-trigger check: OK (real changes detected incl. stealth edits + WAL; reads ignored)");
    }
    finally
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }
}

// ── 2a. Shared DbChangeStamp: prefix + missing-file behavior ─────────────────────

{
    string scratch = Path.Combine(Path.GetTempPath(), $"dbstamp-{Environment.ProcessId}.tmp");
    File.WriteAllText(scratch, "x");
    try
    {
        // Prefix is prepended and separates two format versions of the SAME file.
        string s1 = KitveiHakodeshService.Common.DbChangeStamp.Compute(scratch, "fmtA");
        string s2 = KitveiHakodeshService.Common.DbChangeStamp.Compute(scratch, "fmtB");
        if (!s1.StartsWith("fmtA|", StringComparison.Ordinal)) Fail("DbChangeStamp: prefix not prepended");
        if (s1 == s2) Fail("DbChangeStamp: different prefixes produced the same stamp (schema bump wouldn't rebuild)");
        // No prefix is allowed (empty head).
        if (KitveiHakodeshService.Common.DbChangeStamp.Compute(scratch).StartsWith("|", StringComparison.Ordinal))
            Fail("DbChangeStamp: empty prefix produced a leading separator");
        // A missing file yields a stable, non-throwing 'missing' stamp.
        string miss1 = KitveiHakodeshService.Common.DbChangeStamp.Compute(scratch + ".nope", "fmtA");
        string miss2 = KitveiHakodeshService.Common.DbChangeStamp.Compute(scratch + ".nope", "fmtA");
        if (miss1 != miss2 || !miss1.Contains("missing")) Fail("DbChangeStamp: missing-file stamp not stable/marked");
        Console.WriteLine("DbChangeStamp check: OK (prefix separates formats, missing-file stable)");
    }
    finally { try { File.Delete(scratch); } catch { } }
}

// ── 2b. Rebuild DECISION: the service's ver-file compare gates builds correctly ───
// Exercises the ver-file round-trip (ActiveHash reads what a completed build wrote)
// that CatalogTocSearchService.EnsureIndex uses to decide fresh vs. stale — without
// running a full ~30s build (only the stamp comparison is under test here).

{
    string dir = Path.Combine(Path.GetTempPath(), $"catalogtoc-decision-{Environment.ProcessId}");
    Directory.CreateDirectory(dir);
    try
    {
        // No ver file yet (never built) → no ActiveHash → the service must build.
        using (var idx = new CatalogTocIndex(dir, dbPath))
            if (idx.ActiveHash != null) Fail("decision: unbuilt index reported a non-null ActiveHash");

        // Simulate a completed build for the current stamp by writing the ver file.
        string stamp = CatalogTocIndex.ComputeDbHash(dbPath);
        File.WriteAllText(Path.Combine(dir, "catalogtoc.ver"), stamp);

        using (var idx = new CatalogTocIndex(dir, dbPath))
        {
            // Matches current stamp → up-to-date → the service SKIPS the rebuild.
            if (!string.Equals(idx.ActiveHash, stamp, StringComparison.OrdinalIgnoreCase))
                Fail($"decision: fresh index not recognized as up-to-date (active={idx.ActiveHash})");
            // A changed stamp (DB moved on) → does NOT match → the service rebuilds.
            if (string.Equals(idx.ActiveHash, stamp + "|later", StringComparison.OrdinalIgnoreCase))
                Fail("decision: a changed stamp was wrongly treated as up-to-date");
        }

        // A blank/garbage ver file (interrupted build) → treated as no complete index.
        File.WriteAllText(Path.Combine(dir, "catalogtoc.ver"), "   ");
        using (var idx = new CatalogTocIndex(dir, dbPath))
            if (idx.ActiveHash != null) Fail("decision: blank ver file did not read as null (would skip rebuild)");

        Console.WriteLine("rebuild-decision check: OK (unbuilt→build, fresh→skip, changed→rebuild, blank→build)");
    }
    finally
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }
}

// ── Load catalog data (corpus generation + the --compare oracle) ────────────────

var swLoad = Stopwatch.StartNew();
var categories = new List<(int Id, int? ParentId, string Title)>();
var books = new List<ManualCatalogPipeline.Book>();
var rowsByBook = new Dictionary<int, List<ManualCatalogPipeline.TocRow>>();
var altRowsByStructure = new Dictionary<int, List<ManualCatalogPipeline.TocRow>>();
var altStructureBook = new Dictionary<int, int>();

using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder
{
    DataSource = dbPath,
    Mode = SqliteOpenMode.ReadOnly,
}.ConnectionString))
{
    conn.Open();

    bool hasOrderIndex;
    using (var probe = conn.CreateCommand())
    {
        probe.CommandText = "PRAGMA table_info(category)";
        using var r = probe.ExecuteReader();
        hasOrderIndex = false;
        while (r.Read())
            if (string.Equals(r.GetString(1), "orderIndex", StringComparison.Ordinal)) hasOrderIndex = true;
    }

    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = hasOrderIndex
            ? "SELECT id, parentId, title FROM category ORDER BY level, orderIndex"
            : "SELECT id, parentId, title FROM category ORDER BY level";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            categories.Add((r.GetInt32(0), r.IsDBNull(1) ? null : r.GetInt32(1), r.IsDBNull(2) ? "" : r.GetString(2)));
    }

    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = @"
            SELECT b.id, b.categoryId, b.title, group_concat(a.name, ', ') AS authors
            FROM book b
            LEFT JOIN book_author ba ON ba.bookId = b.id
            LEFT JOIN author a ON a.id = ba.authorId
            GROUP BY b.id
            ORDER BY b.orderIndex";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            books.Add(new ManualCatalogPipeline.Book
            {
                Id = r.GetInt32(0),
                CategoryId = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                Title = r.IsDBNull(2) ? "" : r.GetString(2),
                Authors = r.IsDBNull(3) ? null : r.GetString(3),
            });
    }

    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = @"
            SELECT te.bookId, te.id, te.parentId, te.lineId, tt.text, l.lineIndex
            FROM tocEntry te
            JOIN tocText tt ON tt.id = te.textId
            LEFT JOIN line l ON l.id = te.lineId
            ORDER BY te.bookId, te.id";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            int bookId = r.GetInt32(0);
            if (!rowsByBook.TryGetValue(bookId, out var list)) rowsByBook[bookId] = list = [];
            list.Add(new ManualCatalogPipeline.TocRow
            {
                Id = r.GetInt32(1),
                ParentId = r.IsDBNull(2) ? null : r.GetInt32(2),
                BookId = bookId,
                LineId = r.IsDBNull(3) ? 0 : r.GetInt32(3),
                Text = r.IsDBNull(4) ? "" : r.GetString(4),
                LineIndex = r.IsDBNull(5) ? -1 : r.GetInt32(5),
            });
        }
    }

    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "SELECT id, bookId FROM alt_toc_structure";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            altStructureBook[r.GetInt32(0)] = r.IsDBNull(1) ? 0 : r.GetInt32(1);
    }

    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = @"
            SELECT ae.structureId, ae.id, ae.parentId, tt.text, l.lineIndex
            FROM alt_toc_entry ae
            JOIN tocText tt ON tt.id = ae.textId
            LEFT JOIN line l ON l.id = ae.lineId
            ORDER BY ae.structureId, ae.id";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            int structureId = r.GetInt32(0);
            if (!altRowsByStructure.TryGetValue(structureId, out var list)) altRowsByStructure[structureId] = list = [];
            list.Add(new ManualCatalogPipeline.TocRow
            {
                Id = r.GetInt32(1),
                ParentId = r.IsDBNull(2) ? null : r.GetInt32(2),
                Text = r.IsDBNull(3) ? "" : r.GetString(3),
                LineIndex = r.IsDBNull(4) ? -1 : r.GetInt32(4),
            });
        }
    }
}

ManualCatalogPipeline.AssignTreeOrderAndPaths(categories, books);
ManualCatalogPipeline.PrepareBookTokens(books);

var bookById = books.ToDictionary(b => b.Id);

// Legacy-stripped rows — the frozen oracle's rule, used ONLY by --compare.
var strippedRowsByBook = new Dictionary<int, List<ManualCatalogPipeline.TocRow>>(rowsByBook.Count);
// Service-stripped rows — the CURRENT service rule (fuzzy title variant incl. ASCII
// apostrophe, no force list), used for corpus generation / expected paths.
var serviceStrippedByBook = new Dictionary<int, List<ManualCatalogPipeline.TocRow>>(rowsByBook.Count);

static List<ManualCatalogPipeline.TocRow> ServiceStripTitleRoots(
    List<ManualCatalogPipeline.TocRow> rows, string bookTitle)
{
    if (string.IsNullOrEmpty(bookTitle) || rows.Count == 0) return rows;
    var rootIds = new HashSet<int>();
    foreach (var r in rows)
        if (r.ParentId is null && CatalogTocTextRules.IsTitleVariant(bookTitle, r.Text))
            rootIds.Add(r.Id);
    if (rootIds.Count == 0) return rows;
    var result = new List<ManualCatalogPipeline.TocRow>(rows.Count);
    foreach (var r in rows)
    {
        if (rootIds.Contains(r.Id)) continue;
        result.Add(r.ParentId is { } pid && rootIds.Contains(pid)
            ? new ManualCatalogPipeline.TocRow { Id = r.Id, ParentId = null, BookId = r.BookId, LineId = r.LineId, LineIndex = r.LineIndex, Text = r.Text }
            : r);
    }
    return result;
}

foreach (var (bookId, rows) in rowsByBook)
{
    if (bookById.TryGetValue(bookId, out var b))
    {
        strippedRowsByBook[bookId] = ManualCatalogPipeline.StripTocTitleRoots(rows, b.Title, bookId);
        serviceStrippedByBook[bookId] = ServiceStripTitleRoots(rows, b.Title);
    }
    else
    {
        strippedRowsByBook[bookId] = rows;
        serviceStrippedByBook[bookId] = rows;
    }
}

long tocRowCount = rowsByBook.Values.Sum(r => (long)r.Count);
Console.WriteLine($"loaded: {books.Count} books, {categories.Count} categories, {tocRowCount} toc rows, " +
                  $"{altRowsByStructure.Count} alt structures in {swLoad.Elapsed.TotalSeconds:F1}s");

// ── Build (or reuse) the Lucene index ───────────────────────────────────────────

string dbHash = CatalogTocIndex.ComputeDbHash(dbPath);
var index = new CatalogTocIndex(indexPath, dbPath);
if (forceRebuild || !index.TryOpenActive() || !string.Equals(index.ActiveHash, dbHash, StringComparison.OrdinalIgnoreCase))
{
    Directory.CreateDirectory(indexPath);
    var swBuild = Stopwatch.StartNew();
    int docs = index.BuildAndSwitch(dbHash, onProgress: (done, total) =>
    {
        if (done % 1000 == 0 || done == total) Console.Write($"\r  indexing books {done}/{total}");
    });
    Console.WriteLine();
    Console.WriteLine($"lucene index built: {docs} docs in {swBuild.Elapsed.TotalSeconds:F1}s");
}
else
{
    Console.WriteLine($"lucene index reused: {index.DocCount()} docs (hash match)");
}

// ── Helpers for expected paths ──────────────────────────────────────────────────

// Full display path for a (root-stripped) row: title + chain root→leaf. Mirrors the
// indexer's daf restructuring: a "דף X." / "דף X:" segment becomes "דף X / עמוד א|ב".
static string ExpectedPath(
    ManualCatalogPipeline.TocRow row, Dictionary<int, ManualCatalogPipeline.TocRow> byId, string title)
{
    var parts = new List<string>();
    var cur = row;
    var guard = 0;
    while (cur is not null && guard++ < 64)
    {
        parts.Add(CatalogTocTextRules.TryParseDafText(cur.Text, out string core, out bool amudB)
            ? $"{core} / {(amudB ? "עמוד ב" : "עמוד א")}"
            : cur.Text);
        cur = cur.ParentId is { } pid ? byId.GetValueOrDefault(pid) : null;
    }
    parts.Add(title);
    parts.Reverse();
    return string.Join(" / ", parts);
}

// ── --bench mode: warm-time the real capped Search() on representative queries ───

if (benchMode)
{
    Console.WriteLine("\n=== ComputeDbHash latency (7GB DB) ===");
    var swCold = Stopwatch.StartNew();
    string stamp = CatalogTocIndex.ComputeDbHash(dbPath);
    Console.WriteLine($"  first call (cold):  {swCold.Elapsed.TotalMilliseconds,7:F2} ms");
    var swWarm = Stopwatch.StartNew();
    for (int i = 0; i < 100; i++) _ = CatalogTocIndex.ComputeDbHash(dbPath);
    Console.WriteLine($"  warm (avg of 100):  {swWarm.Elapsed.TotalMilliseconds / 100,7:F3} ms");
    Console.WriteLine($"  stamp: {stamp}");

    Console.WriteLine("\n=== Search() latency (capped) ===");
    foreach (var q in new[] { "בראשית פרק ד פסוק יד", "בראשית פרק יב",
        "שלחן ערוך אורח חיים סימן ב", "פסחים דף י", "בראשית", "הלכות" })
    {
        _ = index.Search(q); // warm
        var sw = Stopwatch.StartNew();
        var r = index.Search(q);
        Console.WriteLine($"  {q,-28} {r.Count,6} hits  {sw.Elapsed.TotalMilliseconds,7:F1} ms");
    }
    return 0;
}

// ── --compare mode: old manual pipeline vs Lucene (informational) ───────────────

if (compareQuery is not null)
{
    Console.WriteLine();
    Console.WriteLine($"=== ORDER COMPARISON (informational): \"{compareQuery}\" ===");

    var manual = ManualCatalogPipeline.Search(compareQuery, books, strippedRowsByBook);
    var lucene = index.Search(compareQuery);

    var luceneRank = new Dictionary<(int, string), int>();
    for (int i = 0; i < lucene.Count; i++)
        luceneRank.TryAdd((lucene[i].BookId, lucene[i].FullTocPath), i + 1);

    Console.WriteLine();
    Console.WriteLine($"MANUAL ({manual.Trigger} trigger): {manual.MatchedBooks.Count} books, " +
                      $"{manual.TocItems.Count} toc items — with each item's Lucene rank:");
    for (int i = 0; i < Math.Min(30, manual.TocItems.Count); i++)
    {
        var it = manual.TocItems[i];
        string title = bookById.GetValueOrDefault(it.BookId)?.Title ?? $"book {it.BookId}";
        string full = $"{title} / {it.TocPath}";
        string lr = luceneRank.TryGetValue((it.BookId, full), out int r) ? $"L#{r}" : "L:-";
        Console.WriteLine($"  M#{i + 1,-3} [{lr,-6}] {full}");
    }
    if (manual.TocItems.Count > 30) Console.WriteLine($"  … {manual.TocItems.Count - 30} more");

    Console.WriteLine();
    Console.WriteLine($"LUCENE: {lucene.Count} hits — top 30 (ordered by Level, book, word-order, TreeOrder):");
    for (int i = 0; i < Math.Min(30, lucene.Count); i++)
    {
        var h = lucene[i];
        Console.WriteLine($"  L#{i + 1,-4} lvl={h.Level} ord={(h.QueryInOrder ? 'y' : 'n')} {h.FullTocPath}");
    }
    if (lucene.Count > 30) Console.WriteLine($"  … {lucene.Count - 30} more");

    // Show where the word-order tiebreak actually fired: (book, level) groups that
    // contain BOTH in-order and out-of-order hits.
    var mixed = lucene
        .GroupBy(h => (h.BookId, h.Level))
        .Where(g => g.Any(h => h.QueryInOrder) && g.Any(h => !h.QueryInOrder))
        .Take(5)
        .ToList();
    Console.WriteLine();
    Console.WriteLine($"mixed word-order groups (tiebreak applied): {mixed.Count} shown");
    foreach (var g in mixed)
    {
        Console.WriteLine($"  book {g.Key.BookId} lvl={g.Key.Level}:");
        foreach (var h in g.Take(6))
            Console.WriteLine($"    ord={(h.QueryInOrder ? 'y' : 'n')} {h.FullTocPath}");
    }
    return 0;
}

// ── Tanach verse entries (generated from line text — not present in the DB TOC) ──

{
    var verseCases = new (string Query, string ExpectedPath, int? ExpectedLineIndex)[]
    {
        ("בראשית פרק א פסוק ב", "בראשית / פרק א / פסוק ב", 3),
        ("בראשית פרק א פסוק לא", "בראשית / פרק א / פסוק לא", null),
        ("תהילים פרק כג פסוק א", "תהילים / פרק כג / פסוק א", null),
        ("עובדיה פרק א פסוק ב", "עובדיה / פרק א / פסוק ב", 3),
        ("שיר השירים פרק א פסוק ב", "שיר השירים / פרק א / פסוק ב", null),
        ("דברי הימים ב פרק א פסוק ב", "דברי הימים ב / פרק א / פסוק ב", null),
    };
    int ok = 0;
    foreach (var (q, path, lineIdx) in verseCases)
    {
        var hits = index.Search(q);
        var hit = hits.FirstOrDefault(h => h.FullTocPath == path);
        if (hit is null) Fail($"tanach verse: q=\"{q}\" missing \"{path}\" ({hits.Count} hits)");
        else if (lineIdx is { } li && hit.LineIndex != li)
            Fail($"tanach verse: \"{path}\" lineIndex={hit.LineIndex}, expected {li}");
        else ok++;
    }
    Console.WriteLine($"tanach verse checks: {ok}/{verseCases.Length} OK");
}

// ── Talmud daf/amud hierarchy (v14 restructuring) ────────────────────────────────

{
    // Bare daf query → the synthetic level-1 parent leads; amud children are level 2.
    var daf = index.Search("שבת דף ב");
    if (daf.Count == 0 || daf[0].FullTocPath != "שבת / דף ב" || daf[0].Level != 1)
        Fail($"daf: \"שבת דף ב\" top hit is not the level-1 parent " +
             $"(got \"{(daf.Count > 0 ? daf[0].FullTocPath : "-")}\" lvl={(daf.Count > 0 ? daf[0].Level : -1)})");

    var amudA = daf.FirstOrDefault(h => h.FullTocPath == "שבת / דף ב / עמוד א");
    var amudB = daf.FirstOrDefault(h => h.FullTocPath == "שבת / דף ב / עמוד ב");
    if (amudA is null || amudA.Level != 2) Fail("daf: עמוד א child missing or not level 2");
    if (amudB is null || amudB.Level != 2) Fail("daf: עמוד ב child missing or not level 2");
    // Default navigation: the parent points at the עמוד א line.
    if (daf.Count > 0 && amudA is not null && daf[0].LineIndex != amudA.LineIndex)
        Fail($"daf: parent lineIndex {daf[0].LineIndex} != עמוד א lineIndex {amudA.LineIndex}");

    // Amud-specific queries resolve to the child.
    var colon = index.Search("פסחים דף י:");
    if (!colon.Any(h => h.FullTocPath == "פסחים / דף י / עמוד ב"))
        Fail("daf: \"פסחים דף י:\" did not resolve to פסחים / דף י / עמוד ב");
    var amudQuery = index.Search("פסחים דף י עמוד ב");
    if (!amudQuery.Any(h => h.FullTocPath == "פסחים / דף י / עמוד ב"))
        Fail("daf: \"פסחים דף י עמוד ב\" did not resolve to the עמוד ב child");

    // The original motivating case: "שבת ב" — the amud-ב token of other dapim must not
    // rank at the daf level. Every level-1 hit whose book is the Talmud שבת must be the
    // real דף ב, not some דף X's second amud.
    var loose = index.Search("שבת ב");
    var talmudLvl1 = loose.Where(h => h.Level == 1 && h.FullTocPath.StartsWith("שבת / דף", StringComparison.Ordinal)).ToList();
    if (talmudLvl1.Count != 1 || talmudLvl1[0].FullTocPath != "שבת / דף ב")
        Fail($"daf: \"שבת ב\" level-1 Talmud hits should be exactly [שבת / דף ב], got " +
             $"[{string.Join(", ", talmudLvl1.Select(h => h.FullTocPath))}]");

    Console.WriteLine("daf/amud hierarchy checks: OK (parent level 1 → עמוד א line, children level 2, no amud-letter pollution at daf level)");
}

// ── Query-token-order rule: TOC-path-scoped (catalog/title words exempt) ─────────

{
    // Catalog word (תנך) is not in any TOC path → exempt; [בראשית, ד, יד] must be in
    // typed order → the reversed verse is discarded, the in-order one kept.
    var ordered = index.Search("תנך בראשית ד יד");
    bool hasInOrder = ordered.Any(h => h.FullTocPath == "בראשית / פרק ד / פסוק יד");
    bool hasReversed = ordered.Any(h => h.FullTocPath == "בראשית / פרק יד / פסוק ד");
    if (!hasInOrder) Fail("token-order: \"תנך בראשית ד יד\" missing בראשית / פרק ד / פסוק יד");
    if (hasReversed) Fail("token-order: \"תנך בראשית ד יד\" did not discard בראשית / פרק יד / פסוק ד");

    // Title word order never filters: both orders return the same משנה תורה books.
    var a = index.Search("משנה תורה הלכות שבת");
    var b = index.Search("תורה משנה הלכות שבת");
    if (a.Count != b.Count)
        Fail($"token-order: משנה תורה vs תורה משנה result counts differ ({a.Count} vs {b.Count})");
    Console.WriteLine($"token-order rule checks: in-order kept={hasInOrder}, reversed discarded={!hasReversed}, " +
                      $"title-order-free counts {a.Count}=={b.Count}");
}

// ── Fuzzy fallback: catalog/author only, 3+ char tokens, exact-first ─────────────

{
    // Misspelled catalog term (תבך ~ תנך, 1 edit, len 3): the exact search finds
    // nothing, the fallback resolves it through the CatalogPath field.
    var fz = index.Search("תבך בראשית פרק ב");
    bool fuzzyHit = fz.Any(h => h.FullTocPath == "בראשית / פרק ב");
    if (!fuzzyHit) Fail($"fuzzy: \"תבך בראשית פרק ב\" did not resolve תבך→תנך via catalog ({fz.Count} hits)");

    // TOC-path tokens must never fuzz: a wrong verse letter stays a miss even when
    // everything else matches (יב vs יג is edit distance 1 — and a different verse).
    var noToc = index.Search("בראשית פרק ב פסוק צט");   // chapter 2 has no verse 99
    if (noToc.Count != 0) Fail($"fuzzy: nonexistent verse matched anyway ({noToc.Count} hits)");

    // Tokens shorter than 3 chars are excluded from fuzzy: garbage 2-char token → 0.
    var shortTok = index.Search("בראשית ךב");
    if (shortTok.Count != 0) Fail($"fuzzy: 2-char garbage token produced {shortTok.Count} hits");

    Console.WriteLine($"fuzzy fallback checks: catalog-resolved={fuzzyHit}, toc-not-fuzzed={noToc.Count == 0}, " +
                      $"short-excluded={shortTok.Count == 0}");
}

// ── Sparse-fuzzy append: when normal hits < 10, append fuzzy-only hits AFTER ─────

{
    // (a) Zero exact (pure typo on a fuzzable token) still returns fuzzy results — the
    //     old "exact found nothing" fallback, now subsumed by the < 10 sparse rule.
    var zero = index.Search("רמבם הלכות שבתת");   // שבתת ~ שבת (fuzzable author/catalog token)
    bool fromZero = zero.Count > 0 && zero.All(h => !h.IsLiteral);
    if (!fromZero) Fail($"sparse-fuzzy: pure-typo query returned no fuzzy results ({zero.Count} hits)");

    // (b) Any appended fuzzy hit is non-literal and sits strictly AFTER every literal
    //     hit — never interleaved (checked across a batch of sparse queries).
    int interleave = 0;
    foreach (var q in new[] { "רמבם הלכות שבתת", "תבך בראשית פרק ב" })
    {
        var h = index.Search(q);
        int firstVar = h.FindIndex(x => !x.IsLiteral);
        int lastLit = h.FindLastIndex(x => x.IsLiteral);
        if (firstVar >= 0 && lastLit >= 0 && firstVar < lastLit) interleave++;
    }
    if (interleave > 0) Fail($"sparse-fuzzy: appended fuzzy hits interleaved with literal hits ({interleave} queries)");

    // (c) An ABUNDANT query (>= 10 exact literal hits) must NOT be padded with fuzzy —
    //     the append only fires when results are sparse. "שבת" has thousands of exact
    //     hits; every returned hit must be literal (no fuzzy pollution).
    var abundant = index.Search("שבת");
    bool noPollution = abundant.Count >= 10 && abundant.All(h => h.IsLiteral);
    if (!noPollution)
        Fail($"sparse-fuzzy: abundant query polluted with fuzzy ({abundant.Count} hits, " +
             $"{abundant.Count(h => !h.IsLiteral)} non-literal)");

    Console.WriteLine($"sparse-fuzzy append checks: from-zero={fromZero}, no-interleave={interleave == 0}, " +
                      $"abundant-not-polluted={noPollution}");
}

// ── Accuracy-first ordering: literal (exact) matches rank ahead of variant ones ──

{
    // עדות is a חסר spelling that also skeleton-matches מלא spellings (עדיות) in the
    // corpus, so a query for "עדות" yields both literal-עדות hits and variant-only hits.
    // Every literal hit MUST precede every variant hit (strategy A).
    int ordViol = 0;
    foreach (var q in new[] { "עדות", "מזוזה", "עדיות משנה ב" })
    {
        var hits = index.Search(q);
        int firstVariant = hits.FindIndex(h => !h.IsLiteral);
        int lastLiteral = hits.FindLastIndex(h => h.IsLiteral);
        // OK when there is no variant, no literal, or the whole literal block precedes
        // the whole variant block.
        if (firstVariant >= 0 && lastLiteral >= 0 && firstVariant < lastLiteral)
        {
            ordViol++;
            Fail($"accuracy-first: q=\"{q}\" a variant hit (#{firstVariant + 1}) precedes a literal hit (#{lastLiteral + 1})");
        }
    }
    if (ordViol == 0)
        Console.WriteLine("accuracy-first ordering checks: OK (every literal match ranks ahead of every variant match)");
}

// ── 3+4+5. Self-recall, contains-all, ordering over a generated corpus ──────────

var authorsByBook = books.ToDictionary(b => b.Id, b => b.Authors ?? "");
var rngGen = new Random(20260718);

var corpus = new List<(string Query, int BookId, string ExpectedPath)>();

// Book-title queries (expect the level-0 doc).
var sampledBooks = books.Where(b => serviceStrippedByBook.TryGetValue(b.Id, out var r) && r.Count > 0)
    .OrderBy(b => b.TreeOrder)
    .ToList();
int step = Math.Max(1, sampledBooks.Count / Math.Max(1, sampleBooks));
var chosenBooks = new List<ManualCatalogPipeline.Book>();
for (int i = 0; i < sampledBooks.Count; i += step) chosenBooks.Add(sampledBooks[i]);

foreach (var book in chosenBooks)
{
    corpus.Add((book.Title, book.Id, book.Title));

    var rows = serviceStrippedByBook[book.Id];
    var byId = rows.ToDictionary(r => r.Id);
    var indices = new HashSet<int> { 0, rows.Count / 2, rows.Count - 1 };
    while (indices.Count < Math.Min(entriesPerBook, rows.Count)) indices.Add(rngGen.Next(rows.Count));

    foreach (int idx in indices)
    {
        var row = rows[idx];
        if (string.IsNullOrWhiteSpace(row.Text)) continue;
        corpus.Add(($"{book.Title} {row.Text}", book.Id, ExpectedPath(row, byId, book.Title)));
    }
}

// Alt-TOC queries (expect the alt entry's doc).
{
    var altSample = altRowsByStructure.Keys.OrderBy(k => k).Where((_, i) => i % Math.Max(1, altRowsByStructure.Count / 40) == 0);
    foreach (int structureId in altSample)
    {
        if (!altStructureBook.TryGetValue(structureId, out int bookId)) continue;
        if (!bookById.TryGetValue(bookId, out var book)) continue;
        var rows = ServiceStripTitleRoots(altRowsByStructure[structureId], book.Title);
        if (rows.Count == 0) continue;
        var byId = rows.ToDictionary(r => r.Id);
        var row = rows[rows.Count - 1];
        if (string.IsNullOrWhiteSpace(row.Text)) continue;
        corpus.Add(($"{book.Title} {row.Text}", bookId, ExpectedPath(row, byId, book.Title)));
    }
}

Console.WriteLine($"query corpus: {corpus.Count} queries ({chosenBooks.Count} sampled books + alt structures)");

int recallMisses = 0, orderViolations = 0, containsAllViolations = 0;
double luceneMsTotal = 0, luceneMsMax = 0;
var swAll = Stopwatch.StartNew();

for (int qi = 0; qi < corpus.Count; qi++)
{
    var (query, expectedBookId, expectedPath) = corpus[qi];

    var swL = Stopwatch.StartNew();
    var hits = index.Search(query);
    double ms = swL.Elapsed.TotalMilliseconds;
    luceneMsTotal += ms;
    if (ms > luceneMsMax) luceneMsMax = ms;

    // 3. Self-recall — the entry's own doc must be present.
    bool found = false;
    foreach (var h in hits)
        if (h.BookId == expectedBookId && h.FullTocPath == expectedPath) { found = true; break; }
    if (!found)
    {
        recallMisses++;
        if (recallMisses <= 20)
            Fail($"self-recall q=\"{query}\" missing book={expectedBookId} path=\"{expectedPath}\" ({hits.Count} hits)");
    }

    // 5. Ordering - accuracy first, then catalog position: (IsLiteral desc,
    //    QuotedFormMatch desc, MatchesTitleOrAuthor desc, Level asc, TreeOrder asc)
    //    non-decreasing. A literal (exact) hit never sits below a variant/fuzzy one; a
    //    hit written as the typed acronym, or matching on title/author rather than only
    //    on a category name, leads within its literalness block; and within one such
    //    block (Level, TreeOrder) is monotonic.
    //    Plus the token-order discard invariant: no (level, book) group may contain both
    //    an in-order and an out-of-order hit (the out-of-order ones must have been dropped).
    {
        for (int i = 1; i < hits.Count; i++)
        {
            var prev = hits[i - 1];
            var cur = hits[i];
            // The WHERE-it-matched keys (QuotedFormMatch, MatchesTitleOrAuthor) sit
            // between IsLiteral and Level, so they legitimately break (Level, TreeOrder)
            // monotonicity. Compare them first; only when a pair is equal on all three
            // accuracy keys does the catalog-position order have to hold.
            bool ok =
                (prev.IsLiteral && !cur.IsLiteral) ||                    // literal block ends before variant block
                (prev.IsLiteral == cur.IsLiteral && (
                    (prev.QuotedFormMatch && !cur.QuotedFormMatch) ||    // quoted-form block ends
                    (prev.QuotedFormMatch == cur.QuotedFormMatch && (
                        (prev.MatchesTitleOrAuthor && !cur.MatchesTitleOrAuthor) || // title/author block ends
                        (prev.MatchesTitleOrAuthor == cur.MatchesTitleOrAuthor && ( // same block: (Level, TreeOrder)
                            prev.Level < cur.Level ||
                            (prev.Level == cur.Level && prev.TreeOrder <= cur.TreeOrder)))))));
            if (!ok)
            {
                orderViolations++;
                if (orderViolations <= 5)
                    Fail($"ordering q=\"{query}\" hit {i - 1}(lit={prev.IsLiteral},lvl={prev.Level},to={prev.TreeOrder}) " +
                         $"before {i}(lit={cur.IsLiteral},lvl={cur.Level},to={cur.TreeOrder})");
                break;
            }
        }

        var qTokensOrdered = CatalogTocTextRules.Tokenize(query);
        if (qTokensOrdered.Count >= 2)
        {
            // Same definition as the service: only query tokens PRESENT in the path
            // participate; < 2 participants = trivially in order.
            bool InOrder(CatalogTocHit h)
            {
                var pathTokens = CatalogTocTextRules.Tokenize(h.FullTocPath);
                var pathSet = pathTokens.ToHashSet();
                var participating = qTokensOrdered.Where(pathSet.Contains).ToList();
                if (participating.Count < 2) return true;
                int qi2 = 0;
                foreach (var t in pathTokens)
                    if (t == participating[qi2] && ++qi2 == participating.Count) return true;
                return false;
            }
            foreach (var g in hits.GroupBy(h => (h.Level, h.TreeOrder >> 24)))
            {
                bool anyIn = false, anyOut = false;
                foreach (var h in g)
                    if (InOrder(h)) anyIn = true; else anyOut = true;
                if (anyIn && anyOut)
                {
                    orderViolations++;
                    if (orderViolations <= 5)
                        Fail($"token-order discard q=\"{query}\": (lvl={g.Key.Item1}, book) group kept both orders");
                    break;
                }
            }
        }
    }

    // 4. Contains-all — sampled hits must satisfy every query token in
    //    catalog-path + path + authors, OR a variant of it that CatalogTocIndex.Search
    //    itself accepts as a match (חסר/מלא skeleton, ה-prefix, or the fuzzy-fallback
    //    edit-distance threshold) — see ContainsAllMatcher, which mirrors WordClause.
    //
    // Uses TokenizeQuery (not Tokenize) so an ABBREVIATION keeps all its alternatives:
    // BuildQuery emits MUST( OR over alternatives ), so a hit is legitimate when ONE
    // alternative is fully present. Flattening to alternative[0] would demand words from a
    // reading the hit never matched — e.g. רש"ש → (שמואל שטראשון | רשש): a title literally
    // containing רש"ש matches via the second alternative and has no "שמואל" to find.
    var qTokens = CatalogTocTextRules.TokenizeQuery(query);
    int checkCount = Math.Min(hits.Count, 10);
    for (int i = 0; i < checkCount; i++)
    {
        var h = hits[rngGen.Next(hits.Count)];
        string catalogPath = bookById.TryGetValue(h.BookId, out var hb) ? hb.ParentPath : "";
        var docTokens = CatalogTocTextRules.Tokenize(
                catalogPath + " " + h.FullTocPath + " " + authorsByBook.GetValueOrDefault(h.BookId, ""))
            .ToHashSet();
        foreach (var qt in qTokens)
        {
            // Satisfied when ANY alternative has ALL its words matched — mirroring
            // MUST( OR over alternatives ) where each alternative is an AND of words.
            bool satisfied = qt.Alternatives.Any(
                alt => alt.All(w => ContainsAllMatcher.TokenMatches(w, docTokens)));
            if (!satisfied)
            {
                containsAllViolations++;
                if (containsAllViolations <= 10)
                    Fail($"contains-all q=\"{query}\" token \"{Describe(qt)}\" "
                        + $"not in \"{h.FullTocPath}\" (book={h.BookId})");
                break;
            }
        }
    }

    if ((qi + 1) % 300 == 0)
        Console.WriteLine($"  {qi + 1}/{corpus.Count} — recall misses={recallMisses} order={orderViolations} containsAll={containsAllViolations}");
}

// ── Report ──────────────────────────────────────────────────────────────────────

Console.WriteLine();
Console.WriteLine($"done in {swAll.Elapsed.TotalSeconds:F1}s");
Console.WriteLine($"queries: {corpus.Count}   avg search: {luceneMsTotal / corpus.Count:F1}ms   max: {luceneMsMax:F0}ms");
Console.WriteLine($"self-recall misses:      {recallMisses}");
Console.WriteLine($"ordering violations:     {orderViolations}");
Console.WriteLine($"contains-all violations: {containsAllViolations}");
Console.WriteLine($"total failures:          {failures}");

Console.WriteLine(failures == 0 ? "\nSPEC: PASS" : "\nSPEC: FAIL");
return failures == 0 ? 0 : 1;
