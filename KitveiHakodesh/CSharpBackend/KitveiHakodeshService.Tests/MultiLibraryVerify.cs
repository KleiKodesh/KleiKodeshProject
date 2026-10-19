// Ad-hoc live verification for ATTACH-based multiple seforim libraries
// (KitveiHakodesh.Core.SeforimDb.SeforimDbAttachedLibraries / SeforimDbLibraryId):
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
//   5. Removing an additional library's path from settings makes a NEW SeforimDbQueries
//      instance stop seeing it (settings are read once per instance, exactly like the
//      primary path) without disturbing the primary database's own ids or content.
using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using KitveiHakodesh.Core.SeforimDb;

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

            try
            {
                BuildLibrary(primaryPath, categoryTitle: "ספרי הראשי", bookTitle: "ספר ראשי", lineText: "תוכן ראשי");
                BuildLibrary(additionalPath, categoryTitle: "ספרי האישי", bookTitle: "ספר אישי", lineText: "תוכן אישי");
                BuildIncompatibleFile(incompatiblePath);

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

        /// <summary>A minimal seforim-shaped database: one category, one book in it, one line.
        /// Schema-compatible (has book/category/line/author/book_author), which is exactly what
        /// SeforimDbAttachedLibraries checks for.</summary>
        private static void BuildLibrary(string dbPath, string categoryTitle, string bookTitle, string lineText)
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
                VALUES (1, 1, '{bookTitle}', 0, 0, 1, 0, 0, 0, 0, 0)");
            Exec(conn, $"INSERT INTO line(id, bookId, lineIndex, content) VALUES (1, 1, 0, '{lineText}')");
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

        private static void Exec(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static string Dump<T>(System.Collections.Generic.IEnumerable<T> items) =>
            string.Join(" | ", items.Select(i => i!.ToString()));
    }
}
