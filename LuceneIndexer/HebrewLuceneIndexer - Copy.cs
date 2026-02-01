//using Lucene.Net.Analysis;
//using Lucene.Net.Analysis.TokenAttributes;
//using Lucene.Net.Documents;
//using Lucene.Net.Index;
//using Lucene.Net.QueryParsers.Classic;
//using Lucene.Net.Search;
//using Lucene.Net.Search.Highlight;
//using Lucene.Net.Store;
//using Lucene.Net.Util;
//using LuceneIndexer;
//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.IO;
//using System.Text;
//using System.Threading.Tasks;

//namespace MinimalIndexer
//{
//    #region Line Structure
//    public struct Line
//    {
//        public int Id { get; set; }
//        public string Content { get; set; }
//        public string BookTitle { get; set; }
//        public string Toc { get; set; }
//    }
//    #endregion

//    #region Hebrew Analyzer
//    public sealed class HebrewAnalyzer : Analyzer
//    {
//        private readonly LuceneVersion _matchVersion;
//        private readonly SmartStemmer _stemmer;
//        private readonly TextNormalizer _normalizer;
//        private readonly bool _useStemmer;

//        public HebrewAnalyzer(LuceneVersion matchVersion, bool useStemmer = false, int stemCacheSize = 100_000, int cacheCleanupMinutes = 30)
//        {
//            _matchVersion = matchVersion;
//            _useStemmer = useStemmer;
//            _normalizer = new TextNormalizer();
//            if (useStemmer)
//                _stemmer = new SmartStemmer(stemCacheSize, cacheCleanupMinutes);
//        }

//        protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
//        {
//            var tokenizer = new HebrewTokenizer(reader, _normalizer);
//            TokenStream stream = new HebrewNormalizationFilter(tokenizer, _normalizer);

//            if (_useStemmer)
//                stream = new HebrewStemFilter(stream, _stemmer);

//            return new TokenStreamComponents(tokenizer, stream);
//        }

//        protected override void Dispose(bool disposing)
//        {
//            base.Dispose(disposing);
//            if (disposing)
//                _stemmer?.Dispose();
//        }
//    }
//    #endregion

//    #region Hebrew Tokenizer
//    public sealed class HebrewTokenizer : Tokenizer
//    {
//        private readonly ICharTermAttribute _termAtt;
//        private readonly IOffsetAttribute _offsetAtt;
//        private readonly IPositionIncrementAttribute _posIncrAtt;
//        private readonly TextNormalizer _normalizer;

//        private readonly StringBuilder _buffer = new StringBuilder(256);
//        private int _offset;
//        private bool _done;
//        private int _finalOffset;

//        private const int MAX_TOKEN_LENGTH = 30;

//        public HebrewTokenizer(TextReader input, TextNormalizer normalizer)
//            : base(input)
//        {
//            _normalizer = normalizer;
//            _termAtt = AddAttribute<ICharTermAttribute>();
//            _offsetAtt = AddAttribute<IOffsetAttribute>();
//            _posIncrAtt = AddAttribute<IPositionIncrementAttribute>();
//        }

//        public override void Reset()
//        {
//            base.Reset();
//            _offset = 0;
//            _done = false;
//            _finalOffset = 0;
//            _buffer.Clear();
//        }

//        public override bool IncrementToken()
//        {
//            if (_done) return false;

//            ClearAttributes();
//            _buffer.Clear();

//            int startOffset = _offset;
//            int ch;
//            bool foundToken = false;

//            while ((ch = m_input.Read()) != -1)
//            {
//                _offset++;
//                if (!IsWhitespace((char)ch))
//                {
//                    _buffer.Append((char)ch);
//                    foundToken = true;
//                    break;
//                }
//            }

//            if (!foundToken)
//            {
//                _done = true;
//                _finalOffset = _offset;
//                return false;
//            }

//            while ((ch = m_input.Read()) != -1)
//            {
//                _offset++;
//                if (IsWhitespace((char)ch))
//                    break;

//                if (_buffer.Length < MAX_TOKEN_LENGTH)
//                    _buffer.Append((char)ch);
//            }

//            if (ch == -1)
//            {
//                _done = true;
//                _finalOffset = _offset;
//            }

//            string normalized = _normalizer.Normalize(_buffer.ToString());
//            if (string.IsNullOrWhiteSpace(normalized))
//                return IncrementToken();

//            _termAtt.SetEmpty().Append(normalized);
//            _offsetAtt.SetOffset(CorrectOffset(startOffset), CorrectOffset(_offset));
//            _posIncrAtt.PositionIncrement = 1;

//            return true;
//        }

//        public override void End()
//        {
//            base.End();
//            int finalOffset = CorrectOffset(_finalOffset);
//            _offsetAtt.SetOffset(finalOffset, finalOffset);
//        }

//        private static bool IsWhitespace(char c)
//        {
//            return char.IsWhiteSpace(c) || c == '\u05BE';
//        }
//    }
//    #endregion

//    #region Normalization Filter
//    public sealed class HebrewNormalizationFilter : TokenFilter
//    {
//        private readonly ICharTermAttribute _termAtt;
//        private readonly TextNormalizer _normalizer;

//        public HebrewNormalizationFilter(TokenStream input, TextNormalizer normalizer)
//            : base(input)
//        {
//            _normalizer = normalizer;
//            _termAtt = AddAttribute<ICharTermAttribute>();
//        }

//        public override bool IncrementToken()
//        {
//            if (!m_input.IncrementToken())
//                return false;

//            var term = _termAtt.ToString();
//            var normalized = _normalizer.Normalize(term);

//            if (normalized != term)
//                _termAtt.SetEmpty().Append(normalized);

//            return true;
//        }
//    }
//    #endregion

//    #region Stem Filter
//    public sealed class HebrewStemFilter : TokenFilter
//    {
//        private readonly ICharTermAttribute _termAtt;
//        private readonly IPositionIncrementAttribute _posIncrAtt;
//        private readonly SmartStemmer _stemmer;

//        private IEnumerator<string> _stemEnumerator;
//        private bool _firstStem = true;

//        public HebrewStemFilter(TokenStream input, SmartStemmer stemmer)
//            : base(input)
//        {
//            _stemmer = stemmer;
//            _termAtt = AddAttribute<ICharTermAttribute>();
//            _posIncrAtt = AddAttribute<IPositionIncrementAttribute>();
//        }

//        public override void Reset()
//        {
//            base.Reset();
//            _stemEnumerator?.Dispose();
//            _stemEnumerator = null;
//            _firstStem = true;
//        }

//        public override bool IncrementToken()
//        {
//            if (_stemEnumerator != null && _stemEnumerator.MoveNext())
//            {
//                _termAtt.SetEmpty().Append(_stemEnumerator.Current);
//                _posIncrAtt.PositionIncrement = _firstStem ? 1 : 0;
//                _firstStem = false;
//                return true;
//            }

//            _stemEnumerator?.Dispose();
//            _stemEnumerator = null;

//            if (!m_input.IncrementToken())
//                return false;

//            _stemEnumerator = _stemmer.Generate(_termAtt.ToString()).GetEnumerator();
//            _firstStem = true;

//            return IncrementToken();
//        }
//    }
//    #endregion

//    #region Hebrew Indexer
//    public sealed class HebrewIndexer : IDisposable
//    {
//        private readonly LuceneVersion _version = LuceneVersion.LUCENE_48;
//        private readonly FSDirectory _directory;
//        private readonly HebrewAnalyzer _analyzer;
//        private readonly IndexWriter _writer;
//        private readonly IndexWriterConfig _config;

//        private readonly BlockingCollection<DbManager.LineWithMetadata> _queue;
//        private readonly Task _consumerTask;

//        public HebrewIndexer(string indexPath, bool useStemmer = false)
//        {
//            _directory = FSDirectory.Open(indexPath);
//            _analyzer = new HebrewAnalyzer(_version, useStemmer);

//            _config = new IndexWriterConfig(_version, _analyzer)
//            {
//                OpenMode = OpenMode.CREATE_OR_APPEND,
//                RAMBufferSizeMB = 256
//            };

//            _writer = new IndexWriter(_directory, _config);

//            _queue = new BlockingCollection<DbManager.LineWithMetadata>(10_000);
//            _consumerTask = Task.Run(ConsumeQueue);
//        }

//        public void IndexLine(DbManager.LineWithMetadata line) => _queue.Add(line);
//        public void IndexLines(IEnumerable<DbManager.LineWithMetadata> lines)
//        {
//            foreach (var line in lines)
//                IndexLine(line);
//        }

//        private void ConsumeQueue()
//        {
//            foreach (var line in _queue.GetConsumingEnumerable())
//                _writer.AddDocument(CreateDocument(line));
//        }

//        private static Document CreateDocument(DbManager.LineWithMetadata line)
//        {
//            var doc = new Document();
//            doc.Add(new Int32Field("id", line.LineId, Field.Store.YES));

//            var ft = new FieldType(TextField.TYPE_STORED)
//            {
//                IndexOptions = IndexOptions.DOCS_AND_FREQS_AND_POSITIONS_AND_OFFSETS,
//                StoreTermVectors = true,
//                StoreTermVectorPositions = true,
//                StoreTermVectorOffsets = true
//            };
//            ft.Freeze();

//            doc.Add(new Field("content", line.Content ?? "", ft));
//            doc.Add(new StringField("bookTitle", line.BookTitle ?? "", Field.Store.YES));
//            doc.Add(new StringField("toc", line.TocText ?? "", Field.Store.YES));

//            return doc;
//        }

//        public void Optimize()
//        {
//            _writer.ForceMerge(1);
//            _writer.Commit();
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
//        private readonly HebrewAnalyzer _analyzer;
//        private readonly DirectoryReader _reader;
//        private readonly IndexSearcher _searcher;

//        public HebrewSearcher(string indexPath, bool useStemmer = false)
//        {
//            _directory = FSDirectory.Open(indexPath);
//            _analyzer = new HebrewAnalyzer(_version, useStemmer);
//            _reader = DirectoryReader.Open(_directory);
//            _searcher = new IndexSearcher(_reader);
//        }

//        public SearchResult[] Search(string queryText, int maxResults = 100)
//        {
//            var parser = new QueryParser(_version, "content", _analyzer);
//            var query = parser.Parse(queryText);

//            var hits = _searcher.Search(query, maxResults).ScoreDocs;
//            var results = new List<SearchResult>();

//            var scorer = new QueryScorer(query);
//            var formatter = new SimpleHTMLFormatter("<mark>", "</mark>");
//            var highlighter = new Highlighter(formatter, scorer)
//            {
//                TextFragmenter = new SimpleFragmenter(200)
//            };

//            foreach (var hit in hits)
//            {
//                var doc = _searcher.Doc(hit.Doc);
//                var content = doc.Get("content");

//                var ts = _analyzer.GetTokenStream("content", new StringReader(content));
//                var highlighted = highlighter.GetBestFragments(ts, content, 3, "...");

//                results.Add(new SearchResult
//                {
//                    Id = doc.GetField("id").GetInt32Value().Value,
//                    Content = content,
//                    BookTitle = doc.Get("bookTitle"),
//                    Toc = doc.Get("toc"),
//                    Score = hit.Score,
//                    HighlightedContent = string.IsNullOrEmpty(highlighted) ? content : highlighted
//                });
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

//    #region Search Result
//    public sealed class SearchResult
//    {
//        public int Id { get; set; }
//        public string Content { get; set; }
//        public string BookTitle { get; set; }
//        public string Toc { get; set; }
//        public float Score { get; set; }
//        public string HighlightedContent { get; set; }
//    }
//    #endregion

//    #region Text Normalizer
//    public sealed class TextNormalizer
//    {
//        [ThreadStatic]
//        private static StringBuilder _sb;

//        public string Normalize(string text)
//        {
//            if (string.IsNullOrWhiteSpace(text))
//                return text ?? "";

//            if (_sb == null)
//                _sb = new StringBuilder(1024);
//            else
//                _sb.Clear();

//            if (_sb.Capacity < text.Length)
//                _sb.Capacity = text.Length;

//            bool insideTag = false;
//            foreach (char c in text)
//            {
//                if (c == '<') { insideTag = true; continue; }
//                if (c == '>') { insideTag = false; continue; }
//                if (insideTag) continue;

//                if (c == '\u05BE') { _sb.Append(' '); continue; }
//                if (c < 1425 || c > 1487)
//                    _sb.Append(char.ToLowerInvariant(c));
//            }

//            return _sb.ToString();
//        }
//    }
//    #endregion
//}
