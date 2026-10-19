using KitveiHakodesh.Core.SeforimDb;
using Microsoft.Data.Sqlite;
using Cat = KitveiHakodesh.Core.SeforimDb.SeforimDbBookTreeOrder.CategoryInput;
using Bk = KitveiHakodesh.Core.SeforimDb.SeforimDbBookTreeOrder.BookInput;

namespace KitveiHakodeshService.Tests;

/// <summary>
/// Checks for the ONE tree-order computation (Core's SeforimDbBookTreeOrder).
///
///   --treeorder-selftest        fixture tree: base text before its mefarshim subfolder,
///                               non-base own books after subfolders, folder-named book
///                               counts as base, dependent book does not, custom last,
///                               orphans last.
///   --treeorder-dump out.tsv    bookId TAB treeOrder for the real DB, for diffing against
///                               an independent simulation.
/// </summary>
public static class TreeOrderCheck
{
    // "sefer" and "mefarshim".
    private const string Sefer = "ספר";
    private const string Mefarshim = "מפרשים";

    public static int SelfTest()
    {
        int failures = 0;
        void Expect(bool ok, string what)
        {
            if (!ok) { failures++; Console.Error.WriteLine("  FAIL: " + what); }
            else Console.WriteLine("  ok:   " + what);
        }

        // Load order = (level, siblings with negative orderIndex last). Folders:
        //   1 Halacha
        //     2 Tur            (own: 100 Tur [base], 101 notes-on-Tur [not base])
        //       3 Mefarshim    (own: 102, 103)
        //     4 Mitzvot        (own: 110 "sefer hamitzvot" [not flagged, title == folder], 111 Chinuch)
        //       5 "sefer hamitzvot" folder? no - 4 IS the folder titled like 110
        //     6 Rishonim       (own: 120 one-off)   subfolders: 7 Rashi (own: 121, 122)
        //   8 Custom root (-1)   (own: 130)
        //   orphan book 140 (category 999 missing)
        //   9 "mishnah berurah" folder with 150 "mishnah berurah" [dependent] and sub 10 (own: 151)
        const string MitzvotTitle = Sefer + " המצוות";   // sefer hamitzvot
        const string MbTitle = "משנה ברורה"; // mishnah berurah
        var categories = new List<Cat>
        {
            new(1, null, "Halacha"), new(-1, null, "Custom"),
            new(2, 1, "Tur"), new(4, 1, MitzvotTitle), new(6, 1, "Rishonim"), new(9, 1, MbTitle),
            new(3, 2, Mefarshim), new(11, 4, Mefarshim), new(7, 6, "Rashi"), new(10, 9, Mefarshim),
        };
        var books = new List<Bk>
        {
            new(100, 2, "Tur", IsBaseBook: true, IsDependent: false),
            new(101, 2, "Notes on Tur", false, true),
            new(102, 3, "Beit Yosef", false, true),
            new(103, 3, "Bach", false, true),
            new(110, 4, MitzvotTitle, false, false),
            new(111, 4, "Chinuch", false, false),
            new(112, 11, "Commentary on Hamitzvot", false, true),
            new(120, 6, "One-off", false, false),
            new(121, 7, "Rashi Berachot", false, true),
            new(122, 7, "Rashi Shabbat", false, true),
            new(130, -1, "Custom book", false, false),
            new(140, 999, "Orphan", false, false),
            new(150, 9, MbTitle, false, true),
            new(151, 10, "Shaar Hatziyun", false, true),
        };

        var o = SeforimDbBookTreeOrder.Compute(categories, books);
        Expect(o.Count == books.Count, "every book numbered");
        Expect(o.Values.Distinct().Count() == books.Count, "numbers unique");
        Expect(o[100] < o[102] && o[100] < o[103], "base text (flag) before its mefarshim subfolder");
        Expect(o[102] < o[101], "non-base own book after the subfolder");
        Expect(o[110] < o[112] && o[112] < o[111], "folder-named unflagged book is base: before its subfolder, sibling after it");
        Expect(o[121] < o[120] && o[122] < o[120], "commentary folder with no base: subfolders first, one-off last");
        Expect(o[151] < o[150], "dependent book titled like its folder is NOT promoted");
        Expect(o[130] > o[122] && o[130] > o[120] && o[130] > o[151], "custom root last among roots");
        Expect(o[140] > o[130], "orphan after everything");
        Expect(o[100] == 0, "very first book is the first folder's base text");

        Console.WriteLine(failures == 0 ? "tree-order selftest: PASS" : $"tree-order selftest: {failures} FAILURES");
        return failures;
    }

    public static int Dump(string dbPath, string outPath)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly,
        }.ConnectionString);
        conn.Open();
        var order = SeforimDbBookTreeOrder.Load(conn);
        using var w = new StreamWriter(outPath);
        foreach (var kv in order.OrderBy(kv => kv.Value))
            w.WriteLine($"{kv.Key}\t{kv.Value}");
        Console.WriteLine($"wrote {order.Count} rows to {outPath}");
        return 0;
    }

    public static bool ColumnExists(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (string.Equals(r.GetString(1), column, StringComparison.Ordinal)) return true;
        return false;
    }
}
