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

        _connection = new SQLiteConnection($"Data Source={dbPath};Version=3;Page Size=4096;");
        _connection.Open();

        try
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText =
                    "PRAGMA journal_mode=WAL; " +
                    "PRAGMA cache_size=-65536; " +   // up to 64 MB page cache (ceiling, not reservation)
                    "PRAGMA temp_store=MEMORY;";     // temp indices/tables in RAM, not disk
                cmd.ExecuteNonQuery();
            }
        }
        catch (Exception ex) { Console.WriteLine("[ZayitDbManager] PRAGMA setup failed: " + ex.Message); }
    }

    public int GetLineCount()
    {
        try { using (var cmd = _connection.CreateCommand()) { cmd.CommandText = "SELECT COUNT(*) FROM line"; return (int)(long)cmd.ExecuteScalar(); } }
        catch (Exception ex) { Console.WriteLine("[ZayitDbManager] GetLineCount: " + ex.Message); return 0; }
    }

    public IEnumerable<(int id, string content)> GetAllLineContents(int afterLineId = 0)
    {
        SQLiteCommand cmd = null;
        SQLiteDataReader reader = null;
        try
        {
            cmd = _connection.CreateCommand();
            if (afterLineId > 0)
            {
                cmd.CommandText = "SELECT id, content FROM line WHERE id > @after ORDER BY id";
                cmd.Parameters.AddWithValue("@after", afterLineId);
            }
            else
            {
                cmd.CommandText = "SELECT id, content FROM line ORDER BY id";
            }
            reader = cmd.ExecuteReader();
        }
        catch (Exception ex) { Console.WriteLine("[ZayitDbManager] GetAllLineContents: " + ex.Message); cmd?.Dispose(); yield break; }
        using (reader) using (cmd) { while (reader.Read()) yield return (reader.GetInt32(0), reader.GetString(1)); }
    }

    /// <summary>
    /// Bulk variant of GetAllLineContents — reads up to <paramref name="batchSize"/> rows
    /// per SQLite query using LIMIT/OFFSET, returning each batch as a List.
    /// Dramatically reduces per-row SQLite overhead compared to the streaming reader
    /// at the cost of slightly higher memory use per batch.
    /// </summary>
    public IEnumerable<List<(int id, string content)>> GetAllLineContentsBulk(int afterLineId = 0, int batchSize = 5000)
    {
        int lastId = afterLineId;
        int batchNumber = 0;
        long totalDbMs = 0;
        var swTotal = System.Diagnostics.Stopwatch.StartNew();

        while (true)
        {
            var batch = new List<(int id, string content)>(batchSize);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT id, content FROM line WHERE id > @after ORDER BY id LIMIT @limit";
                    cmd.Parameters.AddWithValue("@after", lastId);
                    cmd.Parameters.AddWithValue("@limit", batchSize);
                    using (var reader = cmd.ExecuteReader())
                        while (reader.Read())
                            batch.Add((reader.GetInt32(0), reader.GetString(1)));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ZayitDbManager] GetAllLineContentsBulk: " + ex.Message);
                yield break;
            }
            sw.Stop();
            totalDbMs += sw.ElapsedMilliseconds;
            batchNumber++;

            if (batch.Count == 0)
            {
                Console.WriteLine("[ZayitDbManager] Bulk read done — {0} batches  total_db_read={1}ms  total_elapsed={2}ms",
                    batchNumber - 1, totalDbMs, swTotal.ElapsedMilliseconds);
                yield break;
            }

            lastId = batch[batch.Count - 1].id;
            yield return batch;

            if (batch.Count < batchSize)
            {
                Console.WriteLine("[ZayitDbManager] Bulk read done — {0} batches  total_db_read={1}ms  total_elapsed={2}ms",
                    batchNumber, totalDbMs, swTotal.ElapsedMilliseconds);
                yield break;
            }
        }
    }

    public IEnumerable<(int id, string content)> GetLineContentsChunk(int firstLineId, int lastLineId)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, content FROM line WHERE id BETWEEN @s AND @e ORDER BY id";
        cmd.Parameters.AddWithValue("@s", firstLineId);
        cmd.Parameters.AddWithValue("@e", lastLineId);
        SQLiteDataReader reader = null;
        try { reader = cmd.ExecuteReader(); }
        catch (Exception ex) { Console.WriteLine("[ZayitDbManager] GetLineContentsChunk firstId=" + firstLineId + ": " + ex.Message); cmd.Dispose(); yield break; }
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

    public Dictionary<int, (int bookId, string bookTitle, string tocText)> GetLineMetadataBatch(List<int> lineIds)
    {
        var result = new Dictionary<int, (int, string, string)>(lineIds.Count);
        if (lineIds.Count == 0) return result;

        string inClause = string.Join(",", lineIds);
        try
        {
            // Query 1: lineId → bookId (single table, PK lookup)
            var lineBookMap = new Dictionary<int, int>(lineIds.Count);
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT id, bookId FROM line WHERE id IN (" + inClause + ")";
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        lineBookMap[r.GetInt32(0)] = r.GetInt32(1);
            }

            // Query 2: bookId → title (small table, only distinct book IDs)
            var bookTitleMap = new Dictionary<int, string>();
            var distinctBookIds = new HashSet<int>(lineBookMap.Values);
            if (distinctBookIds.Count > 0)
            {
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT id, title FROM book WHERE id IN (" + string.Join(",", distinctBookIds) + ")";
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                            bookTitleMap[r.GetInt32(0)] = r.GetString(1);
                }
            }

            // Query 3: lineId → tocText (3-table join, but no line/book drag)
            var tocTextMap = new Dictionary<int, string>(lineIds.Count);
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT lt.lineId, tt.text " +
                    "FROM line_toc lt " +
                    "JOIN tocEntry te ON te.id = lt.tocEntryId " +
                    "JOIN tocText tt ON tt.id = te.textId " +
                    "WHERE lt.lineId IN (" + inClause + ")";
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        tocTextMap[r.GetInt32(0)] = r.GetString(1);
            }

            // Assemble results
            foreach (var lineId in lineIds)
            {
                if (!lineBookMap.TryGetValue(lineId, out int bookId)) continue;
                bookTitleMap.TryGetValue(bookId, out string bookTitle);
                tocTextMap.TryGetValue(lineId, out string tocText);
                result[lineId] = (bookId, bookTitle ?? "", tocText ?? "");
            }
        }
        catch (Exception ex) { Console.WriteLine("[ZayitDbManager] GetLineMetadataBatch: " + ex.Message); }
        return result;
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
