//using Lucene.Net.Analysis;
//using Lucene.Net.Analysis.Standard;
//using Lucene.Net.Documents;
//using Lucene.Net.Index;
//using Lucene.Net.Index.Extensions;
//using Lucene.Net.QueryParsers.Classic;
//using Lucene.Net.Search;
//using Lucene.Net.Store;
//using Lucene.Net.Util;
//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Text;
//using System.Threading.Tasks;

//namespace MinimalIndexer
//{
//    #region Line Structure
//    public struct Line
//    {
//        public int Id { get; set; }
//        public string Content { get; set; }
//    }
//    #endregion

//    #region Search Result
//    public struct SearchResult
//    {
//        public int LineId { get; set; }
//        public float Score { get; set; }
//    }
//    #endregion

//    #region Text Normalizer
//    /// <summary>
//    /// Shared normalization logic used by both indexing and highlighting.
//    /// Keeps only Hebrew alphabet characters (including sofit forms) and lowercase Latin characters.
//    /// Strips HTML tags: line-breaking tags (br, p, div) become whitespace, all others are removed silently.
//    /// Treats Hebrew maqaf and underscore as whitespace.
//    /// </summary>
//    public static class TextNormalizer
//    {
//        [ThreadStatic]
//        private static StringBuilder _sb;

//        [ThreadStatic]
//        private static StringBuilder _tagBuffer;

//        public static string Normalize(string text)
//        {
//            if (string.IsNullOrWhiteSpace(text)) return "";

//            if (_sb == null) _sb = new StringBuilder(text.Length);
//            else _sb.Clear();

//            if (_tagBuffer == null) _tagBuffer = new StringBuilder(300);

//            bool inTag = false;

//            for (int i = 0; i < text.Length; i++)
//            {
//                char c = text[i];

//                // HTML tag handling
//                if (c == '<')
//                {
//                    inTag = true;
//                    _tagBuffer.Clear();
//                    _tagBuffer.Append(c);
//                    continue;
//                }
//                if (inTag)
//                {
//                    _tagBuffer.Append(c);

//                    if (c == '>')
//                    {
//                        inTag = false;

//                        // Check if this is a line-breaking tag
//                        string tag = _tagBuffer.ToString().ToLowerInvariant();
//                        if (tag.Contains("br") || tag.Contains("<p") || tag.Contains("</p") ||
//                            tag.Contains("<div") || tag.Contains("</div"))
//                        {
//                            _sb.Append(' ');
//                        }
//                        // All other tags are removed silently (no space added)

//                        _tagBuffer.Clear();
//                    }
//                    else if (_tagBuffer.Length > 300)
//                    {
//                        // Safety check - if "tag" is too long, it's probably not a real tag
//                        // Treat as regular text
//                        inTag = false;
//                        foreach (char ch in _tagBuffer.ToString())
//                        {
//                            ProcessCharacter(ch);
//                        }
//                        _tagBuffer.Clear();
//                    }
//                    continue;
//                }

//                ProcessCharacter(c);
//            }

//            return _sb.ToString();
//        }

//        private static void ProcessCharacter(char c)
//        {
//            // Hebrew maqaf (U+05BE) and underscore are treated as whitespace
//            if (c == '\u05BE' || c == '_')
//            {
//                _sb.Append(' ');
//            }
//            // Hebrew alphabet characters (U+05D0 to U+05EA includes all letters and sofit forms)
//            else if (c >= '\u05D0' && c <= '\u05EA')
//            {
//                _sb.Append(c);
//            }
//            // Any whitespace character becomes a space
//            else if (char.IsWhiteSpace(c))
//            {
//                _sb.Append(' ');
//            }
//            // Latin alphabet - keep as lowercase
//            else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
//            {
//                _sb.Append(char.ToLowerInvariant(c));
//            }
//            // All other characters (digits, punctuation, etc.) are stripped
//        }
//    }
//    #endregion

//    #region Hebrew Indexer
//    public sealed class HebrewIndexer : IDisposable
//    {
//        private readonly LuceneVersion _version = LuceneVersion.LUCENE_48;
//        private readonly FSDirectory _directory;
//        private readonly Analyzer _analyzer;
//        private readonly IndexWriter _writer;
//        private readonly BlockingCollection<List<Line>> _queue;
//        private readonly Task _consumerTask;

//        private const int COMMIT_BATCH_SIZE = 1000;

//        public HebrewIndexer(string indexPath)
//        {
//            _directory = FSDirectory.Open(indexPath);
//            _analyzer = new StandardAnalyzer(_version);

//            var config = new IndexWriterConfig(_version, _analyzer)
//            {
//                OpenMode = OpenMode.CREATE_OR_APPEND,
//                RAMBufferSizeMB = 512,
//                MaxBufferedDocs = IndexWriterConfig.DISABLE_AUTO_FLUSH
//            };

//            config.SetMergePolicy(new TieredMergePolicy());

//            _writer = new IndexWriter(_directory, config);

//            _queue = new BlockingCollection<List<Line>>(10);
//            _consumerTask = Task.Run(ConsumeQueue);
//        }

//        public void IndexLines(IEnumerable<Line> lines)
//        {
//            var batch = new List<Line>(lines);
//            if (batch.Count > 0)
//                _queue.Add(batch);
//        }

//        private void ConsumeQueue()
//        {
//            var docBuffer = new List<Document>(COMMIT_BATCH_SIZE);

//            foreach (var lineBatch in _queue.GetConsumingEnumerable())
//            {
//                foreach (var line in lineBatch)
//                {
//                    docBuffer.Add(CreateDocument(line));

//                    if (docBuffer.Count >= COMMIT_BATCH_SIZE)
//                    {
//                        _writer.AddDocuments(docBuffer);
//                        docBuffer.Clear();
//                    }
//                }
//            }

//            // Add remaining documents
//            if (docBuffer.Count > 0)
//            {
//                _writer.AddDocuments(docBuffer);
//            }
//        }

//        private static Document CreateDocument(Line line)
//        {
//            var ft = new FieldType
//            {
//                IsIndexed = true,
//                IsStored = false,
//                IndexOptions = IndexOptions.DOCS_ONLY
//            };
//            ft.Freeze();

//            // Normalize content before indexing
//            string normalizedContent = TextNormalizer.Normalize(line.Content ?? "");

//            var doc = new Document
//            {
//                new Int32Field("id", line.Id, Field.Store.YES),
//                new Field("content", normalizedContent, ft)
//            };
//            return doc;
//        }

//        public void Dispose()
//        {
//            _queue.CompleteAdding();
//            _consumerTask.Wait();

//            _writer.Commit();
//            _writer.Dispose();
//            _directory.Dispose();
//            _analyzer.Dispose();
//        }
//    }
//    #endregion

//    #region Hebrew Searcher
//    public sealed class HebrewSearcher : IDisposable
//    {
//        private readonly LuceneVersion _version = LuceneVersion.LUCENE_48;
//        private readonly FSDirectory _directory;
//        private readonly Analyzer _analyzer;
//        private readonly DirectoryReader _reader;
//        private readonly IndexSearcher _searcher;

//        public HebrewSearcher(string indexPath)
//        {
//            _directory = FSDirectory.Open(indexPath);
//            _analyzer = new StandardAnalyzer(_version);
//            _reader = DirectoryReader.Open(_directory);
//            _searcher = new IndexSearcher(_reader);
//        }

//        /// <summary>
//        /// Search with score filtering - returns line IDs with their relevance scores.
//        /// </summary>
//        /// <param name="queryText">Search query</param>
//        /// <param name="maxResults">Maximum number of results to consider</param>
//        /// <param name="minScoreRatio">Minimum score as ratio of top score (0.0-1.0). Default 0.1 = 10% of top score</param>
//        /// <returns>Array of SearchResult containing line IDs and scores</returns>
//        public SearchResult[] Search(string queryText, int maxResults = 100, float minScoreRatio = 0.1f)
//        {
//            // Normalize query text before parsing
//            string normalizedQuery = TextNormalizer.Normalize(queryText);

//            var parser = new QueryParser(_version, "content", _analyzer);
//            var query = parser.Parse(normalizedQuery);

//            var allHits = _searcher.Search(query, maxResults).ScoreDocs;

//            if (allHits.Length == 0)
//                return new SearchResult[0];

//            // Get the top score to calculate threshold
//            float topScore = allHits[0].Score;
//            float threshold = topScore * minScoreRatio;

//            // Filter by score threshold
//            var results = new List<SearchResult>();
//            for (int i = 0; i < allHits.Length; i++)
//            {
//                if (allHits[i].Score >= threshold)
//                {
//                    var doc = _searcher.Doc(allHits[i].Doc);
//                    results.Add(new SearchResult
//                    {
//                        LineId = doc.GetField("id").GetInt32Value().Value,
//                        Score = allHits[i].Score
//                    });
//                }
//                else
//                {
//                    // Since results are sorted by score, we can break early
//                    break;
//                }
//            }

//            return results.ToArray();
//        }

//        public void Dispose()
//        {
//            _reader.Dispose();
//            _directory.Dispose();
//            _analyzer.Dispose();
//        }
//    }
//    #endregion
//}