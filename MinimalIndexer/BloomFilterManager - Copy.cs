//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Linq;
//using System.Text;

//namespace MinimalIndexer
//{
//    internal class BloomFilterManager
//    {
//        ZayitDbManager _db;
//        const double ErrorRate = 0.001;
//        const string tier1IndexName = "Tier1Filters";
//        const string tier2IndexName = "Tier2Filters";

//        internal BloomFilterManager(ZayitDbManager zayitDbManager)
//        {
//            _db = zayitDbManager;
//        }

//        internal void CreateBloomFilters(short tier1ChunkSize, short tier2ChunkSize)
//        {
//            var totalStopwatch = Stopwatch.StartNew();

//            using (var tier1Writer = new BloomFilterCollectionWriter(tier1IndexName, tier1ChunkSize))
//            using (var tier2Writer = new BloomFilterCollectionWriter(tier2IndexName, tier2ChunkSize))
//            {
//                var books = _db.GetAllBookIds().ToArray();
//                Console.WriteLine($"[CreateBloomFilters] Processing {books.Length} books with Tier1={tier1ChunkSize}, Tier2={tier2ChunkSize}");

//                int tier1FilterId = 0;
//                int tier2FilterCount = 0;
//                int processedBooks = 0;

//                var processingStopwatch = Stopwatch.StartNew();

//                foreach (var (bookId, totalLines) in books)
//                {
//                    var lines = _db.GetLinesByBook(bookId).ToArray();

//                    // Process this book: create multiple Tier 1 filters (large chunks) 
//                    // and even more Tier 2 filters (small chunks within each Tier 1 chunk)
//                    var (tier1Filters, tier2Filters) = ProcessBook(bookId, lines, tier1ChunkSize, tier2ChunkSize);

//                    // Write all filters for this book
//                    foreach (var (tier1Filter, tier1ChunkPosition, tier2FiltersForThisTier1) in tier1Filters)
//                    {
//                        // Write Tier 2 filters first (belonging to this Tier 1 filter)
//                        foreach (var (tier2Filter, tier2ChunkPosition) in tier2FiltersForThisTier1)
//                        {
//                            // Id = chunk position within Tier 1 chunk (0, 1, 2, ...)
//                            // Grouping = parent Tier 1 filter ID
//                            tier2Writer.Commit(tier2Filter, tier2ChunkPosition, tier1FilterId);
//                            tier2FilterCount++;
//                        }

//                        // Write Tier 1 filter
//                        // Id = sequential global Tier 1 filter counter
//                        // Grouping = bookId (so we know which book this chunk belongs to)
//                        tier1Writer.Commit(tier1Filter, tier1FilterId, bookId);
//                        tier1FilterId++;
//                    }

//                    processedBooks++;
//                    if (processedBooks % 100 == 0)
//                    {
//                        double elapsed = processingStopwatch.Elapsed.TotalSeconds;
//                        double rate = processedBooks / elapsed;
//                        double eta = (books.Length - processedBooks) / rate;
//                        Console.WriteLine($"[CreateBloomFilters] {processedBooks}/{books.Length} books ({elapsed:F1}s, {rate:F1} books/sec, ETA: {eta:F0}s)");
//                        //  Console.WriteLine($"  - Tier 1 filters: {tier1FilterId:N0}, Tier 2 filters: {tier2FilterCount:N0}");
//                    }
//                }

//                //important: clear stemmer cache to free memory
//                SmartStemmer.ResetCache();
//                processingStopwatch.Stop();

//                totalStopwatch.Stop();
//                Console.WriteLine($"[CreateBloomFilters] Complete!");
//                Console.WriteLine($"  - Total time: {totalStopwatch.Elapsed.TotalSeconds:F1}s");
//                Console.WriteLine($"  - Processing: {processingStopwatch.Elapsed.TotalSeconds:F1}s");
//                Console.WriteLine($"  - Tier 1 filters: {tier1FilterId:N0}");
//                Console.WriteLine($"  - Tier 2 filters: {tier2FilterCount:N0}");
//                Console.WriteLine($"  - Average: {tier1FilterId / totalStopwatch.Elapsed.TotalSeconds:F0} tier-1 filters/sec");
//            }
//        }

//        private (List<(BloomFilter tier1Filter, int tier1ChunkPosition, List<(BloomFilter, int)> tier2Filters)> tier1Filters, int totalTier2Filters) ProcessBook(
//            int bookId,
//            (int LineIndex, string Content)[] lines,
//            short tier1ChunkSize,
//            short tier2ChunkSize)
//        {
//            var tier1Results = new List<(BloomFilter, int, List<(BloomFilter, int)>)>();

//            int tier1ChunkPosition = 0;
//            int linePositionInBook = 0;
//            int totalTier2Filters = 0;
//            int globalTier2ChunkId = 0; // Global chunk counter within the book (never resets)

//            while (linePositionInBook < lines.Length)
//            {
//                // Process one Tier 1 chunk (e.g., 500 lines)
//                int tier1StartLine = linePositionInBook;
//                int tier1EndLine = Math.Min(linePositionInBook + tier1ChunkSize, lines.Length);

//                var tier1Accumulator = new HashSet<string>();
//                var tier2FiltersForThisTier1 = new List<(BloomFilter, int)>();

//                int linePositionInTier1 = tier1StartLine;

//                while (linePositionInTier1 < tier1EndLine)
//                {
//                    // Process one Tier 2 chunk (e.g., 50 lines)
//                    int tier2StartLine = linePositionInTier1;
//                    int tier2EndLine = Math.Min(linePositionInTier1 + tier2ChunkSize, tier1EndLine);

//                    var tier2Accumulator = new HashSet<string>();
//                    var lineBuffer = new StringBuilder();

//                    for (int i = tier2StartLine; i < tier2EndLine; i++)
//                    {
//                        lineBuffer.AppendLine(lines[i].Content);
//                    }

//                    if (lineBuffer.Length > 0)
//                    {
//                        var tokens = new TextTokenizer(lineBuffer.ToString()).Tokens;

//                        if (tokens.Count > 0)
//                        {
//                            foreach (var token in tokens)
//                            {
//                                tier2Accumulator.Add(token);
//                                tier1Accumulator.Add(token); // Also add to Tier 1
//                            }

//                            var tier2Filter = new BloomFilter(tier2Accumulator.Count, ErrorRate);
//                            foreach (var token in tier2Accumulator)
//                            {
//                                tier2Filter.Add(token);
//                            }

//                            // CRITICAL: Use globalTier2ChunkId (absolute position in book)
//                            // NOT a reset counter per Tier 1 filter
//                            tier2FiltersForThisTier1.Add((tier2Filter, globalTier2ChunkId));
//                            globalTier2ChunkId++;
//                            totalTier2Filters++;
//                        }
//                    }

//                    linePositionInTier1 = tier2EndLine;
//                }

//                // Create Tier 1 filter from accumulated tokens
//                BloomFilter tier1Filter;
//                if (tier1Accumulator.Count > 0)
//                {
//                    tier1Filter = new BloomFilter(tier1Accumulator.Count, ErrorRate);
//                    foreach (var token in tier1Accumulator)
//                    {
//                        tier1Filter.Add(token);
//                    }
//                }
//                else
//                {
//                    tier1Filter = new BloomFilter(1, ErrorRate);
//                }

//                tier1Results.Add((tier1Filter, tier1ChunkPosition, tier2FiltersForThisTier1));
//                tier1ChunkPosition++;
//                linePositionInBook = tier1EndLine;
//            }

//            return (tier1Results, totalTier2Filters);
//        }

//        internal (short tier2ChunkSize, List<(int bookId, int tier2ChunkId)> values) SearchBloomFilters(string[] searchTerms)
//        {
//            var sw = Stopwatch.StartNew();

//            // Initialize readers
//            var tier1Reader = new BloomFilterCollectionReader(tier1IndexName);
//            var tier2Reader = new BloomFilterCollectionReader(tier2IndexName);
//            Console.WriteLine($"[1] Readers init: {sw.ElapsedMilliseconds}ms");

//            // Load and check Tier1 filters (SINGLE PASS)
//            sw.Restart();
//            tier1Reader.LoadAllMetaData();
//            var tier1Filters = tier1Reader.GetAllFilters();
//            Console.WriteLine($"[2] Load Tier1: {sw.ElapsedMilliseconds}ms");

//            sw.Restart();
//            var candidateTier1Filters = new List<(int tier1Id, int bookId)>();
//            foreach (var filter in tier1Filters)
//            {
//                if (filter.Filter.ContainsAll(searchTerms))
//                    candidateTier1Filters.Add((filter.Id, filter.Grouping));
//            }
//            Console.WriteLine($"[3] Tier1 check: {sw.ElapsedMilliseconds}ms, matches: {candidateTier1Filters.Count}");

//            // Load Tier2 metadata and build lookup
//            sw.Restart();
//            tier2Reader.LoadAllMetaData();
//            var tier2ByTier1 = new Dictionary<int, List<BloomFilterCollectionReader.MetaDataModel>>();
//            for (int i = 0; i < tier2Reader.MetaData.Length; i++)
//            {
//                var meta = tier2Reader.MetaData[i];
//                if (!tier2ByTier1.ContainsKey(meta.Grouping))
//                    tier2ByTier1[meta.Grouping] = new List<BloomFilterCollectionReader.MetaDataModel>();
//                tier2ByTier1[meta.Grouping].Add(meta);
//            }
//            Console.WriteLine($"[4] Tier2 metadata & lookup: {sw.ElapsedMilliseconds}ms");

//            // Collect relevant Tier2 filters
//            sw.Restart();
//            var relevantTier2Meta = new List<(int bookId, BloomFilterCollectionReader.MetaDataModel meta)>();
//            foreach (var (tier1Id, bookId) in candidateTier1Filters)
//            {
//                if (tier2ByTier1.TryGetValue(tier1Id, out var tier2List))
//                {
//                    foreach (var meta in tier2List)
//                        relevantTier2Meta.Add((bookId, meta));
//                }
//            }
//            relevantTier2Meta.Sort((a, b) => a.meta.Offset.CompareTo(b.meta.Offset));
//            Console.WriteLine($"[5] Collect & sort Tier2: {sw.ElapsedMilliseconds}ms, count: {relevantTier2Meta.Count}");

//            // Check Tier2 filters
//            sw.Restart();
//            var results = new List<(int bookId, int tier2ChunkId)>();
//            foreach (var (bookId, meta) in relevantTier2Meta)
//            {
//                var filter = tier2Reader.GetFilter(meta);
//                if (filter.ContainsAll(searchTerms))
//                    results.Add((bookId, meta.Id));
//            }
//            Console.WriteLine($"[6] Tier2 check: {sw.ElapsedMilliseconds}ms, matches: {results.Count}");

//            return (tier2Reader.ChunkSize, results);
//        }
//    }
//}