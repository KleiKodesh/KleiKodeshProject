using KitveiHakodesh.Core.SeforimDb;
using KitveiHakodesh.Core.SeforimDbFullTextSearch;
using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.Tests;

/// <summary>
/// Footnotes as virtual notes books (issue #13) on a small synthetic schema-7 database: the id
/// encoding's boundaries, catalog order, a search finding a note word, the note's snippet text,
/// the hit mapping back to its source line, BOM stripping, unreadable rows counted on the search
/// path while indexing stays strict, and the id guard. Run: --notesbook-selftest
/// </summary>
internal static class NotesBookCheck
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
        Ids();
        string dir = Path.Combine(Path.GetTempPath(), "notesbook-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            SearchAndOrder(dir);
            StrictIndexing(dir);
            Guard(dir);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch { /* temp */ }
        }
        Console.WriteLine(_failures == 0 ? "notes-book selftest: PASS" : $"notes-book selftest: {_failures} FAILURES");
        return _failures;
    }

    private static void Ids()
    {
        const long R = SeforimDbNoteIds.ReservedStart;
        const long MaxSafe = (1L << 53) - 1;   // Number.MAX_SAFE_INTEGER: the frontend holds ids exactly
        const long LineCap = 1L << 36;
        Check(R == 4503599627370496L, "R = 2^52");
        Check(!SeforimDbNoteIds.IsVirtual(R - 1) && SeforimDbNoteIds.IsVirtual(R), "R-1 real, R virtual");
        Check(SeforimDbNoteIds.EncodeLine(LineCap - 1, 65535) == MaxSafe, "lineId 2^36-1 / note 65535 -> 2^53-1");
        Check(SeforimDbNoteIds.EncodeLine(0, 0) == R, "lineId 0 / note 0 -> R");
        long v = SeforimDbNoteIds.EncodeLine(10350062, 54);
        Check(SeforimDbNoteIds.TryDecodeLine(v, out long l, out int n) && l == 10350062 && n == 54, "line roundtrip");
        Check(SeforimDbNoteIds.TryDecodeLine(MaxSafe, out long lMax, out int nMax) && lMax == LineCap - 1 && nMax == 65535, "max roundtrip");
        Check(SeforimDbNoteIds.TryDecodeBook(SeforimDbNoteIds.EncodeBook(8440), out long b) && b == 8440, "book roundtrip");
        Check(SeforimDbNoteIds.EncodeBook(R - 1) == 2 * R - 1, "book R-1 -> 2^53-1");
        Check(Throws<ArgumentOutOfRangeException>(() => SeforimDbNoteIds.EncodeLine(LineCap, 0)), "lineId 2^36 rejected");
        Check(Throws<ArgumentOutOfRangeException>(() => SeforimDbNoteIds.EncodeLine(1, 65536)), "noteIndex 65536 rejected");
        Check(Throws<ArgumentOutOfRangeException>(() => SeforimDbNoteIds.EncodeBook(R)), "book R rejected");
        Check(!Throws<SeforimDbNoteIdRangeException>(() => SeforimDbNoteIds.EnsureRepresentable(R - 1, R - 1, LineCap - 1, 0, 65535)), "guard: R-1 ok");
        Check(Throws<SeforimDbNoteIdRangeException>(() => SeforimDbNoteIds.EnsureRepresentable(R, 1, 1, 0, 0)), "guard: line.id R rejected");
        Check(Throws<SeforimDbNoteIdRangeException>(() => SeforimDbNoteIds.EnsureRepresentable(1, R, 1, 0, 0)), "guard: book.id R rejected");
        Check(Throws<SeforimDbNoteIdRangeException>(() => SeforimDbNoteIds.EnsureRepresentable(1, 1, LineCap, 0, 0)), "guard: note lineId 2^36 rejected");
        Check(Throws<SeforimDbNoteIdRangeException>(() => SeforimDbNoteIds.EnsureRepresentable(1, 1, 1, -1, 0)), "guard: noteIndex -1 rejected");
        Check(Throws<SeforimDbNoteIdRangeException>(() => SeforimDbNoteIds.EnsureRepresentable(1, 1, 1, 0, 65536)), "guard: noteIndex 65536 rejected");
    }

    private static string CreateDb(string dir, string name, Action<SqliteConnection> fill)
    {
        string path = Path.Combine(dir, name + ".db");
        using var c = new SqliteConnection("Data Source=" + path + ";Pooling=False");
        c.Open();
        Exec(c, @"
            CREATE TABLE category (id INTEGER PRIMARY KEY, parentId INTEGER, title TEXT, level INTEGER, orderIndex INTEGER);
            CREATE TABLE book (id INTEGER PRIMARY KEY, categoryId INTEGER, title TEXT, orderIndex INTEGER, isBaseBook INTEGER);
            CREATE TABLE author (id INTEGER PRIMARY KEY, name TEXT);
            CREATE TABLE book_author (bookId INTEGER, authorId INTEGER);
            CREATE TABLE line (id INTEGER PRIMARY KEY, bookId INTEGER, lineIndex INTEGER, content TEXT);
            CREATE INDEX idx_line_book_index ON line(bookId, lineIndex);
            CREATE TABLE line_note (lineId INTEGER, noteIndex INTEGER, charPos INTEGER, hasMarker INTEGER, body BLOB,
                                    PRIMARY KEY (lineId, noteIndex));
            INSERT INTO category VALUES (1, NULL, 'root', 0, 0);
            INSERT INTO book VALUES (1, 1, 'first', 1, 0), (2, 1, 'second', 2, 0);");
        fill(c);
        return path;
    }

    private static void SearchAndOrder(string dir)
    {
        string db = CreateDb(dir, "notes", c => Exec(c, @"
            INSERT INTO line VALUES (10, 1, 0, 'alpha beta'), (11, 1, 1, 'gamma delta'), (12, 1, 2, 'epsilon'), (20, 2, 0, 'omega');
            INSERT INTO line_note VALUES (11, 1, 3, 1, char(65279) || 'zetanote kappa'), (11, 0, 1, 1, 'etanote'), (10, 0, 2, 0, 'thetanote');"));

        long n10 = SeforimDbNoteIds.EncodeLine(10, 0), n110 = SeforimDbNoteIds.EncodeLine(11, 0), n111 = SeforimDbNoteIds.EncodeLine(11, 1);
        using (var corpus = new SeforimDbFtsCorpus(db))
        {
            var order = corpus.ReadDocumentsInResultOrder(0).ToList();
            Check(string.Join(",", order.Select(d => d.Id)) == string.Join(",", new[] { 10L, 11, 12, n10, n110, n111, 20 }),
                  "order: book lines, its notes book in source order, next book");
            Check(order.First(d => d.Id == n111).Text == "zetanote kappa", "BOM stripped from a note body");
            Check(corpus.CountDocuments() == 7, "count includes notes");
            var resumed = corpus.ReadDocumentsInResultOrder(4).Select(d => d.Id).ToList();
            Check(resumed.Count == 3 && resumed[0] == n110, "resume skip lands inside the notes book");
            var ranges = corpus.BookPositionRanges();
            var notesBook = SeforimDbNoteIds.EncodeBook(1);
            Check(ranges.TryGetValue(notesBook, out var nr) && nr.First == 4 && nr.Count == 3, "notes book position range");
            Check(corpus.PositionOfLine(n111, ranges) == 6, "position of a note");
            Check(corpus.PositionOfLine(20, ranges) == 7, "position of a later real line unchanged");
            var placement = corpus.FetchPlacements(new[] { n110, 12L });
            Check(placement[n110].BookId == notesBook && placement[12].BookId == 1, "placements: virtual book for a note");
            var texts = corpus.FetchSnippetTexts(new[] { n111 }, new[] { n10 });
            Check(texts.Count == 2 && texts.All(t => t.Before == "" && t.After == ""), "snippet text of a note has no neighbour context");
            Check(texts.First(t => t.LineId == n10).Text == "thetanote", "snippet text = note text");
        }

        string index = Path.Combine(dir, "index-notes");
        using var engine = new SeforimDbFtsEngine(index, db);
        Check(new SeforimDbFtsIndexer(index, db).Build(engine), "index build with notes");

        var hits = Search(engine, "zetanote", out var unreadable);
        Check(hits.Count == 1 && hits[0].LineId == n111, "search finds a note word");
        if (hits.Count == 1)
        {
            var h = hits[0];
            Check(h.IsNote && h.SourceLineId == 11 && h.NoteIndex == 1 && h.SourceBookId == 1 && h.BookId == SeforimDbNoteIds.EncodeBook(1),
                  "hit maps back to its source line / note / book");
        }
        Check(Search(engine, "gamma", out _).Single().IsNote == false, "plain hit is not a note");
        Check(unreadable.Count == 0, "no unreadable rows on a clean db");

        // A row that went bad after the build: the search counts it instead of failing.
        using (var c = new SqliteConnection("Data Source=" + db + ";Pooling=False"))
        {
            c.Open();
            Exec(c, "UPDATE line SET content = X'28B52FFD00' WHERE id = 11; UPDATE line_note SET body = X'28B52FFD00' WHERE lineId = 10;");
        }
        SqliteConnection.ClearAllPools();
        var afterHits = Search(engine, "gamma", out var bad);
        Check(afterHits.Count == 0 && bad.Count == 1 && bad.LineIds()[0] == 11, "unreadable line counted, search not aborted");
        Check(bad.FirstError?.Contains("Line 11") == true, "error names the line");
        Search(engine, "thetanote", out var badNote);
        Check(badNote.Count == 1 && badNote.LineIds()[0] == n10, "unreadable note counted by its virtual id");
        using (var corpus = new SeforimDbFtsCorpus(db))
        {
            var t = corpus.FetchSnippetTexts(new[] { 11L }, Array.Empty<long>());
            Check(t.Count == 1 && t[0].Unreadable && t[0].Text == "", "snippet text of an unreadable row is flagged");
        }
    }

    private static void StrictIndexing(string dir)
    {
        string db = CreateDb(dir, "strict", c => Exec(c, @"
            INSERT INTO line VALUES (10, 1, 0, 'alpha');
            INSERT INTO line_note VALUES (10, 0, 0, 0, X'28B52FFD00');"));
        using var corpus = new SeforimDbFtsCorpus(db);
        try { corpus.ReadDocumentsInResultOrder(0).ToList(); Check(false, "indexing an unreadable note throws"); }
        catch (SeforimDbContentCodecException e) { Check(e.Message.Contains("line 10"), "indexing stays strict, error names the note's line"); }
    }

    private static void Guard(string dir)
    {
        string db = CreateDb(dir, "guard", c => Exec(c, @"
            INSERT INTO line VALUES (4503599627370496, 1, 0, 'alpha');
            INSERT INTO line_note VALUES (5, 0, 0, 0, 'x');"));
        using var corpus = new SeforimDbFtsCorpus(db);
        try { corpus.ReadDocumentsInResultOrder(0).ToList(); Check(false, "guard fails the build"); }
        catch (SeforimDbNoteIdRangeException e) { Check(e.Message.Contains("4503599627370496"), "guard fails the build naming the id"); }
    }

    private static List<FtsHit> Search(SeforimDbFtsEngine engine, string query, out SeforimDbFtsUnreadableRows unreadable)
    {
        unreadable = new SeforimDbFtsUnreadableRows();
        var hits = new List<FtsHit>();
        foreach (var batch in new SeforimDbFtsSearcher().SearchInBatches(engine, query, 50, false, 5, new FtsTermTable(), unreadable: unreadable))
            hits.AddRange(batch.Hits);
        return hits;
    }

    private static bool Throws<T>(Action a) where T : Exception
    {
        try { a(); return false; } catch (T) { return true; }
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
