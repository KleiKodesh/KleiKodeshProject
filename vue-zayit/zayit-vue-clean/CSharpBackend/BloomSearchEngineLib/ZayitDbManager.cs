using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

public sealed class ZayitDbManager : IDisposable
{
    public readonly SQLiteConnection _connection;

    public ZayitDbManager() : this(null) { }

    public ZayitDbManager(string dbPath)
    {
        if (dbPath == null)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string defaultPath = Path.Combine(appData, "io.github.kdroidfilter.seforimapp", "databases", "seforim.db");
            dbPath = Interaction.GetSetting("ZayitApp", "Database", "Path", defaultPath);
        }
        Console.WriteLine("[ZayitDbManager] Opening DB at: " + dbPath + " exists=" + File.Exists(dbPath));
        if (!File.Exists(dbPath)) return;

        _connection = new SQLiteConnection($"Data Source={dbPath};Version=3;Cache Size=10000;Page Size=4096;");
        _connection.Open();

        try
        {
            using (var cmd = _connection.CreateCommand())
            { cmd.CommandText = "PRAGMA journal_mode=WAL;"; cmd.ExecuteNonQuery(); }
        }
        catch (Exception ex) { Console.WriteLine("[ZayitDbManager] WAL mode failed: " + ex.Message); }
    }

    public int GetLineCount()
    {
        try { using (var cmd = _connection.CreateCommand()) { cmd.CommandText = "SELECT COUNT(*) FROM line"; return (int)(long)cmd.ExecuteScalar(); } }
        catch (Exception ex) { Console.WriteLine("[ZayitDbManager] GetLineCount: " + ex.Message); return 0; }
    }

    public IEnumerable<string> GetAllLineContents()
    {
        SQLiteCommand cmd = null;
        SQLiteDataReader reader = null;
        try { cmd = _connection.CreateCommand(); cmd.CommandText = "SELECT content FROM line ORDER BY id"; reader = cmd.ExecuteReader(); }
        catch (Exception ex) { Console.WriteLine("[ZayitDbManager] GetAllLineContents: " + ex.Message); cmd?.Dispose(); yield break; }
        using (reader) using (cmd) { while (reader.Read()) yield return reader.GetString(0); }
    }

    public IEnumerable<(int id, string content)> GetLineContentsChunk(int chunkNumber, int chunkSize)
    {
        int startId = chunkNumber * chunkSize, endId = startId + chunkSize - 1;
        var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, content FROM line WHERE id BETWEEN @s AND @e ORDER BY id";
        cmd.Parameters.AddWithValue("@s", startId);
        cmd.Parameters.AddWithValue("@e", endId);
        SQLiteDataReader reader = null;
        try { reader = cmd.ExecuteReader(); }
        catch (Exception ex) { Console.WriteLine("[ZayitDbManager] GetLineContentsChunk " + chunkNumber + ": " + ex.Message); cmd.Dispose(); yield break; }
        using (reader) using (cmd) { while (reader.Read()) yield return (reader.GetInt32(0), reader.GetString(1)); }
    }

    public string GetLineContent(int lineId)
    {
        try
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT content FROM line WHERE id = @id LIMIT 1";
                cmd.Parameters.AddWithValue("@id", lineId);
                using (var r = cmd.ExecuteReader()) { if (r.Read()) return r.GetString(0); }
            }
            return string.Empty;
        }
        catch (Exception ex) { Console.WriteLine("[ZayitDbManager] GetLineContent " + lineId + ": " + ex.Message); return string.Empty; }
    }

    public (int bookId, string bookTitle, string tocText) GetLineMetadata(int lineId)
    {
        try
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT l.bookId, b.title, COALESCE(tt.text, '')
                    FROM line l
                    INNER JOIN book b ON l.bookId = b.id
                    LEFT JOIN line_toc lt ON l.id = lt.lineId
                    LEFT JOIN tocEntry te ON lt.tocEntryId = te.id
                    LEFT JOIN tocText tt ON te.textId = tt.id
                    WHERE l.id = @id LIMIT 1";
                cmd.Parameters.AddWithValue("@id", lineId);
                using (var r = cmd.ExecuteReader())
                    if (r.Read()) return (r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2));
            }
            return (0, "", "");
        }
        catch (Exception ex) { Console.WriteLine("[ZayitDbManager] GetLineMetadata " + lineId + ": " + ex.Message); return (0, "", ""); }
    }

    public (int lineIndex, int bookId) GetLineIndexFromLineId(int lineId)
    {
        try
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT lineIndex, bookId FROM line WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", lineId);
                using (var r = cmd.ExecuteReader()) if (r.Read()) return (r.GetInt32(0), r.GetInt32(1));
            }
            return (-1, -1);
        }
        catch (Exception ex) { Console.WriteLine("[ZayitDbManager] GetLineIndexFromLineId " + lineId + ": " + ex.Message); return (-1, -1); }
    }

    public List<int> GetLineIdsByTocEntry(int tocEntryId)
    {
        var ids = new List<int>();
        try
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT lineId FROM line_toc WHERE tocEntryId = @id ORDER BY lineId";
                cmd.Parameters.AddWithValue("@id", tocEntryId);
                using (var r = cmd.ExecuteReader()) while (r.Read()) ids.Add(r.GetInt32(0));
            }
        }
        catch (Exception ex) { Console.WriteLine("[ZayitDbManager] GetLineIdsByTocEntry " + tocEntryId + ": " + ex.Message); }
        return ids;
    }

    public List<(int lineIndex, string content)> GetLinesByIds(int bookId, List<int> lineIds)
    {
        var lines = new List<(int, string)>();
        if (lineIds.Count == 0) return lines;
        try
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT lineIndex, content FROM line WHERE bookId = @bookId AND id IN (" + string.Join(",", lineIds) + ") ORDER BY lineIndex";
                cmd.Parameters.AddWithValue("@bookId", bookId);
                using (var r = cmd.ExecuteReader()) while (r.Read()) lines.Add((r.GetInt32(0), r.GetString(1)));
            }
        }
        catch (Exception ex) { Console.WriteLine("[ZayitDbManager] GetLinesByIds bookId=" + bookId + ": " + ex.Message); }
        return lines;
    }

    public void Dispose() { _connection?.Close(); }
}
