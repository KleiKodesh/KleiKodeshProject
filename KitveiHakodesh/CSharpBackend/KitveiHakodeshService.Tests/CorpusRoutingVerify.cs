// Corpus routing verification — the regression harness for user_books.db support.
//
// The routed design's failure mode is SILENT WRONG ROWS (a personal-book id answered
// from the library, a type id passed verbatim between DBs), so every check here compares
// SeforimDbService against DIRECT SQL ground truth on the same databases:
//
//   A. LIBRARY PARITY  — library-id calls must equal direct library SQL exactly, and
//                        they run AFTER user-corpus calls on the same service instance,
//                        so a shared (non-per-corpus) schema probe or cache would fail.
//   B. SYNTHETIC USER  — a generated Otzaria-shaped DB with books/lines/links whose ids
//                        COLLIDE with library ids and whose connection_type ids DISAGREE
//                        with the library's (the realistic case) exercises id shifting,
//                        split-merge, name-keyed type translation, cross-corpus guards,
//                        and the merged LIMIT-50.
//   C. REAL USER DB    — the actual Otzaria user_books.db (catalog+TOC only, empty line
//                        table): catalog union, TOC via tocEntry.lineIndex, empty lines.
//
// The service's Run() swallows exceptions into logs — an exception would fake an empty
// (passing-looking) result — so the test logger fails the run on any LogError.
//
// Usage: dotnet run -- --corpus
using KitveiHakodeshService.SeforimDb;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace KitveiHakodeshService.Tests;

internal static class CorpusRoutingVerify
{
    private const int Base = CorpusIds.UserBooksBase;

    private static int _failures;
    private static readonly CapturingLogger Logger = new();

    public static int Run()
    {
        string libraryPath = SeforimDbLocator.Resolve();
        if (!File.Exists(libraryPath))
        {
            Console.Error.WriteLine($"seforim DB not found: {libraryPath}");
            return 2;
        }
        Console.WriteLine($"library: {libraryPath}");

        string synthPath = Path.Combine(Path.GetTempPath(), $"kh-corpus-synth-{Environment.ProcessId}.db");
        try
        {
            BuildSyntheticUserDb(synthPath);

            // ── A + B: service with the SYNTHETIC user DB ─────────────────────────
            // The registry candidate OUTRANKS the env override; if it's set, sections
            // A/B would silently run against the wrong DB and fail bafflingly.
            if (!string.IsNullOrWhiteSpace(UserBooksDbLocator.LoadRegistryPath()))
            {
                Console.Error.WriteLine(
                    $"setup: HKCU {UserBooksDbLocator.RegistryKeyPath}\\UserBooksPath is set and outranks " +
                    "USER_BOOKS_DB_PATH — clear it before running --corpus");
                return 2;
            }
            Environment.SetEnvironmentVariable("USER_BOOKS_DB_PATH", synthPath);
            var svc = new SeforimDbService(Logger);
            if (!svc.HasUserBooksDb) { Fail("setup: synthetic user DB not resolved"); return 1; }

            using var lib = OpenDirect(libraryPath);
            using var synth = OpenDirect(synthPath);

            // Deliberately poison-order: touch every user-corpus probe FIRST, so a
            // shared (non-per-corpus) probe cache would corrupt the library checks below.
            _ = svc.GetAllTocEntries(Base + 1);
            _ = svc.GetCommentaryLinksForSourceLineRange([Base + 1]);
            _ = svc.GetWordLinkAnchorsForLines([Base + 1]);

            CheckLibraryParity(svc, lib);
            CheckSyntheticRouting(svc, lib, synth);
            CheckFileBackedContent(svc, synthPath);

            // ── C: a fresh service with the REAL user_books.db, if present ────────
            // Clear the synthetic override FIRST, then let the locator's own candidate
            // list find the real DB — section C also exercises the locator itself.
            Environment.SetEnvironmentVariable("USER_BOOKS_DB_PATH", null);
            string? realUserDb = UserBooksDbLocator.Resolve(libraryPath);
            if (realUserDb is not null && File.Exists(realUserDb))
            {
                Console.WriteLine($"real user DB: {realUserDb}");
                var svc2 = new SeforimDbService(Logger);
                using var real = OpenDirect(realUserDb);
                CheckRealUserDb(svc2, lib, real);
            }
            else
            {
                Console.WriteLine("real user DB: not present — section C skipped");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("USER_BOOKS_DB_PATH", null);
            SqliteConnection.ClearAllPools();
            try { File.Delete(synthPath); } catch { }
        }

        if (Logger.Errors > 0)
            Fail($"service logged {Logger.Errors} error(s) — Run() swallowed exceptions (see output above)");

        Console.WriteLine();
        Console.WriteLine($"total failures: {_failures}");
        Console.WriteLine(_failures == 0 ? "CORPUS ROUTING: PASS" : "CORPUS ROUTING: FAIL");
        return _failures == 0 ? 0 : 1;
    }

    // ── A. Library parity: service (with user DB attached) == direct library SQL ──

    private static void CheckLibraryParity(SeforimDbService svc, SqliteConnection lib)
    {
        Console.WriteLine("\n=== A. library parity (after user-corpus poison-order calls) ===");

        // A book with commentary links and real content, discovered from the data.
        int bookId = ScalarInt(lib, @"SELECT id FROM book
            WHERE hasCommentaryConnection = 1 AND totalLines > 100 ORDER BY id LIMIT 1");
        var lineIds = Ints(lib, @"SELECT l.id FROM line l
            WHERE l.bookId = @b AND EXISTS(SELECT 1 FROM link k WHERE k.sourceLineId = l.id)
            ORDER BY l.lineIndex LIMIT 3", ("@b", bookId));
        Console.WriteLine($"  probe book: {bookId}, lines: [{string.Join(",", lineIds)}]");

        // GetBookById
        var info = svc.GetBookById(bookId)!;
        int totalLines = ScalarInt(lib, "SELECT totalLines FROM book WHERE id = @b", ("@b", bookId));
        Eq("GetBookById.totalLines", info.TotalLines, totalLines);

        // GetLinesPaged
        var paged = svc.GetLinesPaged(bookId, 25, 10);
        var pagedDirect = Rows(lib, "SELECT id, lineIndex, content FROM line WHERE bookId = @b ORDER BY lineIndex LIMIT 25 OFFSET 10",
            r => (r.GetInt32(0), r.GetInt32(1), r.GetString(2)), ("@b", bookId));
        Eq("GetLinesPaged.count", paged.Count, pagedDirect.Count);
        for (int i = 0; i < paged.Count; i++)
            if (paged[i].Id != pagedDirect[i].Item1 || paged[i].LineIndex != pagedDirect[i].Item2 || paged[i].Content != pagedDirect[i].Item3)
            { Fail($"GetLinesPaged row {i} differs"); break; }

        // GetAllTocEntries — library derives lineIndex via the line JOIN.
        var toc = svc.GetAllTocEntries(bookId);
        var tocDirect = Rows(lib, @"SELECT te.id, te.parentId, tt.text, l.lineIndex
            FROM tocEntry te JOIN tocText tt ON tt.id = te.textId
            LEFT JOIN line l ON l.id = te.lineId WHERE te.bookId = @b ORDER BY te.id",
            r => (r.GetInt32(0), r.IsDBNull(1) ? (int?)null : r.GetInt32(1), r.GetString(2), r.IsDBNull(3) ? (int?)null : r.GetInt32(3)),
            ("@b", bookId));
        Eq("GetAllTocEntries.count", toc.Count, tocDirect.Count);
        for (int i = 0; i < toc.Count; i++)
            if (toc[i].Id != tocDirect[i].Item1 || toc[i].ParentId != tocDirect[i].Item2
                || toc[i].Text != tocDirect[i].Item3 || toc[i].LineIndex != tocDirect[i].Item4)
            { Fail($"GetAllTocEntries row {i} differs (probe contamination?)"); break; }

        // GetCommentaryLinksForSourceLineRange — the targetLineIndex probe must have
        // stayed TRUE for the library even though the user corpus was probed first.
        var links = svc.GetCommentaryLinksForSourceLineRange(lineIds);
        var linksDirect = Rows(lib, $@"SELECT l.targetBookId, l.targetLineId, l.connectionTypeId, l.targetLineIndex
            FROM link l WHERE l.sourceLineId IN ({string.Join(",", lineIds)})",
            r => (r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.IsDBNull(3) ? 0 : r.GetInt32(3)));
        Eq("GetCommentaryLinks.count", links.Count, linksDirect.Count);
        for (int i = 0; i < links.Count; i++)
            if (links[i].TargetBookId != linksDirect[i].Item1 || links[i].TargetLineId != linksDirect[i].Item2
                || links[i].ConnectionTypeId != linksDirect[i].Item3 || links[i].LineIndex != linksDirect[i].Item4)
            { Fail($"GetCommentaryLinks row {i} differs"); break; }

        // GetAllConnectionTypes — synthetic names ⊂ library names ⇒ EXACTLY the library rows.
        var types = svc.GetAllConnectionTypes();
        var typesDirect = Rows(lib, "SELECT id, name FROM connection_type", r => (r.GetInt32(0), r.GetString(1)));
        Eq("GetAllConnectionTypes.count", types.Count, typesDirect.Count);
        for (int i = 0; i < types.Count; i++)
            if (types[i].Id != typesDirect[i].Item1 || types[i].Name != typesDirect[i].Item2)
            { Fail($"GetAllConnectionTypes row {i} differs"); break; }

        // Reverse lookups with library type ids.
        int commentaryType = ScalarInt(lib, "SELECT id FROM connection_type WHERE name = 'COMMENTARY'");
        int targumType = ScalarInt(lib, "SELECT id FROM connection_type WHERE name = 'TARGUM'");
        var filt = svc.GetStaticFilterBooks(bookId, [commentaryType, targumType]);
        var filtDirect = Rows(lib, $@"SELECT DISTINCT l.targetBookId, l.connectionTypeId FROM link l
            WHERE l.sourceBookId = @b AND l.connectionTypeId IN ({commentaryType},{targumType})",
            r => (r.GetInt32(0), r.GetInt32(1)), ("@b", bookId));
        Eq("GetStaticFilterBooks.count", filt.Count, filtDirect.Count);

        // Catalog union head — the library block must be the direct library rows verbatim.
        var books = svc.GetAllBooks();
        var booksDirect = Rows(lib, @"SELECT b.id, b.categoryId, b.title FROM book b
            LEFT JOIN book_author ba ON ba.bookId = b.id LEFT JOIN author a ON a.id = ba.authorId
            GROUP BY b.id ORDER BY b.orderIndex", r => (r.GetInt32(0), r.GetInt32(1), r.GetString(2)));
        if (books.Count < booksDirect.Count) Fail($"GetAllBooks: merged {books.Count} < library {booksDirect.Count}");
        else
        {
            for (int i = 0; i < booksDirect.Count; i++)
                if (books[i].Id != booksDirect[i].Item1 || books[i].CategoryId != booksDirect[i].Item2 || books[i].Title != booksDirect[i].Item3)
                { Fail($"GetAllBooks library block row {i} differs"); break; }
            for (int i = booksDirect.Count; i < books.Count; i++)
                if (books[i].Id < Base || books[i].CategoryId < Base)
                { Fail($"GetAllBooks appended row {i} not shifted (id={books[i].Id})"); break; }
            Console.WriteLine($"  GetAllBooks: {booksDirect.Count} library + {books.Count - booksDirect.Count} user rows OK");
        }

        var cats = svc.GetAllCategories();
        int libCats = ScalarInt(lib, "SELECT COUNT(*) FROM category");
        if (cats.Count < libCats) Fail($"GetAllCategories: merged {cats.Count} < library {libCats}");
        for (int i = libCats; i < cats.Count; i++)
            if (cats[i].Id < Base || (cats[i].ParentId is int pid && pid < Base))
            { Fail($"GetAllCategories appended row {i} not shifted"); break; }

        // Line→book / index helpers.
        var lineBook = svc.GetBookIdsForLines([lineIds[0]]);
        Eq("GetBookIdsForLines.bookId", lineBook.Count == 1 ? lineBook[0].BookId : -1, bookId);
        var lineIdx = svc.GetLineIndexFromLineId(lineIds[0]);
        int directIdx = ScalarInt(lib, "SELECT lineIndex FROM line WHERE id = @i", ("@i", lineIds[0]));
        Eq("GetLineIndexFromLineId", lineIdx.Count == 1 ? lineIdx[0].LineIndex : -1, directIdx);

        // TOC paths (recursive CTE) — count parity per requested line.
        var paths = svc.GetTocPathsForLines(lineIds);
        int pathsDirect = ScalarInt(lib, $@"SELECT COUNT(DISTINCT lt.lineId) FROM line_toc lt
            WHERE lt.lineId IN ({string.Join(",", lineIds)})");
        Eq("GetTocPathsForLines.count", paths.Count, pathsDirect);

        Console.WriteLine("  library parity: done");
    }

    // ── B. Synthetic user DB: routing, translation, guards ─────────────────────────

    private static void CheckSyntheticRouting(SeforimDbService svc, SqliteConnection lib, SqliteConnection synth)
    {
        Console.WriteLine("\n=== B. synthetic user DB routing ===");

        int libCommentary = ScalarInt(lib, "SELECT id FROM connection_type WHERE name = 'COMMENTARY'");
        int libTargum = ScalarInt(lib, "SELECT id FROM connection_type WHERE name = 'TARGUM'");
        int synCommentary = ScalarInt(synth, "SELECT id FROM connection_type WHERE name = 'COMMENTARY'");
        int synTargum = ScalarInt(synth, "SELECT id FROM connection_type WHERE name = 'TARGUM'");
        if (libCommentary == synCommentary || libTargum == synTargum)
            Console.WriteLine("  note: a synthetic type id coincides with the library's — translation still checked by name");

        // GetBookById routes to the user DB even though library book 1 exists too.
        var info = svc.GetBookById(Base + 1)!;
        Eq("user GetBookById.totalLines", info.TotalLines, ScalarInt(synth, "SELECT totalLines FROM book WHERE id = 1"));

        // Lines come from the user DB, ids shifted.
        var lines = svc.GetLinesPaged(Base + 1, 5, 0);
        Eq("user GetLinesPaged.count", lines.Count, 5);
        if (lines.Count > 0 && (lines[0].Id < Base || lines[0].Content != ScalarStr(synth, "SELECT content FROM line WHERE bookId=1 ORDER BY lineIndex LIMIT 1")))
            Fail("user GetLinesPaged returned wrong rows (library contamination?)");

        // TOC — synthetic tocEntry HAS lineIndex (Otzaria shape): direct-read variant.
        var toc = svc.GetAllTocEntries(Base + 1);
        int tocDirect = ScalarInt(synth, "SELECT COUNT(*) FROM tocEntry WHERE bookId = 1");
        Eq("user GetAllTocEntries.count", toc.Count, tocDirect);
        foreach (var t in toc)
        {
            if (t.Id < Base) { Fail("user TOC id not shifted"); break; }
            if (t.LineIndex is null) { Fail("user TOC lineIndex null — tocEntry.lineIndex variant not used"); break; }
        }

        // Commentary links from user lines: target ids shifted, type ids translated BY NAME.
        var links = svc.GetCommentaryLinksForSourceLineRange([Base + 1, Base + 2, Base + 3]);
        var linksDirect = Rows(synth, @"SELECT l.targetBookId, l.targetLineId, ct.name
            FROM link l JOIN connection_type ct ON ct.id = l.connectionTypeId
            WHERE l.sourceLineId IN (1,2,3)", r => (r.GetInt32(0), r.GetInt32(1), r.GetString(2)));
        Eq("user GetCommentaryLinks.count", links.Count, linksDirect.Count);
        foreach (var l in links)
        {
            if (l.TargetBookId < Base || l.TargetLineId < Base) { Fail("user link target not shifted"); break; }
            // The app-visible type id must be the LIBRARY id of the same name.
            string? name = l.ConnectionTypeId == libCommentary ? "COMMENTARY"
                : l.ConnectionTypeId == libTargum ? "TARGUM" : null;
            if (name is null) { Fail($"user link type {l.ConnectionTypeId} is not a library type id — name translation failed"); break; }
            if (!linksDirect.Exists(d => d.Item1 == l.TargetBookId - Base && d.Item2 == l.TargetLineId - Base && d.Item3 == name))
            { Fail($"user link ({l.TargetBookId},{l.TargetLineId},{name}) has no ground-truth counterpart"); break; }
        }

        // Inbound type translation: app COMMENTARY id → synthetic COMMENTARY id.
        var rev = svc.GetReverseBooks(Base + 2, [libCommentary]);
        Eq("user GetReverseBooks.count", rev.Count, 1);
        if (rev.Count == 1) Eq("user GetReverseBooks.sourceBookId", rev[0].SourceBookId, Base + 1);

        var filt = svc.GetStaticFilterBooks(Base + 1, [libCommentary, libTargum]);
        Eq("user GetStaticFilterBooks.count", filt.Count, 2);
        foreach (var f in filt)
            if (f.TargetBookId < Base || (f.ConnectionTypeId != libCommentary && f.ConnectionTypeId != libTargum))
            { Fail($"user static filter row wrong (book={f.TargetBookId}, type={f.ConnectionTypeId})"); break; }

        // Reverse line data: user target lines ← user source lines, types translated.
        var revLines = svc.GetReverseLineData(
            Ints(synth, "SELECT targetLineId FROM link WHERE connectionTypeId = @t", ("@t", synCommentary))
                .ConvertAll(id => id + Base),
            [libCommentary]);
        int revDirect = ScalarInt(synth, "SELECT COUNT(*) FROM link WHERE connectionTypeId = @t", ("@t", synCommentary));
        Eq("user GetReverseLineData.count", revLines.Count, revDirect);
        foreach (var r in revLines)
            if (r.SourceBookId < Base || r.SourceLineId < Base) { Fail("user reverse line ids not shifted"); break; }

        // Split-merge: a MIXED line-id list returns both corpora's rows.
        int libLineId = ScalarInt(lib, "SELECT id FROM line ORDER BY id LIMIT 1");
        var mixed = svc.GetLineContents([libLineId, Base + 1]);
        Eq("mixed GetLineContents.count", mixed.Count, 2);
        bool haveLib = false, haveUser = false;
        foreach (var m in mixed)
        {
            if (m.Id == libLineId) haveLib = true;
            if (m.Id == Base + 1) haveUser = true;
        }
        if (!haveLib || !haveUser) Fail($"mixed GetLineContents missing a corpus (lib={haveLib}, user={haveUser})");

        // Cross-corpus guards return empty rather than nonsense.
        int anyLibBook = ScalarInt(lib, "SELECT id FROM book ORDER BY id LIMIT 1");
        Eq("cross-corpus GetSectionWithCommentary", svc.GetSectionWithCommentary(anyLibBook, Base + 2, 0, next: true).Count, 0);
        Eq("cross-corpus GetLinkTargetForSourceLineAndBook", svc.GetLinkTargetForSourceLineAndBook(libLineId, Base + 2).Count, 0);

        // Same-corpus section nav on the user DB.
        var nav = svc.GetSectionWithCommentary(Base + 1, Base + 2, -1, next: true);
        Eq("user GetSectionWithCommentary.count", nav.Count, 1);
        if (nav.Count == 1 && nav[0].Id < Base) Fail("user section nav id not shifted");

        var linkTarget = svc.GetLinkTargetForSourceLineAndBook(Base + 1, Base + 2);
        Eq("user GetLinkTargetForSourceLineAndBook.count", linkTarget.Count, 1);
        if (linkTarget.Count == 1 && linkTarget[0].TargetLineId < Base) Fail("user link target id not shifted");

        // TOC paths for user lines (synthetic HAS line_toc rows).
        var paths = svc.GetTocPathsForLines([Base + 1]);
        Eq("user GetTocPathsForLines.count", paths.Count, 1);
        if (paths.Count == 1 && (paths[0].LineId != Base + 1 || paths[0].BookId != Base + 1))
            Fail("user TOC path ids wrong");

        // Enclosing TOC path: one valid user group, one valid library group, one MIXED
        // group (must be dropped, others still answered).
        int libLast = ScalarInt(lib, "SELECT MAX(lt.lineId) FROM line_toc lt WHERE lt.lineId < 1000");
        var enclosing = svc.GetEnclosingTocPathForLineRanges([
            7, Base + 1, Base + 3,      // user range
            8, libLast, libLast,        // library range
            9, libLast, Base + 2,       // MIXED — dropped
        ]);
        bool sawUser = false, sawLib = false, sawMixed = false;
        foreach (var e in enclosing)
        {
            if (e.GroupKey == 7) { sawUser = true; if (e.BookId != Base + 1) Fail("enclosing user group bookId wrong"); }
            if (e.GroupKey == 8) sawLib = true;
            if (e.GroupKey == 9) sawMixed = true;
        }
        if (!sawUser) Fail("enclosing: user group missing");
        if (!sawLib) Fail("enclosing: library group missing");
        if (sawMixed) Fail("enclosing: MIXED group was answered — must be dropped");

        // Word anchors. Contract: Supported reflects the LIBRARY's capability once the
        // library has been probed; before that it stays true (never tell callers to
        // stop on ignorance). A user-only batch alone must not settle it to false.
        bool libHasAnchor = ScalarInt(lib, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='link_anchor'") > 0;
        var anchorsUserOnly = svc.GetWordLinkAnchorsForLines([Base + 1]);
        Eq("user GetWordLinkAnchors.rows", anchorsUserOnly.Rows.Count, 0);
        if (!anchorsUserOnly.Supported && !libHasAnchor)
            Fail("GetWordLinkAnchors: a user-only batch settled Supported=false before the library was probed");
        int libLineForAnchor = ScalarInt(lib, "SELECT id FROM line ORDER BY id LIMIT 1");
        var anchorsLib = svc.GetWordLinkAnchorsForLines([libLineForAnchor]);
        Eq("GetWordLinkAnchors.supported after library probe", anchorsLib.Supported, libHasAnchor);
        var anchorsUserAgain = svc.GetWordLinkAnchorsForLines([Base + 1]);
        Eq("GetWordLinkAnchors.supported (user batch, library known)", anchorsUserAgain.Supported, libHasAnchor);

        // Title lookups union: a user-only title resolves to a shifted id; a title also
        // present in the library lists the library id FIRST.
        string userOnlyTitle = ScalarStr(synth, "SELECT title FROM book WHERE id = 3");
        var byTitle = svc.GetBookIdByExactTitle(userOnlyTitle);
        if (byTitle.Count == 0 || byTitle[byTitle.Count - 1].Id != Base + 3)
            Fail($"title union: user-only title '{userOnlyTitle}' did not resolve to shifted id");

        // Alt TOC structures + entries.
        var alt = svc.GetAltTocStructures(Base + 1);
        Eq("user GetAltTocStructures.count", alt.Count, 1);
        if (alt.Count == 1)
        {
            if (alt[0].Id < Base) Fail("user alt structure id not shifted");
            var altEntries = svc.GetAllAltTocEntries(alt[0].Id);
            Eq("user GetAllAltTocEntries.count", altEntries.Count,
                ScalarInt(synth, "SELECT COUNT(*) FROM alt_toc_entry WHERE structureId = 1"));
        }

        // Default commentators.
        var dc = svc.GetDefaultCommentators(Base + 1);
        Eq("user GetDefaultCommentators.count", dc.Count, 1);
        if (dc.Count == 1) Eq("user GetDefaultCommentators.id", dc[0].CommentatorBookId, Base + 2);

        // Merged LIMIT 50: both corpora match the pattern; the merged list caps at 50
        // with the library block first.
        var bold = svc.GetLinesWithContentPatternForBooks(
            [ScalarInt(lib, "SELECT bookId FROM line WHERE content LIKE '%<b>%' LIMIT 1"), Base + 1],
            "%<b>%");
        if (bold.Count > 50) Fail($"merged LIMIT 50 exceeded: {bold.Count}");
        if (bold.Count == 50 && bold[0].BookId >= Base)
            Fail("merged LIMIT 50: library block not first");

        Console.WriteLine("  synthetic routing: done");
    }

    // ── B2. File-backed content (Otzaria model: text in FILES, empty line table) ──

    private static void CheckFileBackedContent(SeforimDbService svc, string synthPath)
    {
        Console.WriteLine("\n=== B2. file-backed content ===");

        // Give synthetic book 3 a real file and the Otzaria shape: filePath set,
        // totalLines = 0, and NO rows in `line` for it (delete the seeded ones).
        string bookFile = Path.Combine(Path.GetTempPath(), $"kh-corpus-book-{Environment.ProcessId}.txt");
        // BOM + CRLF on one line + trailing \n — the exact shapes the reader must handle.
        File.WriteAllText(bookFile,
            "<h1>ספר קובץ</h1>\nשורה אחת\r\nשורה שתיים\n<h2>פרק ב</h2>\nשורה אחרונה\n",
            new System.Text.UTF8Encoding(true));
        try
        {
            using (var rw = new SqliteConnection($"Data Source={synthPath}"))
            {
                rw.Open();
                using var cmd = rw.CreateCommand();
                cmd.CommandText =
                    "UPDATE book SET filePath = @p, fileType = 'txt', totalLines = 0 WHERE id = 3; " +
                    "DELETE FROM line WHERE bookId = 3;";
                cmd.Parameters.AddWithValue("@p", bookFile);
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            // '\n'-split of the file: 6 elements (trailing empty line KEPT — the split
            // indexes must match Otzaria's 0-based tocEntry.lineIndex exactly).
            var info = svc.GetBookById(Base + 3)!;
            Eq("file-backed GetBookById.totalLines", info.TotalLines, 6);

            var page = svc.GetLinesPaged(Base + 3, 3, 1);
            Eq("file-backed GetLinesPaged.count", page.Count, 3);
            if (page.Count == 3)
            {
                Eq("file-backed page[0].lineIndex", page[0].LineIndex, 1);
                Eq("file-backed page[0].content", page[0].Content, "שורה אחת");
                Eq("file-backed CRLF trimmed", page[1].Content, "שורה שתיים");
                Eq("file-backed page ids are 0 (no line ids exist)", page[0].Id, 0);
            }

            var one = svc.GetLineByBookAndLineIndex(Base + 3, 3);
            Eq("file-backed single line.count", one.Count, 1);
            if (one.Count == 1) Eq("file-backed single line.content", one[0].Content, "<h2>פרק ב</h2>");

            Eq("file-backed beyond-end page", svc.GetLinesPaged(Base + 3, 10, 99).Count, 0);
            Eq("file-backed missing lineIndex", svc.GetLineByBookAndLineIndex(Base + 3, 99).Count, 0);

            // A book that still has DB lines must keep serving them (no file fallback).
            var dbBook = svc.GetLinesPaged(Base + 1, 3, 0);
            if (dbBook.Count != 3 || dbBook[0].Id < Base)
                Fail("file-backed: DB-lined book stopped serving DB rows");

            // Books 1/2 have no filePath — totalLines stays the DB value.
            Eq("non-file-backed totalLines untouched", svc.GetBookById(Base + 1)!.TotalLines, 9);

            Console.WriteLine("  file-backed content: done");
        }
        finally
        {
            try { File.Delete(bookFile); } catch { }
        }
    }

    // ── C. The real Otzaria user_books.db ────────────────────────────────────────

    private static void CheckRealUserDb(SeforimDbService svc, SqliteConnection lib, SqliteConnection real)
    {
        Console.WriteLine("\n=== C. real user_books.db ===");

        int userBooks = ScalarInt(real, "SELECT COUNT(*) FROM book");
        int userCats = ScalarInt(real, "SELECT COUNT(*) FROM category");
        int libBooks = ScalarInt(lib, "SELECT COUNT(*) FROM book");
        int libCats = ScalarInt(lib, "SELECT COUNT(*) FROM category");

        var books = svc.GetAllBooks();
        Eq("real GetAllBooks.count", books.Count, libBooks + userBooks);
        int shifted = 0;
        foreach (var b in books) if (b.Id >= Base) shifted++;
        Eq("real GetAllBooks.shifted", shifted, userBooks);

        var cats = svc.GetAllCategories();
        Eq("real GetAllCategories.count", cats.Count, libCats + userCats);

        // A real user book with a TOC: entries must come back with usable lineIndex
        // (via tocEntry.lineIndex — the line table is EMPTY in this DB).
        int tocBook = ScalarInt(real, @"SELECT bookId FROM tocEntry GROUP BY bookId ORDER BY COUNT(*) DESC LIMIT 1");
        var toc = svc.GetAllTocEntries(Base + tocBook);
        int tocDirect = ScalarInt(real, "SELECT COUNT(*) FROM tocEntry WHERE bookId = @b", ("@b", tocBook));
        Eq($"real GetAllTocEntries(book {tocBook}).count", toc.Count, tocDirect);
        int withIndex = 0;
        foreach (var t in toc) if (t.LineIndex is not null) withIndex++;
        int directWithIndex = ScalarInt(real, "SELECT COUNT(*) FROM tocEntry WHERE bookId = @b AND lineIndex IS NOT NULL", ("@b", tocBook));
        Eq("real TOC lineIndex coverage", withIndex, directWithIndex);
        if (tocDirect > 0 && withIndex == 0)
            Fail("real TOC: every lineIndex is null — the tocEntry.lineIndex variant was not used");

        // Content lives in FILES, not the DB — served from the file, ground truth
        // computed here by reading the same file independently.
        string? realFile = null;
        int realTxtBook = 0;
        using (var cmd = real.CreateCommand())
        {
            cmd.CommandText = @"SELECT id, filePath FROM book
                WHERE fileType = 'txt' AND filePath IS NOT NULL ORDER BY id LIMIT 5";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (File.Exists(r.GetString(1))) { realTxtBook = r.GetInt32(0); realFile = r.GetString(1); break; }
            }
        }
        if (realFile is null)
        {
            Console.WriteLine("  (no on-disk txt book found — file-content spot check skipped)");
        }
        else
        {
            string[] expected = File.ReadAllText(realFile, System.Text.Encoding.UTF8).Split('\n');
            var bi = svc.GetBookById(Base + realTxtBook)!;
            Eq($"real file-backed totalLines (book {realTxtBook})", bi.TotalLines, expected.Length);
            var page = svc.GetLinesPaged(Base + realTxtBook, 3, 0);
            Eq("real file-backed first page.count", page.Count, Math.Min(3, expected.Length));
            if (page.Count > 0)
                Eq("real file-backed first line", page[0].Content, expected[0].TrimEnd('\r'))
                ;
        }

        // Title lookup for a real user book title (may also exist in the library —
        // the shifted id must be present either way).
        string title = ScalarStr(real, "SELECT title FROM book WHERE id = @b", ("@b", tocBook));
        var ids = svc.GetBookIdByExactTitle(title);
        if (!ids.Exists(x => x.Id == Base + tocBook))
            Fail($"real title lookup: '{title}' missing shifted id {Base + tocBook}");

        Console.WriteLine($"  real user DB: {userBooks} books, {userCats} categories, TOC({tocBook})={tocDirect} rows — done");
    }

    // ── Synthetic DB generator (Otzaria schema shape, service-relevant tables) ────
    //
    // Book/line ids collide with library ids (both start at 1) and connection types are
    // inserted in an order that DISAGREES with the library (TARGUM first), because
    // Otzaria assigns type ids lazily in encounter order — disagreement is the
    // realistic case. Schema shape mirrors the real user_books.db: tocEntry HAS
    // lineIndex, link has NO targetLineIndex, and there is NO link_anchor table.

    private static void BuildSyntheticUserDb(string path)
    {
        File.Delete(path);
        using var db = new SqliteConnection($"Data Source={path}");
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE category (id INTEGER PRIMARY KEY AUTOINCREMENT, parentId INTEGER,
                title TEXT NOT NULL, level INTEGER NOT NULL DEFAULT 0, orderIndex INTEGER NOT NULL DEFAULT 999);
            CREATE TABLE book (id INTEGER PRIMARY KEY AUTOINCREMENT, categoryId INTEGER NOT NULL,
                sourceId INTEGER NOT NULL, title TEXT NOT NULL, orderIndex INTEGER NOT NULL DEFAULT 999,
                totalLines INTEGER NOT NULL DEFAULT 0, hasTeamim INTEGER NOT NULL DEFAULT 0,
                hasTargumConnection INTEGER NOT NULL DEFAULT 0, hasReferenceConnection INTEGER NOT NULL DEFAULT 0,
                hasSourceConnection INTEGER NOT NULL DEFAULT 0, hasCommentaryConnection INTEGER NOT NULL DEFAULT 0,
                hasOtherConnection INTEGER NOT NULL DEFAULT 0, hasAltStructures INTEGER NOT NULL DEFAULT 0,
                isPersonal INTEGER DEFAULT 0, filePath TEXT DEFAULT NULL, fileType TEXT DEFAULT 'txt');
            CREATE TABLE book_author (bookId INTEGER NOT NULL, authorId INTEGER NOT NULL);
            CREATE TABLE author (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL);
            CREATE TABLE line (id INTEGER PRIMARY KEY AUTOINCREMENT, bookId INTEGER NOT NULL,
                lineIndex INTEGER NOT NULL, content TEXT NOT NULL);
            CREATE TABLE tocText (id INTEGER PRIMARY KEY AUTOINCREMENT, text TEXT NOT NULL UNIQUE);
            CREATE TABLE tocEntry (id INTEGER PRIMARY KEY AUTOINCREMENT, bookId INTEGER NOT NULL,
                parentId INTEGER, textId INTEGER NOT NULL, level INTEGER NOT NULL, lineId INTEGER,
                lineIndex INTEGER, isLastChild INTEGER NOT NULL DEFAULT 0, hasChildren INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE line_toc (lineId INTEGER PRIMARY KEY, tocEntryId INTEGER NOT NULL);
            CREATE TABLE connection_type (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE);
            CREATE TABLE link (id INTEGER PRIMARY KEY AUTOINCREMENT, sourceBookId INTEGER NOT NULL,
                targetBookId INTEGER NOT NULL, sourceLineId INTEGER NOT NULL, targetLineId INTEGER NOT NULL,
                connectionTypeId INTEGER NOT NULL);
            CREATE TABLE alt_toc_structure (id INTEGER PRIMARY KEY AUTOINCREMENT, bookId INTEGER NOT NULL,
                key TEXT NOT NULL, title TEXT, heTitle TEXT);
            CREATE TABLE alt_toc_entry (id INTEGER PRIMARY KEY AUTOINCREMENT, structureId INTEGER NOT NULL,
                parentId INTEGER, textId INTEGER NOT NULL, level INTEGER NOT NULL, lineId INTEGER,
                isLastChild INTEGER NOT NULL DEFAULT 0, hasChildren INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE default_commentator (bookId INTEGER NOT NULL, commentatorBookId INTEGER NOT NULL,
                position INTEGER NOT NULL);

            INSERT INTO connection_type (name) VALUES ('TARGUM'), ('SOURCE'), ('COMMENTARY'), ('REFERENCE');
            INSERT INTO category (parentId, title, level, orderIndex) VALUES (NULL, 'ספרים אישיים', 0, 1);
            INSERT INTO book (categoryId, sourceId, title, totalLines, hasCommentaryConnection, hasTargumConnection, hasAltStructures, isPersonal)
                VALUES (1, 1, 'קונטרס בדיקה א', 9, 1, 1, 1, 1),
                       (1, 1, 'הערות בדיקה', 6, 0, 0, 0, 1),
                       (1, 1, 'תרגום בדיקה ייחודי', 6, 0, 0, 0, 1);
        ";
        cmd.ExecuteNonQuery();

        using var tx = db.BeginTransaction();
        using var ins = db.CreateCommand();
        ins.Transaction = tx;

        // Lines: book 1 → ids 1..9, book 2 → 10..15, book 3 → 16..21.
        ins.CommandText = "INSERT INTO line (bookId, lineIndex, content) VALUES (@b, @i, @c)";
        var pB = ins.CreateParameter(); pB.ParameterName = "@b"; ins.Parameters.Add(pB);
        var pI = ins.CreateParameter(); pI.ParameterName = "@i"; ins.Parameters.Add(pI);
        var pC = ins.CreateParameter(); pC.ParameterName = "@c"; ins.Parameters.Add(pC);
        foreach (var (book, count) in new[] { (1, 9), (2, 6), (3, 6) })
            for (int i = 0; i < count; i++)
            {
                pB.Value = book; pI.Value = i; pC.Value = $"<b>פרק</b> שורה {i + 1} של ספר {book}";
                ins.ExecuteNonQuery();
            }

        ins.Parameters.Clear();
        ins.CommandText = @"
            INSERT INTO tocText (text) VALUES ('פרק א'), ('פרק ב'), ('פרק ג'), ('חלק ראשון');
            -- Otzaria shape: lineIndex ON the entry, lineId NULL.
            INSERT INTO tocEntry (bookId, parentId, textId, level, lineId, lineIndex, hasChildren)
                VALUES (1, NULL, 1, 0, NULL, 0, 0), (1, NULL, 2, 0, NULL, 3, 0), (1, NULL, 3, 0, NULL, 6, 0);
            INSERT INTO line_toc (lineId, tocEntryId) VALUES (1,1),(2,1),(3,1),(4,2),(5,2),(6,2),(7,3),(8,3),(9,3);
            -- book2 comments on book1 (COMMENTARY=3 here, 1 in the library);
            -- book3 is its targum (TARGUM=1 here, 3 in the library).
            INSERT INTO link (sourceBookId, targetBookId, sourceLineId, targetLineId, connectionTypeId)
                VALUES (1,2,1,10,3),(1,2,2,11,3),(1,2,3,12,3),
                       (1,3,1,16,1),(1,3,2,17,1),(1,3,3,18,1);
            INSERT INTO alt_toc_structure (bookId, key, title, heTitle) VALUES (1, 'chapters', 'Chapters', 'פרקים');
            INSERT INTO alt_toc_entry (structureId, parentId, textId, level, lineId) VALUES (1, NULL, 4, 0, 1);
            INSERT INTO default_commentator (bookId, commentatorBookId, position) VALUES (1, 2, 0);
        ";
        ins.ExecuteNonQuery();
        tx.Commit();
    }

    // ── Plumbing ────────────────────────────────────────────────────────────────

    private static SqliteConnection OpenDirect(string path)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
        }.ConnectionString);
        conn.Open();
        return conn;
    }

    private static int ScalarInt(SqliteConnection conn, string sql, params (string, object)[] ps)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        object? o = cmd.ExecuteScalar();
        return o is null or DBNull ? 0 : Convert.ToInt32(o);
    }

    private static string ScalarStr(SqliteConnection conn, string sql, params (string, object)[] ps)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return cmd.ExecuteScalar() as string ?? "";
    }

    private static List<int> Ints(SqliteConnection conn, string sql, params (string, object)[] ps)
    {
        var list = new List<int>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetInt32(0));
        return list;
    }

    private static List<T> Rows<T>(SqliteConnection conn, string sql, Func<SqliteDataReader, T> map, params (string, object)[] ps)
    {
        var list = new List<T>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    private static void Eq<T>(string what, T actual, T expected)
    {
        if (EqualityComparer<T>.Default.Equals(actual, expected))
            Console.WriteLine($"  OK   {what} = {actual}");
        else
            Fail($"{what}: got {actual}, expected {expected}");
    }

    private static void Fail(string message)
    {
        _failures++;
        Console.Error.WriteLine("  FAIL " + message);
    }

    /// <summary>ILogger that surfaces the service's swallowed exceptions as test output;
    /// Run() catching an exception must not read as a legitimate empty result.</summary>
    private sealed class CapturingLogger : ILogger<SeforimDb.SeforimDbService>
    {
        public int Errors;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error) Errors++;
            Console.Error.WriteLine($"  [svc:{logLevel}] {formatter(state, exception)}{(exception is null ? "" : " :: " + exception.Message)}");
        }
    }
}
