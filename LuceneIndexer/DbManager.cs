using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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

            var sql = $@"
                SELECT id, content FROM line 
                WHERE id IN ({string.Join(",", ids)})
                ORDER BY CASE id
                {string.Join(Environment.NewLine, ids.Select((id, i) => $"WHEN {id} THEN {i}"))}
                END;
                ";

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
        /// Streams all lines with metadata (book title and TOC text) for indexing.
        /// </summary>
        public IEnumerable<LineWithMetadata> StreamAllLinesWithMetadata()
        {
            var queue = new BlockingCollection<LineWithMetadata>(512);

            Task.Run(() =>
            {
                try
                {
                    const string sql = @"
                        SELECT 
                            l.id,
                            l.content,
                            b.title AS bookTitle,
                            COALESCE(tt.text, '') AS tocText
                        FROM line l
                        INNER JOIN book b ON l.bookId = b.id
                        LEFT JOIN tocEntry te ON l.tocEntryId = te.id
                        LEFT JOIN tocText tt ON te.textId = tt.id
                        ORDER BY l.id;";

                    using (var cmd = new SQLiteCommand(sql, _connection))
                    using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess))
                    {
                        while (reader.Read())
                        {
                            queue.Add(new LineWithMetadata(
                                lineId: reader.GetInt32(0),
                                content: reader.GetString(1),
                                bookTitle: reader.IsDBNull(2) ? "" : reader.GetString(2),
                                tocText: reader.IsDBNull(3) ? "" : reader.GetString(3)
                            ));
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
        /// Streams all lines ordered by document hierarchy using book order chunks.
        /// Lines are fetched in chunks based on book order determined by the category tree traversal.
        /// </summary>
        public IEnumerable<Line> StreamAllLinesByDocumentOrder()
        {
            var queue = new BlockingCollection<Line>(512);

            Task.Run(() =>
            {
                try
                {
                    // Get the detailed document order to determine book sequence
                    var detailedOrder = GetDetailedDocumentOrder();

                    // Extract books in order
                    var booksInOrder = new List<int>();
                    foreach (var (type, id) in detailedOrder)
                    {
                        if (type == "book")
                        {
                            booksInOrder.Add(id);
                        }
                    }

                    // Stream lines book by book in the correct order
                    foreach (var bookId in booksInOrder)
                    {
                        const string sql = @"
                            SELECT id, content 
                            FROM line 
                            WHERE bookId = @bookId 
                            ORDER BY id;";

                        using (var cmd = new SQLiteCommand(sql, _connection))
                        {
                            cmd.Parameters.AddWithValue("@bookId", bookId);
                            using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess))
                            {
                                while (reader.Read())
                                {
                                    int lineId = reader.GetInt32(0);
                                    string content = reader.GetString(1);
                                    queue.Add(new Line(lineId, content));
                                }
                            }
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
            const string sql = "SELECT Id, ParentId, Title, Level, OrderIndex FROM category ORDER BY Level, OrderIndex, Id;";
            var results = new List<Category>();

            using (var cmd = new SQLiteCommand(sql, _connection))
            using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess))
            {
                while (reader.Read())
                {
                    results.Add(new Category
                    {
                        Id = reader.GetInt32(0),
                        ParentId = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1),
                        Title = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        Level = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                        OrderIndex = reader.IsDBNull(4) ? 999 : reader.GetInt32(4),
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

            public Line(int lineId, string content)
            {
                LineId = lineId;
                Content = content;
            }
        }

        public readonly struct LineWithMetadata
        {
            public int LineId { get; }
            public string Content { get; }
            public string BookTitle { get; }
            public string TocText { get; }

            public LineWithMetadata(int lineId, string content, string bookTitle, string tocText)
            {
                LineId = lineId;
                Content = content;
                BookTitle = bookTitle;
                TocText = tocText;
            }
        }

        public void Dispose()
        {
            _connection?.Dispose();
        }
    }

    /// <summary>
    /// Represents a category in the document hierarchy.
    /// </summary>
    public class Category
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public string Title { get; set; }
        public int Level { get; set; }
        public int OrderIndex { get; set; }  // Add this field
        public Book[] Books { get; set; }
        public List<Category> Children { get; set; }
    }

    /// <summary>
    /// Represents a book within a category.
    /// </summary>
    public class Book
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public string Title { get; set; }
        public int OrderIndex { get; set; }
    }
}