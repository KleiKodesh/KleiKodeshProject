//using System;
//using System.Diagnostics;
//using System.Linq;

//namespace MinimalIndexer
//{
//    internal class ChunkSizeConfig
//    {
//        internal short? Tier1ChunkSizeOverride { get; set; } = 25;
//        internal short? Tier2ChunkSizeOverride { get; set; } = 5;
//        internal int TargetTier1Filters { get; set; } = 8000;
//        internal int GranularityRatio { get; set; } = 12;
//        internal short MinTier1ChunkSize { get; set; } = 50;
//        internal short MaxTier1ChunkSize { get; set; } = 5000;
//        internal short MinTier2ChunkSize { get; set; } = 10;
//        internal short MaxTier2ChunkSize { get; set; } = 500;
//        internal double SmallBookAdjustmentFactor { get; set; } = 0.5;
//    }

//    internal class BloomFilterChunkSize
//    {
//        private readonly ZayitDbManager _db;

//        internal BloomFilterChunkSize(ZayitDbManager db)
//        {
//            _db = db;
//        }

//        internal (short tier1, short tier2) CalculateOptimalChunkSizes(ChunkSizeConfig config)
//        {
//            if (config == null)
//            {
//                config = new ChunkSizeConfig();
//            }

//            // If both overrides are set, use them directly
//            if (config.Tier1ChunkSizeOverride.HasValue && config.Tier2ChunkSizeOverride.HasValue)
//            {
//                Console.WriteLine("[ChunkSizeOptimizer] Using manual chunk size overrides:");
//                Console.WriteLine("    - Tier 1 chunk size: " + config.Tier1ChunkSizeOverride.Value + " lines");
//                Console.WriteLine("    - Tier 2 chunk size: " + config.Tier2ChunkSizeOverride.Value + " lines");
//                return (config.Tier1ChunkSizeOverride.Value, config.Tier2ChunkSizeOverride.Value);
//            }

//            Console.WriteLine("[ChunkSizeOptimizer] Analyzing corpus...");
//            var stopwatch = Stopwatch.StartNew();
//            var books = _db.GetAllBookIds().ToArray();

//            // Calculate corpus statistics
//            long totalLines = 0;
//            int totalBooks = books.Length;
//            int minLines = int.MaxValue;
//            int maxLines = 0;

//            foreach (var book in books)
//            {
//                int id = book.Item1;
//                int lines = book.Item2;
//                totalLines += lines;
//                minLines = Math.Min(minLines, lines);
//                maxLines = Math.Max(maxLines, lines);
//            }

//            double avgLinesPerBook = totalLines / (double)totalBooks;

//            // Calculate Tier 1 chunk size
//            short tier1ChunkSize;
//            if (config.Tier1ChunkSizeOverride.HasValue)
//            {
//                tier1ChunkSize = config.Tier1ChunkSizeOverride.Value;
//            }
//            else
//            {
//                long rawTier1Chunk = totalLines / config.TargetTier1Filters;
//                tier1ChunkSize = (short)Clamp(rawTier1Chunk, config.MinTier1ChunkSize, config.MaxTier1ChunkSize);

//                if (avgLinesPerBook < tier1ChunkSize)
//                {
//                    tier1ChunkSize = (short)Clamp(avgLinesPerBook * config.SmallBookAdjustmentFactor,
//                        config.MinTier1ChunkSize, config.MaxTier1ChunkSize);
//                }
//            }

//            // Calculate Tier 2 chunk size
//            short tier2ChunkSize;
//            if (config.Tier2ChunkSizeOverride.HasValue)
//            {
//                tier2ChunkSize = config.Tier2ChunkSizeOverride.Value;
//            }
//            else
//            {
//                long rawTier2Chunk = tier1ChunkSize / config.GranularityRatio;
//                tier2ChunkSize = (short)Clamp(rawTier2Chunk, config.MinTier2ChunkSize, config.MaxTier2ChunkSize);
//            }

//            // Estimates
//            long estimatedTier1Filters = totalLines / tier1ChunkSize;
//            long estimatedTier2Filters = totalLines / tier2ChunkSize;
//            double estimatedMemoryMB = (estimatedTier1Filters + estimatedTier2Filters) * 150 / 1024.0 / 1024.0;
//            double estimatedCreationTime = (estimatedTier1Filters + estimatedTier2Filters) / 15000.0;

//            stopwatch.Stop();

//            Console.WriteLine("[ChunkSizeOptimizer] Analysis complete in " + stopwatch.ElapsedMilliseconds + "ms");
//            Console.WriteLine("  Corpus Statistics:");
//            Console.WriteLine("    - Total books: " + totalBooks);
//            Console.WriteLine("    - Total lines: " + totalLines);
//            Console.WriteLine("    - Average lines per book: " + avgLinesPerBook);
//            Console.WriteLine("    - Min/Max lines per book: " + minLines + "/" + maxLines);
//            Console.WriteLine("  Optimal Configuration:");
//            Console.WriteLine("    - Tier 1 chunk size: " + tier1ChunkSize + " lines");
//            Console.WriteLine("    - Tier 2 chunk size: " + tier2ChunkSize + " lines");
//            Console.WriteLine("    - Granularity ratio: 1:" + (tier1ChunkSize / tier2ChunkSize));
//            Console.WriteLine("  Expected Results:");
//            Console.WriteLine("    - Tier 1 filters: ~" + estimatedTier1Filters);
//            Console.WriteLine("    - Tier 2 filters: ~" + estimatedTier2Filters);
//            Console.WriteLine("    - Total filters: ~" + (estimatedTier1Filters + estimatedTier2Filters));
//            Console.WriteLine("    - Estimated index size: ~" + estimatedMemoryMB + " MB");
//            Console.WriteLine("    - Estimated creation time: ~" + estimatedCreationTime + " minutes\n");

//            return (tier1ChunkSize, tier2ChunkSize);
//        }

//        private static long Clamp(long value, long min, long max)
//        {
//            return Math.Max(min, Math.Min(max, value));
//        }

//        private static long Clamp(double value, long min, long max)
//        {
//            return Clamp((long)value, min, max);
//        }
//    }
//}
