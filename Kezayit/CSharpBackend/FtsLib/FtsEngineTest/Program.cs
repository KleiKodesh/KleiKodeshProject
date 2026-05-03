using System;
using System.IO;

namespace FtsEngineTest
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("FtsEngine Test Suite");
            Console.WriteLine("====================\n");

            string test;
            string indexDir;

            if (args.Length == 0)
            {
                test = ShowMenuAndGetSelection();
                if (string.IsNullOrWhiteSpace(test))
                    return;
                indexDir = GetIndexDirectory();
                if (string.IsNullOrWhiteSpace(indexDir))
                    return;
            }
            else if (args.Length == 1)
            {
                test = args[0].ToLower();
                // correctness test manages its own temp dirs
                if (test == "correct" || test == "0")
                {
                    CorrectnessTest.Run();
                    return;
                }
                Console.WriteLine("Error: indexDir parameter is required");
                Console.WriteLine("Usage: FtsEngineTest.exe [test] [indexDir]");
                Console.WriteLine("\nExample:");
                Console.WriteLine("  FtsEngineTest.exe quick C:\\indexes\\test1");
                Console.WriteLine("  FtsEngineTest.exe full C:\\indexes\\full_db");
                return;
            }
            else
            {
                test = args[0].ToLower();
                indexDir = args[1];
            }

            switch (test)
            {
                case "correct":
                    CorrectnessTest.Run();
                    break;
                case "1":
                case "quick":
                    FullDbTest.Run(lineLimit: 500_000, indexDir: indexDir);
                    break;

                case "2":
                case "medium":
                    FullDbTest.Run(lineLimit: 1_000_000, indexDir: indexDir);
                    break;

                case "3":
                case "large":
                    FullDbTest.Run(lineLimit: 3_000_000, indexDir: indexDir);
                    break;

                case "4":
                case "full":
                    FullDbTest.Run(lineLimit: 0, indexDir: indexDir);
                    break;

                case "5":
                case "all":
                    RunAllTests(indexDir);
                    break;

                default:
                    Console.WriteLine($"Unknown test: {test}");
                    break;
            }
        }

        static string ShowMenuAndGetSelection()
        {
            Console.WriteLine("Select test to run:\n");

            Console.WriteLine("0) correct - correctness & stitch tests (run first!)");
            Console.WriteLine("1) quick  - 500k lines (~1 min)");
            Console.WriteLine("2) medium - 1M lines (~2 min)");
            Console.WriteLine("3) large  - 3M lines (~6 min)");
            Console.WriteLine("4) full   - all 5.4M lines (~17 min)");
            Console.WriteLine("5) all    - run all tests sequentially\n");

            Console.Write("Enter choice (1-5 or name): ");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
                return null;

            return input.Trim().ToLower();
        }

        static string GetIndexDirectory()
        {
            Console.Write("\nEnter index directory path (e.g., C:\\indexes\\test1): ");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine("Error: index directory is required");
                return null;
            }

            string indexDir = input.Trim();
            try
            {
                Directory.CreateDirectory(indexDir);
                Console.WriteLine($"Using index directory: {indexDir}\n");
                return indexDir;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating directory: {ex.Message}");
                return null;
            }
        }

        static void RunAllTests(string baseIndexDir)
        {
            var tests = new[]
            {
                ("quick", 500_000),
                ("medium", 1_000_000),
                ("large", 3_000_000),
                ("full", 0)
            };

            foreach (var (name, limit) in tests)
            {
                string indexDir = Path.Combine(baseIndexDir, name);
                Console.WriteLine($"\n\n{'='} Running {name} test {'='}\n");
                FullDbTest.Run(lineLimit: limit, indexDir: indexDir);
                Console.WriteLine("\nPress any key to continue to next test...");
                Console.ReadKey();
            }
        }
    }
}
