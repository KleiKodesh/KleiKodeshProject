using BloomSearchEngineLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BloomSearchEngineTest
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var engine = new BloomFilterIndexer();

            engine.DatabaseInitProgressChanged += (s, e) =>
            {
                Console.WriteLine(
                    $"[DB {e.Percentage:0.0}%] " +
                    $"{e.ProcessedRows}/{e.TotalRows} rows | " +
                    $"Elapsed: {e.Elapsed:mm\\:ss} | ETA: {e.Eta:mm\\:ss}");
            };

            engine.IndexProgressChanged += (s, e) =>
            {
                Console.WriteLine(
                    $"[IDX {e.Percentage:0.0}%] " +
                    $"{e.ProcessedChunks}/{e.TotalChunks} chunks | " +
                    $"Elapsed: {e.Elapsed:mm\\:ss} | ETA: {e.Eta:mm\\:ss}");
            };

            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("Choose action:");
                Console.WriteLine("1 - Create index");
                Console.WriteLine("2 - Search and save to HTML");
                Console.WriteLine("0 - Exit");
                Console.Write("> ");

                var choice = Console.ReadLine();
                if (choice == "0")
                    break;

                switch (choice)
                {
                    case "1":
                        {
                            Console.WriteLine("Initializing database + creating Bloom filters...");
                            var sw = Stopwatch.StartNew();
                            engine.CreateBloomFilters();
                            sw.Stop();
                            Console.WriteLine($"Indexing completed in {sw.Elapsed.TotalSeconds:0.000} s");
                            break;
                        }

                    case "2":
                        {
                            Console.Write("Search query: ");
                            var query = Console.ReadLine();

                            if (string.IsNullOrWhiteSpace(query))
                            {
                                Console.WriteLine("Query cannot be empty.");
                                break;
                            }

                            var swTotal = Stopwatch.StartNew();
                            var swFirst = Stopwatch.StartNew();

                            int count = 0;
                            double firstResultSec = -1;
                            var results = new List<SearchResultItem>();

                            var searcher = new BloomFilterSearcher();
                            foreach (var result in searcher.Search(query))
                            {
                                if (count == 0)
                                {
                                    swFirst.Stop();
                                    firstResultSec = swFirst.Elapsed.TotalSeconds;
                                }

                                results.Add(result);
                                count++;
                            }

                            swTotal.Stop();

                            Console.WriteLine();
                            Console.WriteLine($"Results: {count}");
                            if (count > 0)
                            {
                                Console.WriteLine($"Time to first result: {firstResultSec:0.000} s");
                                Console.WriteLine($"Average per result: {swTotal.Elapsed.TotalSeconds / count:0.000} s");
                            }
                            Console.WriteLine($"Total search time: {swTotal.Elapsed.TotalSeconds:0.000} s");

                            if (count == 0)
                                Console.WriteLine("No results to display.");

                            break;
                        }

                    default:
                        Console.WriteLine("Invalid choice.");
                        break;
                }
            }

            Console.WriteLine("Goodbye 👋");
        }
    }
}
