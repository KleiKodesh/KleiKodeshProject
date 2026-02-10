using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;

public sealed class ZayitDbManager : IDisposable
{
    readonly SQLiteConnection _connection;
    public IDbConnection Connection => _connection;

    public ZayitDbManager()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dbPath = Path.Combine(
            appData,
            "io.github.kdroidfilter.seforimapp",
            "databases",
            "seforim.db"
        );
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

    /// <summary>
    /// Gets the current chunk size stored in the database, or null if not initialized.
    /// </summary>
    public int? GetCurrentChunkSize()
    {
        using (var cmd = _connection.CreateCommand())
        {
            // Check if metadata table exists
            cmd.CommandText = @"
                SELECT name FROM sqlite_master 
                WHERE type='table' AND name='bloom_metadata'";

            if (cmd.ExecuteScalar() == null)
                return null;

            // Get chunk size from metadata
            cmd.CommandText = "SELECT chunk_size FROM bloom_metadata LIMIT 1";
            var result = cmd.ExecuteScalar();
            return result != null ? (int)(long)result : (int?)null;
        }
    }

    /// <summary>
    /// Checks if chunk_id column exists and matches the desired chunk size.
    /// </summary>
    public bool NeedsChunkIndexUpdate(int desiredChunkSize)
    {
        var currentChunkSize = GetCurrentChunkSize();
        return currentChunkSize == null || currentChunkSize.Value != desiredChunkSize;
    }

    /// <summary>
    /// One-time/update setup: adds/updates chunk_id column and index for fast chunk retrieval.
    /// Recreates the index if chunk size has changed.
    /// </summary>
    public void InitializeChunkIndex(int chunkSize, Action<int, int> progressCallback = null)
    {
        using (var cmd = _connection.CreateCommand())
        {
            // Schema changes in one transaction
            using (var transaction = _connection.BeginTransaction())
            {
                // Create metadata table, add column, etc.
                // ... (keep schema changes here)
                transaction.Commit();
            }

            // Get total rows
            cmd.CommandText = "SELECT COUNT(*) FROM line";
            int totalRows = (int)(long)cmd.ExecuteScalar();

            const int batchSize = 50000;
            int processedRows = 0;

            // Update in separate transactions per batch
            while (processedRows < totalRows)
            {
                using (var transaction = _connection.BeginTransaction())
                {
                    cmd.CommandText = $@"
                    UPDATE line 
                    SET chunk_id = (id / {chunkSize})
                    WHERE id >= {processedRows} AND id < {processedRows + batchSize}";
                    cmd.ExecuteNonQuery();

                    transaction.Commit(); // Release lock after each batch!
                }

                processedRows += batchSize;
                progressCallback?.Invoke(Math.Min(processedRows, totalRows), totalRows);
            }

            // Create index in final transaction
            using (var transaction = _connection.BeginTransaction())
            {
                cmd.CommandText = "DROP INDEX IF EXISTS idx_line_chunk_id";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "CREATE INDEX idx_line_chunk_id ON line(chunk_id)";
                cmd.ExecuteNonQuery();

                cmd.CommandText = @"
                INSERT OR REPLACE INTO bloom_metadata (id, chunk_size) 
                VALUES (1, @chunkSize)";
                cmd.Parameters.AddWithValue("@chunkSize", chunkSize);
                cmd.ExecuteNonQuery();

                transaction.Commit();
            }
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
    /// Gets lines for a specific chunk using indexed chunk_id (fast!).
    /// chunkNumber is zero-based: chunk 0 → rows 0–24, chunk 50 → rows 1250–1274
    /// </summary>
    public IEnumerable<string> GetLineContentsChunk(int chunkNumber, int chunkSize)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText =
            "SELECT content FROM line WHERE chunk_id = @chunkId ORDER BY id";
        cmd.Parameters.AddWithValue("@chunkId", chunkNumber);

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
    /// Gets lines for multiple chunks using IN query (efficient for master chunks).
    /// Returns line data with LineId and Content for all chunks in one query.
    /// </summary>
    public IEnumerable<LineData> GetLineContentsForChunks(List<int> chunkIds, int chunkSize)
    {
        if (chunkIds == null || chunkIds.Count == 0)
            yield break;

        var cmd = _connection.CreateCommand();

        // Build IN clause with parameters
        var parameters = new List<string>();
        for (int i = 0; i < chunkIds.Count; i++)
        {
            var paramName = $"@chunkId{i}";
            parameters.Add(paramName);
            cmd.Parameters.AddWithValue(paramName, chunkIds[i]);
        }

        cmd.CommandText = $@"
            SELECT id, content 
            FROM line 
            WHERE chunk_id IN ({string.Join(",", parameters)})
            ORDER BY id";

        SQLiteDataReader reader = null;
        try
        {
            reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                yield return new LineData
                {
                    LineId = (int)(long)reader.GetValue(0),
                    Content = reader.GetString(1)
                };
            }
        }
        finally
        {
            reader?.Dispose();
            cmd.Dispose();
        }
    }

    public void Dispose()
    {
        _connection?.Close();
    }
}

/// <summary>
/// Container for line data returned from bulk queries.
/// </summary>
public class LineData
{
    public int LineId { get; set; }
    public string Content { get; set; }
}