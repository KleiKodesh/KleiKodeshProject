using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace MinimalIndexer
{
    /// <summary>
    /// Indexes Hebrew text with normalized content for efficient searching.
    /// Stores book title and TOC text as metadata.
    /// </summary>
    public sealed class Indexer : IDisposable
    {
        private readonly IndexWriter _writer;
        private readonly BlockingCollection<Document> _blockingQueue;

        private readonly FieldType MyContentFieldType = new FieldType
        {
            IsIndexed = true,
            IsStored = false,
            IndexOptions = IndexOptions.DOCS_ONLY,
        };

        const LuceneVersion VERSION = LuceneVersion.LUCENE_48;
        const short BLOCKING_QUEUE_LIMIT = 1000;

        public Indexer(string indexPath)
        {
            var directory = FSDirectory.Open(indexPath);
            var analyzer = new StandardAnalyzer(VERSION);
            var config = new IndexWriterConfig(VERSION, analyzer) { OpenMode = OpenMode.CREATE };
            _writer = new IndexWriter(directory, config);
            _blockingQueue = new BlockingCollection<Document>(BLOCKING_QUEUE_LIMIT);

            _ = Task.Run(() =>
            {
                foreach (var entry in _blockingQueue.GetConsumingEnumerable())
                    _writer.AddDocument(entry);
            });
        }

        /// <summary>
        /// Creates a document with content and metadata.
        /// </summary>
        /// <param name="id">Line ID</param>
        /// <param name="content">Line content (will be normalized)</param>
        /// <param name="bookTitle">Book title (stored, not indexed)</param>
        /// <param name="tocText">Table of contents text (stored, not indexed)</param>
        public void CreateDocument(int id, string content, string bookTitle, string tocText)
        {
            if (string.IsNullOrWhiteSpace(content))
                return;

            // Normalize content before indexing
            string normalizedContent = TextNormalizer.Normalize(content);

            _blockingQueue.Add(new Document
            {
                new Int32Field("id", id, Field.Store.YES),
                new Field("content", normalizedContent, MyContentFieldType),
                new StringField("bookTitle", bookTitle ?? "", Field.Store.YES),
                new StringField("tocText", tocText ?? "", Field.Store.YES)
            });
        }

        public void Dispose()
        {
            _blockingQueue.CompleteAdding();
            _writer.Commit();
            _writer.Dispose();
        }
    }
}