//using Dapper;
//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Data;
//using System.Data.SQLite;
//using System.IO;
//using System.Threading;
//using System.Threading.Tasks;

//namespace LuceneIndexer
//{
//    public class DbManager : IDisposable
//    {
//        readonly SQLiteConnection _connection;
//        public IDbConnection Connection => _connection;

//        public DbManager(string dbPath = null)
//        {
//            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
//            string defaultDbPath = Path.Combine(
//                appData,
//                "io.github.kdroidfilter.seforimapp",
//                "databases",
//                "seforim.db"
//            );
//            dbPath = dbPath ?? defaultDbPath;

//            if (!File.Exists(dbPath))
//            {
//                var searchPaths = new[]
//                {
//                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
//                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
//                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming")
//                };

//                foreach (var basePath in searchPaths)
//                {
//                    var searchPath = Path.Combine(basePath, "io.github.kdroidfilter.seforimapp", "databases", "seforim.db");
//                    if (File.Exists(searchPath))
//                    {
//                        dbPath = searchPath;
//                        break;
//                    }
//                }

//                if (!File.Exists(dbPath))
//                    throw new FileNotFoundException($"Database file not found. Searched path: {dbPath}");
//            }

//            var connectionString = $"Data Source={dbPath};Version=3;Read Only=True;";
//            _connection = new SQLiteConnection(connectionString);
//            _connection.Open();
//        }

//        public int GetLineCount()
//        {
//            const string sql = "SELECT COUNT(*) FROM line;";
//            return _connection.ExecuteScalar<int>(sql);
//        }

//        /// <summary>
//        /// Streams lines in batches of <paramref name="batchSize"/> using a producer-consumer pattern.
//        /// </summary>
//        public IEnumerable<List<LineWithMetadata>> StreamAllLinesInBatches(int batchSize = 100, CancellationToken? token = null)
//        {
//            var blockingQueue = new BlockingCollection<List<LineWithMetadata>>(boundedCapacity: 10); // max 10 batches in memory
//            var cts = CancellationTokenSource.CreateLinkedTokenSource(token ?? CancellationToken.None);

//            Task.Run(() =>
//            {
//                try
//                {
//                    const string sql = @"
//WITH RECURSIVE category_path AS (
//    SELECT id, id as rootId, level, orderIndex, CAST(printf('%05d', orderIndex) AS TEXT) as sortPath
//    FROM category
//    WHERE parentId IS NULL
//    UNION ALL
//    SELECT c.id, cp.rootId, c.level, c.orderIndex, cp.sortPath || '-' || printf('%05d', c.orderIndex)
//    FROM category c
//    JOIN category_path cp ON c.parentId = cp.id
//)
//SELECT
//    b.title        AS BookTitle,
//    tt.text        AS TocText,
//    l.id           AS LineId,
//    l.content      AS Content
//FROM line l
//JOIN book b          ON b.id = l.bookId
//JOIN category_path c ON c.id = b.categoryId
//LEFT JOIN line_toc t ON t.lineId = l.id
//LEFT JOIN tocText tt ON tt.id = t.tocEntryId
//ORDER BY c.sortPath, b.orderIndex, l.lineIndex;
//";

//                    using (var reader = _connection.ExecuteReader(sql, commandTimeout: 300))
//                    {
//                        var parser = reader.GetRowParser<LineWithMetadata>();
//                        var batch = new List<LineWithMetadata>(batchSize);

//                        while (reader.Read())
//                        {
//                            batch.Add(parser(reader));

//                            if (batch.Count >= batchSize)
//                            {
//                                blockingQueue.Add(batch, cts.Token);
//                                batch = new List<LineWithMetadata>(batchSize);
//                            }
//                        }

//                        if (batch.Count > 0)
//                            blockingQueue.Add(batch, cts.Token);
//                    }
//                }
//                finally
//                {
//                    blockingQueue.CompleteAdding();
//                }
//            }, cts.Token);

//            foreach (var batch in blockingQueue.GetConsumingEnumerable(cts.Token))
//            {
//                yield return batch;
//            }
//        }

//        public struct LineWithMetadata
//        {
//            public string BookTitle { get; set; }
//            public string TocText { get; set; }
//            public int LineId { get; set; }
//            public string Content { get; set; }
//        }

//        public void Dispose()
//        {
//            _connection?.Close();
//            _connection?.Dispose();
//        }
//    }
//}
