using FtsLib.Index;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FtsLibTest
{
    internal static class SkipListTest
    {
        public static void Run()
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("  ── Skip List Correctness Test ──");
            Console.ResetColor();

            TestBasicIntersection();
            TestSkipAcrossBoundary();
            TestNoResults();
            TestSingleTerm();
            TestLargeList();
            TestRealWorldScale();
            TestSingleTermExact();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  ✓ ALL SKIP LIST TESTS PASSED");
            Console.ResetColor();
        }

        static void TestBasicIntersection()
        {
            var idx = new IndexManager();
            foreach (var id in new[] { 1, 2, 3, 4, 5 }) idx.Add("a", id);
            foreach (var id in new[] { 2, 4, 6 })       idx.Add("b", id);
            var res = idx.Search(new[] { "a", "b" }).OrderBy(x => x).ToList();
            Assert(res.SequenceEqual(new[] { 2, 4 }), "BasicIntersection", res);
        }

        static void TestSkipAcrossBoundary()
        {
            var idx = new IndexManager();
            foreach (var id in new[] { 1, 200, 400 }) idx.Add("rare", id);
            for (int i = 1; i <= 500; i++)             idx.Add("common", i);
            var res = idx.Search(new[] { "rare", "common" }).OrderBy(x => x).ToList();
            Assert(res.SequenceEqual(new[] { 1, 200, 400 }), "SkipAcrossBoundary", res);
        }

        static void TestNoResults()
        {
            var idx = new IndexManager();
            foreach (var id in new[] { 1, 2, 3 }) idx.Add("a", id);
            var res = idx.Search(new[] { "a", "missing" }).ToList();
            Assert(res.Count == 0, "NoResults", res);
        }

        static void TestSingleTerm()
        {
            var idx = new IndexManager();
            foreach (var id in new[] { 5, 10, 15 }) idx.Add("x", id);
            var res = idx.Search(new[] { "x" }).OrderBy(x => x).ToList();
            Assert(res.SequenceEqual(new[] { 5, 10, 15 }), "SingleTerm", res);
        }

        static void TestLargeList()
        {
            var idx = new IndexManager();
            for (int i = 1; i <= 1000; i++)      idx.Add("freq", i);
            for (int i = 50; i <= 1000; i += 50) idx.Add("rare", i);
            var expected = Enumerable.Range(1, 20).Select(i => i * 50).ToList();
            var res = idx.Search(new[] { "freq", "rare" }).OrderBy(x => x).ToList();
            Assert(res.SequenceEqual(expected), "LargeList", res);
        }

        // 800k common + 2k rare — mimics כי + ביצחק scale
        static void TestRealWorldScale()
        {
            var idx = new IndexManager();
            for (int i = 1; i <= 800_000; i++) idx.Add("common", i);
            var rareIds = new List<int>();
            for (int i = 0; i < 2000; i++)
            {
                int id = 1 + i * 400;
                rareIds.Add(id);
                idx.Add("rare", id);
            }
            var res = idx.Search(new[] { "common", "rare" }).OrderBy(x => x).ToList();
            Assert(res.SequenceEqual(rareIds), "RealWorldScale", res);
        }

        // Single term in a large shared index — result must be exactly that term's entries
        static void TestSingleTermExact()
        {
            var idx = new IndexManager();
            var expected = new List<int>();

            // Add many other terms first to fill the shared buffer
            for (int t = 0; t < 1000; t++)
                for (int i = 1; i <= 100; i++)
                    idx.Add("term" + t, i * (t + 1));

            // Now add our target term with large IDs
            for (int i = 1; i <= 5_444_192; i += 2440)
            {
                idx.Add("ביצחק", i);
                expected.Add(i);
            }

            // Add more terms after to ensure buffer is shared
            for (int t = 0; t < 500; t++)
                for (int i = 1; i <= 50; i++)
                    idx.Add("after" + t, i * (t + 1));

            var res = idx.Search(new[] { "ביצחק" }).OrderBy(x => x).ToList();
            Assert(res.Count == expected.Count && res.SequenceEqual(expected),
                   $"SingleTermExact (expected {expected.Count}, got {res.Count})", res);
        }

        static void Assert(bool condition, string name, List<int> actual)
        {
            if (condition)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  · {name}: OK");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ✗ {name}: FAIL — got [{string.Join(", ", actual.Take(20))}]{(actual.Count > 20 ? "..." : "")}  (count={actual.Count})");
                Console.ResetColor();
                Environment.Exit(1);
            }
        }
    }
}
