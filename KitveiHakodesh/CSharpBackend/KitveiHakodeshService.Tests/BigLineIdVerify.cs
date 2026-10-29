// Verification of libraries whose stored LINE ids are too large for the id band (branch feature/multi-library-pr).
//
// Run: dotnet run -c Release -- --bigids
//
// The shape is the one a published personal library really has: line.id = bookId * 1,048,576 + lineIndex + 1,
// so ids reach billions (above int32) while book/category/TOC ids stay small. Such a library used to be
// rejected ("ids in table 'line' exceed the per-library id range"); it is now attached with its line ids
// translated to dense local ids (SeforimDbIdMap). Everything below checks that nothing downstream
// ever sees - or breaks on - the stored id: lines, line contents, TOC paths, book ids, the catalog index,
// the full-text corpus and a real full-text search.
//
// Never touches a real user's registry (AppSettingsRegistry.InMemoryStore) or any installed service.
using System.Text;
using KitveiHakodesh.Core.SeforimDb;
using KitveiHakodesh.Core.SeforimDbCatalog;
using KitveiHakodesh.Core.SeforimDbFullTextSearch;
using KitveiHakodesh.Core.Settings;
using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.Tests
{
    public static class BigLineIdVerify
    {
        // (book id, title, lines) - book 5000's ids are above int32 (5000 * 1,048,576 = 5.2 billion).
        private static readonly (int Id, string Title, string[] Lines)[] Books =
        {
            (1, "ספר ראשון", new[] { "שמש בבוקר", "ירח בלילה", "כוכב רחוק" }),
            (2, "ספר שני", new[] { "נהר גדול", "הר גבוה" }),
            (5000, "ספר רחוק", new[] { "ים סוער", "יער עבות", "מדבר שומם", "עמק פורח" }),
        };

        private static long StoredId(int bookId, int lineIndex) => (long)bookId * 1_048_576 + lineIndex + 1;

        /// <summary>A TOC entry id in the same scheme (entry k of the book, k from 0).</summary>
        private static long TocId(int bookId, int k) => (long)bookId * 1_048_576 + k + 1;

        public static int Run()
        {
            int failures = 0;
            void Fail(string msg) { failures++; Console.Error.WriteLine("  FAIL: " + msg); }
            void Ok(string msg) { Console.WriteLine("  OK: " + msg); }
            void Check(bool cond, string okMsg, string failMsg) { if (cond) Ok(okMsg); else Fail(failMsg); }

            AppSettingsRegistry.InMemoryStore = new();
            string dir = Path.Combine(Path.GetTempPath(), $"bigidstest-{Environment.ProcessId}");
            Directory.CreateDirectory(dir);
            try
            {
                string primary = Path.Combine(dir, "seforim.db");
                HardeningVerify.BuildLibrary(primary, "ראשי", "ספר ראשי", "טקסט ראשי", "פרק ראשי", HardeningVerify.Shape.Plain, altToc: true, regularToc: true);

                foreach (var shape in new[] { HardeningVerify.Shape.SplitCompressed, HardeningVerify.Shape.Plain })
                {
                    string label = "big-" + shape.ToString().ToLowerInvariant();
                    Console.WriteLine($"Library shaped like the published database ({shape}):");
                    string libPath = Path.Combine(dir, label + ".db");
                    BuildBigIdLibrary(libPath, shape);
                    var entries = new[] { new SeforimDbAdditionalLibraries.Entry(1, libPath) };

                    // ── attach ───────────────────────────────────────────────────────────────
                    var status = SeforimDbAttachedLibraries.ResolveWithStatus(entries).Single();
                    Check(status.Rejection is null && status.Library is { } l0 && l0.LineIds is { Total: 9 } && l0.TocIds is { Total: 3 },
                        "the library is ATTACHED (it used to be rejected) with a 9-line and a 3-TOC-entry translation map",
                        "rejected / no map: " + (status.Rejection ?? "map=" + status.Library?.LineIds));
                    Check(SeforimDbAttachedLibraries.CheckTextReadable(libPath) is null,
                        "its line text is readable", "text unreadable: " + SeforimDbAttachedLibraries.CheckTextReadable(libPath));
                    var libs = SeforimDbAttachedLibraries.Resolve(entries);
                    if (libs.Count != 1) { Fail("Resolve did not return the library"); continue; }
                    var lib = libs[0];

                    // ── map round trip ───────────────────────────────────────────────────────
                    var map = lib.LineIds!;
                    bool roundTrip = true;
                    var expectedLocal = 0;
                    foreach (var book in Books.OrderBy(b => b.Id))
                        for (int i = 0; i < book.Lines.Length; i++)
                        {
                            expectedLocal++;
                            long stored = StoredId(book.Id, i);
                            if (map.ToLocal(stored) != expectedLocal || map.ToStored(expectedLocal) != stored) roundTrip = false;
                        }
                    Check(roundTrip, "stored <-> local ids round trip for every line (including ids above int32)", "map round trip is wrong");
                    Check(map.ToLocal(5) == 0 && map.ToLocal(StoredId(1, 3)) == 0 && map.ToLocal(StoredId(3, 0)) == 0 && map.ToStored(0) == -1 && map.ToStored(10) == -1,
                        "an id that is not a line of the library maps to nothing", "a foreign id was accepted by the map");

                    // ── queries ──────────────────────────────────────────────────────────────
                    var q = new SeforimDbQueries(primary, entries);
                    var books = q.GetAllBooks();
                    var far = books.Single(b => b.Title == "ספר רחוק");
                    var second = books.Single(b => b.Title == "ספר שני");
                    var linesFar = q.GetLinesPaged(far.Id, 10, 0);
                    Check(linesFar.Count == 4 && linesFar.Select(l => l.Content).SequenceEqual(Books[2].Lines),
                        "GetLinesPaged returns the far book's lines in order", "far book lines: " + string.Join("|", linesFar.Select(l => l.Content)));
                    Check(linesFar.All(l => l.Id > 1 * SeforimDbLibraryId.OffsetUnit && l.Id < 2 * SeforimDbLibraryId.OffsetUnit)
                          && linesFar.Select(l => l.Id).Distinct().Count() == 4,
                        "line ids handed out are GLOBAL ids inside the library's band (never the stored 5-billion id)",
                        "line ids: " + string.Join(",", linesFar.Select(l => l.Id)));
                    var contents = q.GetLineContents(linesFar.Select(l => l.Id).ToList());
                    Check(contents.Count == 4 && linesFar.All(l => contents.Any(c => c.Id == l.Id && c.Content == l.Content)),
                        "GetLineContents answers for those global ids", "contents: " + string.Join("|", contents.Select(c => c.Id + "=" + c.Content)));
                    var bookIds = q.GetBookIdsForLines(linesFar.Select(l => l.Id).ToList());
                    Check(bookIds.Count == 4 && bookIds.All(b => b.BookId == far.Id),
                        "GetBookIdsForLines maps each line back to its book", "book ids: " + string.Join(",", bookIds.Select(b => b.LineId + "->" + b.BookId)));
                    var toc = q.GetTocPathsForLines(linesFar.Select(l => l.Id).ToList());
                    Check(toc.Lines.Count == 2 && toc.Paths.Count == 2 && toc.Paths.Any(p => p.Path.Contains("פרק מרוחק") && p.Path.Contains("תת פרק מרוחק"))
                          && toc.Lines.All(l => l.LineId == linesFar[2].Id || l.LineId == linesFar[3].Id)
                          && toc.Lines.All(l => l.TocEntryId > SeforimDbLibraryId.OffsetUnit && l.TocEntryId < 2 * SeforimDbLibraryId.OffsetUnit),
                        "GetTocPathsForLines resolves the far book's TOC entries (parent and child) for its two lines, with local TOC ids (line_toc, parentId and tocEntry.id hold stored ids)",
                        "toc: lines=" + string.Join(",", toc.Lines.Select(l => l.LineId)) + " paths=" + string.Join("|", toc.Paths.Select(p => p.Path)));
                    var idx = q.GetLineIndexFromLineId(linesFar[3].Id);
                    Check(idx.Count == 1 && idx[0].LineIndex == 3 && idx[0].BookId == far.Id,
                        "GetLineIndexFromLineId finds line 3 of the far book", "line index: " + string.Join(",", idx.Select(i => i.LineIndex + "@" + i.BookId)));
                    // Navigation inside a book of the additional library: its TOC tree, jump to a chapter by title,
                    // and a line by its index - all answered from THAT library, with global ids.
                    var tocAll = q.GetAllTocEntries(far.Id);
                    Check(tocAll.Count == 2 && tocAll.Any(t => t.Text.Contains("תת פרק מרוחק") && t.LineId == linesFar[3].Id && t.LineIndex == 3 && t.ParentId is int)
                          && tocAll.All(t => t.Id > SeforimDbLibraryId.OffsetUnit && t.Id < 2 * SeforimDbLibraryId.OffsetUnit)
                          && tocAll.Any(t => t.ParentId is null && t.LineId == linesFar[2].Id),
                        "GetAllTocEntries returns the far book's TOC tree with global entry and line ids",
                        "toc entries: " + string.Join("|", tocAll.Select(t => t.Id + ":" + t.Text + "@" + t.LineId + "<" + t.ParentId)));
                    var byPrefix = q.GetTocEntryByTextPrefix(far.Id, "תת פרק%");
                    Check(byPrefix.Count == 1 && byPrefix[0].LineIndex == 3 && byPrefix[0].Id == tocAll.First(t => t.Text.Contains("תת פרק מרוחק")).Id,
                        "GetTocEntryByTextPrefix finds a chapter of the far book by its title", "by prefix: " + string.Join("|", byPrefix.Select(t => t.Id + "@" + t.LineIndex)));
                    var byIndex = q.GetLineByBookAndLineIndex(far.Id, 2);
                    Check(byIndex.Count == 1 && byIndex[0].Id == linesFar[2].Id && byIndex[0].Content == "מדבר שומם",
                        "GetLineByBookAndLineIndex returns line 2 of the far book with its global id", "by index: " + string.Join("|", byIndex.Select(l => l.Id + "=" + l.Content)));
                    var linesSecond = q.GetLinesPaged(second.Id, 10, 0);
                    Check(linesSecond.Count == 2 && linesSecond[0].Content == "נהר גדול",
                        "another book of the same library reads correctly", "second book: " + string.Join("|", linesSecond.Select(l => l.Content)));
                    Check(q.GetLineContents(new List<int> { SeforimDbLibraryId.ToGlobal(1, 999) }).Count == 0,
                        "a local id outside the library returns no row (and does not throw)", "stale id returned a row");

                    // ── catalog index ────────────────────────────────────────────────────────
                    var skipped = new List<string>();
                    using (var cat = new SeforimDbCatalogIndex(Path.Combine(dir, "Cat-" + label), primary, libs) { SourceSkipped = (p, e) => skipped.Add(p + ": " + e.Message) })
                    {
                        cat.BuildAndSwitch(SeforimDbCatalogIndex.ComputeDbHash(primary, libs));
                        Check(skipped.Count == 0, "catalog build does not skip the library", "catalog skipped: " + string.Join(" | ", skipped));
                        Check(cat.Search("ספר רחוק", CancellationToken.None).Any(h => h.BookId == far.Id),
                            "the far book is found by title in the catalog index", "far book not found in the catalog");
                        Check(cat.Search("פרק מרוחק", CancellationToken.None).Any(h => h.BookId == far.Id && h.FullTocPath.Contains("פרק מרוחק")),
                            "its TOC entry is found in the catalog index (TOC rows reference stored line ids)", "TOC entry not found in the catalog");
                    }

                    // ── full-text corpus ─────────────────────────────────────────────────────
                    using (var corpus = new CompositeFtsCorpus(primary, libs))
                    {
                        var all = corpus.ReadDocumentsInResultOrder(0).ToList();
                        Check(corpus.CountDocuments() == 10 && all.Count == 10,
                            "the full-text corpus counts and reads primary + 9 library lines", $"corpus count {corpus.CountDocuments()}, read {all.Count}");
                        Check(all.Select(a => a.Id).Distinct().Count() == 10 && all.All(a => a.Id > 0),
                            "every document id is a distinct, valid global id (no overflow)", "ids: " + string.Join(",", all.Select(a => a.Id)));
                        var placed = corpus.FetchPlacedDocuments(linesFar.Select(l => l.Id)).ToList();
                        Check(placed.Count == 4 && placed.All(p => p.Placement.BookId == far.Id) &&
                              placed.OrderBy(p => p.Placement.LineIndex).Select(p => p.Placement.LineIndex).SequenceEqual(new[] { 0, 1, 2, 3 }),
                            "FetchPlacedDocuments reports the right book and line index", "placed: " + string.Join(",", placed.Select(p => p.Placement.BookId + "/" + p.Placement.LineIndex)));
                        var snippets = corpus.FetchSnippetTexts(new[] { linesFar[0].Id }, new[] { linesFar[1].Id });
                        var snippet = snippets.FirstOrDefault(x => x.LineId == linesFar[1].Id);
                        Check(snippets.Any(x => x.LineId == linesFar[0].Id && x.Text == "ים סוער") &&
                              snippet != null && snippet.Text == "יער עבות" && snippet.Before == "ים סוער" && snippet.After == "מדבר שומם עמק פורח",
                            "FetchSnippetTexts returns a far-book line (and a short one with its neighbours) under global ids", "snippets: " + string.Join("|", snippets.Select(x => x.LineId + "=" + x.Before + "<" + x.Text + ">" + x.After)));
                        var ranges = corpus.BookPositionRanges();
                        Check(ranges.TryGetValue(far.Id, out var farRange) && farRange.Count == 4 && corpus.PositionOfLine(linesFar[2].Id, ranges) == farRange.First + 2,
                            "BookPositionRanges and PositionOfLine place a far-book line at the book's first position plus its rank", "ranges: " + string.Join(",", ranges.Select(r => r.Key + ":" + r.Value.First + "+" + r.Value.Count)));
                        var neighbours = corpus.FetchNeighbourText(new[] { linesFar[1].Id }, 1);
                        Check(neighbours.TryGetValue(linesFar[1].Id, out var n) && n.Previous == "ים סוער" && n.Next == "מדבר שומם",
                            "neighbour text of a far-book line is its previous and next line", "neighbours: " + string.Join(",", neighbours.Select(k => k.Key + ":" + k.Value.Previous + "/" + k.Value.Next)));
                        Check(corpus.GetDocumentText(linesFar[3].Id) == "עמק פורח", "GetDocumentText reads one line by its local id", "GetDocumentText: " + corpus.GetDocumentText(linesFar[3].Id));
                        long upTo = corpus.CountDocumentsUpTo(linesFar[0].Id);
                        Check(upTo == 1 + 5 || upTo == 6 || upTo > 0, "CountDocumentsUpTo does not throw on a translated id", "count up to: " + upTo);
                    }

                    // ── a real full-text search ──────────────────────────────────────────────
                    string fidx = Path.Combine(dir, "Fts-" + label);
                    var indexer = new SeforimDbFtsIndexer(fidx, primary, libs);
                    using var engine = new SeforimDbFtsEngine(fidx, primary, libs);
                    Check(indexer.Build(engine), "the FTS index builds with the library configured", "FTS build returned false");
                    var searcher = new SeforimDbFtsSearcher();
                    var hits = searcher.SearchInBatches(engine, "עמק", 0, false, 6, new FtsTermTable()).SelectMany(b => b.Hits).ToList();
                    Check(hits.Count == 1 && hits[0].BookId == far.Id && hits[0].LineId == linesFar[3].Id,
                        "a search for a word in the far book finds exactly that line, with the right book and the local line id",
                        "hits: " + string.Join(" | ", hits.Select(h => (h.LineId, h.BookId))) + " expected line " + linesFar[3].Id + " book " + far.Id);
                    engine.Dispose();
                }

                // ── layouts the map must refuse ──────────────────────────────────────────────
                Console.WriteLine("Layouts that cannot be translated are still rejected:");
                {
                    string gap = Path.Combine(dir, "gap.db");
                    BuildBigIdLibrary(gap, HardeningVerify.Shape.Plain);
                    using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = gap }.ConnectionString))
                    {
                        c.Open();
                        HardeningVerify.Exec(c, $"UPDATE line SET id = id + 7 WHERE bookId = 5000 AND lineIndex = 3");   // a hole inside the book
                    }
                    SqliteConnection.ClearAllPools();
                    var st = SeforimDbAttachedLibraries.ResolveWithStatus(new[] { new SeforimDbAdditionalLibraries.Entry(1, gap) }).Single();
                    Check(st.Rejection is { } r1 && r1.StartsWith("ids in table 'line'") && r1.Contains("not consecutive"),
                        "line ids with a hole inside a book are rejected with a reason", "gap library: " + (st.Rejection ?? "ACCEPTED"));

                    // A book that HAS lines but none with lineIndex 0 must not be silently left out of the map.
                    string noZero = Path.Combine(dir, "nozero.db");
                    BuildBigIdLibrary(noZero, HardeningVerify.Shape.Plain);
                    using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = noZero }.ConnectionString))
                    {
                        c.Open();
                        HardeningVerify.Exec(c, "UPDATE line SET lineIndex = lineIndex + 1 WHERE bookId = 2");
                    }
                    SqliteConnection.ClearAllPools();
                    var st4 = SeforimDbAttachedLibraries.ResolveWithStatus(new[] { new SeforimDbAdditionalLibraries.Entry(1, noZero) }).Single();
                    Check(st4.Rejection is { } r4 && r4.StartsWith("ids in table 'line'") && r4.Contains("lineIndex 0"),
                        "a book with lines but no lineIndex 0 is rejected with a reason, not silently dropped from the map", "no-zero library: " + (st4.Rejection ?? "ACCEPTED"));

                    // The stamp of a translated library carries the layout signature, and it changes with the layout.
                    string stampA = SeforimDbContentFingerprint.ComputeAll(primary, SeforimDbAttachedLibraries.Resolve(new[] { new SeforimDbAdditionalLibraries.Entry(1, Path.Combine(dir, "big-plain.db")) }));
                    Check(stampA.Contains("|ids="), "the fingerprint of a translated library includes its id-layout signature", "stamp: " + stampA);

                    string ordinary = Path.Combine(dir, "ordinary.db");
                    HardeningVerify.BuildLibrary(ordinary, "רגיל", "ספר רגיל", "שורה רגילה", "פרק", HardeningVerify.Shape.Plain, altToc: false, regularToc: true);
                    var st3 = SeforimDbAttachedLibraries.ResolveWithStatus(new[] { new SeforimDbAdditionalLibraries.Entry(1, ordinary) }).Single();
                    Check(st3.Rejection is null && st3.Library is { LineIds: null },
                        "an ordinary library (small ids) is untouched: attached, no translation", "ordinary library: " + (st3.Rejection ?? "map present"));
                }
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

            Console.WriteLine(failures == 0 ? "ALL BIG-LINE-ID CHECKS PASSED" : $"{failures} BIG-LINE-ID CHECK(S) FAILED");
            return failures;
        }

        /// <summary>Three books with consecutive line ids in the published database's scheme
        /// (id = bookId * 1,048,576 + lineIndex + 1), a regular TOC referencing stored line ids, and
        /// line text either in line.content or split into line_content (zstd with a dictionary).</summary>
        private static void BuildBigIdLibrary(string path, HardeningVerify.Shape shape)
        {
            using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ConnectionString);
            conn.Open();
            bool split = shape != HardeningVerify.Shape.Plain;
            HardeningVerify.Exec(conn, @"
                CREATE TABLE category(id INTEGER PRIMARY KEY, parentId INTEGER, title TEXT, level INTEGER);
                CREATE TABLE book(id INTEGER PRIMARY KEY, categoryId INTEGER, title TEXT, orderIndex INTEGER,
                    hasTeamim INTEGER, totalLines INTEGER);
                CREATE TABLE author(id INTEGER PRIMARY KEY, name TEXT);
                CREATE TABLE book_author(bookId INTEGER, authorId INTEGER);
                CREATE TABLE tocText(id INTEGER PRIMARY KEY, text TEXT);
                CREATE TABLE tocEntry(id INTEGER PRIMARY KEY, bookId INTEGER, parentId INTEGER, textId INTEGER,
                    lineId INTEGER, level INTEGER, hasChildren INTEGER);
                CREATE TABLE line_toc(lineId INTEGER, tocEntryId INTEGER);");
            HardeningVerify.Exec(conn, split
                ? "CREATE TABLE line(id INTEGER PRIMARY KEY, bookId INTEGER, lineIndex INTEGER); CREATE INDEX idx_line_book_index ON line(bookId, lineIndex); CREATE TABLE line_content(id INTEGER PRIMARY KEY, content)"
                : "CREATE TABLE line(id INTEGER PRIMARY KEY, bookId INTEGER, lineIndex INTEGER, content TEXT); CREATE INDEX idx_line_book_index ON line(bookId, lineIndex)");
            HardeningVerify.Exec(conn, "INSERT INTO category VALUES (1, NULL, 'ספרייה גדולה', 0)");

            byte[]? dict = null;
            ZstdSharp.Compressor? compressor = null;
            if (shape == HardeningVerify.Shape.SplitCompressed)
            {
                var (d, firstFrame) = HardeningVerify.Compress(Books[0].Lines[0], 7);
                dict = d;
                compressor = new ZstdSharp.Compressor(3);
                compressor.LoadDictionary(d);
                HardeningVerify.Exec(conn, "CREATE TABLE zstd_dict(id INTEGER PRIMARY KEY, dict BLOB)");
                using var dc = conn.CreateCommand();
                dc.CommandText = "INSERT INTO zstd_dict VALUES (1, $d)";
                dc.Parameters.AddWithValue("$d", dict);
                dc.ExecuteNonQuery();
            }

            using var tx = conn.BeginTransaction();
            foreach (var (id, title, lines) in Books)
            {
                using (var b = conn.CreateCommand())
                {
                    b.Transaction = tx;
                    b.CommandText = "INSERT INTO book VALUES ($id, 1, $t, $o, 0, $n)";
                    b.Parameters.AddWithValue("$id", id); b.Parameters.AddWithValue("$t", title);
                    b.Parameters.AddWithValue("$o", id); b.Parameters.AddWithValue("$n", lines.Length);
                    b.ExecuteNonQuery();
                }
                for (int i = 0; i < lines.Length; i++)
                {
                    using var l = conn.CreateCommand();
                    l.Transaction = tx;
                    long stored = StoredId(id, i);
                    if (!split)
                    {
                        l.CommandText = "INSERT INTO line VALUES ($id, $b, $i, $c)";
                        l.Parameters.AddWithValue("$c", lines[i]);
                    }
                    else
                    {
                        l.CommandText = "INSERT INTO line VALUES ($id, $b, $i); INSERT INTO line_content VALUES ($id, $c)";
                        l.Parameters.AddWithValue("$c", compressor != null ? compressor.Wrap(Encoding.UTF8.GetBytes(lines[i])).ToArray() : Encoding.UTF8.GetBytes(lines[i]));
                    }
                    l.Parameters.AddWithValue("$id", stored); l.Parameters.AddWithValue("$b", id); l.Parameters.AddWithValue("$i", i);
                    l.ExecuteNonQuery();
                }
            }
            // TOC: book 2 -> entry 2 over lines 0..1, book 5000 -> entry 1 over lines 2..3 (stored ids everywhere).
            foreach (var sql in new[]
            {
                "INSERT INTO tocText VALUES (1, 'פרק מרוחק'), (2, 'תת פרק מרוחק'), (3, 'פרק שני')",
                // Two entries in book 5000 (a root and a child) so parent ids are exercised too.
                $"INSERT INTO tocEntry VALUES ({TocId(5000, 0)}, 5000, NULL, 1, {StoredId(5000, 2)}, 1, 1), ({TocId(5000, 1)}, 5000, {TocId(5000, 0)}, 2, {StoredId(5000, 3)}, 2, 0), ({TocId(2, 0)}, 2, NULL, 3, {StoredId(2, 0)}, 1, 0)",
                $"INSERT INTO line_toc VALUES ({StoredId(5000, 2)}, {TocId(5000, 0)}), ({StoredId(5000, 3)}, {TocId(5000, 1)}), ({StoredId(2, 0)}, {TocId(2, 0)}), ({StoredId(2, 1)}, {TocId(2, 0)})",
            })
            {
                using var c = conn.CreateCommand(); c.Transaction = tx; c.CommandText = sql; c.ExecuteNonQuery();
            }
            tx.Commit();
            compressor?.Dispose();
            SqliteConnection.ClearAllPools();
        }
    }
}
