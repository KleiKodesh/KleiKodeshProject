using KitveiHakodesh.Core.SeforimDb;
using KitveiHakodesh.Core.UserAnnotations;
using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.Tests;

/// <summary>
/// Library switch merges user_settings.db: two synthetic libraries whose book/line ids differ,
/// rows translated by title + lineIndex, destination rows untouched, duplicates skipped,
/// untranslatable rows counted, a second run adds nothing. Run: --userdb-carryover-selftest
/// </summary>
internal static class UserDbCarryOverCheck
{
    private static int _failures;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _failures++;
    }

    public static int Run()
    {
        _failures = 0;
        string dir = Path.Combine(Path.GetTempPath(), "userdb-carryover-" + Guid.NewGuid().ToString("N"));
        try
        {
            string oldLib = Library(Path.Combine(dir, "old"), (10, "A"), (11, "B"), (12, "Dup"), (13, "Dup"));
            string newLib = Library(Path.Combine(dir, "new"), (20, "B"), (21, "A"));   // ids differ, A lineIds differ
            // old line id = book*100 + index; new line id = book*1000 + index

            // Destination missing: merged into a fresh file.
            string oldUser = UserAnnotationStore.DeriveDatabasePath(oldLib);
            new UserAnnotationStore(oldUser).EnsureCreated();
            Sql(oldUser,
                "INSERT INTO user_highlights (bookId,lineId,startOffset,endOffset,colorArgb,createdAt) VALUES (10,1003,0,5,1,1),(12,1201,0,1,1,1),(11,1102,2,4,7,1);" +
                "INSERT INTO user_notes (bookId,lineId,startOffset,endOffset,note,quote,createdAt,updatedAt) VALUES (10,1001,0,2,'n','q',1,1);" +
                "INSERT INTO user_bookmarks (owner,bookmarkId,lineId,lineIndex,label) VALUES ('10',5,1002,2,'x'),('pdf:c:/a.pdf',6,0,0,'p'),('99',7,0,0,'gone');" +
                $"INSERT INTO user_highlights (bookId,lineId,startOffset,endOffset,colorArgb,createdAt) VALUES ({SeforimDbNoteIds.EncodeBook(10)},{SeforimDbNoteIds.EncodeLine(1003, 4)},0,1,1,1);" +
                "CREATE TABLE user_editions (id INTEGER PRIMARY KEY AUTOINCREMENT, bookId INTEGER NOT NULL, kind TEXT NOT NULL, filePath TEXT NULL, hbId TEXT NULL, title TEXT NOT NULL, addedAt INTEGER NOT NULL);" +
                "INSERT INTO user_editions (bookId,kind,filePath,hbId,title,addedAt) VALUES (11,'hb',NULL,'123','t',1);");

            string newUser = UserAnnotationStore.DeriveDatabasePath(newLib);
            // Pre-existing destination row: same range on book B (new id 20), different colour.
            new UserAnnotationStore(newUser).EnsureCreated();
            Sql(newUser, "INSERT INTO user_highlights (bookId,lineId,startOffset,endOffset,colorArgb,createdAt) VALUES (20,20002,2,4,9,1);");

            var r = UserAnnotationDbCarryOver.MergeInto(oldLib, newLib)!;
            Console.WriteLine("  " + r);
            var h = r.Tables.First(t => t.Table == "user_highlights");
            Check(h.Merged == 2 && h.AlreadyThere == 1 && h.Unmatched == 1, "highlights: A + notes-book merged, B already there, Dup unmatched");
            Check(Scalar(newUser, "SELECT count(*) FROM user_highlights WHERE bookId=21 AND lineId=21003") == 1, "A line 3 remapped to new ids");
            Check(Scalar(newUser, $"SELECT count(*) FROM user_highlights WHERE bookId={SeforimDbNoteIds.EncodeBook(21)} AND lineId={SeforimDbNoteIds.EncodeLine(21003, 4)}") == 1, "notes-book ids re-encoded");
            Check(Scalar(newUser, "SELECT colorArgb FROM user_highlights WHERE bookId=20") == 9, "destination row untouched");
            Check(Scalar(newUser, "SELECT count(*) FROM user_notes WHERE bookId=21 AND lineId=21001") == 1, "note remapped");
            var b = r.Tables.First(t => t.Table == "user_bookmarks");
            Check(b.Merged == 2 && b.Unmatched == 1, "bookmarks: book + pdf merged, missing book unmatched");
            Check(Scalar(newUser, "SELECT lineId FROM user_bookmarks WHERE owner='21' AND bookmarkId=5") == 21002, "bookmark owner + lineId remapped");
            Check(Scalar(newUser, "SELECT count(*) FROM user_editions WHERE bookId=20 AND hbId='123'") == 1, "edition remapped");

            var again = UserAnnotationDbCarryOver.MergeInto(oldLib, newLib)!;
            Check(again.Tables.All(t => t.Merged == 0), "second run merges nothing");

            // Destination missing entirely.
            string fresh = Library(Path.Combine(dir, "fresh"), (30, "A"));
            var f = UserAnnotationDbCarryOver.MergeInto(oldLib, fresh)!;
            Check(f.Tables.First(t => t.Table == "user_highlights").Merged == 2, "missing destination created and merged");
            Check(UserAnnotationDbCarryOver.MergeInto(Path.Combine(dir, "none", "seforim.db"), newLib) is null, "no source file -> null");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch { /* temp */ }
        }
        Console.WriteLine(_failures == 0 ? "userdb carry-over selftest: PASS" : $"userdb carry-over selftest: {_failures} FAILURES");
        return _failures;
    }

    /// <summary>A seforim.db with the given books, each with lines 0..4 (old style ids book*100+i
    /// for ids under 20, book*1000+i otherwise, so the two libraries never share line ids).</summary>
    private static string Library(string folder, params (long Id, string Title)[] books)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "seforim.db");
        var sql = new System.Text.StringBuilder("CREATE TABLE book (id INTEGER PRIMARY KEY, title TEXT);CREATE TABLE line (id INTEGER PRIMARY KEY, bookId INTEGER, lineIndex INTEGER);");
        foreach (var (id, title) in books)
        {
            sql.Append($"INSERT INTO book VALUES ({id},'{title}');");
            for (int i = 0; i < 5; i++) sql.Append($"INSERT INTO line VALUES ({(id < 20 ? id * 100 : id * 1000) + i},{id},{i});");
        }
        Sql(path, sql.ToString());
        return path;
    }

    private static void Sql(string path, string sql)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static long Scalar(string path, string sql)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar() ?? -1L);
    }
}
