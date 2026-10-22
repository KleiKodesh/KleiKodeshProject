// Regression guard for "a library built by another tool is rejected with 'required tables have
// unexpected columns'". The probe used to demand every column of the official seforim.db's book
// table (hasTeamim, ...). A personal library (otzarya-unofficial-books.db) has book.fileType but
// none of the has*Connection / hasTeamim flags, no line.content (the text is in line_content),
// no alt_toc_* / link tables. It must attach, list its books, give a book's info (flags default
// to 0), read its lines and be found by book-name search.
//
// Synthetic libraries always run. Real files run too when KK_REAL_LIBS names them (separated by
// a semicolon); they are only ever opened read-only.
using System;
using System.IO;
using System.Linq;
using System.Threading;
using KitveiHakodesh.Core.SeforimDb;
using KitveiHakodesh.Core.Settings;
using KitveiHakodesh.Core.SeforimDbCatalog;
using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.Tests
{
    public static class PersonalLibraryShapeVerify
    {
        private const string BookTitle = "ספר אישי בצורת אוצריא";
        private const string LineText = "טקסט אישי בצורת אוצריא";

        public static int Run()
        {
            int failures = 0;
            void Check(bool ok, string what, string? detail = null)
            {
                if (ok) Console.WriteLine("  OK: " + what);
                else { failures++; Console.Error.WriteLine("  FAIL: " + what + (detail is null ? "" : " - " + detail)); }
            }

            string dir = Path.Combine(Path.GetTempPath(), "personalshape-" + Environment.ProcessId);
            Directory.CreateDirectory(dir);
            try
            {
                string primary = Path.Combine(dir, "seforim.db");
                HardeningVerify.BuildLibrary(primary, "ראשי", "ספר ראשי", "טקסט ראשי", "פרק", HardeningVerify.Shape.Plain, altToc: true, regularToc: true);

                foreach (var shape in new[] { HardeningVerify.Shape.SplitCompressed, HardeningVerify.Shape.SplitPlain })
                {
                    string lib = Path.Combine(dir, "personal-" + shape + ".db");
                    HardeningVerify.BuildLibrary(lib, "אישי", BookTitle, LineText, "פרק", shape, altToc: false, regularToc: true, dictSeed: 7);
                    MakePersonalShape(lib);
                    var old = DateTime.UtcNow.AddHours(-2);
                    File.SetLastWriteTimeUtc(primary, old);
                    File.SetLastWriteTimeUtc(lib, old);
                    string tag = "[" + shape + "] ";

                    var entries = new[] { new SeforimDbAdditionalLibraries.Entry(1, lib) };
                    var status = SeforimDbAttachedLibraries.ResolveWithStatus(entries)[0];
                    Check(status.Rejection is null && status.Library is not null, tag + "attached (Attached=true)", status.Rejection);
                    Check(SeforimDbAttachedLibraries.CheckTextReadable(lib) is null, tag + "line text readable");

                    var q = new SeforimDbQueries(primary, entries);
                    var book = q.GetAllBooks().FirstOrDefault(b => b.LibraryLabel != null && b.Title == BookTitle);
                    Check(book != null, tag + "book listed in the catalog");
                    if (book != null)
                    {
                        var info = q.GetBookById(book.Id);
                        Check(info != null && info.TotalLines == 1 && info.HasTeamim == 0 && info.HasCommentaryConnection == 0,
                            tag + "GetBookById works; missing has* flags default to 0");
                        var lines = q.GetLinesPaged(book.Id, 10, 0);
                        Check(lines.Count == 1 && lines[0].Content == LineText, tag + "GetLinesPaged returns the line text",
                            string.Join("|", lines.Select(l => l.Content)));
                    }

                    string idx = Path.Combine(dir, "idx-" + shape);
                    using var coordinator = new SeforimDbCatalogIndexCoordinator(() => primary, idx, (_, _, _) => { }, entries);
                    coordinator.EnsureIndex();
                    Thread.Sleep(200);
                    var deadline = DateTime.UtcNow.AddSeconds(60);
                    while (DateTime.UtcNow < deadline && (coordinator.IsIndexing || !coordinator.IsReady)) Thread.Sleep(100);
                    var result = coordinator.Search(BookTitle);
                    Check(result.Ready && (result.Results?.Any(h => h.Level == 0 && h.BookId == SeforimDbLibraryId.ToGlobal(1, 1)) ?? false),
                        tag + "book-name search finds the library's book");
                }

                string? real = Environment.GetEnvironmentVariable("KK_REAL_LIBS");
                if (!string.IsNullOrWhiteSpace(real))
                {
                    foreach (string path in real.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        string tag = "[real " + Path.GetFileName(path) + "] ";
                        var entries = new[] { new SeforimDbAdditionalLibraries.Entry(1, path) };
                        var status = SeforimDbAttachedLibraries.ResolveWithStatus(entries)[0];
                        Check(status.Rejection is null, tag + "attached", status.Rejection);
                        if (status.Rejection is not null) continue;
                        string? unreadable = SeforimDbAttachedLibraries.CheckTextReadable(path);
                        Check(unreadable is null, tag + "line text readable", unreadable);
                        var q = new SeforimDbQueries(primary, entries);
                        var books = q.GetAllBooks().Where(b => b.LibraryLabel != null).ToList();
                        Check(books.Count > 0, tag + "books listed: " + books.Count);
                        var sample = books.FirstOrDefault(b => !string.IsNullOrWhiteSpace(b.Title));
                        if (sample == null) continue;
                        Check(books.Any(b => b.Title == sample.Title), tag + "book found by title: " + sample.Title);
                        var info = q.GetBookById(sample.Id);
                        Check(info != null, tag + "GetBookById works", info == null ? "null" : null);
                        var lines = q.GetLinesPaged(sample.Id, 5, 0);
                        Check(lines.Count > 0 && lines.Any(l => !string.IsNullOrEmpty(l.Content)), tag + "lines read: " + lines.Count,
                            lines.Count > 0 ? lines[0].Content : null);
                    }
                }
            }
            finally
            {
                try { SqliteConnection.ClearAllPools(); Directory.Delete(dir, recursive: true); } catch { }
            }

            Console.WriteLine(failures == 0 ? "ALL OK" : failures + " FAILURE(S)");
            return failures;
        }

        /// <summary>Turns an official-shaped library into the shape of an Otzarya personal
        /// library: no has* flags on book, a fileType column instead, no link tables.</summary>
        private static void MakePersonalShape(string path)
        {
            using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ConnectionString))
            {
                conn.Open();
                foreach (string col in new[] { "hasTeamim", "hasTargumConnection", "hasReferenceConnection", "hasCommentaryConnection", "hasOtherConnection" })
                    Exec(conn, "ALTER TABLE book DROP COLUMN " + col);
                Exec(conn, "ALTER TABLE book ADD COLUMN fileType TEXT");
                Exec(conn, "DROP TABLE IF EXISTS link");
            }
            SqliteConnection.ClearAllPools();
        }

        private static void Exec(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }
}
