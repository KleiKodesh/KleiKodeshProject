using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MinimalIndexer
{
    internal class BloomFilterManager
    {
        ZayitDbManager _db;
        string _tier1Id;
        string _tier2Id;
        const short _tier1ChunkSize = 500;
        const short _tier2ChunkSize = 25;

        internal BloomFilterManager(ZayitDbManager zayitDbManager)
        {
            _tier1Id = $"tier1_{_tier1ChunkSize}";
            _tier2Id = $"tier2_{_tier2ChunkSize}";
            _db = zayitDbManager;
        }

        internal void CreateBloomFilters()
        {
            const double ErrorRate = 0.001;
            var stopwatch = Stopwatch.StartNew();

            using (var tier1Writer = new BloomFilterCollectionWriter(_tier1Id, _tier1ChunkSize))
            using (var tier2Writer = new BloomFilterCollectionWriter(_tier2Id, _tier2ChunkSize))
            {
                var books = _db.GetAllBookIds().ToArray();
                var tier1Sb = new StringBuilder();
                var tier2Sb = new StringBuilder();
                int globalTier1ChunkId = 0;  // GLOBAL counter across all books
                int processedBooks = 0;

                void createTier1Filter(int bookId)
                {
                    if (tier1Sb.Length > 0)
                    {
                        var tokens = new TextTokenizer(tier1Sb.ToString()).Tokens;
                        tier1Sb.Clear();
                        if (tokens.Count > 0)
                        {
                            var bloom = new BloomFilter(tokens.Count, ErrorRate);
                            foreach (var token in tokens)
                                bloom.Add(token);
                            // Tier 1: Id = GLOBAL tier1 chunk number, Grouping = bookId
                            tier1Writer.Commit(bloom, globalTier1ChunkId, bookId);
                            globalTier1ChunkId++;
                        }
                    }
                }

                void createTier2Filter(int currentGlobalTier1ChunkId, int bookRelativeTier2Chunk)
                {
                    if (tier2Sb.Length > 0)
                    {
                        var tokens = new TextTokenizer(tier2Sb.ToString()).Tokens;
                        tier2Sb.Clear();
                        if (tokens.Count > 0)
                        {
                            var bloom = new BloomFilter(tokens.Count, ErrorRate);
                            foreach (var token in tokens)
                                bloom.Add(token);
                            // CRITICAL: Store BOOK-RELATIVE tier2 chunk as ID
                            // Id = book-relative tier2 chunk (0, 1, 2, 3... within THIS book)
                            // Grouping = GLOBAL tier1 chunk id (for lookup during search)
                            tier2Writer.Commit(bloom, bookRelativeTier2Chunk, currentGlobalTier1ChunkId);
                        }
                    }
                }

                foreach (var (bookId, totalLines) in books)
                {
                    int linesInTier1Chunk = 0;
                    int linesInTier2Chunk = 0;
                    int currentTier1ChunkId = globalTier1ChunkId;  // Capture ID at start of tier1 chunk
                    int tier2SubChunkId = 0;  // Sub-chunk within current tier1 chunk (0-49)
                    int bookRelativeTier2Chunk = 0;  // BOOK-RELATIVE tier2 chunk counter (resets per book)

                    foreach (var line in _db.GetLinesByBook(bookId))
                    {
                        // Add to both tiers
                        tier1Sb.AppendLine(line.Content);
                        tier2Sb.AppendLine(line.Content);

                        linesInTier1Chunk++;
                        linesInTier2Chunk++;

                        // Check tier 2 first (smaller chunks)
                        if (linesInTier2Chunk >= _tier2ChunkSize)
                        {
                            createTier2Filter(currentTier1ChunkId, bookRelativeTier2Chunk);
                            tier2SubChunkId++;
                            bookRelativeTier2Chunk++;  // Increment book-relative counter
                            linesInTier2Chunk = 0;
                        }

                        // Check tier 1
                        if (linesInTier1Chunk >= _tier1ChunkSize)
                        {
                            createTier1Filter(bookId);
                            currentTier1ChunkId = globalTier1ChunkId;  // Update to new global ID
                            tier2SubChunkId = 0;  // Reset sub-chunk counter
                            linesInTier1Chunk = 0;
                        }
                    }

                    // Process remaining lines
                    if (tier2Sb.Length > 0)
                        createTier2Filter(currentTier1ChunkId, bookRelativeTier2Chunk);
                    if (tier1Sb.Length > 0)
                        createTier1Filter(bookId);

                    Console.WriteLine($"Processed {++processedBooks} / {books.Length} books");
                }

                // Important: clear stemmer cache to free memory
                SmartStemmer.ResetCache();
                stopwatch.Stop();
                Console.WriteLine($"Total Processed: {processedBooks} in: {stopwatch.Elapsed.TotalMinutes:F2} minutes");
            }
        }

        internal (short chunkSize, (int bookId, int chunkId)[] values) SearchBloomFilters(string[] searchTerms)
        {
            var tier1Reader = new BloomFilterCollectionReader(_tier1Id);
            var terms = searchTerms;

            // Step 1: Search tier 1 (500-line chunks) in parallel
            var tier1Matches = SearchTier1(tier1Reader, terms);

            Console.WriteLine($"Tier 1 matches: {tier1Matches.Count} chunks out of {tier1Reader.MetaDataCount}");

            if (tier1Matches.Count == 0)
            {
                return (_tier2ChunkSize, Array.Empty<(int, int)>());
            }

            // Step 2: For each tier 1 match, search corresponding tier 2 chunks
            var tier2Reader = new BloomFilterCollectionReader(_tier2Id);
            var tier2Matches = SearchTier2(tier2Reader, tier1Matches, terms);

            Console.WriteLine($"Tier 2 matches: {tier2Matches.Length} sub-chunks");

            return (_tier2ChunkSize, tier2Matches);
        }

        private List<(int bookId, int globalTier1ChunkId)> SearchTier1(BloomFilterCollectionReader reader, string[] terms)
        {
            int workers = Environment.ProcessorCount;
            int n = reader.Filters.Count;
            int step = (n + workers - 1) / workers;

            var locals = new List<(int, int)>[workers];

            Parallel.For(0, workers, w =>
            {
                int start = w * step;
                int end = start + step;
                if (start >= n) return;
                if (end > n) end = n;

                var local = new List<(int, int)>(256);

                for (int i = start; i < end; i++)
                {
                    var g = reader.Filters[i];
                    int bookId = g.Key;  // Grouping is bookId
                    var list = g.Value;
                    int c = list.Count;
                    int j = 0;

                    // Batch of 4
                    for (; j + 3 < c; j += 4)
                    {
                        var f0 = list[j];
                        var f1 = list[j + 1];
                        var f2 = list[j + 2];
                        var f3 = list[j + 3];

                        // f.Id is the GLOBAL tier1 chunk ID
                        if (f0.Filter.ContainsAll(terms)) local.Add((bookId, f0.Id));
                        if (f1.Filter.ContainsAll(terms)) local.Add((bookId, f1.Id));
                        if (f2.Filter.ContainsAll(terms)) local.Add((bookId, f2.Id));
                        if (f3.Filter.ContainsAll(terms)) local.Add((bookId, f3.Id));
                    }

                    // Tail
                    for (; j < c; j++)
                    {
                        var f = list[j];
                        if (f.Filter.ContainsAll(terms))
                            local.Add((bookId, f.Id));
                    }
                }

                locals[w] = local;
            });

            // Merge results
            var result = new List<(int, int)>();
            for (int i = 0; i < workers; i++)
                if (locals[i] != null)
                    result.AddRange(locals[i]);

            return result;
        }

        private (int bookId, int chunkId)[] SearchTier2(
            BloomFilterCollectionReader reader,
            List<(int bookId, int globalTier1ChunkId)> tier1Matches,
            string[] terms)
        {
            // Create lookup for tier1ChunkId -> bookId
            var tier1ToBook = new Dictionary<int, int>();
            foreach (var match in tier1Matches)
            {
                tier1ToBook[match.globalTier1ChunkId] = match.bookId;
            }

            // Create set of tier1 chunk IDs to search
            var tier1ChunkIds = new HashSet<int>();
            foreach (var match in tier1Matches)
            {
                tier1ChunkIds.Add(match.globalTier1ChunkId);
            }

            int workers = Environment.ProcessorCount;
            int n = reader.Filters.Count;
            int step = (n + workers - 1) / workers;

            var locals = new List<(int, int)>[workers];

            Parallel.For(0, workers, w =>
            {
                int start = w * step;
                int end = start + step;
                if (start >= n) return;
                if (end > n) end = n;

                var local = new List<(int, int)>(256);

                for (int i = start; i < end; i++)
                {
                    var g = reader.Filters[i];
                    int globalTier1ChunkId = g.Key; // Grouping is GLOBAL tier1 chunk id

                    // Only search if this tier 1 chunk matched
                    if (!tier1ChunkIds.Contains(globalTier1ChunkId))
                        continue;

                    int bookId = tier1ToBook[globalTier1ChunkId];
                    var list = g.Value;
                    int c = list.Count;
                    int j = 0;

                    // CRITICAL FIX: f.Id is now BOOK-RELATIVE tier2 chunk
                    // No calculation needed - just use it directly!

                    // Batch of 4
                    for (; j + 3 < c; j += 4)
                    {
                        var f0 = list[j];
                        var f1 = list[j + 1];
                        var f2 = list[j + 2];
                        var f3 = list[j + 3];

                        // f.Id is already the book-relative tier2 chunk - use directly
                        if (f0.Filter.ContainsAll(terms)) local.Add((bookId, f0.Id));
                        if (f1.Filter.ContainsAll(terms)) local.Add((bookId, f1.Id));
                        if (f2.Filter.ContainsAll(terms)) local.Add((bookId, f2.Id));
                        if (f3.Filter.ContainsAll(terms)) local.Add((bookId, f3.Id));
                    }

                    // Tail
                    for (; j < c; j++)
                    {
                        var f = list[j];
                        if (f.Filter.ContainsAll(terms))
                            local.Add((bookId, f.Id));
                    }
                }

                locals[w] = local;
            });

            // Merge results
            int total = 0;
            for (int i = 0; i < workers; i++)
                if (locals[i] != null)
                    total += locals[i].Count;

            var result = new (int, int)[total];
            int pos = 0;

            for (int i = 0; i < workers; i++)
            {
                var list = locals[i];
                if (list == null) continue;
                list.CopyTo(result, pos);
                pos += list.Count;
            }

            return result;
        }
    }
}