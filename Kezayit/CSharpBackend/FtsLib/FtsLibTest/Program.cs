using System;
using System.IO;

namespace FtsLibTest
{
    /// <summary>
    /// Usage:
    ///   FtsLibTest.exe                   — run all tiers, write results to test_results.txt
    ///   FtsLibTest.exe 500k              — run 500k tier only
    ///   FtsLibTest.exe 1m                — run 1M tier only
    ///   FtsLibTest.exe 3m                — run 3M tier only
    ///   FtsLibTest.exe full              — run full DB tier only
    ///   FtsLibTest.exe validate [dir]    — validate existing index
    /// </summary>
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            string resultsFile = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "test_results.txt");

            if (args.Length > 0)
            {
                string cmd = args[0].ToLowerInvariant();

                if (cmd == "validate")
                {
                    string dir = args.Length > 1
                        ? args[1]
                        : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "index_test_500k");
                    IndexTest.Validate(dir);
                    return;
                }

                foreach (var tier in IndexTest.Tiers)
                {
                    if (tier.label.ToLowerInvariant() == cmd)
                    {
                        using (var log = new StreamWriter(resultsFile, append: true,
                                                          System.Text.Encoding.UTF8))
                        {
                            log.WriteLine($"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                            IndexTest.RunTier(tier.label, tier.limit, log);
                            log.WriteLine();
                        }
                        Console.WriteLine($"\nResults appended to: {resultsFile}");
                        return;
                    }
                }

                Console.WriteLine($"Unknown command '{args[0]}'.");
                Console.WriteLine("Valid: 500k | 1m | 3m | full | validate [dir]");
                return;
            }

            // No args — run all tiers, write everything to results file
            using (var log = new StreamWriter(resultsFile, append: false,
                                              System.Text.Encoding.UTF8))
            {
                log.WriteLine($"=== FtsLib Test Run — {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                log.WriteLine();

                foreach (var tier in IndexTest.Tiers)
                {
                    IndexTest.RunTier(tier.label, tier.limit, log);
                    log.WriteLine();
                    Console.WriteLine();
                }

                log.WriteLine("=== All tiers complete ===");
            }

            Console.WriteLine($"\nFull results written to: {resultsFile}");
        }
    }
}
