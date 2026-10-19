// Ad-hoc live verification for ATTACH-based multiple seforim libraries
// (KitveiHakodesh.Core.SeforimDb.SeforimDbAttachedLibraries / SeforimDbLibraryId /
// KitveiHakodesh.Core.Settings.SeforimDbAdditionalLibraries):
//
//   1. With no additional libraries configured, the catalog and content behave exactly as
//      before — same ids, no LibraryLabel.
//   2. With one additional library ATTACHed, its categories/books appear in the SAME catalog
//      list, with ids shifted into their own band and LibraryLabel set; its lines are readable
//      through GetLinesPaged with the SAME global book id GetAllBooks returned.
//   3. A primary-db id and an attached-library id that happen to share the same LOCAL id
//      (both databases start their own book/category ids at 1) resolve to two DIFFERENT,
//      correct books — the whole point of SeforimDbLibraryId's offset.
//   4. An additional library missing a required table (no `line`) is skipped — it does not
//      appear in AttachedLibraries and does not break reading the primary database.
//   5. Removing an additional library makes a NEW SeforimDbQueries instance stop seeing it,
//      without disturbing the primary database's own ids or content.
//   6. ID-DRIFT REGRESSION (the bug a position-based index had): with TWO additional libraries
//      (A at persisted slot 1, B at persisted slot 2), removing A must NOT change B's slot — B's
//      global book id must be IDENTICAL before and after A is gone. This is exactly the failure
//      mode that would silently reassign a user's saved highlights to the wrong book.
//   7. A library whose own ids reach OffsetUnit is refused (not attached) rather than silently
//      colliding with the next library's band.
//   8. A library using the split line-content shape (line_content/zstd_dict) still appears in
//      the catalog, but reading its lines throws a clear NotSupportedException instead of
//      guessing at a codec built for a different connection's dictionaries.
//   9. SeforimDbAdditionalLibraries' own parsing/counter logic (ParseEntries, SerializeEntries,
//      NextIndexFrom) round-trips and never reuses a retired index — tested directly via
//      InternalsVisibleTo, without touching the real Windows registry.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using KitveiHakodesh.Core.SeforimDb;
using KitveiHakodesh.Core.Settings;

namespace KitveiHakodeshService.Tests
{
    public static class MultiLibraryVerify
    {
        public static int Run()
        {
            int failures = 0;
            void Fail(string msg) { failures++; Console.Error.WriteLine("  FAIL: " + msg); }
            void Ok(string msg) { Console.WriteLine("  OK: " + msg); }

            string dir = Path.Combine(Path.GetTempPath(), $"multilibtest-{Environment.ProcessId}");
            Directory.CreateDirectory(dir);
            string primaryPath = Path.Combine(dir, "seforim.db");
            string additionalPath = Path.Combine(dir, "personal.db");
            string incompatiblePath = Path.Combine(dir, "incompatible.db");
            string libraryAPath = Path.Combine(dir, "libraryA.db");
            string libraryBPath = Path.Combine(dir, "libraryB.db");
            string oversizedIdPath = Path.Combine(dir, "oversized.db");
            string splitContentPath = Path.Combine(dir, "split.db");

            try
            {
                BuildLibrary(primaryPath, categoryTitle: "ספרי הראשי", bookTitle: "ספר ראשי", lineText: "תוכן ראשי", bookId: 1);
                BuildLibrary(additionalPath, categoryTitle: "ספרי האישי", bookTitle: "ספר אישי", lineText: "תוכן אישי", bookId: 1);
                BuildIncompatibleFile(incompatiblePath);
                BuildLibrary(libraryAPath, categoryTitle: "קטגוריית A", bookTitle: "ספר A", lineText: "תוכן A", bookId: 1);
                BuildLibrary(libraryBPath, categoryTitle: "קטגוריית B", bookTitle: "ספר B", lineText: "תוכן B", bookId: 1);
                BuildLibrary(oversizedIdPath, categoryTitle: "חריג", bookTitle: "ספר חריג", lineText: "תוכן חריג", bookId: SeforimDbLibraryIdOffsetUnitForTest);
                BuildSplitContentLibrary(splitContentPath);

                // ── 1. No additional libraries: unchanged behaviour ─────────────────────
                {
                    var queries = new SeforimDbQueries(primaryPath);
                    if (queries.AttachedLibraries.Count != 0)
                        Fail($"no additional libraries configured, but AttachedLibraries has {queries.AttachedLibraries.Count}");
                    else
                        Ok("no additional libraries configured -> AttachedLibraries is empty");

                    var books = queries.GetAllBooks();
                    if (books.Count != 1 || books[0].Id != 1 || books[0].LibraryLabel != null)
                        Fail($"primary-only catalog changed shape: {Dump(books)}");
                    else
                        Ok("primary-only catalog: one book, id=1, LibraryLabel=null (unchanged)");
                }

                // ── 2 & 3. One additional library attached, same-local-id collision ─────
                {
                    var queries = new SeforimDbQueries(primaryPath, new[] { additionalPath });
                    if (queries.AttachedLibraries.Count != 1)
                        Fail($"expected 1 attached library, got {queries.AttachedLibraries.Count}");
                    else
                        Ok("one compatible additional library attached");

                    var books = queries.GetAllBooks().OrderBy(b => b.Id).ToList();
                    if (books.Count != 2)
                    {
                        Fail($"expected 2 books across both libraries, got {books.Count}: {Dump(books)}");
                    }
                    else
                    {
                        var primaryBook = books[0];
                        var additionalBook = books[1];

                        if (primaryBook.Id != 1 || primaryBook.LibraryLabel != null)
                            Fail($"primary book changed identity once a library was attached: {Dump(books)}");
                        else
                            Ok("primary book keeps id=1 and LibraryLabel=null once a library is attached (no id churn)");

                        // Same local id (1) as the primary book, but a DIFFERENT global id and a
                        // library label — this IS the collision SeforimDbLibraryId exists to avoid.
                        if (additionalBook.Id == primaryBook.Id || additionalBook.LibraryLabel is null)
                            Fail($"additional-library book did not get a distinct id/label: {Dump(books)}");
                        else
                            Ok($"additional-library book got a distinct global id ({additionalBook.Id}) and label '{additionalBook.LibraryLabel}'");

                        // Reading its lines by the id the CATALOG returned must reach the RIGHT
                        // database's line table, not the primary's (which also has a bookId=1 row).
                        var lines = queries.GetLinesPaged(additionalBook.Id, 10, 0);
                        if (lines.Count != 1 || lines[0].Content != "תוכן אישי")
                            Fail($"GetLinesPaged(additional book id) returned wrong content: {Dump(lines)}");
                        else
                            Ok("GetLinesPaged with the additional library's global book id returns ITS line, not the primary's");

                        var primaryLines = queries.GetLinesPaged(primaryBook.Id, 10, 0);
                        if (primaryLines.Count != 1 || primaryLines[0].Content != "תוכן ראשי")
                            Fail($"GetLinesPaged(primary book id) returned wrong content once a library was attached: {Dump(primaryLines)}");
                        else
                            Ok("GetLinesPaged with the primary book id still returns the primary's line");

                        var info = queries.GetBookById(additionalBook.Id);
                        if (info is null)
                            Fail("GetBookById(additional book id) returned null");
                        else
                            Ok("GetBookById resolves an additional-library id");
                    }
                }

                // ── 4. Incompatible file is skipped, primary keeps working ───────────────
                {
                    var queries = new SeforimDbQueries(primaryPath, new[] { incompatiblePath });
                    if (queries.AttachedLibraries.Count != 0)
                        Fail($"incompatible file was attached anyway: {Dump(queries.AttachedLibraries.Select(l => l.Path))}");
                    else
                        Ok("a file missing required tables is skipped, not attached");

                    var books = queries.GetAllBooks();
                    if (books.Count != 1 || books[0].Id != 1)
                        Fail($"primary catalog broke when an incompatible additional path was configured: {Dump(books)}");
                    else
                        Ok("primary catalog still correct alongside a skipped incompatible path");
                }

                // ── 5. Removing a library from the constructor list drops it cleanly ────
                {
                    var withLibrary = new SeforimDbQueries(primaryPath, new[] { additionalPath });
                    int countWith = withLibrary.GetAllBooks().Count;

                    var withoutLibrary = new SeforimDbQueries(primaryPath, Array.Empty<string>());
                    var booksWithout = withoutLibrary.GetAllBooks();
                    if (booksWithout.Count != countWith - 1 || booksWithout[0].Id != 1)
                        Fail($"removing the additional library did not cleanly drop its rows: {Dump(booksWithout)}");
                    else
                        Ok("a fresh instance without the additional library sees only the primary catalog");
                }

                // ── 6. ID-DRIFT REGRESSION: removing an EARLIER library must not move a
                //      LATER one's slot. This is the exact bug the persisted Entry.Index design
                //      (SeforimDbAdditionalLibraries) exists to prevent — a position-based index
                //      would renumber B from slot 2 to slot 1 the moment A disappears, silently
                //      reassigning every id a user's saved highlights might reference. ─────────
                {
                    var entryA = new SeforimDbAdditionalLibraries.Entry(1, libraryAPath);
                    var entryB = new SeforimDbAdditionalLibraries.Entry(2, libraryBPath);

                    var withBoth = new SeforimDbQueries(primaryPath, new[] { entryA, entryB });
                    var bookB_before = withBoth.GetAllBooks().SingleOrDefault(b => b.LibraryLabel == "libraryB");
                    if (bookB_before is null)
                    {
                        Fail("setup: library B's book was not found with both A and B attached");
                    }
                    else
                    {
                        // A is "removed" (or merely unavailable) — B's entry is passed with the
                        // SAME index it always had, exactly as a real Settings-backed read would
                        // produce after SeforimDbAdditionalLibraries.Remove(pathOfA).
                        var withoutA = new SeforimDbQueries(primaryPath, new[] { entryB });
                        var bookB_after = withoutA.GetAllBooks().SingleOrDefault(b => b.LibraryLabel == "libraryB");

                        if (bookB_after is null)
                            Fail("library B's book vanished once A was removed");
                        else if (bookB_after.Id != bookB_before.Id)
                            Fail($"ID DRIFT: library B's book id changed from {bookB_before.Id} to {bookB_after.Id} " +
                                 "after an unrelated earlier library was removed — this is the bug this test exists to catch");
                        else
                            Ok($"library B keeps its global book id ({bookB_after.Id}) after library A is removed — no id drift");

                        // And B's lines must still be readable under that SAME id.
                        if (bookB_after is not null)
                        {
                            var lines = withoutA.GetLinesPaged(bookB_after.Id, 10, 0);
                            if (lines.Count != 1 || lines[0].Content != "תוכן B")
                                Fail($"library B's lines unreadable (or wrong) after A was removed: {Dump(lines)}");
                            else
                                Ok("library B's lines still read correctly by the same id after A was removed");
                        }
                    }
                }

                // ── 7. A library whose own ids reach OffsetUnit is refused, not attached ─
                {
                    var queries = new SeforimDbQueries(primaryPath, new[] { oversizedIdPath });
                    if (queries.AttachedLibraries.Count != 0)
                        Fail($"a library with an id >= OffsetUnit was attached anyway: {Dump(queries.AttachedLibraries.Select(l => l.Path))}");
                    else
                        Ok("a library whose own id reaches OffsetUnit is refused (would collide with the next band)");
                }

                // ── 8. Split line-content shape: catalog works, reading lines throws ────
                {
                    var queries = new SeforimDbQueries(primaryPath, new[] { splitContentPath });
                    if (queries.AttachedLibraries.Count != 1)
                    {
                        Fail($"split-content library was not attached at all (expected it to attach for catalog purposes): {queries.AttachedLibraries.Count}");
                    }
                    else
                    {
                        var book = queries.GetAllBooks().SingleOrDefault(b => b.LibraryLabel == "split");
                        if (book is null)
                        {
                            Fail("split-content library's book is missing from the catalog");
                        }
                        else
                        {
                            Ok("split-content library still appears in the catalog (book/category work without touching line text)");
                            bool threw = false;
                            try { queries.GetLinesPaged(book.Id, 10, 0); }
                            catch (NotSupportedException) { threw = true; }
                            if (!threw)
                                Fail("GetLinesPaged on a split-content attached library did not throw NotSupportedException — it would have returned wrong/garbage content instead");
                            else
                                Ok("GetLinesPaged on a split-content attached library throws NotSupportedException instead of guessing");
                        }
                    }
                }

                // ── 9. SeforimDbAdditionalLibraries' pure parsing/counter logic ──────────
                {
                    var roundTrip = new[]
                    {
                        new SeforimDbAdditionalLibraries.Entry(1, @"C:\a\personal.db"),
                        new SeforimDbAdditionalLibraries.Entry(5, @"C:\b\second.db"),
                    };
                    string serialized = SeforimDbAdditionalLibraries.SerializeEntries(roundTrip);
                    var parsed = SeforimDbAdditionalLibraries.ParseEntries(serialized);
                    if (parsed.Count != 2 || parsed[0].Index != 1 || parsed[0].Path != @"C:\a\personal.db"
                        || parsed[1].Index != 5 || parsed[1].Path != @"C:\b\second.db")
                        Fail($"ParseEntries(SerializeEntries(x)) did not round-trip: '{serialized}' -> {parsed.Count} entries");
                    else
                        Ok("SerializeEntries/ParseEntries round-trip correctly, including a non-contiguous index (5)");

                    var withGarbage = SeforimDbAdditionalLibraries.ParseEntries("1|ok.db;garbage;0|zero.db;-1|neg.db;2|");
                    if (withGarbage.Count != 1 || withGarbage[0].Index != 1 || withGarbage[0].Path != "ok.db")
                        Fail($"ParseEntries did not drop malformed segments cleanly: {withGarbage.Count} entries");
                    else
                        Ok("ParseEntries drops malformed segments (no separator, index <= 0, empty path) without failing the whole read");

                    if (SeforimDbAdditionalLibraries.NextIndexFrom(null) != 1
                        || SeforimDbAdditionalLibraries.NextIndexFrom("") != 1
                        || SeforimDbAdditionalLibraries.NextIndexFrom("not a number") != 1
                        || SeforimDbAdditionalLibraries.NextIndexFrom("0") != 1
                        || SeforimDbAdditionalLibraries.NextIndexFrom("7") != 7)
                        Fail("NextIndexFrom did not treat missing/invalid/non-positive counters as 'first slot' and a valid counter as itself");
                    else
                        Ok("NextIndexFrom: unset/invalid -> slot 1; a real counter value passes through unchanged");
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

        // Mirrors KitveiHakodesh.Core.SeforimDb.SeforimDbLibraryId.OffsetUnit without depending on
        // an internal constant from a test that is meant to exercise PUBLIC/behavioural surface —
        // if the real constant ever changes, this test will simply need the same update to stay
        // meaningful, which is an acceptable coupling for an id right at the boundary.
        private const int SeforimDbLibraryIdOffsetUnitForTest = 50_000_000;

        /// <summary>A minimal seforim-shaped database: one category, one book in it, one line.
        /// Schema-compatible (has book/category/line/author/book_author), which is exactly what
        /// SeforimDbAttachedLibraries checks for. <paramref name="bookId"/> lets a test build a
        /// library whose own id does not start at 1 (the OffsetUnit boundary case).</summary>
        private static void BuildLibrary(string dbPath, string categoryTitle, string bookTitle, string lineText, int bookId)
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
                ";
                cmd.ExecuteNonQuery();
            }

            Exec(conn, $"INSERT INTO category(id, parentId, title, level) VALUES (1, NULL, '{categoryTitle}', 0)");
            Exec(conn, $@"INSERT INTO book(id, categoryId, title, orderIndex, hasTeamim, totalLines,
                hasTargumConnection, hasReferenceConnection, hasSourceConnection, hasCommentaryConnection, hasOtherConnection)
                VALUES ({bookId}, 1, '{bookTitle}', 0, 0, 1, 0, 0, 0, 0, 0)");
            Exec(conn, $"INSERT INTO line(id, bookId, lineIndex, content) VALUES (1, {bookId}, 0, '{lineText}')");
        }

        /// <summary>A valid SQLite file that is NOT seforim-shaped (no `line` table) — must be
        /// recognised as incompatible rather than attached and failing on the first query.</summary>
        private static void BuildIncompatibleFile(string dbPath)
        {
            using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE something_else(id INTEGER PRIMARY KEY)";
            cmd.ExecuteNonQuery();
        }

        /// <summary>A seforim-shaped database using the SPLIT line-content shape (SeforimLibrary
        /// schema 6+): `line` has no `content` column, the text lives in `line_content` instead.
        /// Catalog-compatible (book/category/author tables are normal), but line-text reads must
        /// be refused for an ATTACHed library — see SeforimDbQueries.GetLinesPagedFromAttachedLibrary.</summary>
        private static void BuildSplitContentLibrary(string dbPath)
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
                    CREATE TABLE line(id INTEGER PRIMARY KEY, bookId INTEGER, lineIndex INTEGER);
                    CREATE TABLE line_content(id INTEGER PRIMARY KEY, content TEXT);
                ";
                cmd.ExecuteNonQuery();
            }

            Exec(conn, "INSERT INTO category(id, parentId, title, level) VALUES (1, NULL, 'פיצול', 0)");
            Exec(conn, @"INSERT INTO book(id, categoryId, title, orderIndex, hasTeamim, totalLines,
                hasTargumConnection, hasReferenceConnection, hasSourceConnection, hasCommentaryConnection, hasOtherConnection)
                VALUES (1, 1, 'ספר מפוצל', 0, 0, 1, 0, 0, 0, 0, 0)");
            Exec(conn, "INSERT INTO line(id, bookId, lineIndex) VALUES (1, 1, 0)");
            Exec(conn, "INSERT INTO line_content(id, content) VALUES (1, 'תוכן מפוצל')");
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
