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
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();
        }
    }

    public int GetLineCount()
    {
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM line";
            return (int)(long)cmd.ExecuteScalar();
        }
    }

    public IEnumerable<string> GetAllLineContents()
    {
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT content FROM line ORDER BY id";
            using (var reader = cmd.ExecuteReader())
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
            while (reader.Read())
                yield return reader.GetString(0);
        }
        finally
        {
            reader?.Dispose();
            cmd.Dispose();
        }
    }

    /// <summary>
    /// Gets content for a specific line ID.
    /// </summary>
    public string GetLineContent(int lineId)
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

    /// <summary>
    /// Gets enriched line data including book and TOC information for a specific line ID.
    /// </summary>
    public (int bookId, string bookTitle, string tocText) GetLineMetadata(int lineId)
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

    /// <summary>
    /// Gets line index and book ID for a specific line ID.
    /// </summary>
    public (int lineIndex, int bookId) GetLineIndexFromLineId(int lineId)
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

    public void Dispose()
    {
        _connection?.Close();
    }
}