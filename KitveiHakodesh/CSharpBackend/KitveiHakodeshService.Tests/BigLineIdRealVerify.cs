// Check of a REAL library whose line ids are too large for the id band (read-only).
//
// Run: dotnet run -c Release -- --bigids-real "<path to the library .db>"
//
// The file is only ever opened read-only (immutable); indexes are written to a temp folder; the
// primary database is a tiny synthetic one, so the official seforim.db is never touched.
using KitveiHakodesh.Core.SeforimDb;
using KitveiHakodesh.Core.SeforimDbCatalog;
using KitveiHakodesh.Core.SeforimDbFullTextSearch;
using KitveiHakodesh.Core.Settings;
using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.Tests
{
    public static class BigLineIdRealVerify
    {
        public static int Run(string libraryPath)
        {
            int failures = 0;
            void Fail(string msg) { failures++; Console.Error.WriteLine("  FAIL: " + msg); }
            void Ok(string msg) { Console.WriteLine("  OK: " + msg); }
            void Check(bool cond, string okMsg, string failMsg) { if (cond) Ok(okMsg); else Fail(failMsg); }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            string Elapsed() => $"[{clock.Elapsed.TotalSeconds:F0}s]";

            string dir = Path.Combine(Path.GetTempPath(), $"bigidsreal-{Environment.ProcessId}");
            Directory.CreateDirectory(dir);
            AppSettingsRegistry.InMemoryStore = new();
            try
            {
                Console.WriteLine("Library: " + libraryPath + "  (" + new FileInfo(libraryPath).Length / (1024 * 1024) + " MB)");
                string primary = Path.Combine(dir, "seforim.db");
                HardeningVerify.BuildLibrary(primary, "ראשי", "ספר ראשי", "טקסט ראשי", "פרק ראשי", HardeningVerify.Shape.Plain, altToc: true, regularToc: true);
                var entries = new[] { new SeforimDbAdditionalLibraries.Entry(3, libraryPath) };

                // Ground truth, straight from the file.
                long totalLines, bookCount, booksWithLines; long maxStored;
                var firstIds = new Dictionary<int, long>(); var bookLines = new Dictionary<int, int>(); var titles = new Dictionary<int, string>();
                using (var c = new SqliteConnection($"Data Source={libraryPath};Mode=ReadOnly"))
                {
                    c.Open();
                    totalLines = Scalar(c, "SELECT COUNT(*) FROM line");
                    maxStored = Scalar(c, "SELECT MAX(id) FROM line");
                    bookCount = Scalar(c, "SELECT COUNT(*) FROM book");
                    booksWithLines = Scalar(c, "SELECT COUNT(DISTINCT bookId) FROM line");
                    using var cmd = c.CreateCommand();
                    cmd.CommandText = "SELECT b.id, b.title, b.totalLines, (SELECT id FROM line WHERE bookId=b.id AND lineIndex=0) FROM book b";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        int id = r.GetInt32(0); titles[id] = r.IsDBNull(1) ? "" : r.GetString(1);
                        bookLines[id] = r.IsDBNull(2) ? 0 : r.GetInt32(2);
                        if (!r.IsDBNull(3)) firstIds[id] = r.GetInt64(3);
                    }
                }
                Console.WriteLine($"  file: {totalLines:N0} lines, {bookCount:N0} books ({booksWithLines:N0} with lines), highest stored line id {maxStored:N0}");

                // ── attach ───────────────────────────────────────────────────────────────────
                var st = SeforimDbAttachedLibraries.ResolveWithStatus(entries).Single();
                Check(st.Rejection is null && st.Library is { } lib0 && lib0.LineIds is not null,
                    $"ATTACHED with a line-id map {Elapsed()}", "rejected: " + (st.Rejection ?? "no map"));
                if (st.Library is not { } lib || lib.LineIds is not { } map) return failures + 1;
                Check(map.Total == totalLines && map.BlockCount == booksWithLines,
                    $"the map covers all {map.Total:N0} lines in {map.BlockCount:N0} books (file says {totalLines:N0} / {booksWithLines:N0})",
                    $"map {map.Total:N0} lines / {map.BlockCount:N0} blocks vs file {totalLines:N0} / {booksWithLines:N0}");
                long tocCount; using (var c2 = new SqliteConnection($"Data Source={libraryPath};Mode=ReadOnly")) { c2.Open(); tocCount = Scalar(c2, "SELECT COUNT(*) FROM tocEntry"); }
                Check(lib.TocIds is { } tm && tm.Total == tocCount, $"the TOC-entry ids are translated too ({lib.TocIds?.Total:N0} entries; file says {tocCount:N0})", $"TOC map {lib.TocIds?.Total} vs file {tocCount}");
                Check(SeforimDbAttachedLibraries.CheckTextReadable(libraryPath) is null, "its line text is readable", "text unreadable");

                // ── map vs the file, 20,000 random lines + both ends of every book ───────────
                var rnd = new Random(42); int bad = 0; int checkedIds = 0;
                using (var c = new SqliteConnection($"Data Source={libraryPath};Mode=ReadOnly"))
                {
                    c.Open();
                    using var cmd = c.CreateCommand();
                    cmd.CommandText = "SELECT id FROM line WHERE bookId=$b AND lineIndex=$i";
                    var pb = cmd.Parameters.Add("$b", SqliteType.Integer); var pi = cmd.Parameters.Add("$i", SqliteType.Integer);
                    var ids = bookLines.Where(k => k.Value > 0 && firstIds.ContainsKey(k.Key)).ToList();
                    int prevLocal = 0; long prevStored = 0;
                    foreach (var (bookId, n) in ids.OrderBy(k => firstIds[k.Key]))
                        foreach (int i in n == 1 ? new[] { 0 } : new[] { 0, n - 1 })
                        {
                            pb.Value = bookId; pi.Value = i;
                            long stored = (long)cmd.ExecuteScalar()!;
                            int local = map.ToLocal(stored);
                            if (local < 1 || map.ToStored(local) != stored || (i == 0 && local <= prevLocal) ) bad++;
                            if (i == 0 && stored <= prevStored) bad++;
                            if (i == 0) { prevLocal = local; prevStored = stored; }
                            checkedIds++;
                        }
                    for (int k = 0; k < 20000; k++)
                    {
                        var (bookId, n) = ids[rnd.Next(ids.Count)];
                        int i = rnd.Next(n);
                        pb.Value = bookId; pi.Value = i;
                        long stored = (long)cmd.ExecuteScalar()!;
                        int local = map.ToLocal(stored);
                        if (local < 1 || map.ToStored(local) != stored) bad++;
                        checkedIds++;
                    }
                }
                Check(bad == 0, $"{checkedIds:N0} stored line ids translate to local ids and back exactly, in the file's order", $"{bad} ids translated wrongly");

                // ── queries on 40 random books ───────────────────────────────────────────────
                var q = new SeforimDbQueries(primary, entries);
                var allBooks = q.GetAllBooks();
                var libBooks = allBooks.Where(b => b.LibraryLabel != null).ToList();
                Check(libBooks.Count == bookCount, $"GetAllBooks lists all {libBooks.Count:N0} books of the library", $"{libBooks.Count:N0} books vs file {bookCount:N0}");
                int queryBad = 0; int sampled = 0; var sampleBooks = libBooks.OrderBy(_ => rnd.Next()).Take(40).ToList();
                foreach (var b in sampleBooks)
                {
                    var (_, localBook) = SeforimDbLibraryId.Resolve(b.Id);
                    int expected = bookLines[localBook];
                    if (expected == 0) continue;
                    sampled++;
                    var lines = q.GetLinesPaged(b.Id, expected + 5, 0);
                    if (lines.Count != expected || lines.Select(l => l.Id).Distinct().Count() != expected
                        || lines.Select(l => l.LineIndex).SequenceEqual(Enumerable.Range(0, expected)) == false
                        || lines.Any(l => l.Id <= SeforimDbLibraryId.ToGlobal(3, 0) || l.Id >= SeforimDbLibraryId.ToGlobal(4, 0))) { queryBad++; continue; }
                    var probe = new List<int> { lines[0].Id, lines[expected - 1].Id, lines[expected / 2].Id };
                    var contents = q.GetLineContents(probe);
                    if (contents.Count != 3 || probe.Any(id => contents.All(c => c.Id != id || string.IsNullOrEmpty(c.Content)) )) { queryBad++; continue; }
                    foreach (var l in new[] { lines[0], lines[expected - 1], lines[expected / 2] })
                        if (contents.Single(c => c.Id == l.Id).Content != l.Content) queryBad++;
                    var owners = q.GetBookIdsForLines(probe);
                    if (owners.Count != 3 || owners.Any(o => o.BookId != b.Id)) queryBad++;
                    var li = q.GetLineIndexFromLineId(lines[expected / 2].Id);
                    if (li.Count != 1 || li[0].LineIndex != expected / 2 || li[0].BookId != b.Id) queryBad++;
                }
                Check(queryBad == 0, $"{sampled} random books: every line read in order with distinct local ids, same text through GetLineContents, owners and line indexes right {Elapsed()}",
                    $"{queryBad} problems in {sampled} sampled books");
                var firstTextBook = sampleBooks.First(b => bookLines[SeforimDbLibraryId.Resolve(b.Id).LocalId] > 3);
                Console.WriteLine("  sample line: " + q.GetLinesPaged(firstTextBook.Id, 1, 0)[0].Content.Substring(0, Math.Min(60, q.GetLinesPaged(firstTextBook.Id, 1, 0)[0].Content.Length)));

                // ── catalog index over the real file ─────────────────────────────────────────
                var libs = SeforimDbAttachedLibraries.Resolve(entries);
                var skipped = new List<string>();
                using (var cat = new SeforimDbCatalogIndex(Path.Combine(dir, "Cat"), primary, libs) { SourceSkipped = (p, e) => skipped.Add(p + ": " + e.Message) })
                {
                    int docs = cat.BuildAndSwitch(SeforimDbCatalogIndex.ComputeDbHash(primary, libs));
                    Check(skipped.Count == 0 && docs > booksWithLines, $"catalog index built: {docs:N0} documents, library not skipped {Elapsed()}", "catalog: " + string.Join(" | ", skipped) + $" docs={docs}");
                    var target = titles.FirstOrDefault(t => t.Value.Contains("ידיד נפש")).Value ?? titles.Values.First(t => t.Length > 3);
                    var hits = cat.Search(target, CancellationToken.None).ToList();
                    Check(hits.Any(h => h.Level == 0 && libBooks.Any(b => b.Id == h.BookId && b.Title == target)),
                        $"searching the catalog for \"{target}\" finds that book of the library", $"catalog search for \"{target}\": {hits.Count} hits, none is the book");
                    var tocHit = hits.FirstOrDefault(h => h.Level > 0);
                    if (tocHit is not null)
                    {
                        var path = q.GetTocPathsForLines(new List<int> { SeforimDbLibraryId.ToGlobal(3, 1) });
                        Ok("(a TOC entry was also returned by the catalog search: " + tocHit.FullTocPath + ")");
                    }
                }

                // ── full-text index over the real file ───────────────────────────────────────
                Console.WriteLine("  building the full-text index over " + totalLines.ToString("N0") + " lines ... " + Elapsed());
                string fidx = Path.Combine(dir, "Fts");
                var indexer = new SeforimDbFtsIndexer(fidx, primary, libs);
                using var engine = new SeforimDbFtsEngine(fidx, primary, libs);
                Check(indexer.Build(engine), $"FTS index built {Elapsed()}", "FTS build returned false");
                var searcher = new SeforimDbFtsSearcher();
                int ftsBad = 0, ftsHits = 0;
                foreach (string word in new[] { "ידיד", "אהבה", "שקלים" })
                {
                    var hits = searcher.SearchInBatches(engine, word, 0, false, 6, new FtsTermTable()).SelectMany(b => b.Hits).Take(60).ToList();
                    ftsHits += hits.Count;
                    var lineIds = hits.Select(h => h.LineId).ToList();
                    var contents = lineIds.Count == 0 ? new List<LineContentRow>() : q.GetLineContents(lineIds);
                    var owners = lineIds.Count == 0 ? new List<LineBookRow>() : q.GetBookIdsForLines(lineIds);
                    foreach (var h in hits)
                    {
                        var content = contents.FirstOrDefault(c => c.Id == h.LineId)?.Content;
                        var owner = owners.FirstOrDefault(o => o.LineId == h.LineId)?.BookId;
                        if (content is null || !content.Contains(word) || owner != h.BookId) ftsBad++;
                    }
                    Console.WriteLine($"  \"{word}\": {hits.Count} hits (first 60 checked)");
                }
                Check(ftsHits > 0 && ftsBad == 0, $"every checked FTS hit's text contains the word and its line really belongs to the reported book ({ftsHits} hits) {Elapsed()}", $"{ftsBad} of {ftsHits} FTS hits did not match their line text/book");
                engine.Dispose();
            }
            catch (Exception ex)
            {
                Fail("unhandled exception: " + ex);
            }
            finally
            {
                AppSettingsRegistry.InMemoryStore = null;
                SqliteConnection.ClearAllPools();
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
            Console.WriteLine(failures == 0 ? "REAL-LIBRARY BIG-LINE-ID CHECKS PASSED" : $"{failures} REAL-LIBRARY CHECK(S) FAILED");
            return failures;
        }

        private static long Scalar(SqliteConnection c, string sql)
        {
            using var cmd = c.CreateCommand(); cmd.CommandText = sql;
            return Convert.ToInt64(cmd.ExecuteScalar());
        }
    }
}
