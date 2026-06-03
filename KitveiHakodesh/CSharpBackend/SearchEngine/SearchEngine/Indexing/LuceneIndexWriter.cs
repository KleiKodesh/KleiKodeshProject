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
    /// the row text is indexed under the field "text".
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
        public const string FieldRowId = "rowId";
        public const string FieldText  = "text";

        private readonly FSDirectory _directory;
        private readonly IndexWriter _writer;
        private bool _disposed;

        // Reusable field type — frozen once, shared across all AddDocument calls.
        private static readonly FieldType _textFieldType;

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

        public LuceneIndexWriter(string indexPath, bool deleteExistingIndex = true)
        {
            if (deleteExistingIndex && System.IO.Directory.Exists(indexPath))
                System.IO.Directory.Delete(indexPath, recursive: true);

            _directory = FSDirectory.Open(indexPath);
            var config = new IndexWriterConfig(LuceneVersion.LUCENE_48,
                new HebrewAnalyzer())
            {
                // Bulk indexing tuning: 256 MB RAM buffer (default 16 MB).
                // Kept moderate so the NRT reader doesn't have to flush huge segments.
                RAMBufferSizeMB = 256.0,
            };
            _writer = new IndexWriter(_directory, config);
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
            foreach (var (id, content) in db.ReadLines(ct: ct))
            {
                AddDocument(id, content);
                count++;
                onProgress?.Invoke(count, totalRows);
            }

            onProgress?.Invoke(count, totalRows);
            _writer.Commit();
            _writer.ForceMerge(1);
        }

        /// <summary>
        /// Adds a single row to the index without committing.
        /// </summary>
        public void AddDocument(int id, string content)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LuceneIndexWriter));

            var doc = new Document();
            doc.Add(new Int32Field(FieldRowId, id, Field.Store.YES));
            doc.Add(new Field(FieldText, content ?? string.Empty, _textFieldType));
            _writer.AddDocument(doc);
        }

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
