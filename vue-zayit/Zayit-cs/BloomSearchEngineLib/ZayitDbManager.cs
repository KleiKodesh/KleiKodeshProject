using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

public sealed class ZayitDbManager : IDisposable
{
    public readonly SQLiteConnection _connection;

    public ZayitDbManager()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string defaultPath = Path.Combine(
            appData,
            "io.github.kdroidfilter.seforimapp",
            "databases",
            "seforim.db"
        );
        string dbPath = Interaction.GetSetting("ZayitApp", "Database", "Path", defaultPath);
        if (!File.Exists(dbPath))
            return; // No database 

        var connectionString = $"Data Source={dbPath};Version=3;Cache Size=10000;Page Size=4096;";
        _connection = new SQLiteConnection(connectionString);
        _connection.Open();

        // Enable WAL mode - allows reads during writes!
        try
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA journal_mode=WAL;";
                cmd.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZayitDbManager] WARNING: Failed to enable WAL mode: {ex.Message}");
        }
    }

    public int GetLineCount()
    {
        try
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM line";
                return (int)(long)cmd.ExecuteScalar();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZayitDbManager] ERROR in GetLineCount: {ex.Message}");
            return 0;
        }
    }

    public IEnumerable<string> GetAllLineContents()
    {
        SQLiteCommand cmd = null;
        SQLiteDataReader reader = null;
        
        try
        {
            cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT content FROM line ORDER BY id";
            reader = cmd.ExecuteReader();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZayitDbManager] ERROR in GetAllLineContents: {ex.Message}");
            cmd?.Dispose();
            yield break;
        }
        
        using (reader)
        using (cmd)
        {
            while (reader.Read())
                yield return reader.GetString(0);
        }
    }

    /// <summary>
    /// Gets lines for a specific chunk using RANGE query on line id (fast with primary key!).
    /// chunkNumber is zero-based: chunk 0 → rows 0–24, chunk 50 → rows 1250–1274
    /// </summary>
    public IEnumerable<string> GetLineContentsChunk(int chunkNumber, int chunkSize)
    {
        int startId = chunkNumber * chunkSize;
        int endId = startId + chunkSize - 1;

        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT content 
            FROM line 
            WHERE id BETWEEN @startId AND @endId 
            ORDER BY id";
        cmd.Parameters.AddWithValue("@startId", startId);
        cmd.Parameters.AddWithValue("@endId", endId);

        SQLiteDataReader reader = null;
        try
        {
            reader = cmd.ExecuteReader();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZayitDbManager] ERROR in GetLineContentsChunk for chunk {chunkNumber}: {ex.Message}");
            cmd.Dispose();
            yield break;
        }

        using (reader)
        using (cmd)
        {
            while (reader.Read())
                yield return reader.GetString(0);
        }
    }

    /// <summary>
    /// Gets content for a specific line ID.
    /// </summary>
    public string GetLineContent(int lineId)
    {
        try
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT content FROM line WHERE id = @lineId LIMIT 1";
                cmd.Parameters.AddWithValue("@lineId", lineId);

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return reader.GetString(0);
                    }
                }
            }

            return string.Empty;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZayitDbManager] ERROR in GetLineContent for lineId {lineId}: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Gets enriched line data including book and TOC information for a specific line ID.
    /// </summary>
    public (int bookId, string bookTitle, string tocText) GetLineMetadata(int lineId)
    {
        try
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT 
                        l.bookId,
                        b.title,
                        COALESCE(tt.text, '') as tocText
                    FROM line l
                    INNER JOIN book b ON l.bookId = b.id
                    LEFT JOIN line_toc lt ON l.id = lt.lineId
                    LEFT JOIN tocEntry te ON lt.tocEntryId = te.id
                    LEFT JOIN tocText tt ON te.textId = tt.id
                    WHERE l.id = @lineId
                      AND b.externalLibraryId IS NULL
                    LIMIT 1";
                cmd.Parameters.AddWithValue("@lineId", lineId);

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return (
                            reader.GetInt32(0),
                            reader.GetString(1),
                            reader.IsDBNull(2) ? "" : reader.GetString(2)
                        );
                    }
                }
            }

            return (0, "", "");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZayitDbManager] ERROR in GetLineMetadata for lineId {lineId}: {ex.Message}");
            return (0, "", "");
        }
    }

    /// <summary>
    /// Gets line index and book ID for a specific line ID.
    /// </summary>
    public (int lineIndex, int bookId) GetLineIndexFromLineId(int lineId)
    {
        try
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT lineIndex, bookId FROM line WHERE id = @lineId";
                cmd.Parameters.AddWithValue("@lineId", lineId);

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return (reader.GetInt32(0), reader.GetInt32(1));
                    }
                }
            }

            return (-1, -1);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZayitDbManager] ERROR in GetLineIndexFromLineId for lineId {lineId}: {ex.Message}");
            return (-1, -1);
        }
    }

    /// <summary>
    /// Gets all line IDs associated with a TOC entry.
    /// </summary>
    public List<int> GetLineIdsByTocEntry(int tocEntryId)
    {
        var lineIds = new List<int>();
        
        try
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT lineId
                    FROM line_toc
                    WHERE tocEntryId = @tocEntryId
                    ORDER BY lineId";
                cmd.Parameters.AddWithValue("@tocEntryId", tocEntryId);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lineIds.Add(reader.GetInt32(0));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZayitDbManager] ERROR in GetLineIdsByTocEntry for tocEntryId {tocEntryId}: {ex.Message}");
        }

        return lineIds;
    }

    /// <summary>
    /// Gets line content for multiple line IDs.
    /// </summary>
    public List<(int lineIndex, string content)> GetLinesByIds(int bookId, List<int> lineIds)
    {
        var lines = new List<(int lineIndex, string content)>();
        
        if (lineIds.Count == 0)
            return lines;

        try
        {
            using (var cmd = _connection.CreateCommand())
            {
                var idParams = string.Join(",", lineIds);
                cmd.CommandText = $@"
                    SELECT lineIndex, content
                    FROM line
                    WHERE bookId = @bookId
                      AND id IN ({idParams})
                    ORDER BY lineIndex";
                cmd.Parameters.AddWithValue("@bookId", bookId);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lines.Add((reader.GetInt32(0), reader.GetString(1)));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZayitDbManager] ERROR in GetLinesByIds for bookId {bookId}: {ex.Message}");
        }

        return lines;
    }

    public void Dispose()
    {
        _connection?.Close();
    }
}