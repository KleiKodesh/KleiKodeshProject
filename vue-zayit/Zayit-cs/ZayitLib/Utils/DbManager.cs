using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Threading.Tasks;
using Zayit.Models;

namespace LuceneIndexer
{
    public sealed class DbManager : IDisposable
    {
        private readonly SQLiteConnection _connection;
        public IDbConnection Connection => _connection;

        public DbManager(string dbPath = null)
        {
            if (dbPath == null)
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                dbPath = Path.Combine(
                    appData,
                    "io.github.kdroidfilter.seforimapp",
                    "databases",
                    "seforim.db");
            }

            if (!File.Exists(dbPath))
                throw new FileNotFoundException($"Database not found: {dbPath}");

            _connection = new SQLiteConnection(
                $"Data Source={dbPath};Mode=ReadOnly;Cache=Shared;");
            _connection.Open();

            ApplyReadOnlyPragmas();
        }

        private void ApplyReadOnlyPragmas()
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = @"
                    PRAGMA query_only = ON;
                    PRAGMA synchronous = OFF;
                    PRAGMA journal_mode = OFF;
                    PRAGMA temp_store = MEMORY;
                    PRAGMA cache_size = -32768;   -- ~32 MB (safe for low RAM)
                    PRAGMA mmap_size = 0;
                ";
                cmd.ExecuteNonQuery();
            }
        }

        public int GetLineCount()
        {
            using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM line;", _connection))
                return Convert.ToInt32(cmd.ExecuteScalar());
        }

        /// <summary>
        /// Retrieves lines by their IDs.
        /// </summary>
        public List<Line> GetLinesByIds(List<int> ids)
        {
            if (ids == null || ids.Count == 0)
                return new List<Line>();

            var sql = $"SELECT id, content FROM line WHERE id IN ({string.Join(",", ids)}) ORDER BY id;";
            var results = new List<Line>(ids.Count);

            using (var cmd = new SQLiteCommand(sql, _connection))
            using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess))
            {
                while (reader.Read())
                    results.Add(new Line(reader.GetInt32(0), reader.GetString(1)));
            }

            return results;
        }

        /// <summary>
        /// Streams all lines sequentially on a background thread.
        /// Constant memory, no batching.
        /// </summary>
        public IEnumerable<Line> StreamAllLines()
        {
            var queue = new BlockingCollection<Line>(512); // small buffer for low RAM

            Task.Run(() =>
            {
                try
                {
                    const string sql = "SELECT id, content FROM line ORDER BY id;";
                    using (var cmd = new SQLiteCommand(sql, _connection))
                    using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess))
                    {
                        while (reader.Read())
                            queue.Add(new Line(reader.GetInt32(0), reader.GetString(1)));
                    }
                }
                finally
                {
                    queue.CompleteAdding();
                }
            });

            return queue.GetConsumingEnumerable();
        }

        /// <summary>
        /// Streams all lines ordered by document hierarchy (depth-first tree traversal).
        /// Lines are ordered based on which book they belong to and the book's position in the document tree.
        /// </summary>
        public IEnumerable<Line> StreamAllLinesByDocumentOrder()
        {
            var queue = new BlockingCollection<Line>(512);

            Task.Run(() =>
            {
                try
                {
                    // Get the document order to determine book order
                    var detailedOrder = GetDetailedDocumentOrder();

                    // Create a map of book ID to its order position
                    var bookOrder = new Dictionary<int, int>();
                    int position = 0;
                    foreach (var (type, id) in detailedOrder)
                    {
                        if (type == "book")
                        {
                            bookOrder[id] = position++;
                        }
                    }

                    // SQL to get lines with their book ID for ordering
                    const string sql = @"
                        SELECT l.id, l.content, l.bookId 
                        FROM line l 
                        ORDER BY l.bookId, l.id;";

                    int lineOrder = 0;

                    using (var cmd = new SQLiteCommand(sql, _connection))
                    using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess))
                    {
                        while (reader.Read())
                        {
                            int lineId = reader.GetInt32(0);
                            string content = reader.GetString(1);
                            int bookId = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);

                            // Get the book's order position from the detected hierarchy
                            int bookOrderPosition = bookOrder.ContainsKey(bookId) ? bookOrder[bookId] : -1;

                            // Calculate overall order: (book position * large multiplier) + line order within book
                            int overallOrder = (bookOrderPosition >= 0) ? (bookOrderPosition * 1000000) + lineOrder : lineOrder;

                            queue.Add(new Line(lineId, content, bookId, overallOrder));
                            lineOrder++;
                        }
                    }
                }
                finally
                {
                    queue.CompleteAdding();
                }
            });

            return queue.GetConsumingEnumerable();
        }

        /// <summary>
        /// Retrieves all categories from the database.
        /// </summary>
        public Category[] GetAllCategories()
        {
            const string sql = "SELECT Id, ParentId, Title, Level FROM category ORDER BY Level, Id;";
            var results = new List<Category>();

            using (var cmd = new SQLiteCommand(sql, _connection))
            using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess))
            {
                while (reader.Read())
                {
                    results.Add(new Category
                    {
                        Id = reader.GetInt32(0),
                        ParentId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                        Title = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        Level = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                        Books = new Book[0],
                        Children = new List<Category>()
                    });
                }
            }

            return results.ToArray();
        }

        /// <summary>
        /// Retrieves all books from the database.
        /// </summary>
        public Book[] GetAllBooks()
        {
            const string sql = "SELECT Id, CategoryId, Title, OrderIndex FROM book ORDER BY CategoryId, OrderIndex;";
            var results = new List<Book>();

            using (var cmd = new SQLiteCommand(sql, _connection))
            using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess))
            {
                while (reader.Read())
                {
                    results.Add(new Book
                    {
                        Id = reader.GetInt32(0),
                        CategoryId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                        Title = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        OrderIndex = reader.IsDBNull(3) ? 0 : reader.GetInt32(3)
                    });
                }
            }

            return results.ToArray();
        }

        /// <summary>
        /// Gets the document order using depth-first traversal of the category tree.
        /// </summary>
        public List<int> GetDocumentOrder()
        {
            var categories = GetAllCategories();
            var books = GetAllBooks();
            return DocumentOrderCalculator.CalculateDocumentOrder(categories, books);
        }

        /// <summary>
        /// Gets detailed document order including both categories and books.
        /// </summary>
        public List<(string Type, int Id)> GetDetailedDocumentOrder()
        {
            var categories = GetAllCategories();
            var books = GetAllBooks();
            return DocumentOrderCalculator.CalculateDetailedDocumentOrder(categories, books);
        }

        public readonly struct Line
        {
            public int LineId { get; }
            public string Content { get; }
            public int BookId { get; }
            public int Order { get; }

            public Line(int lineId, string content)
            {
                LineId = lineId;
                Content = content;
                BookId = 0;
                Order = 0;
            }

            public Line(int lineId, string content, int bookId)
            {
                LineId = lineId;
                Content = content;
                BookId = bookId;
                Order = 0;
            }

            public Line(int lineId, string content, int bookId, int order)
            {
                LineId = lineId;
                Content = content;
                BookId = bookId;
                Order = order;
            }
        }

        public void Dispose()
        {
            _connection?.Dispose();
        }
    }
}
