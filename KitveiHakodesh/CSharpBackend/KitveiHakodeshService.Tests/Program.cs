// Catalog TOC search parity test.
//
// Ground truth: ManualCatalogPipeline — a faithful C# port of the catalog page's manual
// search (book matcher + TOC heuristics + SegmentSearchTree scorer). Candidate: the
// Lucene CatalogTocIndex the service now uses.
//
// For an extensive corpus of queries (curated + generated from real books/TOC entries):
//   1. TOC recall  — every TOC item the manual way returns must be in the Lucene results
//   2. Book recall — every book the manual matcher returns must be in the Lucene results
//   3. Dedup sanity — ancestor-dedup results must be a subset of the raw results
// plus first-line spot checks for book docs and a rebuild-hash unit check.
//
// Usage: dotnet run -c Release [-- --db <seforim.db>] [--books 300] [--entries 3]
//        [--rebuild] [--max-queries N]
using System.Diagnostics;
using KitveiHakodeshService.Catalog;
using KitveiHakodeshService.Tests;
using Microsoft.Data.Sqlite;

// ── Args ────────────────────────────────────────────────────────────────────────

string? dbPath = null;
string? indexPath = null;
string? compareQuery = null;
int sampleBooks = 300, entriesPerBook = 3, maxQueries = int.MaxValue;
bool forceRebuild = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--db": dbPath = args[++i]; break;
        case "--index": indexPath = args[++i]; break;
        case "--books": sampleBooks = int.Parse(args[++i]); break;
        case "--entries": entriesPerBook = int.Parse(args[++i]); break;
        case "--max-queries": maxQueries = int.Parse(args[++i]); break;
        case "--rebuild": forceRebuild = true; break;
        case "--compare": compareQuery = args[++i]; break;
    }
}

dbPath ??= Environment.GetEnvironmentVariable("DB_PATH");
if (string.IsNullOrWhiteSpace(dbPath))
{
    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    string zayit = Path.Combine(appData, "io.github.kdroidfilter.seforimapp", "databases", "seforim.db");
    string otzaria = Path.Combine(appData, "otzaria", "books", "seforim.db");
    dbPath = File.Exists(zayit) ? zayit : otzaria;
}
if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"seforim DB not found: {dbPath}");
    return 2;
}
indexPath ??= Path.Combine(AppContext.BaseDirectory, "CatalogTocIndex.test");

Console.WriteLine($"db:    {dbPath}");
Console.WriteLine($"index: {indexPath}");

// ── Rebuild-hash unit check (the rebuild trigger itself) ────────────────────────

{
    string scratch = Path.Combine(Path.GetTempPath(), $"catalogtoc-hash-probe-{Environment.ProcessId}.tmp");
    File.WriteAllText(scratch, "one");
    string h1 = CatalogTocIndex.ComputeDbHash(scratch);
    string h1Again = CatalogTocIndex.ComputeDbHash(scratch);
    File.WriteAllText(scratch, "two-longer");
    string h2 = CatalogTocIndex.ComputeDbHash(scratch);
    File.Delete(scratch);
    if (h1 != h1Again) { Console.Error.WriteLine("FAIL: DB hash is not stable for an unchanged file"); return 2; }
    if (h1 == h2) { Console.Error.WriteLine("FAIL: DB hash did not change when the file changed"); return 2; }
    Console.WriteLine("hash-trigger unit check: OK (stable when unchanged, changes on file change)");
}

// ── Load catalog data (same order the frontend loads it) ───────────────────────

var swLoad = Stopwatch.StartNew();
var categories = new List<(int Id, int? ParentId, string Title)>();
var books = new List<ManualCatalogPipeline.Book>();
var rowsByBook = new Dictionary<int, List<ManualCatalogPipeline.TocRow>>();

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
}

ManualCatalogPipeline.AssignTreeOrderAndPaths(categories, books);
ManualCatalogPipeline.PrepareBookTokens(books);

var bookById = books.ToDictionary(b => b.Id);
var strippedRowsByBook = new Dictionary<int, List<ManualCatalogPipeline.TocRow>>(rowsByBook.Count);
foreach (var (bookId, rows) in rowsByBook)
    strippedRowsByBook[bookId] = bookById.TryGetValue(bookId, out var b)
        ? ManualCatalogPipeline.StripTocTitleRoots(rows, b.Title, bookId)
        : rows;

long tocRowCount = rowsByBook.Values.Sum(r => (long)r.Count);
Console.WriteLine($"loaded: {books.Count} books, {categories.Count} categories, " +
                  $"{tocRowCount} toc rows in {swLoad.Elapsed.TotalSeconds:F1}s");

// ── Build (or reuse) the Lucene index ───────────────────────────────────────────

string verFile = Path.Combine(indexPath, "catalogtoc.ver");
string dbHash = CatalogTocIndex.ComputeDbHash(dbPath);
bool needBuild = forceRebuild || !File.Exists(verFile)
    || !string.Equals(File.ReadAllText(verFile).Trim(), dbHash, StringComparison.OrdinalIgnoreCase);

var index = new CatalogTocIndex(indexPath, dbPath);
if (needBuild)
{
    if (Directory.Exists(indexPath)) Directory.Delete(indexPath, recursive: true);
    Directory.CreateDirectory(indexPath);
    var swBuild = Stopwatch.StartNew();
    int docs = index.Build(onProgress: (done, total) =>
    {
        if (done % 1000 == 0 || done == total) Console.Write($"\r  indexing books {done}/{total}");
    });
    Console.WriteLine();
    File.WriteAllText(verFile, dbHash);
    Console.WriteLine($"lucene index built: {docs} docs in {swBuild.Elapsed.TotalSeconds:F1}s");
}
else
{
    Console.WriteLine($"lucene index reused: {index.DocCount()} docs (hash match)");
}

// ── Order comparison mode (--compare "<query>") ─────────────────────────────────
// Prints the manual pipeline's ranked results next to the Lucene ranking so the two
// orderings can be eyeballed side by side.

if (compareQuery is not null)
{
    Console.WriteLine();
    Console.WriteLine($"=== ORDER COMPARISON: \"{compareQuery}\" ===");

    var manual = ManualCatalogPipeline.Search(compareQuery, books, strippedRowsByBook);
    var lucene = index.Search(compareQuery, dedupAncestors: true);

    var luceneRank = new Dictionary<(int, int), int>();
    for (int i = 0; i < lucene.Count; i++)
        if (lucene[i].Kind == "toc")
            luceneRank.TryAdd((lucene[i].BookId, lucene[i].TocEntryId), i + 1);

    Console.WriteLine();
    Console.WriteLine($"MANUAL ({manual.Trigger} trigger): {manual.MatchedBooks.Count} books, " +
                      $"{manual.TocItems.Count} toc items — with each item's Lucene rank:");
    for (int i = 0; i < Math.Min(30, manual.TocItems.Count); i++)
    {
        var it = manual.TocItems[i];
        string title = bookById.GetValueOrDefault(it.BookId)?.Title ?? $"book {it.BookId}";
        string lr = luceneRank.TryGetValue((it.BookId, it.TocEntryId), out int r) ? $"L#{r}" : "L:-";
        Console.WriteLine($"  M#{i + 1,-3} [{lr,-6}] {title} / {it.TocPath}");
    }
    if (manual.TocItems.Count > 30) Console.WriteLine($"  … {manual.TocItems.Count - 30} more");

    Console.WriteLine();
    Console.WriteLine($"LUCENE: {lucene.Count} hits — top 30:");
    var manualRank = new Dictionary<(int, int), int>();
    for (int i = 0; i < manual.TocItems.Count; i++)
        manualRank.TryAdd((manual.TocItems[i].BookId, manual.TocItems[i].TocEntryId), i + 1);
    for (int i = 0; i < Math.Min(30, lucene.Count); i++)
    {
        var h = lucene[i];
        string mr = h.Kind == "book" ? "book " :
            manualRank.TryGetValue((h.BookId, h.TocEntryId), out int r) ? $"M#{r,-3}" : "M:-  ";
        Console.WriteLine($"  L#{i + 1,-3} [{mr}] {h.BookTitle}{(h.TocPath.Length > 0 ? " / " + h.TocPath : "")}");
    }
    if (lucene.Count > 30) Console.WriteLine($"  … {lucene.Count - 30} more");
    return 0;
}

// ── First-line spot check for book docs ─────────────────────────────────────────

{
    var rng = new Random(7);
    int checkedBooks = 0, flFails = 0;
    using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
    {
        DataSource = dbPath,
        Mode = SqliteOpenMode.ReadOnly,
    }.ConnectionString);
    conn.Open();
    foreach (var b in books.OrderBy(_ => rng.Next()).Take(20))
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM line WHERE bookId = @b ORDER BY lineIndex LIMIT 1";
        cmd.Parameters.AddWithValue("@b", b.Id);
        object? expected = cmd.ExecuteScalar();
        if (expected is null) continue; // book with no lines — no doc-level claim to check

        var hits = index.Search(b.Title, dedupAncestors: false);
        var bookHit = hits.FirstOrDefault(h => h.Kind == "book" && h.BookId == b.Id);
        checkedBooks++;
        if (bookHit is null)
        {
            Console.Error.WriteLine($"  FIRSTLINE MISS: book {b.Id} '{b.Title}' — no book hit for its own title");
            flFails++;
        }
        else if (bookHit.LineId != Convert.ToInt32(expected))
        {
            Console.Error.WriteLine(
                $"  FIRSTLINE WRONG: book {b.Id} '{b.Title}' — lineId {bookHit.LineId}, expected {expected}");
            flFails++;
        }
    }
    Console.WriteLine($"book-doc first-line spot check: {checkedBooks - flFails}/{checkedBooks} OK");
    if (flFails > 0) Console.Error.WriteLine($"  {flFails} first-line failures");
}

// ── Query corpus ────────────────────────────────────────────────────────────────

var queries = new List<string>
{
    // curated — realistic catalog queries across trigger types and quirks
    "בראשית פרק ד",
    "בראשית פרק ד פסוק ב",
    "שלחן ערוך אורח חיים סימן א",
    "שו\"ע אורח חיים סימן קכח",
    "שוע יורה דעה סימן א",
    "שולחן ערוך אבן העזר",
    "משנה תורה הלכות שבת",
    "רמבם הלכות תשובה פרק ג",
    "הרמבם הלכות תשובה",
    "פסחים דף י",
    "מסכת פסחים דף י:",
    "פסחים דף ד.",
    "שבת דף קנז",
    "תהלים מזמור כג",
    "תהילים מזמור קיט",
    "משנה ברורה סימן א",
    "טור יורה דעה סימן א",
    "זוהר בראשית",
    "רשי בראשית פרק א",
    "רש\"י בראשית פרק א",
    "אבן עזרא שמות פרק ב",
    "ברכות פרק א משנה ב",
    "משניות ברכות פרק א",
    "שער הכוונות שער א",
    "ילקוט שמעוני רמז א",
    "מדרש רבה פרשה א",
    "ספר החינוך מצוה א",
    "חיי אדם כלל א",
    "קיצור שלחן ערוך סימן א",
    "נידה דף ב",
    "נדה דף ב",
    "שבועות פרק א",
    "אגרת הרמבן",
    "אורחות צדיקים שער א",
    "מסילת ישרים פרק א",
    "פרק א",              // no book part at all
    "א",                   // single letter
    "בראשית",             // pure book query (no toc trigger)
    "ספר",                 // broad prefix
};

var rngGen = new Random(20260717);
var sampled = books.Where(b => strippedRowsByBook.TryGetValue(b.Id, out var r) && r.Count > 0)
    .OrderBy(b => b.TreeOrder)
    .ToList();
int step = Math.Max(1, sampled.Count / Math.Max(1, sampleBooks));
var chosenBooks = new List<ManualCatalogPipeline.Book>();
for (int i = 0; i < sampled.Count; i += step) chosenBooks.Add(sampled[i]);

foreach (var book in chosenBooks)
{
    var rows = strippedRowsByBook[book.Id];
    var byId = rows.ToDictionary(r => r.Id);
    var indices = new HashSet<int> { 0, rows.Count / 2, rows.Count - 1 };
    while (indices.Count < Math.Min(entriesPerBook, rows.Count)) indices.Add(rngGen.Next(rows.Count));

    foreach (int idx in indices)
    {
        var row = rows[idx];
        if (string.IsNullOrWhiteSpace(row.Text)) continue;

        queries.Add($"{book.Title} {row.Text}");
        if (row.ParentId is { } pid && byId.TryGetValue(pid, out var parent) && !string.IsNullOrWhiteSpace(parent.Text))
            queries.Add($"{book.Title} {parent.Text} {row.Text}");

        // prefix truncation of the last word
        var words = ManualCatalogPipeline.ToQueryWords($"{book.Title} {row.Text}");
        if (words.Length > 0 && words[^1].Length >= 3)
            queries.Add(string.Join(' ', words[..^1].Append(words[^1][..^1])));
    }
}

queries = queries
    .Select(q => q.Trim())
    .Where(q => q.Length > 0)
    .Distinct()
    .Take(maxQueries)
    .ToList();
Console.WriteLine($"query corpus: {queries.Count} queries ({chosenBooks.Count} sampled books)");

// ── Run the comparison ──────────────────────────────────────────────────────────

int tocMisses = 0, bookMisses = 0, dedupViolations = 0;
int queriesWithTocItems = 0;
long manualItemsTotal = 0;
double manualMsTotal = 0, luceneMsTotal = 0, luceneMsMax = 0;
var missSamples = new List<string>();
var swAll = Stopwatch.StartNew();

for (int qi = 0; qi < queries.Count; qi++)
{
    string q = queries[qi];

    var swM = Stopwatch.StartNew();
    var manual = ManualCatalogPipeline.Search(q, books, strippedRowsByBook);
    manualMsTotal += swM.Elapsed.TotalMilliseconds;

    var swL = Stopwatch.StartNew();
    var lucene = index.Search(q, dedupAncestors: false);
    double lms = swL.Elapsed.TotalMilliseconds;
    luceneMsTotal += lms;
    if (lms > luceneMsMax) luceneMsMax = lms;

    var luceneTocSet = new HashSet<(int, int)>();
    var luceneBookSet = new HashSet<int>();
    foreach (var h in lucene)
    {
        if (h.Kind == "toc") luceneTocSet.Add((h.BookId, h.TocEntryId));
        else luceneBookSet.Add(h.BookId);
    }

    // 1. TOC recall
    if (manual.TocItems.Count > 0) queriesWithTocItems++;
    manualItemsTotal += manual.TocItems.Count;
    foreach (var item in manual.TocItems)
    {
        if (!luceneTocSet.Contains((item.BookId, item.TocEntryId)))
        {
            tocMisses++;
            if (missSamples.Count < 40)
                missSamples.Add($"TOC  q=\"{q}\" book={item.BookId} " +
                    $"'{bookById.GetValueOrDefault(item.BookId)?.Title}' toc={item.TocEntryId} path=\"{item.TocPath}\"");
        }
    }

    // 2. Book recall
    foreach (var mb in manual.MatchedBooks)
    {
        if (!luceneBookSet.Contains(mb.Id))
        {
            bookMisses++;
            if (missSamples.Count < 40)
                missSamples.Add($"BOOK q=\"{q}\" book={mb.Id} '{mb.Title}' (path: {mb.ParentPath})");
        }
    }

    // 3. Dedup sanity (subsample — a second search per probe)
    if (qi % 25 == 0)
    {
        var deduped = index.Search(q, dedupAncestors: true);
        foreach (var h in deduped)
        {
            if (h.Kind == "toc" && !luceneTocSet.Contains((h.BookId, h.TocEntryId))) dedupViolations++;
            if (h.Kind == "book" && !luceneBookSet.Contains(h.BookId)) dedupViolations++;
        }
    }

    if ((qi + 1) % 200 == 0)
        Console.WriteLine($"  {qi + 1}/{queries.Count} queries — misses so far: toc={tocMisses} book={bookMisses}");
}

// ── Report ──────────────────────────────────────────────────────────────────────

Console.WriteLine();
Console.WriteLine($"done in {swAll.Elapsed.TotalSeconds:F1}s");
Console.WriteLine($"queries: {queries.Count} ({queriesWithTocItems} produced manual TOC items, " +
                  $"{manualItemsTotal} manual TOC items total)");
Console.WriteLine($"avg manual: {manualMsTotal / queries.Count:F1}ms   " +
                  $"avg lucene: {luceneMsTotal / queries.Count:F1}ms   max lucene: {luceneMsMax:F0}ms");
Console.WriteLine($"TOC recall misses:  {tocMisses}");
Console.WriteLine($"book recall misses: {bookMisses}");
Console.WriteLine($"dedup violations:   {dedupViolations}");

if (missSamples.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("miss samples:");
    foreach (var s in missSamples) Console.WriteLine("  " + s);
}

bool ok = tocMisses == 0 && bookMisses == 0 && dedupViolations == 0;
Console.WriteLine(ok ? "\nPARITY: PASS — Lucene returns everything the manual way returns."
                     : "\nPARITY: FAIL");
return ok ? 0 : 1;
