using KitveiHakodesh.Core.SeforimDb;
using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.Tests;

/// <summary>
/// Publisher protection + banner (Otzaria schema 7 book_protection / banner / book_banner) on small
/// synthetic databases: tables missing → nothing protected, levels 0/1/2/5, strictest of many,
/// banner {title} expansion. Run: --bookprotection-selftest
/// </summary>
internal static class BookProtectionCheck
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
        string dir = Path.Combine(AppContext.BaseDirectory, "bookprotection-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Rules();
            NoTables(dir);
            WithTables(dir);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch { /* scratch */ }
        }
        Console.WriteLine(_failures == 0 ? "book-protection selftest: PASS" : $"book-protection selftest: {_failures} FAILURES");
        return _failures;
    }

    private static void Rules()
    {
        var none = BookProtection.ForLevel(0);
        Check(none.AllowsEditableExport && none.AllowsPdfExport && none.MaxCopyLines == null && none.MaxPrintLines == null, "level 0: no limits");
        var one = BookProtection.ForLevel(1);
        Check(!one.AllowsEditableExport && one.AllowsPdfExport && one.MaxCopyLines == 40 && one.MaxPrintLines == null, "level 1: no editable export, copy 40");
        var two = BookProtection.ForLevel(2);
        Check(!two.AllowsEditableExport && !two.AllowsPdfExport && two.MaxCopyLines == 40 && two.MaxPrintLines == 40, "level 2: also no PDF, print 40");
        Check(BookProtection.ForLevel(5).Level == 2, "level 5 treated as 2");
        Check(BookProtection.ForLevel(-3).Level == 0, "negative = none");
        Check(BookProtection.Strictest(new[] { null, one, two, none }).Level == 2, "strictest of many = 2");
        Check(BookProtection.Strictest(Array.Empty<BookProtection>()).Level == 0, "strictest of none = 0");
    }

    private static void NoTables(string dir)
    {
        var books = new SeforimDbQueries(CreateDb(dir, "schema6", _ => { })).GetAllBooks();
        Check(books.Count == 3 && books.All(b => b.Protection == null && b.PublisherBanner == null), "schema 6: every book unprotected, no banner");
    }

    private static void WithTables(string dir)
    {
        var books = new SeforimDbQueries(CreateDb(dir, "schema7", c => Exec(c, @"
            CREATE TABLE book_protection (bookId INTEGER PRIMARY KEY, level INTEGER NOT NULL);
            CREATE TABLE banner (id INTEGER PRIMARY KEY, text TEXT NOT NULL);
            CREATE TABLE book_banner (bookId INTEGER PRIMARY KEY, bannerId INTEGER NOT NULL);
            INSERT INTO book_protection VALUES (1, 0), (2, 1), (3, 5);
            INSERT INTO banner VALUES (1, 'By [the publisher](https://x.org/?b={title}) - {title}');
            INSERT INTO book_banner VALUES (2, 1);"))).GetAllBooks().ToDictionary(b => b.Id);
        Check(books[1].Protection == null, "level 0 row: unprotected");
        Check(books[2].Protection?.Level == 1, "level 1 row");
        Check(books[3].Protection?.Level == 2 && books[3].Protection!.MaxPrintLines == 40, "level 5 row: treated as 2");
        Check(books[2].PublisherBanner == "By [the publisher](https://x.org/?b=second%20book) - second book", "banner: {title} expanded, encoded in link");
        Check(books[1].PublisherBanner == null, "no banner row: null");
        // Through Core's source-generated resolver (what the AOT service uses); the frontend
        // camelCases the keys on decode.
        var options = MessagePack.MessagePackSerializerOptions.Standard.WithResolver(
            MessagePack.Resolvers.CompositeResolver.Create(global::KitveiHakodesh.Core.CoreMessagePackResolver.Instance, MessagePack.Resolvers.StandardResolver.Instance));
        string wire = MessagePack.MessagePackSerializer.ConvertToJson(MessagePack.MessagePackSerializer.Serialize(books[2], options));
        Check(wire.Contains("\"Protection\":{\"Level\":1,\"AllowsEditableExport\":false", StringComparison.Ordinal)
            && wire.Contains("\"MaxCopyLines\":40", StringComparison.Ordinal)
            && wire.Contains("\"PublisherBanner\":\"By [the publisher]", StringComparison.Ordinal), "wire: protection + publisherBanner on the catalog row");
    }

    private static string CreateDb(string dir, string name, Action<SqliteConnection> fill)
    {
        string path = Path.Combine(dir, name + ".db");
        using var c = new SqliteConnection("Data Source=" + path + ";Pooling=False");
        c.Open();
        Exec(c, @"
            CREATE TABLE category (id INTEGER PRIMARY KEY, parentId INTEGER, title TEXT, level INTEGER, orderIndex INTEGER);
            CREATE TABLE book (id INTEGER PRIMARY KEY, categoryId INTEGER, title TEXT, orderIndex INTEGER, hasTeamim INTEGER);
            CREATE TABLE author (id INTEGER PRIMARY KEY, name TEXT);
            CREATE TABLE book_author (bookId INTEGER, authorId INTEGER);
            INSERT INTO category VALUES (1, NULL, 'root', 0, 0);
            INSERT INTO book VALUES (1, 1, 'first', 1, 0), (2, 1, 'second book', 2, 0), (3, 1, 'third', 3, 0);");
        fill(c);
        return path;
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
