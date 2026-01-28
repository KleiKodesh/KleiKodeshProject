//using System;
//using System.Linq;

//namespace MinimalIndexer
//{
//    internal class IndexDiagnostic
//    {
//        internal static void DiagnoseIndex()
//        {
//            Console.WriteLine("\n=== Index Structure Diagnostic ===\n");

//            try
//            {
//                using (var readerI = new BloomFilterCollectionReader("Tier1Filters"))
//                using (var readerII = new BloomFilterCollectionReader("Tier2Filters"))
//                {
//                    readerI.LoadAllMetaData();
//                    readerII.LoadAllMetaData();

//                    Console.WriteLine($"Level I: {readerI.MetaDataCount:N0} filters (chunk size: {readerI.ChunkSize})");
//                    Console.WriteLine($"Level II: {readerII.MetaDataCount:N0} filters (chunk size: {readerII.ChunkSize})\n");

//                    // Check Level I structure
//                    Console.WriteLine("=== Level I Analysis ===");
//                    var levelIGroupings = readerI.MetaData
//                        .GroupBy(m => m.Grouping)
//                        .OrderBy(g => g.Key)
//                        .ToList();

//                    Console.WriteLine($"Unique books (groupings) in Level I: {levelIGroupings.Count:N0}");
//                    Console.WriteLine($"Expected: 1 filter per book");

//                    var booksWithMultipleFilters = levelIGroupings.Where(g => g.Count() > 1).ToList();
//                    if (booksWithMultipleFilters.Any())
//                    {
//                        Console.WriteLine($"❌ ERROR: {booksWithMultipleFilters.Count} books have multiple Level I filters!");
//                        foreach (var book in booksWithMultipleFilters.Take(5))
//                        {
//                            Console.WriteLine($"  Book {book.Key}: {book.Count()} filters");
//                        }
//                    }
//                    else
//                    {
//                        Console.WriteLine("✓ Each book has exactly 1 Level I filter");
//                    }

//                    // Show sample Level I records
//                    Console.WriteLine("\nSample Level I metadata (first 5):");
//                    foreach (var meta in readerI.MetaData.Take(5))
//                    {
//                        Console.WriteLine($"  Id={meta.Id}, Grouping(BookId)={meta.Grouping}, Size={meta.Length:N0}");
//                    }

//                    // Check Level II structure
//                    Console.WriteLine("\n=== Level II Analysis ===");
//                    var levelIIGroupings = readerII.MetaData
//                        .GroupBy(m => m.Grouping)
//                        .OrderBy(g => g.Key)
//                        .ToList();

//                    Console.WriteLine($"Unique books (groupings) in Level II: {levelIIGroupings.Count:N0}");
//                    Console.WriteLine($"Filters per book (avg): {readerII.MetaDataCount / (double)levelIIGroupings.Count:F1}");

//                    // Check if Ids are per-book (starting from 0) or global
//                    Console.WriteLine("\nChecking Level II Id structure...");
//                    bool idsArePerBook = true;
//                    var firstBook = levelIIGroupings.First();
//                    var firstBookFilters = firstBook.OrderBy(m => m.Id).ToList();

//                    if (firstBookFilters.First().Id != 0)
//                    {
//                        Console.WriteLine($"❌ ERROR: First book's first filter has Id={firstBookFilters.First().Id}, expected 0");
//                        idsArePerBook = false;
//                    }

//                    var secondBook = levelIIGroupings.Skip(1).FirstOrDefault();
//                    if (secondBook != null)
//                    {
//                        var secondBookFilters = secondBook.OrderBy(m => m.Id).ToList();
//                        if (secondBookFilters.First().Id != 0)
//                        {
//                            Console.WriteLine($"❌ ERROR: Second book's first filter has Id={secondBookFilters.First().Id}, expected 0");
//                            Console.WriteLine($"  This suggests Ids are global counters, not per-book chunk positions!");
//                            idsArePerBook = false;
//                        }
//                    }

//                    if (idsArePerBook)
//                    {
//                        Console.WriteLine("✓ Level II Ids appear to be per-book chunk positions (start at 0 for each book)");
//                    }
//                    else
//                    {
//                        Console.WriteLine("❌ Level II Ids are GLOBAL COUNTERS - INDEX NEEDS REBUILD!");
//                    }

//                    // Show sample Level II records for first few books
//                    Console.WriteLine("\nSample Level II metadata (first 3 books):");
//                    foreach (var book in levelIIGroupings.Take(3))
//                    {
//                        var filters = book.OrderBy(m => m.Id).ToList();
//                        Console.WriteLine($"\n  Book {book.Key}: {filters.Count} chunks");
//                        foreach (var meta in filters.Take(3))
//                        {
//                            Console.WriteLine($"    Id(ChunkPos)={meta.Id}, Size={meta.Length:N0}, Offset={meta.Offset:N0}");
//                        }
//                        if (filters.Count > 3)
//                        {
//                            Console.WriteLine($"    ... and {filters.Count - 3} more");
//                        }
//                    }

//                    // Check for books with unusual chunk counts
//                    Console.WriteLine("\n=== Books with Most/Least Chunks ===");
//                    var sortedByChunks = levelIIGroupings
//                        .Select(g => new { BookId = g.Key, ChunkCount = g.Count() })
//                        .OrderByDescending(x => x.ChunkCount)
//                        .ToList();

//                    Console.WriteLine("Top 5 books by chunk count:");
//                    foreach (var book in sortedByChunks.Take(5))
//                    {
//                        Console.WriteLine($"  Book {book.BookId}: {book.ChunkCount:N0} chunks");
//                    }

//                    Console.WriteLine("\nBottom 5 books by chunk count:");
//                    foreach (var book in sortedByChunks.OrderBy(x => x.ChunkCount).Take(5))
//                    {
//                        Console.WriteLine($"  Book {book.BookId}: {book.ChunkCount:N0} chunks");
//                    }

//                    // Final verdict
//                    Console.WriteLine("\n=== Diagnostic Summary ===");
//                    if (idsArePerBook && levelIGroupings.Count == levelIIGroupings.Count)
//                    {
//                        Console.WriteLine("✓✓✓ Index structure looks CORRECT!");
//                        Console.WriteLine("    - Level I: 1 filter per book");
//                        Console.WriteLine("    - Level II: Chunk Ids are per-book positions");
//                        Console.WriteLine("    - Both levels reference the same books");
//                    }
//                    else
//                    {
//                        Console.WriteLine("❌❌❌ Index structure has ISSUES!");
//                        Console.WriteLine("    You need to rebuild the index with the corrected code.");
//                    }
//                }
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"Error during diagnostic: {ex.Message}");
//                Console.WriteLine(ex.StackTrace);
//            }
//        }
//    }
//}