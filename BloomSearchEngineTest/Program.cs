using BloomSearchEngineLib;
using System;
using System.Diagnostics;

namespace BloomSearchEngineTest
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var engine = new BloomSearchEngineLib.BloomFilterIndexer();
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
                Console.WriteLine("2 - Search");
                Console.WriteLine("3 - Search and save to HTML");
                Console.WriteLine("4 - Test text normalizer");
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

                            break;
                        }
                    case "3":
                        {
                            Console.Write("Search query: ");
                            var query = Console.ReadLine();

                            if (string.IsNullOrWhiteSpace(query))
                            {
                                Console.WriteLine("Query cannot be empty.");
                                break;
                            }

                            var swTotal = Stopwatch.StartNew();
                            var swFirst = new Stopwatch();
                            int count = 0;
                            double firstResultMs = -1;
                            var results = new System.Collections.Generic.List<SearchResultItem>();

                            swFirst.Start();
                            var searchEngine = new BloomSearchEngineLib.BloomFilterSearcher();
                            foreach (var result in searchEngine.Search(query))
                            {
                                if (count == 0)
                                {
                                    swFirst.Stop();
                                    firstResultMs = swFirst.Elapsed.TotalSeconds;
                                }
                                results.Add(result);
                                count++;
                            }
                            swTotal.Stop();

                            Console.WriteLine();
                            Console.WriteLine($"Results: {count}");
                            if (count > 0)
                            {
                                Console.WriteLine($"Time to first result: {firstResultMs:0.000} s");
                                Console.WriteLine($"Average per result: {swTotal.Elapsed.TotalSeconds / count:0.000} s");
                            }
                            Console.WriteLine($"Total search time: {swTotal.Elapsed.TotalSeconds:0.000} s");

                            if (count > 0)
                            {
                                string htmlPath = SearchIndexHelper.GenerateHtmlReport(query, results, swTotal.Elapsed, firstResultMs);
                                Console.WriteLine($"HTML report saved to: {htmlPath}");

                                // Open the HTML file in default browser
                                try
                                {
                                    Process.Start(new ProcessStartInfo
                                    {
                                        FileName = htmlPath,
                                        UseShellExecute = true
                                    });
                                    Console.WriteLine("Opening in browser...");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Could not open browser: {ex.Message}");
                                }
                            }
                            else
                            {
                                Console.WriteLine("No results to display.");
                            }
                            break;
                        }
                    case "4":
                        {
                            TextNormalizerTest.RunAll();
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