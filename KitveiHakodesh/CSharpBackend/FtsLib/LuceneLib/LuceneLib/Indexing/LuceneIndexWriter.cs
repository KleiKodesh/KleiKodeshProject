using System;
using Lucene.Net.Analysis.Standard;
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
            {
                Console.WriteLine($"[LuceneIndexWriter] Deleting existing index at {indexPath}");
                System.IO.Directory.Delete(indexPath, recursive: true);
            }

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
            long count    = 0;
            var  progress = new ProgressReporter(totalRows);

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
                progress.Tick(count);
                onProgress?.Invoke(count, totalRows);
            }

            progress.Complete(count);
            onProgress?.Invoke(count, totalRows);

            Console.WriteLine($"[LuceneIndexWriter] Committing {count:N0} rows…");
            _writer.Commit();

            Console.WriteLine($"[LuceneIndexWriter] Merging segments…");
            _writer.ForceMerge(1);

            Console.WriteLine($"[LuceneIndexWriter] Done. {count:N0} rows in " +
                              $"{ProgressReporter.FormatElapsed(progress.Elapsed)}");
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
