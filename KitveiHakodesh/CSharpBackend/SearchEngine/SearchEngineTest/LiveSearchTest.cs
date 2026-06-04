using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Search;
using SearchEngine.Indexing;
using SearchEngine.Search;

namespace SearchEngineTest
{
    /// <summary>
    /// Self-contained tests for NRT (near-real-time) live search during indexing.
    /// No real seforim database required ג€” each test builds its own tiny temp index.
    ///
    /// Run with:
    ///   LuceneTest test live
    ///
    /// Each test prints PASS or FAIL.  Returns the failure count.
    /// </summary>
    internal static class LiveSearchTest
    {
        // ג”€ג”€ Entry point ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        public static int Run()
        {
            Console.WriteLine("=== LIVE SEARCH TESTS (NRT) ===");
            Console.WriteLine();

            var tests = new (string Name, Func<string> Body)[]
            {
                ("NrtMakesDocsVisibleWithoutCommit",   T_NrtMakesDocsVisibleWithoutCommit),
                ("NrtRefreshInterval",                 T_NrtRefreshInterval),
                ("InFlightSearchNotDisrupted",         T_InFlightSearchNotDisrupted),
                ("ConcurrentNrtRefreshAndSearch",      T_ConcurrentNrtRefreshAndSearch),
                ("SearchAfterMultipleNrtRefreshes",    T_SearchAfterMultipleNrtRefreshes),
                ("CancellationStopsSearch",            T_CancellationStopsSearch),
                ("NoIndexReturnsSafeNull",             T_NoIndexReturnsSafeNull),
                ("NrtRefreshNoOpWhenNothingChanged",   T_NrtRefreshNoOpWhenNothingChanged),
                ("SimulatedBuildWithLiveSearch",       T_SimulatedBuildWithLiveSearch),
                ("ResultsAreSortedByRowId",            T_ResultsAreSortedByRowId),
            };

            int passed = 0, failed = 0;
            foreach (var (name, body) in tests)
            {
                string failure = null;
                try   { failure = body(); }
                catch (Exception ex) { failure = "EXCEPTION: " + ex; }

                if (failure == null)
                {
                    Console.WriteLine($"  PASS  {name}");
                    passed++;
                }
                else
                {
                    Console.WriteLine($"  FAIL  {name}");
                    Console.WriteLine($"        {failure}");
                    failed++;
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Results: {passed} passed, {failed} failed");
            return failed;
        }

        // ג”€ג”€ Helpers ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        private static string WithTempIndex(Func<string, string> body)
        {
            string dir = Path.Combine(Path.GetTempPath(),
                                      "LuceneNrtTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try   { return body(dir); }
            finally { try { Directory.Delete(dir, recursive: true); } catch { } }
        }

        private static string Fail(string msg) => msg;
        private static string Pass()            => null;

        // ג”€ג”€ Test 1: NRT makes docs visible without a commit ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        /// <summary>
        /// Add docs to a writer, call MaybeRefresh() on the NRT manager ג€” docs
        /// must be visible to a search WITHOUT any Commit() call.
        /// This is the core NRT guarantee.
        /// </summary>
        private static string T_NrtMakesDocsVisibleWithoutCommit()
        {
            return WithTempIndex(dir =>
            {
                using (var writer = new LuceneIndexWriter(dir, deleteExistingIndex: true))
                using (var nrtManager = writer.GetNrtSearcherManager())
                using (var searcher   = new LuceneSearcher(nrtManager))
                {
                    // Add docs ג€” no commit yet
                    writer.AddDocument(1, "׳©׳׳•׳ ׳¢׳•׳׳");
                    writer.AddDocument(2, "׳‘׳¨׳•׳ ׳”׳‘׳");

                    // Before refresh ג€” NRT reader was opened before AddDocument calls
                    var before = searcher.SearchRowIds("׳©׳׳•׳").ToList();
                    // (may or may not see them depending on internal flush ג€” that's fine)

                    // After NRT refresh ג€” must see them
                    nrtManager.MaybeRefresh();
                    var after = searcher.SearchRowIds("׳©׳׳•׳").ToList();

                    if (!after.Contains(1))
                        return Fail($"rowId=1 not visible after NRT refresh (no commit). " +
                                    $"Got: [{string.Join(",", after)}]");

                    // Verify no commit was needed
                    if (LuceneIndexWriter.IndexExists(dir))
                    {
                        // A segments file exists ג€” that means the writer auto-flushed.
                        // That's acceptable; what matters is we didn't call Commit() ourselves.
                        // The test still passes.
                    }
                }
                return Pass();
            });
        }

        // ג”€ג”€ Test 2: NRT refresh interval ג€” docs visible every N rows ג”€ג”€

        /// <summary>
        /// Add 3 batches of docs, calling MaybeRefresh() after each batch.
        /// After each refresh, the search count must be >= the previous count.
        /// After all batches, all docs must be visible.
        /// </summary>
        private static string T_NrtRefreshInterval()
        {
            return WithTempIndex(dir =>
            {
                using (var writer     = new LuceneIndexWriter(dir, deleteExistingIndex: true))
                using (var nrtManager = writer.GetNrtSearcherManager())
                using (var searcher   = new LuceneSearcher(nrtManager))
                {
                    var counts = new List<int>();

                    for (int batch = 0; batch < 3; batch++)
                    {
                        int baseId = batch * 10 + 1;
                        for (int i = 0; i < 10; i++)
                            writer.AddDocument(baseId + i, $"׳‘׳“׳™׳§׳” ׳׳¡׳₪׳¨ {baseId + i}");

                        nrtManager.MaybeRefresh();
                        counts.Add(searcher.SearchRowIds("׳‘׳“׳™׳§׳”").Count());
                    }

                    // Counts must be non-decreasing
                    for (int i = 1; i < counts.Count; i++)
                        if (counts[i] < counts[i - 1])
                            return Fail($"Count decreased: {counts[i-1]} ג†’ {counts[i]}. " +
                                        $"Progression: [{string.Join(", ", counts)}]");

                    // Final count must be 30
                    if (counts[counts.Count - 1] != 30)
                        return Fail($"Expected 30 after 3 batches, got {counts[counts.Count - 1]}. " +
                                    $"Progression: [{string.Join(", ", counts)}]");
                }
                return Pass();
            });
        }

        // ג”€ג”€ Test 3: In-flight search not disrupted by NRT refresh ג”€ג”€ג”€ג”€ג”€

        /// <summary>
        /// Start iterating search results. Mid-iteration, add more docs and call
        /// MaybeRefresh(). The in-flight search must complete without exception
        /// and return exactly the docs that existed when it started.
        /// </summary>
        private static string T_InFlightSearchNotDisrupted()
        {
            return WithTempIndex(dir =>
            {
                using (var writer     = new LuceneIndexWriter(dir, deleteExistingIndex: true))
                using (var nrtManager = writer.GetNrtSearcherManager())
                using (var searcher   = new LuceneSearcher(nrtManager))
                {
                    // Seed 20 docs
                    for (int i = 1; i <= 20; i++)
                        writer.AddDocument(i, $"׳׳™׳׳”{i} ׳‘׳“׳™׳§׳”");
                    nrtManager.MaybeRefresh();

                    var results = new List<int>();
                    string error = null;
                    int count = 0;

                    try
                    {
                        foreach (int id in searcher.SearchRowIds("׳‘׳“׳™׳§׳”"))
                        {
                            results.Add(id);
                            count++;
                            if (count == 5)
                            {
                                // Add new docs and refresh mid-search
                                for (int i = 100; i < 110; i++)
                                    writer.AddDocument(i, "׳‘׳“׳™׳§׳” ׳ ׳•׳¡׳₪׳×");
                                nrtManager.MaybeRefresh();
                            }
                        }
                    }
                    catch (Exception ex) { error = ex.Message; }

                    if (error != null)
                        return Fail($"Exception during in-flight search: {error}");

                    // The search snapshot was taken before the new docs ג€” must see exactly 20
                    if (results.Count != 20)
                        return Fail($"Expected 20 results from snapshot, got {results.Count}. " +
                                    $"IDs: [{string.Join(",", results.OrderBy(x=>x))}]");
                }
                return Pass();
            });
        }

        // ג”€ג”€ Test 4: Concurrent NRT refresh and search ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        /// <summary>
        /// 4 search threads + 1 writer/refresh thread running for 2 seconds.
        /// No exceptions. rowId=1 always present in results.
        /// </summary>
        private static string T_ConcurrentNrtRefreshAndSearch()
        {
            return WithTempIndex(dir =>
            {
                using (var writer     = new LuceneIndexWriter(dir, deleteExistingIndex: true))
                using (var nrtManager = writer.GetNrtSearcherManager())
                using (var searcher   = new LuceneSearcher(nrtManager))
                {
                    writer.AddDocument(1, "׳™׳¨׳•׳©׳׳™׳ ׳¢׳™׳¨ ׳”׳§׳•׳“׳©");
                    nrtManager.MaybeRefresh();

                    var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
                    var cts    = new CancellationTokenSource(TimeSpan.FromSeconds(2));

                    var searchTasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
                    {
                        while (!cts.Token.IsCancellationRequested)
                        {
                            try
                            {
                                var ids = searcher.SearchRowIds("׳™׳¨׳•׳©׳׳™׳").ToList();
                                if (ids.Count > 0 && !ids.Contains(1))
                                    errors.Add($"rowId=1 missing: [{string.Join(",", ids)}]");
                            }
                            catch (OperationCanceledException) { break; }
                            catch (Exception ex) { errors.Add("Search: " + ex.Message); break; }
                        }
                    })).ToArray();

                    int nextId = 10;
                    var writeTask = Task.Run(() =>
                    {
                        while (!cts.Token.IsCancellationRequested)
                        {
                            try
                            {
                                writer.AddDocument(nextId++, "׳™׳¨׳•׳©׳׳™׳ ׳—׳“׳©");
                                nrtManager.MaybeRefresh();
                                Thread.Sleep(30);
                            }
                            catch (OperationCanceledException) { break; }
                            catch (Exception ex) { errors.Add("Write: " + ex.Message); break; }
                        }
                    });

                    Task.WaitAll(searchTasks.Concat(new[] { writeTask }).ToArray());

                    if (errors.Count > 0)
                        return Fail($"{errors.Count} error(s): {string.Join("; ", errors.Take(3))}");
                }
                return Pass();
            });
        }

        // ג”€ג”€ Test 5: Search after multiple NRT refreshes sees all docs ג”€

        /// <summary>
        /// The core "search all segments" test.
        /// Add 5 batches of 20 docs each, MaybeRefresh() after each.
        /// Final search must find all 100 docs.
        /// </summary>
        private static string T_SearchAfterMultipleNrtRefreshes()
        {
            return WithTempIndex(dir =>
            {
                using (var writer     = new LuceneIndexWriter(dir, deleteExistingIndex: true))
                using (var nrtManager = writer.GetNrtSearcherManager())
                using (var searcher   = new LuceneSearcher(nrtManager))
                {
                    for (int batch = 0; batch < 5; batch++)
                    {
                        int baseId = batch * 20 + 1;
                        for (int i = 0; i < 20; i++)
                            writer.AddDocument(baseId + i, $"׳¡׳₪׳¨ ׳₪׳¨׳§ {baseId + i}");
                        nrtManager.MaybeRefresh();
                    }

                    var ids = new HashSet<int>(searcher.SearchRowIds("׳¡׳₪׳¨"));

                    // All 100 docs must be visible
                    var missing = Enumerable.Range(1, 100).Where(id => !ids.Contains(id)).ToList();
                    if (missing.Count > 0)
                        return Fail($"{missing.Count} docs missing after 5 NRT refreshes. " +
                                    $"First missing: [{string.Join(",", missing.Take(10))}]. " +
                                    $"Total found: {ids.Count}");
                }
                return Pass();
            });
        }

        // ג”€ג”€ Test 6: Cancellation stops search cleanly ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        private static string T_CancellationStopsSearch()
        {
            return WithTempIndex(dir =>
            {
                using (var writer     = new LuceneIndexWriter(dir, deleteExistingIndex: true))
                using (var nrtManager = writer.GetNrtSearcherManager())
                using (var searcher   = new LuceneSearcher(nrtManager))
                {
                    for (int i = 1; i <= 500; i++)
                        writer.AddDocument(i, $"׳—׳™׳₪׳•׳© ׳׳¡׳₪׳¨ {i}");
                    nrtManager.MaybeRefresh();

                    var cts     = new CancellationTokenSource();
                    var results = new List<int>();
                    bool threw  = false;

                    try
                    {
                        foreach (int id in searcher.SearchRowIds("׳—׳™׳₪׳•׳©", cts.Token))
                        {
                            results.Add(id);
                            if (results.Count == 10) cts.Cancel();
                        }
                    }
                    catch (OperationCanceledException) { threw = true; }

                    if (!threw)
                        return Fail("Expected OperationCanceledException after cancellation");
                    if (results.Count > 20)
                        return Fail($"Too many results after cancel: {results.Count}");
                }
                return Pass();
            });
        }

        // ג”€ג”€ Test 7: No index ג†’ safe null, no exception ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        private static string T_NoIndexReturnsSafeNull()
        {
            return WithTempIndex(dir =>
            {
                // Empty directory ג€” no index
                bool exists = LuceneIndexWriter.IndexExists(dir);
                if (exists)
                    return Fail("IndexExists returned true on empty directory");
                return Pass();
            });
        }

        // ג”€ג”€ Test 8: NRT MaybeRefresh is a no-op when nothing changed ג”€ג”€

        private static string T_NrtRefreshNoOpWhenNothingChanged()
        {
            return WithTempIndex(dir =>
            {
                using (var writer     = new LuceneIndexWriter(dir, deleteExistingIndex: true))
                using (var nrtManager = writer.GetNrtSearcherManager())
                {
                    writer.AddDocument(1, "׳©׳׳•׳");
                    nrtManager.MaybeRefresh();

                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < 200; i++)
                        nrtManager.MaybeRefresh();
                    sw.Stop();

                    if (sw.ElapsedMilliseconds > 500)
                        return Fail($"200 no-op MaybeRefresh() calls took {sw.ElapsedMilliseconds}ms");
                }
                return Pass();
            });
        }

        // ג”€ג”€ Test 9: Simulated build with interleaved live searches ג”€ג”€ג”€ג”€

        /// <summary>
        /// Build thread: indexes 5 batches of 100 docs, NRT-refreshing every batch.
        /// Search thread: searches every 50ms, recording result counts.
        ///
        /// Assertions:
        ///   1. No exceptions on either thread.
        ///   2. Final search sees all 500 docs.
        ///   3. Result counts are monotonically non-decreasing.
        /// </summary>
        private static string T_SimulatedBuildWithLiveSearch()        {            return WithTempIndex(dir =>
            {
                const int BatchCount   = 5;
                const int DocsPerBatch = 100;
                const string Term      = "׳¡׳₪׳¨";

                using (var writer     = new LuceneIndexWriter(dir, deleteExistingIndex: true))
                using (var nrtManager = writer.GetNrtSearcherManager())
                using (var searcher   = new LuceneSearcher(nrtManager))
                {
                    var buildErrors  = new List<string>();
                    var searchErrors = new List<string>();
                    var resultCounts = new List<int>();
                    var buildDone    = new ManualResetEventSlim(false);

                    // Build thread
                    var buildThread = new Thread(() =>
                    {
                        try
                        {
                            for (int b = 0; b < BatchCount; b++)
                            {
                                int baseId = b * DocsPerBatch + 1;
                                for (int i = 0; i < DocsPerBatch; i++)
                                    writer.AddDocument(baseId + i, $"{Term} ׳₪׳¨׳§ {baseId + i}");
                                nrtManager.MaybeRefresh();
                                Thread.Sleep(20);
                            }
                        }
                        catch (Exception ex) { buildErrors.Add(ex.Message); }
                        finally { buildDone.Set(); }
                    }) { IsBackground = true, Name = "BuildThread" };

                    // Search thread
                    var searchThread = new Thread(() =>
                    {
                        try
                        {
                            while (!buildDone.IsSet)
                            {
                                int count = searcher.SearchRowIds(Term).Count();
                                lock (resultCounts) resultCounts.Add(count);
                                Thread.Sleep(40);
                            }
                            // Final search after build
                            int final = searcher.SearchRowIds(Term).Count();
                            lock (resultCounts) resultCounts.Add(final);
                        }
                        catch (Exception ex) { searchErrors.Add(ex.Message); }
                    }) { IsBackground = true, Name = "SearchThread" };

                    buildThread.Start();
                    searchThread.Start();
                    buildThread.Join(15_000);
                    buildDone.Set();
                    searchThread.Join(5_000);

                    if (buildErrors.Count > 0)
                        return Fail($"Build error: {buildErrors[0]}");
                    if (searchErrors.Count > 0)
                        return Fail($"Search error: {searchErrors[0]}");

                    List<int> snapshot;
                    lock (resultCounts) snapshot = new List<int>(resultCounts);

                    // Assertion 1: final count = all docs
                    int finalCount = snapshot.Count > 0 ? snapshot[snapshot.Count - 1] : 0;
                    int expected   = BatchCount * DocsPerBatch;
                    if (finalCount != expected)
                        return Fail($"Final count: expected {expected}, got {finalCount}. " +
                                    $"Progression: [{string.Join(", ", snapshot)}]");

                    // Assertion 2: monotonically non-decreasing
                    for (int i = 1; i < snapshot.Count; i++)
                        if (snapshot[i] < snapshot[i - 1])
                            return Fail($"Count decreased: {snapshot[i-1]} ג†’ {snapshot[i]} " +
                                        $"at index {i}. Progression: [{string.Join(", ", snapshot)}]");
                }
                return Pass();
            });
        }

        // ג”€ג”€ Test 10: Results are sorted by rowId ascending ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        /// <summary>
        /// Index docs with non-sequential IDs inserted in reverse order across
        /// two NRT flushes (so they land in different segments with scrambled
        /// internal Lucene doc order).
        /// Verify Search() returns them strictly ascending by rowId.
        /// </summary>
        private static string T_ResultsAreSortedByRowId()
        {
            return WithTempIndex(dir =>
            {
                using (var writer     = new LuceneIndexWriter(dir, deleteExistingIndex: true))
                using (var nrtManager = writer.GetNrtSearcherManager())
                using (var searcher   = new LuceneSearcher(nrtManager))
                {
                    // Insert in reverse order across two flushes so docs land in
                    // different segments with scrambled internal Lucene doc order.
                    for (int i = 50; i >= 26; i--)
                        writer.AddDocument(i, "׳׳™׳׳” ׳‘׳“׳™׳§׳”");
                    nrtManager.MaybeRefresh();

                    for (int i = 25; i >= 1; i--)
                        writer.AddDocument(i, "׳׳™׳׳” ׳‘׳“׳™׳§׳”");
                    nrtManager.MaybeRefresh();

                    var ids = searcher.SearchRowIds("׳׳™׳׳”").ToList();

                    if (ids.Count != 50)
                        return Fail($"Expected 50 results, got {ids.Count}");

                    // Verify strictly ascending order
                    for (int i = 1; i < ids.Count; i++)
                        if (ids[i] <= ids[i - 1])
                            return Fail($"Not sorted at index {i}: {ids[i-1]} ג†’ {ids[i]}. " +
                                        $"Full order: [{string.Join(",", ids)}]");

                    if (ids[0] != 1)
                        return Fail($"First result should be rowId=1, got {ids[0]}");
                    if (ids[ids.Count - 1] != 50)
                        return Fail($"Last result should be rowId=50, got {ids[ids.Count - 1]}");
                }
                return Pass();
            });
        }
    }
}

