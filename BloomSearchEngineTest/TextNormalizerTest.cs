using BloomSearchEngineLib;
using System;
using System.Diagnostics;

namespace BloomSearchEngineTest
{
    internal static class TextNormalizerTest
    {
        public static void RunAll()
        {
            RunAutoTests();
            RunStressTest(1_000_000);
        }

        private static void RunAutoTests()
        {
            Console.WriteLine();
            Console.WriteLine("=== TextNormalizer Automatic Tests ===");
            Console.WriteLine();

            Test("Simple Hebrew",
                "שלום עולם");

            Test("Nikkud",
                "שָׁלוֹם עוֹלָם");

            Test("Cantillation",
                "וַיְהִ֖י הָאָדָ֑ם");

            Test("Maqaf",
                "שלום־עולם");

            Test("Dash",
                "שלום-עולם");

            Test("Underscore",
                "שלום_עולם");

            Test("HTML tags",
                "<div>שלום <b>עולם</b></div>");

            Test("Quotes inside word",
                "שלו\"ם עו\"לם");

            Test("Punctuation",
                "שלום, עולם! מה שלומך?");

            Test("Mixed garbage",
                "<p>שָׁלוֹם־עוֹלָם_123!!!</p>");

            Console.WriteLine();
        }

        private static void Test(string name, string input)
        {
            var sw = Stopwatch.StartNew();
            var output = input.NormalizeText();
            sw.Stop();

            Console.WriteLine($"[{name}]");
            Console.WriteLine($"IN : {input}");
            Console.WriteLine($"OUT: {output}");
            Console.WriteLine($"Time: {sw.ElapsedTicks} ticks");

            if (string.IsNullOrWhiteSpace(output))
                Console.WriteLine("⚠ WARNING: output is empty");

            Console.WriteLine();
        }

        private static void RunStressTest(int iterations)
        {
            Console.WriteLine("=== TextNormalizer Stress Test ===");

            string sample =
                "<div>שָׁלוֹם־עוֹלָם_ABC</div>\n" +
                "וַיְהִ֖י הָאָדָ֑ם \"שלום\"";

            sample.Normalize(); // warmup

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
                sample.Normalize();
            sw.Stop();

            Console.WriteLine($"Iterations: {iterations:N0}");
            Console.WriteLine($"Total time: {sw.Elapsed}");
            Console.WriteLine($"Avg per call: {sw.Elapsed.TotalMilliseconds / iterations:0.000000} ms");
            Console.WriteLine();
        }
    }
}
