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
        string _id;
        const short _chunkSize = 25;

        internal BloomFilterManager(ZayitDbManager zayitDbManager)
        {
            _id = $"by{_chunkSize}";
            _db = zayitDbManager;
        }

        internal void CreateBloomFilters()
        {
            const double ErrorRate = 0.001;
            var stopwatch = Stopwatch.StartNew();
            using (var writer = new BloomFilterCollectionWriter(_id, _chunkSize))
            {
                var books = _db.GetAllBookIds().ToArray();
                var stb = new StringBuilder();
                int filterCount = 0;
                int processedBooks = 0;

                void createFilter(int grouping)
                {
                    if (stb.Length > 0)
                    {
                        var tokens = new TextTokenizer(stb.ToString()).Tokens;
                        stb.Clear();
                        if (tokens.Count > 0)
                        {
                            var bloom = new BloomFilter(tokens.Count, ErrorRate);
                            foreach (var token in tokens)
                                bloom.Add(token);
                            writer.Commit(bloom, filterCount++, grouping);
                        }
                    }
                }

                foreach (var (bookId, totalLines) in books)
                {
                    filterCount = 0;
                    int linesInCurrentChunk = 0;
                    foreach (var line in _db.GetLinesByBook(bookId))
                    {
                        stb.AppendLine(line.Content);
                        linesInCurrentChunk++;
                        if (linesInCurrentChunk >= _chunkSize)
                        {
                            createFilter(bookId);
                            linesInCurrentChunk = 0;
                        }
                    }
                    //process remaining lines
                    createFilter(bookId);
                    Console.WriteLine($"Processed {++processedBooks} / {books.Length} books");
                }


                //important: clear stemmer cache to free memory
                SmartStemmer.ResetCache();
                stopwatch.Stop();
                Console.WriteLine($"Total Processed: {processedBooks} in: {stopwatch.Elapsed.TotalMinutes}");
            }
        }

        internal (short chunkSize, (int bookId, int chunkId)[] values) SearchBloomFilters(string[] searchTerms)
        {
            var reader = new BloomFilterCollectionReader(_id);
            var terms = searchTerms;

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
                    int bookId = g.Key;
                    var list = g.Value;
                    int c = list.Count;
                    int j = 0;

                    // ---- batch of 4 ----
                    for (; j + 3 < c; j += 4)
                    {
                        var f0 = list[j];
                        var f1 = list[j + 1];
                        var f2 = list[j + 2];
                        var f3 = list[j + 3];

                        if (f0.Filter.ContainsAll(terms)) local.Add((bookId, f0.Id));
                        if (f1.Filter.ContainsAll(terms)) local.Add((bookId, f1.Id));
                        if (f2.Filter.ContainsAll(terms)) local.Add((bookId, f2.Id));
                        if (f3.Filter.ContainsAll(terms)) local.Add((bookId, f3.Id));
                    }

                    // ---- tail ----
                    for (; j < c; j++)
                    {
                        var f = list[j];
                        if (f.Filter.ContainsAll(terms))
                            local.Add((bookId, f.Id));
                    }
                }

                locals[w] = local;
            });

            // Merge (ordered)
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

            return (reader.ChunkSize, result);
        }
    }
}