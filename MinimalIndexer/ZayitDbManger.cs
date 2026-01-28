using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;

namespace MinimalIndexer
{
    internal sealed class ZayitDbManager : IDisposable
    {
        readonly SQLiteConnection _connection;
        internal IDbConnection Connection => _connection;

        internal ZayitDbManager(string dbPath = null)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string defaultDbPath = Path.Combine(
                appData,
                "io.github.kdroidfilter.seforimapp",
                "databases",
                "seforim.db"
            );
            dbPath = dbPath ?? defaultDbPath;

            if (!File.Exists(dbPath))
            {
                throw new FileNotFoundException("Database file not found", dbPath);
            }

            var connectionString = $"Data Source={dbPath};Version=3;Read Only=True;Cache Size=10000;Page Size=4096;";
            _connection = new SQLiteConnection(connectionString);
            _connection.Open();

            OptimizeSQLiteSettings();
            EnsureIndexedColumns();
        }

        void OptimizeSQLiteSettings()
        {
            // Apply safe optimizations one at a time
            // If any fail, continue with the rest

            TryExecutePragma("PRAGMA temp_store = MEMORY;", "temp_store");
            TryExecutePragma("PRAGMA mmap_size = 268435456;", "mmap_size");
            TryExecutePragma("PRAGMA cache_size = 10000;", "cache_size");
            TryExecutePragma("PRAGMA page_size = 4096;", "page_size");
            TryExecutePragma("PRAGMA synchronous = OFF;", "synchronous");
            TryExecutePragma("PRAGMA journal_mode = MEMORY;", "journal_mode"); // Changed from OFF to MEMORY
            TryExecutePragma("PRAGMA locking_mode = EXCLUSIVE;", "locking_mode");
        }

        private void TryExecutePragma(string pragma, string name)
        {
            try
            {
                _connection.Execute(pragma);
            }
            catch (SQLiteException ex)
            {
                // Silently ignore - these are optimizations, not requirements
                // Uncomment below to debug which PRAGMAs are failing:
                // Console.WriteLine($"[DB] Could not set {name}: {ex.Message}");
            }
        }

        void EnsureIndexedColumns()
        {
            var stopwatch = Stopwatch.StartNew();

            // Check if indexes exist first to avoid unnecessary work
            var indexes = _connection.Query<string>(
                "SELECT name FROM sqlite_master WHERE type='index' AND name IN ('idx_line_book', 'idx_line_toc_lineId', 'idx_tocText_id')"
            );

            var existingIndexes = new HashSet<string>(indexes);

            if (!existingIndexes.Contains("idx_line_book"))
            {
                Console.WriteLine("[DB] Creating index on Line.bookId...");
                _connection.Execute("CREATE INDEX idx_line_book ON Line(bookId, lineIndex);");
            }

            if (!existingIndexes.Contains("idx_line_toc_lineId"))
            {
                Console.WriteLine("[DB] Creating index on Line_Toc.lineId...");
                _connection.Execute("CREATE INDEX idx_line_toc_lineId ON Line_Toc(lineId);");
            }

            if (!existingIndexes.Contains("idx_tocText_id"))
            {
                Console.WriteLine("[DB] Creating index on TocText.id...");
                _connection.Execute("CREATE INDEX idx_tocText_id ON TocText(id);");
            }

            stopwatch.Stop();
            if (stopwatch.ElapsedMilliseconds > 100)
            {
                Console.WriteLine($"[DB] Index creation completed in {stopwatch.ElapsedMilliseconds}ms");
            }
        }

        internal IEnumerable<(int id, int totalLines)> GetAllBookIds()
        {
            const string sql = "SELECT id, totalLines FROM book ORDER BY id;";
            return _connection.Query<(int, int)>(sql, buffered: false);
        }

        internal IEnumerable<(int lineIndex, string Content)> GetLinesByBook(int bookId)
        {
            // Use parameterized query to prevent SQL injection and enable query plan caching
            const string sql = "SELECT lineIndex, content FROM Line WHERE bookId = @BookId ORDER BY lineIndex;";
            return _connection.Query<(int, string)>(
                sql,
                new { BookId = bookId },
                buffered: false
            );
        }

        /// <summary>
        /// Stream lines for specific block numbers.
        /// Block numbers are zero-based indices into the chunked line space.
        /// </summary>
        internal IEnumerable<LineWithMetadata> GetLinesForBlock(int bookId, int chunkId, short chunkSize)
        {
            const string sql = @"
                SELECT
                    l.id,
                    l.content,
                    b.title AS bookTitle,
                    tt.text AS toc
                FROM Line l
                INNER JOIN Book b ON l.bookId = b.id
                LEFT JOIN Line_Toc lt ON lt.lineId = l.id
                LEFT JOIN TocText tt ON tt.id = lt.tocEntryId
                WHERE l.bookId = @BookId 
                  AND l.lineIndex >= @FromIndex 
                  AND l.lineIndex <= @ToIndex
                ORDER BY l.lineIndex;";

            int fromIndex = chunkId * chunkSize;
            int toIndex = fromIndex + chunkSize - 1;

            return _connection.Query<LineWithMetadata>(
                sql,
                new { BookId = bookId, FromIndex = fromIndex, ToIndex = toIndex },
                buffered: false
            );
        }

        /// <summary>
        /// Batch version for loading multiple blocks at once - much faster for search results
        /// </summary>
        internal IEnumerable<LineWithMetadata> GetLinesForBlocks(
    (int bookId, int chunkId)[] blocks,
    short chunkSize)
        {
            var rangesByBook = new Dictionary<int, List<(int from, int to)>>();

            // Build ranges
            for (int i = 0; i < blocks.Length; i++)
            {
                var b = blocks[i];
                int from = b.chunkId * chunkSize;
                int to = from + chunkSize - 1;

                if (!rangesByBook.TryGetValue(b.bookId, out var list))
                {
                    list = new List<(int, int)>(8);
                    rangesByBook[b.bookId] = list;
                }
                list.Add((from, to));
            }

            foreach (var kv in rangesByBook)
            {
                int bookId = kv.Key;
                var ranges = MergeRanges(kv.Value);

                // Build WHERE: (lineIndex BETWEEN a AND b) OR ...
                var sb = new System.Text.StringBuilder(256);
                sb.Append(@"
SELECT l.id, l.content, b.title, tt.text
FROM Line l
JOIN Book b ON l.bookId = b.id
LEFT JOIN Line_Toc lt ON lt.lineId = l.id
LEFT JOIN TocText tt ON tt.id = lt.tocEntryId
WHERE l.bookId = @bookId AND (");

                for (int i = 0; i < ranges.Count; i++)
                {
                    if (i != 0) sb.Append(" OR ");
                    sb.Append("(l.lineIndex BETWEEN @f").Append(i)
                      .Append(" AND @t").Append(i).Append(")");
                }

                sb.Append(");"); // no ORDER BY (index already sorted)

                using (var cmd = new SQLiteCommand(sb.ToString(), _connection))
                {
                    cmd.Parameters.AddWithValue("@bookId", bookId);

                    for (int i = 0; i < ranges.Count; i++)
                    {
                        cmd.Parameters.AddWithValue("@f" + i, ranges[i].fromIndex);
                        cmd.Parameters.AddWithValue("@t" + i, ranges[i].toIndex);
                    }

                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            yield return new LineWithMetadata
                            {
                                Id = r.GetInt32(0),
                                Content = r.GetString(1),
                                BookTitle = r.GetString(2),
                                Toc = r.IsDBNull(3) ? null : r.GetString(3)
                            };
                        }
                    }
                }
            }
        }


        private List<(int fromIndex, int toIndex)> MergeRanges(List<(int fromIndex, int toIndex)> ranges)
        {
            if (ranges.Count == 0) return ranges;

            ranges.Sort((a, b) => a.fromIndex.CompareTo(b.fromIndex));

            var merged = new List<(int, int)>();
            var current = ranges[0];

            for (int i = 1; i < ranges.Count; i++)
            {
                var next = ranges[i];

                // Merge if overlapping or adjacent (within 10 lines)
                if (next.fromIndex <= current.toIndex + 10)
                {
                    current = (current.fromIndex, Math.Max(current.toIndex, next.toIndex));
                }
                else
                {
                    merged.Add(current);
                    current = next;
                }
            }

            merged.Add(current);
            return merged;
        }

        public void Dispose()
        {
            _connection?.Close();
            _connection?.Dispose();
        }
    }

    /// <summary>
    /// Represents a single line with its book and TOC info.
    /// </summary>
    internal struct LineWithMetadata
    {
        internal int Id { get; set; }
        internal string Content { get; set; }
        internal string BookTitle { get; set; }
        internal string Toc { get; set; }
    }
}