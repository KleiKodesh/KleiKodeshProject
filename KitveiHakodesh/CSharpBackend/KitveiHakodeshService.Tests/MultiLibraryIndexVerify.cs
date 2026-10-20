// Ad-hoc live verification that TYPED SEARCH (catalog TOC search + full-text search) actually
// covers an attached additional library, not just the browse/catalog RPCs
// (getAllCategories/getAllBooks/getBookById/getLinesPaged, already covered by
// MultiLibraryVerify.cs). This is the gap a real user hit: a 7055-book personal library attached
// via SeforimDbAdditionalLibraries, where neither search box found anything from it, even though
// browsing the category tree worked fine.
//
// Covers:
//   1. SeforimDbQueries.GetBookIdsForLines / GetTocPathsForLines / GetLineIndexFromLineId resolve
//      a GLOBAL line id from an attached library to its real book/TOC path — the enrichment step
//      every full-text hit goes through (SeforimDbFtsSearcher.EnrichHits). Before this change
//      these three only ever queried the primary database's own `line` table, so a hit from an
//      attached library enriched to BookId 0 / an empty TOC path.
//   2. CompositeFtsCorpus (the new multi-source IFtsCorpus/IFtsOrderedCorpus) counts, reads,
//      fetches and translates ids correctly across the primary database and an attached library,
//      concatenated in ascending library-index order with GLOBAL ids.
//   3. SeforimDbFtsIndexer + SeforimDbFtsEngine, wired with an additional library, actually BUILD
//      an index that finds a word that exists ONLY in the additional library's one book, and the
//      hit's BookId matches the SAME global id SeforimDbQueries.GetAllBooks() returns for that
//      book — i.e. a real search-to-navigation round trip, not just an index that builds.
//   4. SeforimDbCatalogIndex (the Lucene catalog TOC index), wired with an additional library,
//      finds that library's book BY TITLE, again with a BookId matching GetAllBooks().
//   5. SeforimDbContentFingerprint.ComputeAll changes when the SET of additional libraries
//      changes, even if every individual file's own content is untouched — this is what makes
//      RebuildIfDbChanged (on BOTH indexes) pick up an added/removed library, not just a changed
//      primary database.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Data.Sqlite;
using KitveiHakodesh.Core.SeforimDb;
using KitveiHakodesh.Core.Settings;
using KitveiHakodesh.Core.SeforimDbFullTextSearch;
using KitveiHakodesh.Core.SeforimDbCatalog;

namespace KitveiHakodeshService.Tests
{
    public static class MultiLibraryIndexVerify
    {
        public static int Run()
        {
            int failures = 0;
            void Fail(string msg) { failures++; Console.Error.WriteLine("  FAIL: " + msg); }
            void Ok(string msg) { Console.WriteLine("  OK: " + msg); }

            string dir = Path.Combine(Path.GetTempPath(), $"multilibindextest-{Environment.ProcessId}");
            Directory.CreateDirectory(dir);
            string primaryPath = Path.Combine(dir, "seforim.db");
            string additionalPath = Path.Combine(dir, "personal.db");

            try
            {
                BuildLibraryWithToc(primaryPath,
                    categoryTitle: "ספרי הראשי", bookTitle: "ספר ראשי התגלות",
                    lineText: "שלום עולם ראשי", tocText: "פרק א ראשי", bookId: 1);
                BuildLibraryWithToc(additionalPath,
                    categoryTitle: "ספרי האישי", bookTitle: "ספר אישי מיוחד",
                    lineText: "שלום עולם מיוחד", tocText: "פרק א מיוחד", bookId: 1);

                var additionalEntries = new[] { new SeforimDbAdditionalLibraries.Entry(1, additionalPath) };
                var resolvedLibraries = SeforimDbAttachedLibraries.Resolve(additionalEntries);
                if (resolvedLibraries.Count != 1)
                {
                    Fail($"setup: additional library did not resolve/attach: {resolvedLibraries.Count}");
                    return failures;
                }

                var queries = new SeforimDbQueries(primaryPath, additionalEntries);
                var books = queries.GetAllBooks();
                var primaryBook = books.Single(b => b.LibraryLabel == null);
                var additionalBook = books.Single(b => b.LibraryLabel == "personal");

                // ── 1. GetBookIdsForLines / GetTocPathsForLines / GetLineIndexFromLineId ────
                {
                    // Line 1 of each library — global ids computed the same way ToGlobal does.
                    int primaryLineId = 1;
                    int additionalLineId = SeforimDbLibraryId.ToGlobal(1, 1);

                    var bookIds = queries.GetBookIdsForLines(new List<int> { primaryLineId, additionalLineId });
                    var byLine = bookIds.ToDictionary(r => r.LineId, r => r.BookId);
                    if (!byLine.TryGetValue(primaryLineId, out int pb) || pb != primaryBook.Id)
                        Fail($"GetBookIdsForLines: primary line resolved to bookId {(byLine.TryGetValue(primaryLineId, out var x) ? x : -1)}, expected {primaryBook.Id}");
                    else if (!byLine.TryGetValue(additionalLineId, out int ab) || ab != additionalBook.Id)
                        Fail($"GetBookIdsForLines: ADDITIONAL LIBRARY line resolved to bookId {(byLine.TryGetValue(additionalLineId, out var y) ? y : -1)}, expected {additionalBook.Id} "
                           + "— this is exactly the enrichment gap that left every FTS hit from an attached library with BookId 0");
                    else
                        Ok("GetBookIdsForLines resolves a line from BOTH the primary and an attached library to the correct GLOBAL bookId");

                    var tocPaths = queries.GetTocPathsForLines(new List<int> { primaryLineId, additionalLineId });
                    var tocByLine = tocPaths.ToDictionary(r => r.LineId);
                    if (!tocByLine.TryGetValue(additionalLineId, out var additionalToc) || additionalToc.BookId != additionalBook.Id
                        || !additionalToc.TocPath.Contains("מיוחד"))
                        Fail($"GetTocPathsForLines did not resolve the attached library's line to its own TOC path/book: "
                           + $"found={(tocByLine.TryGetValue(additionalLineId, out var f) ? f.TocPath + "/" + f.BookId : "<none>")}");
                    else
                        Ok($"GetTocPathsForLines resolves an attached library's line to ITS TOC path ('{additionalToc.TocPath}') and bookId ({additionalToc.BookId})");

                    var lineIndex = queries.GetLineIndexFromLineId(additionalLineId);
                    if (lineIndex.Count != 1 || lineIndex[0].BookId != additionalBook.Id)
                        Fail($"GetLineIndexFromLineId(attached library line) did not resolve to its bookId: {Dump(lineIndex)}");
                    else
                        Ok("GetLineIndexFromLineId resolves an attached library's line id to its global bookId (open-after-FTS navigation)");
                }

                // ── 2. CompositeFtsCorpus ────────────────────────────────────────────────
                {
                    using var corpus = new TestableCompositeFtsCorpus(primaryPath, resolvedLibraries);

                    long totalDocs = corpus.CountDocuments();
                    if (totalDocs != 2)
                        Fail($"CompositeFtsCorpus.CountDocuments(): expected 2 (one line per library), got {totalDocs}");
                    else
                        Ok("CompositeFtsCorpus.CountDocuments() sums across the primary and the attached library");

                    var ordered = corpus.ReadDocumentsInResultOrder(0).ToList();
                    int additionalGlobalLineId = SeforimDbLibraryId.ToGlobal(1, 1);
                    if (ordered.Count != 2 || ordered[0].Id != 1 || ordered[1].Id != additionalGlobalLineId)
                        Fail($"CompositeFtsCorpus.ReadDocumentsInResultOrder: expected [1, {additionalGlobalLineId}] in order, got [{string.Join(",", ordered.Select(o => o.Id))}]");
                    else if (!ordered[1].Text.Contains("מיוחד"))
                        Fail($"CompositeFtsCorpus.ReadDocumentsInResultOrder returned wrong text for the attached library's line: '{ordered[1].Text}'");
                    else
                        Ok("CompositeFtsCorpus.ReadDocumentsInResultOrder concatenates primary-then-library-1 with correct GLOBAL ids and text");

                    // Resuming from skip=1 (as if the primary's one line was already indexed)
                    // must continue into the additional library, not repeat or skip it.
                    var resumed = corpus.ReadDocumentsInResultOrder(1).ToList();
                    if (resumed.Count != 1 || resumed[0].Id != additionalGlobalLineId)
                        Fail($"CompositeFtsCorpus.ReadDocumentsInResultOrder(skip=1) did not resume correctly into the additional library: {string.Join(",", resumed.Select(r => r.Id))}");
                    else
                        Ok("CompositeFtsCorpus.ReadDocumentsInResultOrder(skip=N) resumes correctly across the library boundary");

                    var fetched = corpus.FetchDocuments(new[] { 1, additionalGlobalLineId }).ToDictionary(r => r.Id);
                    if (!fetched.TryGetValue(additionalGlobalLineId, out var fetchedRow) || !fetchedRow.Text.Contains("מיוחד"))
                        Fail("CompositeFtsCorpus.FetchDocuments did not translate the attached library's id/content correctly");
                    else
                        Ok("CompositeFtsCorpus.FetchDocuments resolves a GLOBAL id from the attached library back to its own content");

                    string direct = corpus.GetDocumentText(additionalGlobalLineId);
                    if (direct is null || !direct.Contains("מיוחד"))
                        Fail($"CompositeFtsCorpus.GetDocumentText(attached library global id) returned '{direct}'");
                    else
                        Ok("CompositeFtsCorpus.GetDocumentText resolves a GLOBAL id from the attached library");
                }

                // ── 3. Full build + search round trip (SeforimDbFtsIndexer + SeforimDbFtsEngine) ──
                {
                    string ftsIndexPath = Path.Combine(dir, "FtsIndex");
                    var indexer = new SeforimDbFtsIndexer(ftsIndexPath, primaryPath, resolvedLibraries);
                    using var engine = new SeforimDbFtsEngine(ftsIndexPath, primaryPath, resolvedLibraries);

                    bool built = indexer.Build(engine);
                    if (!built)
                    {
                        Fail("SeforimDbFtsIndexer.Build with an additional library returned false (expected a completed build)");
                    }
                    else
                    {
                        Ok("SeforimDbFtsIndexer.Build completes with an additional library configured");

                        var searcher = new SeforimDbFtsSearcher(new SeforimDbQueries(primaryPath, additionalEntries));
                        var hits = searcher.SearchInBatches(engine, "מיוחד", maxWordDistance: 0, requireOrdered: false,
                            contextWords: 6, expandKetiv: false).SelectMany(b => b).ToList();

                        if (hits.Count == 0)
                        {
                            Fail("FTS search for a word that exists ONLY in the attached library's book found NOTHING — "
                               + "this is the exact bug reported live: 'אני רוצה את כל הספרים שלי באינדקס'");
                        }
                        else if (hits.Any(h => h.BookId == additionalBook.Id))
                        {
                            Ok($"FTS search finds the attached library's content, with BookId ({additionalBook.Id}) matching GetAllBooks() exactly — navigation from the hit resolves to the right book");
                        }
                        else
                        {
                            Fail($"FTS search found a hit for the attached library's word, but its BookId did not match GetAllBooks(): {Dump(hits.Select(h => (h.LineId, h.BookId)))}, expected bookId {additionalBook.Id}");
                        }

                        var primaryHits = searcher.SearchInBatches(engine, "ראשי", maxWordDistance: 0, requireOrdered: false,
                            contextWords: 6, expandKetiv: false).SelectMany(b => b).ToList();
                        if (primaryHits.Count == 0 || !primaryHits.Any(h => h.BookId == primaryBook.Id))
                            Fail("FTS search for the PRIMARY database's own word regressed once an additional library was configured");
                        else
                            Ok("FTS search still finds the primary database's own content correctly alongside an additional library");
                    }
                }

                // ── 4. Catalog TOC (Lucene) build + search with an additional library ────
                {
                    string catalogIndexPath = Path.Combine(dir, "CatalogTocIndex");
                    using var catalogIndex = new SeforimDbCatalogIndex(catalogIndexPath, primaryPath, resolvedLibraries);
                    string hash = SeforimDbCatalogIndex.ComputeDbHash(primaryPath, resolvedLibraries);
                    int docCount = catalogIndex.BuildAndSwitch(hash);
                    if (docCount == 0)
                    {
                        Fail("SeforimDbCatalogIndex.BuildAndSwitch with an additional library produced 0 documents");
                    }
                    else
                    {
                        var hits = catalogIndex.Search("מיוחד", CancellationToken.None);
                        if (hits.Count == 0)
                        {
                            Fail("Catalog TOC search for the attached library's book title found NOTHING — the same gap as the FTS one, for typed book-title search");
                        }
                        else if (hits.Any(h => h.BookId == additionalBook.Id))
                        {
                            Ok($"Catalog TOC search finds the attached library's book by title, with BookId ({additionalBook.Id}) matching GetAllBooks() exactly");
                        }
                        else
                        {
                            Fail($"Catalog TOC search found a hit for the attached library's title, but BookId did not match GetAllBooks(): {Dump(hits.Select(h => (h.FullTocPath, h.BookId)))}, expected {additionalBook.Id}");
                        }

                        var primaryHits = catalogIndex.Search("ראשי", CancellationToken.None);
                        if (primaryHits.Count == 0 || !primaryHits.Any(h => h.BookId == primaryBook.Id))
                            Fail("Catalog TOC search for the PRIMARY database's own book regressed once an additional library was configured");
                        else
                            Ok("Catalog TOC search still finds the primary database's own book correctly alongside an additional library");
                    }
                }

                // ── 5. The combined content fingerprint reacts to the LIBRARY SET changing ──
                {
                    string withNone = SeforimDbContentFingerprint.ComputeAll(primaryPath, Array.Empty<SeforimDbAttachedLibraries.Library>(), "x");
                    string withOne = SeforimDbContentFingerprint.ComputeAll(primaryPath, resolvedLibraries, "x");
                    if (withNone == withOne)
                        Fail("SeforimDbContentFingerprint.ComputeAll did not change when an additional library was added — "
                           + "RebuildIfDbChanged would never notice a newly-attached library on either index");
                    else
                        Ok("SeforimDbContentFingerprint.ComputeAll changes when the additional-library SET changes, "
                         + "not only when a source's own content changes — both indexes rebuild to cover it");

                    string withOneAgain = SeforimDbContentFingerprint.ComputeAll(primaryPath, resolvedLibraries, "x");
                    if (withOne != withOneAgain)
                        Fail("SeforimDbContentFingerprint.ComputeAll is not stable for the SAME library set — would rebuild forever");
                    else
                        Ok("SeforimDbContentFingerprint.ComputeAll is stable for an unchanged library set");
                }

                // ── 6. End-to-end: SeforimDbFtsIndexer.IsIndexStale() actually flips when the
                //      library SET changes under an index directory that was built WITHOUT the
                //      additional library — the real mechanism RebuildIfDbChanged depends on,
                //      not just the fingerprint function in isolation (section 5 above). ───────
                {
                    string ftsIndexPath2 = Path.Combine(dir, "FtsIndex2");
                    var indexerNoLib = new SeforimDbFtsIndexer(ftsIndexPath2, primaryPath);
                    using (var engineNoLib = new SeforimDbFtsEngine(ftsIndexPath2, primaryPath))
                        indexerNoLib.Build(engineNoLib);

                    if (indexerNoLib.IsIndexStale())
                    {
                        Fail("a just-built single-library index reports itself stale with no changes — setup broken, cannot verify the library-set-change detection");
                    }
                    else
                    {
                        var indexerWithLib = new SeforimDbFtsIndexer(ftsIndexPath2, primaryPath, resolvedLibraries);
                        if (!indexerWithLib.IsIndexStale())
                            Fail("SeforimDbFtsIndexer.IsIndexStale() did NOT detect that an additional library was added since the index was built — "
                               + "RebuildIfDbChanged would never pick up the new library's content");
                        else
                            Ok("SeforimDbFtsIndexer.IsIndexStale() detects a library added since the index was built, at the real mechanism level (not just the fingerprint function)");
                    }
                }
            }
            catch (Exception ex)
            {
                Fail("unhandled exception: " + ex);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
            }

            return failures;
        }

        /// <summary>Exposes CompositeFtsCorpus (internal to KitveiHakodesh.Core) to this test
        /// project via InternalsVisibleTo, with the IDisposable/IEnumerable surface a test needs
        /// without re-enumerating lazy sequences twice (ReadDocuments* are iterator methods).</summary>
        private sealed class TestableCompositeFtsCorpus : IDisposable
        {
            private readonly CompositeFtsCorpus _inner;
            public TestableCompositeFtsCorpus(string primaryPath, IReadOnlyList<SeforimDbAttachedLibraries.Library> additionalLibraries)
                => _inner = new CompositeFtsCorpus(primaryPath, additionalLibraries);
            public long CountDocuments() => _inner.CountDocuments();
            public List<(int Id, string Text)> ReadDocumentsInResultOrder(long skip) => _inner.ReadDocumentsInResultOrder(skip).ToList();
            public List<(int Id, string Text, string Title)> FetchDocuments(IEnumerable<int> ids) => _inner.FetchDocuments(ids).ToList();
            public string GetDocumentText(int id) => _inner.GetDocumentText(id);
            public void Dispose() => _inner.Dispose();
        }

        /// <summary>A minimal seforim-shaped database with the extra tables the catalog TOC
        /// indexer needs (tocEntry/tocText/line_toc/alt_toc_structure/alt_toc_entry) in addition
        /// to the plain book/category/line/author shape MultiLibraryVerify's BuildLibrary uses —
        /// one book, one category, one line, one TOC entry over that line.</summary>
        private static void BuildLibraryWithToc(string dbPath, string categoryTitle, string bookTitle, string lineText, string tocText, int bookId)
        {
            using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString);
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE category(id INTEGER PRIMARY KEY, parentId INTEGER, title TEXT, level INTEGER);
                    CREATE TABLE book(id INTEGER PRIMARY KEY, categoryId INTEGER, title TEXT, orderIndex INTEGER,
                        hasTeamim INTEGER, totalLines INTEGER, hasTargumConnection INTEGER, hasReferenceConnection INTEGER,
                        hasSourceConnection INTEGER, hasCommentaryConnection INTEGER, hasOtherConnection INTEGER);
                    CREATE TABLE author(id INTEGER PRIMARY KEY, name TEXT);
                    CREATE TABLE book_author(bookId INTEGER, authorId INTEGER);
                    CREATE TABLE line(id INTEGER PRIMARY KEY, bookId INTEGER, lineIndex INTEGER, content TEXT);
                    CREATE TABLE tocText(id INTEGER PRIMARY KEY, text TEXT);
                    CREATE TABLE tocEntry(id INTEGER PRIMARY KEY, bookId INTEGER, parentId INTEGER, textId INTEGER,
                        lineId INTEGER, level INTEGER, hasChildren INTEGER);
                    CREATE TABLE line_toc(lineId INTEGER, tocEntryId INTEGER);
                    CREATE TABLE alt_toc_structure(id INTEGER PRIMARY KEY, bookId INTEGER, title TEXT, heTitle TEXT);
                    CREATE TABLE alt_toc_entry(id INTEGER PRIMARY KEY, structureId INTEGER, parentId INTEGER, textId INTEGER, lineId INTEGER);
                ";
                cmd.ExecuteNonQuery();
            }

            Exec(conn, $"INSERT INTO category(id, parentId, title, level) VALUES (1, NULL, '{categoryTitle}', 0)");
            Exec(conn, $@"INSERT INTO book(id, categoryId, title, orderIndex, hasTeamim, totalLines,
                hasTargumConnection, hasReferenceConnection, hasSourceConnection, hasCommentaryConnection, hasOtherConnection)
                VALUES ({bookId}, 1, '{bookTitle}', 0, 0, 1, 0, 0, 0, 0, 0)");
            Exec(conn, $"INSERT INTO line(id, bookId, lineIndex, content) VALUES (1, {bookId}, 0, '{lineText}')");
            Exec(conn, $"INSERT INTO tocText(id, text) VALUES (1, '{tocText}')");
            Exec(conn, $"INSERT INTO tocEntry(id, bookId, parentId, textId, lineId, level, hasChildren) VALUES (1, {bookId}, NULL, 1, 1, 1, 0)");
            Exec(conn, "INSERT INTO line_toc(lineId, tocEntryId) VALUES (1, 1)");
        }

        private static void Exec(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static string Dump<T>(IEnumerable<T> items) =>
            string.Join(" | ", items.Select(i => i!.ToString()));
    }
}
