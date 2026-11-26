using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using KitveiHakodesh.Core.Common;
using KitveiHakodesh.Core.SeforimDb;

namespace KitveiHakodesh.Core.UserAnnotations;

/// <summary>
/// Translates a book/line id of one seforim library into the id of the same book/line in
/// another. Libraries (Otzaria, Zayit, our download, and Otzaria across releases) assign ids
/// independently, so the stable key is the book TITLE (must be unique in both libraries) plus
/// the line's lineIndex within the book. Either database missing or unreadable = nothing maps.
/// Notes-book (virtual) ids are decoded, translated by their source book/line, re-encoded.
/// </summary>
internal sealed class LibraryIdMap : IDisposable
{
    private readonly SqliteConnection? _from;
    private readonly SqliteConnection? _to;
    private readonly Dictionary<long, long?> _books = new();

    public LibraryIdMap(string fromSeforimDbPath, string toSeforimDbPath)
    {
        _from = TryOpen(fromSeforimDbPath);
        _to = TryOpen(toSeforimDbPath);
    }

    private static SqliteConnection? TryOpen(string path)
    {
        if (!System.IO.File.Exists(path)) return null;
        try { return SqliteConnectionFactory.OpenCorpusProbe(path); }
        catch (SqliteException) { return null; }
    }

    public bool TryMapBook(long fromBook, out long toBook)
    {
        toBook = 0;
        bool notesBook = SeforimDbNoteIds.TryDecodeBook(fromBook, out long real);
        if (!notesBook) real = fromBook;
        if (MapRealBook(real) is not long mapped) return false;
        toBook = notesBook ? SeforimDbNoteIds.EncodeBook(mapped) : mapped;
        return true;
    }

    /// <summary>Line by its id: looks up the line's lineIndex in the old library.</summary>
    public bool TryMapLine(long fromBook, long fromLine, out long toBook, out long toLine)
    {
        toBook = toLine = 0;
        if (!TryMapBook(fromBook, out toBook)) return false;
        bool note = SeforimDbNoteIds.TryDecodeLine(fromLine, out long realLine, out int noteIndex);
        if (!note) realLine = fromLine;
        long realBook = SeforimDbNoteIds.SourceBookOf(fromBook);
        long realToBook = SeforimDbNoteIds.SourceBookOf(toBook);

        if (Scalar(_from, "SELECT lineIndex FROM line WHERE id = @a AND bookId = @b", realLine, realBook) is not long index) return false;
        if (Scalar(_to, "SELECT id FROM line WHERE bookId = @a AND lineIndex = @b", realToBook, index) is not long mapped) return false;
        toLine = note ? SeforimDbNoteIds.EncodeLine(mapped, noteIndex) : mapped;
        return true;
    }

    /// <summary>Line by its lineIndex (bookmarks store it): only the book needs translating.</summary>
    public bool TryMapLineByIndex(long fromBook, long lineIndex, out long toBook, out long toLine)
    {
        toLine = 0;
        if (!TryMapBook(fromBook, out toBook)) return false;
        if (SeforimDbNoteIds.IsVirtual(toBook)) return false;   // bookmarks are on real books
        if (Scalar(_to, "SELECT id FROM line WHERE bookId = @a AND lineIndex = @b", toBook, lineIndex) is not long mapped) return false;
        toLine = mapped;
        return true;
    }

    private long? MapRealBook(long fromBook)
    {
        if (_books.TryGetValue(fromBook, out var cached)) return cached;
        long? result = null;
        if (_from is not null && _to is not null && Text(_from, "SELECT title FROM book WHERE id = @a", fromBook) is string title
            && Count(_from, title) == 1 && Count(_to, title) == 1)
            result = Scalar(_to, "SELECT id FROM book WHERE title = @a", title, null) as long?;
        _books[fromBook] = result;
        return result;
    }

    private static long Count(SqliteConnection c, string title) =>
        Scalar(c, "SELECT count(*) FROM book WHERE title = @a", title, null) as long? ?? 0;

    private static string? Text(SqliteConnection c, string sql, object a)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@a", a);
        return cmd.ExecuteScalar() as string;
    }

    private static object? Scalar(SqliteConnection? c, string sql, object a, object? b)
    {
        if (c is null) return null;
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@a", a);
        if (b is not null) cmd.Parameters.AddWithValue("@b", b);
        object? v = cmd.ExecuteScalar();
        return v is DBNull ? null : v;
    }

    public void Dispose()
    {
        _from?.Dispose();
        _to?.Dispose();
    }
}
