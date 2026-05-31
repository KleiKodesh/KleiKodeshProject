using System;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using LuceneLib.SeforimDb;
using LuceneLib.Tokenization;

namespace LuceneLib.Indexing
{
    /// <summary>
    /// Builds a Lucene index from the Zayit database rows.
    /// Each row's integer id becomes the stored field "rowId";
    /// the row text is indexed under the field "text".
    /// </summary>
    public sealed class LuceneIndexWriter : IDisposable
    {
        // Field name constants — shared with LuceneSearcher.
        public const string FieldRowId = "rowId";
        public const string FieldText  = "text";

        private readonly FSDirectory _directory;
        private readonly IndexWriter _writer;
        private bool _disposed;

        public LuceneIndexWriter(string indexPath, bool deleteExistingIndex = true)
        {
            if (deleteExistingIndex && System.IO.Directory.Exists(indexPath))
                System.IO.Directory.Delete(indexPath, recursive: true);

            _directory = FSDirectory.Open(indexPath);
            var config = new IndexWriterConfig(LuceneVersion.LUCENE_48,
                new HebrewAnalyzer())
            {
                // Bulk indexing tuning: 512 MB RAM buffer (default 16 MB)
                RAMBufferSizeMB = 250.0,
            };
            _writer = new IndexWriter(_directory, config);
        }

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
                var doc = new Document();
                doc.Add(new Int32Field(FieldRowId, id, Field.Store.YES));
                
                // Text field: indexed only, no storage, no norms, docs-only posting
                var fieldType = new FieldType
                {
                    IsIndexed = true,
                    IsStored = false,
                    OmitNorms = true,
                    IndexOptions = IndexOptions.DOCS_ONLY
                };
                fieldType.Freeze();
                var textField = new Field(FieldText, content ?? string.Empty, fieldType);
                doc.Add(textField);
                
                _writer.AddDocument(doc);
                count++;
                onProgress?.Invoke(count, totalRows);
            }

            onProgress?.Invoke(count, totalRows);
            _writer.Commit();
            _writer.ForceMerge(1);
        }

        /// <summary>
        /// Adds a single row to the index without committing.
        /// Used by <see cref="LuceneLib.SeforimDb.SeforimIndex.BuildIndex"/> for
        /// row-by-row indexing with periodic commits.
        /// </summary>
        public void AddDocument(int id, string content)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LuceneIndexWriter));

            var doc = new Document();
            doc.Add(new Int32Field(FieldRowId, id, Field.Store.YES));

            var fieldType = new FieldType
            {
                IsIndexed    = true,
                IsStored     = false,
                OmitNorms    = true,
                IndexOptions = IndexOptions.DOCS_ONLY
            };
            fieldType.Freeze();
            doc.Add(new Field(FieldText, content ?? string.Empty, fieldType));

            _writer.AddDocument(doc);
        }

        /// <summary>
        /// Commits all pending documents to disk, making them visible to readers.
        /// </summary>
        public void Commit()
        {
            if (_disposed) return;
            _writer.Commit();
        }

        /// <summary>
        /// Merges all segments into one, optimising the index for read performance.
        /// Call once after a completed build — not during incremental indexing.
        /// Blocks until the merge is complete.
        /// </summary>
        public void ForceMerge()
        {
            if (_disposed) return;
            _writer.ForceMerge(1);
            _writer.Commit(); // commit the merge so readers see the single segment
        }

        /// <summary>
        /// Returns true when a committed Lucene index exists at <paramref name="indexPath"/>.
        /// Lucene writes <c>segments_N</c> files after each commit — their presence
        /// means the index is ready to open.
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
