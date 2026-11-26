using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using KitveiHakodesh.Core.Common;
using KitveiHakodesh.Core.UserEditions;

namespace KitveiHakodesh.Core.UserAnnotations;

/// <summary>
/// The user's highlights, notes, bookmarks and editions live in <c>Settings\user_settings.db</c>
/// NEXT TO the seforim database (<see cref="UserAnnotationStore.DeriveDatabasePath"/>), so
/// switching libraries would leave them behind in the old folder. On a switch the old file's
/// rows are MERGED into the new library's file: rows the new file lacks are added, rows it
/// already has are left alone. Destination rows are never changed or deleted.
///
/// BOOK/LINE IDS DIFFER BETWEEN LIBRARIES. Otzaria, Zayit and our own download are generated
/// separately (and Otzaria's own ids move between releases), so a bookId/lineId from the old
/// library is translated through both seforim.db files: book by its title (unique in both),
/// line by its lineIndex in that book. A row that cannot be translated is skipped and counted.
/// Notes-book (virtual) ids are decoded to their source book/line, translated, re-encoded.
/// </summary>
public static class UserAnnotationDbCarryOver
{
    /// <summary>Per table: rows added, rows the destination already had, rows whose book/line
    /// could not be found in the new library.</summary>
    public readonly record struct TableCount(string Table, int Merged, int AlreadyThere, int Unmatched);

    public sealed record Result(string Source, string Destination, IReadOnlyList<TableCount> Tables)
    {
        public override string ToString()
        {
            var parts = new List<string>(Tables.Count);
            foreach (var t in Tables)
                parts.Add($"{t.Table}: {t.Merged} merged, {t.AlreadyThere} already there, {t.Unmatched} unmatched");
            return string.Join("; ", parts);
        }
    }

    /// <summary>Merges the previous library's user database into the new library's (creating
    /// it when missing). Null when there is nothing to do (no source file, same file). Throws on
    /// an IO/SQLite failure; the merge is one transaction, so a failure adds nothing.</summary>
    public static Result? MergeInto(string? previousSeforimDbPath, string? newSeforimDbPath)
    {
        if (string.IsNullOrWhiteSpace(previousSeforimDbPath) || string.IsNullOrWhiteSpace(newSeforimDbPath)) return null;

        string source = UserAnnotationStore.DeriveDatabasePath(previousSeforimDbPath!);
        string destination = UserAnnotationStore.DeriveDatabasePath(newSeforimDbPath!);
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) return null;
        if (!File.Exists(source)) return null;

        new UserAnnotationStore(destination).EnsureCreated();

        using var ids = new LibraryIdMap(previousSeforimDbPath!, newSeforimDbPath!);
        using var connection = SqliteConnectionFactory.OpenUserData(destination);
        Exec(connection, UserEditionStore.SchemaSql);

        using (var attach = connection.CreateCommand())
        {
            attach.CommandText = "ATTACH DATABASE @path AS src";
            attach.Parameters.AddWithValue("@path", source);
            attach.ExecuteNonQuery();
        }
        try
        {
            var sourceTables = SourceTables(connection);
            var tables = new List<TableCount>(4);
            using (var tx = connection.BeginTransaction())
            {
                tables.Add(sourceTables.Contains("user_highlights") ? MergeHighlights(connection, tx, ids) : new TableCount("user_highlights", 0, 0, 0));
                tables.Add(sourceTables.Contains("user_notes") ? MergeNotes(connection, tx, ids) : new TableCount("user_notes", 0, 0, 0));
                tables.Add(sourceTables.Contains("user_bookmarks") ? MergeBookmarks(connection, tx, ids) : new TableCount("user_bookmarks", 0, 0, 0));
                tables.Add(sourceTables.Contains("user_editions") ? MergeEditions(connection, tx, ids) : new TableCount("user_editions", 0, 0, 0));
                tx.Commit();
            }
            return new Result(source, destination, tables);
        }
        finally
        {
            Exec(connection, "DETACH DATABASE src");
        }
    }

    private static HashSet<string> SourceTables(SqliteConnection c)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT name FROM src.sqlite_master WHERE type = 'table'";
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetString(0));
        return set;
    }

    private static TableCount MergeHighlights(SqliteConnection c, SqliteTransaction tx, LibraryIdMap ids)
    {
        var rows = ReadAll(c, tx, "SELECT bookId, lineId, startOffset, endOffset, colorArgb, createdAt FROM src.user_highlights ORDER BY id");
        int merged = 0, there = 0, unmatched = 0;
        foreach (var row in rows)
        {
            if (!ids.TryMapLine(Long(row[0]), Long(row[1]), out long book, out long line)) { unmatched++; continue; }
            // Same range on the same line = already there (the destination's colour wins).
            if (Exists(c, tx, "SELECT 1 FROM main.user_highlights WHERE bookId=@b AND lineId=@l AND startOffset=@s AND endOffset=@e",
                    ("@b", book), ("@l", line), ("@s", row[2]), ("@e", row[3]))) { there++; continue; }
            Run(c, tx, "INSERT INTO main.user_highlights (bookId, lineId, startOffset, endOffset, colorArgb, createdAt) VALUES (@b,@l,@s,@e,@c,@t)",
                ("@b", book), ("@l", line), ("@s", row[2]), ("@e", row[3]), ("@c", row[4]), ("@t", row[5]));
            merged++;
        }
        return new TableCount("user_highlights", merged, there, unmatched);
    }

    private static TableCount MergeNotes(SqliteConnection c, SqliteTransaction tx, LibraryIdMap ids)
    {
        var rows = ReadAll(c, tx, "SELECT bookId, lineId, startOffset, endOffset, note, quote, createdAt, updatedAt FROM src.user_notes ORDER BY id");
        int merged = 0, there = 0, unmatched = 0;
        foreach (var row in rows)
        {
            if (!ids.TryMapLine(Long(row[0]), Long(row[1]), out long book, out long line)) { unmatched++; continue; }
            if (Exists(c, tx, "SELECT 1 FROM main.user_notes WHERE bookId=@b AND lineId=@l AND startOffset=@s AND endOffset=@e AND note=@n",
                    ("@b", book), ("@l", line), ("@s", row[2]), ("@e", row[3]), ("@n", row[4]))) { there++; continue; }
            Run(c, tx, "INSERT INTO main.user_notes (bookId, lineId, startOffset, endOffset, note, quote, createdAt, updatedAt) VALUES (@b,@l,@s,@e,@n,@q,@c,@u)",
                ("@b", book), ("@l", line), ("@s", row[2]), ("@e", row[3]), ("@n", row[4]), ("@q", row[5]), ("@c", row[6]), ("@u", row[7]));
            merged++;
        }
        return new TableCount("user_notes", merged, there, unmatched);
    }

    private static TableCount MergeBookmarks(SqliteConnection c, SqliteTransaction tx, LibraryIdMap ids)
    {
        var rows = ReadAll(c, tx,
            "SELECT owner, bookmarkId, lineId, lineIndex, letterOffset, selectionLetterCount, label, tocPath, snippet, pdfTop FROM src.user_bookmarks ORDER BY id");
        int merged = 0, there = 0, unmatched = 0;
        foreach (var row in rows)
        {
            string owner = (string)row[0]!;
            object? lineId = row[2];
            // A library book's owner is its id as text; anything else ('pdf:<path>') is library-independent.
            if (long.TryParse(owner, NumberStyles.None, CultureInfo.InvariantCulture, out long srcBook))
            {
                if (!ids.TryMapLineByIndex(srcBook, Long(row[3]), out long book, out long line)) { unmatched++; continue; }
                owner = book.ToString(CultureInfo.InvariantCulture);
                lineId = line;
            }
            if (Exists(c, tx, "SELECT 1 FROM main.user_bookmarks WHERE owner=@o AND bookmarkId=@k", ("@o", owner), ("@k", row[1]))) { there++; continue; }
            Run(c, tx,
                "INSERT INTO main.user_bookmarks (owner, bookmarkId, lineId, lineIndex, letterOffset, selectionLetterCount, label, tocPath, snippet, pdfTop) " +
                "VALUES (@o,@k,@l,@i,@lo,@sc,@la,@tp,@sn,@pt)",
                ("@o", owner), ("@k", row[1]), ("@l", lineId), ("@i", row[3]), ("@lo", row[4]), ("@sc", row[5]),
                ("@la", row[6]), ("@tp", row[7]), ("@sn", row[8]), ("@pt", row[9]));
            merged++;
        }
        return new TableCount("user_bookmarks", merged, there, unmatched);
    }

    private static TableCount MergeEditions(SqliteConnection c, SqliteTransaction tx, LibraryIdMap ids)
    {
        var rows = ReadAll(c, tx, "SELECT bookId, kind, filePath, hbId, title, addedAt FROM src.user_editions ORDER BY id");
        int merged = 0, there = 0, unmatched = 0;
        foreach (var row in rows)
        {
            if (!ids.TryMapBook(Long(row[0]), out long book)) { unmatched++; continue; }
            // Same identity as UserEditionStore.Add: book + kind + file path / HebrewBooks id.
            if (Exists(c, tx, "SELECT 1 FROM main.user_editions WHERE bookId=@b AND kind=@k AND filePath IS @f AND hbId IS @h",
                    ("@b", book), ("@k", row[1]), ("@f", row[2]), ("@h", row[3]))) { there++; continue; }
            Run(c, tx, "INSERT INTO main.user_editions (bookId, kind, filePath, hbId, title, addedAt) VALUES (@b,@k,@f,@h,@t,@a)",
                ("@b", book), ("@k", row[1]), ("@f", row[2]), ("@h", row[3]), ("@t", row[4]), ("@a", row[5]));
            merged++;
        }
        return new TableCount("user_editions", merged, there, unmatched);
    }

    private static long Long(object? v) => Convert.ToInt64(v, CultureInfo.InvariantCulture);

    private static List<object?[]> ReadAll(SqliteConnection c, SqliteTransaction tx, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        var rows = new List<object?[]>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var row = new object?[r.FieldCount];
            for (int i = 0; i < row.Length; i++) row[i] = r.IsDBNull(i) ? null : r.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    private static bool Exists(SqliteConnection c, SqliteTransaction tx, string sql, params (string Name, object? Value)[] ps)
    {
        using var cmd = Prepare(c, tx, sql, ps);
        return cmd.ExecuteScalar() is not null;
    }

    private static void Run(SqliteConnection c, SqliteTransaction tx, string sql, params (string Name, object? Value)[] ps)
    {
        using var cmd = Prepare(c, tx, sql, ps);
        cmd.ExecuteNonQuery();
    }

    private static SqliteCommand Prepare(SqliteConnection c, SqliteTransaction tx, string sql, (string Name, object? Value)[] ps)
    {
        var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in ps) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
