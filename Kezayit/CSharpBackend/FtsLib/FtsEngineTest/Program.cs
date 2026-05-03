using System;
using System.Collections.Generic;

namespace FtsEngineTest
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("FtsEngine Test Suite");
            Console.WriteLine("====================\n");

            if (args.Length == 0)
            {
                Console.WriteLine("Usage: FtsEngineTest.exe [test]");
                Console.WriteLine("  quick      - 500k lines (~1 min)");
                Console.WriteLine("  medium     - 1M lines (~2 min)");
                Console.WriteLine("  large      - 3M lines (~6 min)");
                Console.WriteLine("  full       - all 5.4M lines (~17 min)");
                Console.WriteLine("  all        - run all tests sequentially");
                return;
            }

            string test = args[0].ToLower();

            switch (test)
            {
                case "quick":
                    FullDbTest.Run(lineLimit: 500_000);
                    break;
                case "medium":
                    FullDbTest.Run(lineLimit: 1_000_000);
                    break;
                case "large":
                    FullDbTest.Run(lineLimit: 3_000_000);
                    break;
                case "full":
                    FullDbTest.Run(lineLimit: 0);
                    break;
                case "all":
                    RunAllTests();
                    break;
                default:
                    Console.WriteLine($"Unknown test: {test}");
                    break;
            }
        }

        static void RunAllTests()
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
                Console.WriteLine($"\n\n{'='} Running {name} test {'='}\n");
                FullDbTest.Run(lineLimit: limit);
                Console.WriteLine("\nPress any key to continue to next test...");
                Console.ReadKey();
            }
        }
    }
}