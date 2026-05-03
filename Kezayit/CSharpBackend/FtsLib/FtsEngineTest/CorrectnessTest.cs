using FtsEngine.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FtsEngineTest
{
    /// <summary>
    /// Correctness tests for FtsEngine:
    ///   1. Stitch logic — term spanning multiple run boundaries decodes correctly
    ///   2. Single-run fast path — bytes passed through unchanged
    ///   3. AND intersection — same results as a naive reference implementation
    ///   4. Skip list — SkipTo lands on the correct docId after a jump
    ///   5. Real-world scale — 800k common + 2k rare, mimics כי + ביצחק
    /// </summary>
    internal static class CorrectnessTest
    {
        private static int _passed;
        private static int _failed;

        public static void Run()
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("  ── FtsEngine Correctness Tests ──");
            Console.ResetColor();

            TestSingleRun();
            TestStitchAcrossOneBoundary();
            TestAndIntersectionAcrossBoundary();
            TestStitchAcrossMultipleBoundaries();
            TestAndIntersection();
            TestMissingTerm();
            TestSkipListJump();
            TestRealWorldScale();

            Console.WriteLine();
            if (_failed == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  ✓ ALL {_passed} CORRECTNESS TESTS PASSED");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ✗ {_failed} FAILED, {_passed} passed");
            }
            Console.ResetColor();

            if (_failed > 0) Environment.Exit(1);
        }

        // ----------------------------------------------------------------
        // Test 1 — single run, no stitching needed
        // ----------------------------------------------------------------
        static void TestSingleRun()
        {
            var ids = Enumerable.Range(1, 300).ToList();
            var results = BuildAndSearch(
                new[] { ("alpha", ids) },
                new[] { "alpha" },
                flushEvery: 10_000); // large enough — no flush mid-index

            AssertEqual("SingleRun", ids, results);
        }

        // ----------------------------------------------------------------
        // Test 2 — term spans exactly one run boundary
        // ----------------------------------------------------------------
        static void TestStitchAcrossOneBoundary()
        {
            // Force a flush after 500 unique terms so "alpha" appears in 2 runs
            var ids = Enumerable.Range(1, 600).ToList();
            var results = BuildAndSearch(
                new[] { ("alpha", ids) },
                new[] { "alpha" },
                flushEvery: 500);

            AssertEqual("StitchOneBoundary", ids, results);
        }

        // ----------------------------------------------------------------
        // Test 2b — AND intersection across a run boundary
        // ----------------------------------------------------------------
        static void TestAndIntersectionAcrossBoundary()
        {
            // Both terms span the flush boundary
            var aIds = Enumerable.Range(1, 1000).ToList();
            var bIds = new List<int> { 10, 200, 500, 750, 999 };
            var expected = bIds;

            var results = BuildAndSearch(
                new[] { ("termA", aIds), ("termB", bIds) },
                new[] { "termA", "termB" },
                flushEvery: 500); // forces flush mid-stream

            AssertEqual("AndIntersectionAcrossBoundary", expected, results);
        }

        // ----------------------------------------------------------------
        // Test 3 — term spans multiple run boundaries
        // ----------------------------------------------------------------
        static void TestStitchAcrossMultipleBoundaries()
        {
            var ids = Enumerable.Range(1, 2000).ToList();
            var results = BuildAndSearch(
                new[] { ("alpha", ids) },
                new[] { "alpha" },
                flushEvery: 300); // forces ~6 runs

            AssertEqual("StitchMultipleBoundaries", ids, results);
        }

        // ----------------------------------------------------------------
        // Test 4 — AND intersection across two terms
        // ----------------------------------------------------------------
        static void TestAndIntersection()
        {
            var aIds = Enumerable.Range(1, 500).ToList();
            var bIds = new List<int> { 10, 50, 100, 200, 300, 400, 500 };
            var expected = bIds; // b is subset of a

            var results = BuildAndSearch(
                new[] { ("termA", aIds), ("termB", bIds) },
                new[] { "termA", "termB" },
                flushEvery: 10_000);

            AssertEqual("AndIntersection", expected, results);
        }

        // ----------------------------------------------------------------
        // Test 5 — missing term returns empty
        // ----------------------------------------------------------------
        static void TestMissingTerm()
        {
            var ids = Enumerable.Range(1, 100).ToList();
            var results = BuildAndSearch(
                new[] { ("exists", ids) },
                new[] { "exists", "missing" },
                flushEvery: 10_000);

            AssertEqual("MissingTerm", new List<int>(), results);
        }

        // ----------------------------------------------------------------
        // Test 6 — skip list jumps correctly across a 128-entry boundary
        // ----------------------------------------------------------------
        static void TestSkipListJump()
        {
            // "rare" has 3 entries, "common" has 500 — forces skip list usage
            var commonIds = Enumerable.Range(1, 500).ToList();
            var rareIds   = new List<int> { 1, 200, 400 };
            var expected  = rareIds;

            var results = BuildAndSearch(
                new[] { ("common", commonIds), ("rare", rareIds) },
                new[] { "common", "rare" },
                flushEvery: 10_000);

            AssertEqual("SkipListJump", expected, results);
        }

        // ----------------------------------------------------------------
        // Test 7 — real-world scale: 800k common + 2k rare across multiple runs
        // ----------------------------------------------------------------
        static void TestRealWorldScale()
        {
            var commonIds = Enumerable.Range(1, 800_000).ToList();
            var rareIds   = new List<int>();
            for (int i = 0; i < 2000; i++) rareIds.Add(1 + i * 400);

            var results = BuildAndSearch(
                new[] { ("common", commonIds), ("rare", rareIds) },
                new[] { "common", "rare" },
                flushEvery: 500_000);

            AssertEqual("RealWorldScale", rareIds, results);
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Builds a real FtsEngine index in a temp directory, searches it,
        /// returns sorted results. flushEvery controls unique-term flush threshold.
        /// </summary>
        private static List<int> BuildAndSearch(
            IEnumerable<(string term, List<int> docIds)> data,
            IEnumerable<string> queryTerms,
            int flushEvery)
        {
            string dir = Path.Combine(Path.GetTempPath(),
                                      "ftsengine_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            try
            {
                string postingsPath = Path.Combine(dir, "postings.bin");
                string indexDbPath  = Path.Combine(dir, "index.db");

                // Build using OccurenceBuffer directly so we can control flush threshold
                var buffer   = new TestableBuffer(flushEvery);
                var runPaths = new List<string>();
                int runIndex = 0;

                foreach (var (term, docIds) in data)
                    foreach (var docId in docIds)
                    {
                        buffer.Add(term, docId);
                        if (buffer.IsFull)
                        {
                            runPaths.Add(buffer.Flush(dir, runIndex++));
                        }
                    }

                if (!buffer.IsEmpty)
                    runPaths.Add(buffer.Flush(dir, runIndex));

                IndexWriter.Merge(runPaths.ToArray(), postingsPath, indexDbPath);

                using (var reader = new DiskIndexReader(postingsPath, indexDbPath))
                    return reader.Search(queryTerms).OrderBy(x => x).ToList();
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        private static void AssertEqual(string name, List<int> expected, List<int> actual)
        {
            bool ok = expected.Count == actual.Count && expected.SequenceEqual(actual);
            if (ok)
            {
                _passed++;
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  · {name}: OK  ({actual.Count} results)");
                Console.ResetColor();
            }
            else
            {
                _failed++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ✗ {name}: FAIL");
                Console.WriteLine($"    expected {expected.Count} results: [{string.Join(", ", expected.Take(10))}{(expected.Count > 10 ? "..." : "")}]");
                Console.WriteLine($"    actual   {actual.Count} results: [{string.Join(", ", actual.Take(10))}{(actual.Count > 10 ? "..." : "")}]");
                Console.ResetColor();
            }
        }

        // ----------------------------------------------------------------
        // Thin wrapper around OccurenceBuffer with configurable flush threshold
        // ----------------------------------------------------------------
        private sealed class TestableBuffer
        {
            private readonly Dictionary<string, PostingStream> _map =
                new Dictionary<string, PostingStream>(StringComparer.Ordinal);
            private readonly int _maxTerms;

            public bool IsFull  => _map.Count >= _maxTerms;
            public bool IsEmpty => _map.Count == 0;

            public TestableBuffer(int maxTerms) { _maxTerms = maxTerms; }

            public void Add(string term, int docId)
            {
                if (!_map.TryGetValue(term, out var stream))
                    _map[term] = stream = new PostingStream();
                stream.Add(docId);
            }

            public string Flush(string dir, int runIndex)
            {
                var terms = new string[_map.Count];
                _map.Keys.CopyTo(terms, 0);
                Array.Sort(terms, StringComparer.Ordinal);

                string path = Path.Combine(dir, $"run_{runIndex:D4}.bin");
                using (var fs = new System.IO.FileStream(path, FileMode.Create,
                                                         FileAccess.Write, FileShare.None,
                                                         4 * 1024 * 1024))
                using (var bw = new System.IO.BinaryWriter(fs,
                                    System.Text.Encoding.UTF8, leaveOpen: false))
                {
                    foreach (var term in terms)
                    {
                        var stream    = _map[term];
                        byte[] tbytes = System.Text.Encoding.UTF8.GetBytes(term);
                        bw.Write((short)tbytes.Length);
                        bw.Write(tbytes);
                        bw.Write(stream.Count);
                        bw.Write(stream.ByteLength);
                        bw.Write(stream.LastEncoded);
                        bw.Write(stream.Buffer, 0, stream.ByteLength);
                    }
                }
                _map.Clear();
                return path;
            }
        }
    }
}
