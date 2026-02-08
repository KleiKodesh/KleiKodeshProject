using System;
using System.Diagnostics;

namespace BloomSearchEngineTest
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var engine = new BloomFilterSearchEngine();

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
                            Console.WriteLine($"Indexing completed in {sw.Elapsed.Seconds}");
                            break;
                        }

                    case "2":
                        {
                            Console.Write("Search query: ");
                            var query = Console.ReadLine();

                            var swTotal = Stopwatch.StartNew();
                            var swFirst = new Stopwatch();

                            int count = 0;
                            double firstResultMs = -1;

                            swFirst.Start();

                            foreach (var result in engine.SearchByChunk(query))
                            {
                                if (count == 0)
                                {
                                    swFirst.Stop();
                                    firstResultMs = swFirst.Elapsed.TotalSeconds;
                                }

                                count++;
                                // optional: Console.WriteLine(result);
                            }

                            swTotal.Stop();

                            Console.WriteLine();
                            Console.WriteLine($"Results: {count}");

                            if (count > 0)
                            {
                                Console.WriteLine($"Time to first result: {firstResultMs} s");
                                Console.WriteLine($"Average per result: {swTotal.ElapsedMilliseconds / (double)count:0.00} ms");
                            }

                            Console.WriteLine($"Total search time: {swTotal.Elapsed}");
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
