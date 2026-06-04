using System;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using SearchEngine.SeforimDb;
using SearchEngine.Tokenization;

namespace SearchEngine.Indexing
{
    /// <summary>
    /// Builds a Lucene index from the Zayit database rows.
    /// Each row's integer id becomes the stored field "rowId";
    /// the row text is indexed under the field "text";
    /// the row's bookId is stored as an int field "bookId".
    ///
    /// Index modes
    /// -----------
    /// <b>Minimal</b> (default, <c>storeMetadata = false</c>): stores only <c>rowId</c>.
    /// <c>bookId</c>, <c>bookTitle</c>, and <c>tocPath</c> are not stored in the index;
    /// the searcher fetches them from the database at query time.
    /// Produces a significantly smaller index and faster build.
    ///
    /// <b>Full</b> (<c>storeMetadata = true</c>): also stores <c>bookId</c>,
    /// <c>bookTitle</c>, and <c>tocPath</c> so searches need no database round-trip
    /// for those fields.
    ///
    /// The mode is not written to a separate file — callers detect it at open time
    /// by inspecting the field metadata via <see cref="DetectMetadataStored"/>.
    ///
    /// Near-real-time search during build
    /// ------------------------------------
    /// Call <see cref="GetNrtSearcherManager"/> after the first
    /// <see cref="AddDocument"/> call to obtain a <see cref="SearcherManager"/>
    /// backed directly by this writer's in-memory buffer.  Calling
    /// <see cref="SearcherManager.MaybeRefresh"/> on that manager makes all
    /// documents added since the last refresh visible to new searches — without
    /// requiring a commit to disk.  This gives sub-second search latency during
    /// a build that commits only every 250 000 rows.
    /// </summary>
    public sealed class LuceneIndexWriter : IDisposable
    {
        // Field name constants — shared with LuceneSearcher.
        public const string FieldRowId     = "rowId";
        public const string FieldText      = "text";
        public const string FieldBookId    = "bookId";
        public const string FieldBookTitle = "bookTitle";
        public const string FieldTocPath   = "tocPath";

        private readonly FSDirectory _directory;
        private readonly IndexWriter _writer;
        private readonly bool        _storeMetadata;
        private bool _disposed;

        // Reusable field type — frozen once, shared across all AddDocument calls.
        private static readonly FieldType _textFieldType;

        // Reusable field instances — mutated per AddDocument call to avoid per-row allocation.
        // Not thread-safe; AddDocument must not be called concurrently (the writer isn't either).
        private readonly Int32Field   _rowIdField;
        private readonly StoredField  _bookIdField;
        private readonly StoredField  _bookTitleField;
        private readonly StoredField  _tocPathField;
        private readonly Field        _textField;
        private readonly Document     _reusableDoc;

        static LuceneIndexWriter()
        {
            _textFieldType = new FieldType
            {
                IsIndexed    = true,
                IsStored     = false,
                OmitNorms    = true,
                IndexOptions = IndexOptions.DOCS_ONLY,
            };
            _textFieldType.Freeze();

            // %word% expansion generates up to 1296 terms (35×35 + 35 + 35 + 1).
            // Raise the cap to 1200 — GrammarExpander trims its output to match.
            BooleanQuery.MaxClauseCount = 1200;
        }

        /// <summary>
        /// Opens or creates a Lucene index at <paramref name="indexPath"/>.
        /// </summary>
        /// <param name="indexPath">Directory that will hold the Lucene segments.</param>
        /// <param name="deleteExistingIndex">
        ///   When true (default), any existing index directory is deleted before opening,
        ///   producing a clean build.  Pass false to append to an existing index (resume).
        /// </param>
        /// <param name="storeMetadata">
        ///   When false (default), only <c>rowId</c> is stored per document.
        ///   <c>bookId</c>, <c>bookTitle</c>, and <c>tocPath</c> are omitted from storage;
        ///   the searcher fetches them from the database at query time.
        ///   When true, all four fields are stored in the index.
        /// </param>
        public LuceneIndexWriter(string indexPath,
                                 bool deleteExistingIndex = true,
                                 bool storeMetadata       = false)
        {
            _storeMetadata = storeMetadata;

            if (deleteExistingIndex && System.IO.Directory.Exists(indexPath))
                System.IO.Directory.Delete(indexPath, recursive: true);

            _directory = FSDirectory.Open(indexPath);
            var config = new IndexWriterConfig(LuceneVersion.LUCENE_48,
                new HebrewAnalyzer())
            {
                RAMBufferSizeMB = 256.0,
            };
            _writer = new IndexWriter(_directory, config);

            // Pre-allocate reusable field instances and the document that holds them.
            // AddDocument mutates these in-place rather than allocating new objects per row.
            _rowIdField = new Int32Field(FieldRowId, 0, Field.Store.YES);
            _textField  = new Field(FieldText, string.Empty, _textFieldType);

            _reusableDoc = new Document();
            _reusableDoc.Add(_rowIdField);

            if (_storeMetadata)
            {
                _bookIdField    = new StoredField(FieldBookId, 0);
                _bookTitleField = new StoredField(FieldBookTitle, string.Empty);
                _tocPathField   = new StoredField(FieldTocPath, string.Empty);
                _reusableDoc.Add(_bookIdField);
                _reusableDoc.Add(_bookTitleField);
                _reusableDoc.Add(_tocPathField);
            }

            _reusableDoc.Add(_textField);
        }

        // ── Mode detection ────────────────────────────────────────────

        /// <summary>
        /// Inspects the first document in a committed index to determine whether it
        /// was built with <c>storeMetadata = true</c>.  Reading one document is cheap —
        /// Lucene loads only the stored fields for that single document ID.
        ///
        /// Returns true when the <c>bookId</c> field is present and has a stored value,
        /// meaning the index carries full metadata.  Returns false for a minimal index
        /// or when no committed index exists at <paramref name="indexPath"/>.
        /// </summary>
        public static bool DetectMetadataStored(string indexPath)
        {
            if (!IndexExists(indexPath)) return false;
            try
            {
                using (var directory = FSDirectory.Open(indexPath))
                using (var reader    = DirectoryReader.Open(directory))
                {
                    if (reader.MaxDoc == 0) return false;
                    // Read the first document — no deletion check needed because
                    // a fresh FTS build never produces deleted documents.
                    var doc = reader.Document(0);
                    return doc.GetField(FieldBookId) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        // ── NRT ───────────────────────────────────────────────────────

        /// <summary>
        /// Returns a <see cref="SearcherManager"/> that is backed directly by this
        /// writer's in-memory buffer (near-real-time reader).
        ///
        /// Call <see cref="SearcherManager.MaybeRefresh"/> on the returned manager
        /// to make newly added documents visible to the next search — no commit
        /// required.  The manager must be disposed by the caller when the build ends.
        ///
        /// This method may be called at any point after construction.
        /// </summary>
        public SearcherManager GetNrtSearcherManager()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LuceneIndexWriter));
            // SearcherManager(IndexWriter, ...) opens an NRT reader from the writer.
            return new SearcherManager(_writer, applyAllDeletes: true, searcherFactory: null);
        }

        // ── Indexing ──────────────────────────────────────────────────

        /// <summary>
        /// Indexes all rows from <paramref name="db"/> in id order.
        /// Calls <paramref name="onProgress"/> with (currentCount, totalCount) updates.
        /// </summary>
        public void IndexAll(ZayitDb db, long totalRows = 0,
            Action<long, long> onProgress = null,
            System.Threading.CancellationToken ct = default)
        {
            long count = 0;
            foreach (var book in db.ReadAllBooks())
            {
                var tocMap = db.BuildTocPathMap(book.BookId, book.BookTitle);
                foreach (var (id, content) in db.ReadLinesForBook(book.BookId))
                {
                    tocMap.TryGetValue(id, out string tocPath);
                    AddDocument(id, book.BookId, book.BookTitle, tocPath ?? string.Empty, content);
                    count++;
                    onProgress?.Invoke(count, totalRows);
                }
            }

            onProgress?.Invoke(count, totalRows);
            _writer.Commit();
            _writer.ForceMerge(1);
        }

        /// <summary>
        /// Adds a single row to the index without committing.
        /// Reuses pre-allocated field instances — no per-row heap allocation.
        /// rowId is always indexed (for sort) and stored.
        /// bookId, bookTitle, and tocPath are stored only when the index was opened
        /// with <c>storeMetadata = true</c>; otherwise they are silently ignored.
        /// </summary>
        public void AddDocument(int id, int bookId, string bookTitle, string tocPath, string content)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LuceneIndexWriter));

            _rowIdField.SetInt32Value(id);

            if (_storeMetadata)
            {
                _bookIdField.SetInt32Value(bookId);
                _bookTitleField.SetStringValue(bookTitle ?? string.Empty);
                _tocPathField.SetStringValue(tocPath ?? string.Empty);
            }

            _textField.SetStringValue(content ?? string.Empty);

            _writer.AddDocument(_reusableDoc);
        }

        /// <summary>
        /// Convenience overload for tests and tooling — indexes a row with no book
        /// metadata. bookId=0, bookTitle and tocPath are left empty.
        /// </summary>
        public void AddDocument(int id, string content)
            => AddDocument(id, 0, string.Empty, string.Empty, content);

        /// <summary>
        /// Commits all pending documents to disk, making them durable and visible
        /// to disk-based readers.  NRT readers see documents before commit via
        /// <see cref="GetNrtSearcherManager"/>.
        /// </summary>
        public void Commit()
        {
            if (_disposed) return;
            _writer.Commit();
        }

        /// <summary>
        /// Merges all segments into one for optimal read performance.
        /// Call once after a completed build.  Blocks until the merge is complete.
        /// </summary>
        public void ForceMerge()
        {
            if (_disposed) return;
            _writer.ForceMerge(1);
            _writer.Commit();
        }

        /// <summary>
        /// Returns true when a committed Lucene index exists at <paramref name="indexPath"/>.
        /// </summary>
        public static bool IndexExists(string indexPath)
        {
            if (!System.IO.Directory.Exists(indexPath)) return false;
            foreach (var f in System.IO.Directory.GetFiles(indexPath))
            {
                string name = System.IO.Path.GetFileName(f);
                if (name.StartsWith("segments_") && name != "segments.gen")
                    return true;
            }
            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _writer?.Dispose();
            _directory?.Dispose();
        }
    }
}
