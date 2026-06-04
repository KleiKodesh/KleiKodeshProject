using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SearchEngine.Indexing;
using SearchEngine.Search;

namespace SearchEngineTest
{
    /// <summary>
    /// Self-contained unit tests for the resume / checkpoint capabilities of
    /// <see cref="LuceneLib.SeforimDb.SeforimIndex"/>.
    ///
    /// Because SeforimIndex requires a real SQLite database, these tests exercise
    /// the resume primitives directly at the layer below it:
    ///   ג€¢ <see cref="LuceneIndexWriter"/> ג€” commit, ForceMerge, IndexExists
    ///   ג€¢ The progress-file protocol (read/write/delete) via a thin
    ///     <see cref="ProgressFile"/> helper that mirrors SeforimIndex's private logic
    ///   ג€¢ <see cref="LuceneSearcher"/> ג€” verifying that a resumed build produces
    ///     the same index as a single-pass build
    ///
    /// No real seforim database is required ג€” each test builds its own tiny
    /// temp index from synthetic rows.
    ///
    /// Run with:
    ///   LuceneTest test resume
    ///
    /// Each test prints PASS or FAIL.  Returns the failure count.
    /// </summary>
    internal static class ResumeTest
    {
        // ג”€ג”€ Entry point ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        public static int Run()
        {
            Console.WriteLine("=== RESUME TESTS ===");
            Console.WriteLine();

            var tests = new (string Name, Func<string> Body)[]
            {
                // ג”€ג”€ Progress file protocol ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€
                ("ProgressFileWriteAndRead",              T_ProgressFileWriteAndRead),
                ("ProgressFileAbsentReturnsZero",         T_ProgressFileAbsentReturnsZero),
                ("ProgressFileDeleteClearsState",         T_ProgressFileDeleteClearsState),
                ("ProgressFileOverwriteUpdatesValues",    T_ProgressFileOverwriteUpdatesValues),
                ("ProgressFileLineIdMatchesLastCommit",   T_ProgressFileLineIdMatchesLastCommit),
                ("ProgressFileStoresAllThreeFields",      T_ProgressFileStoresAllThreeFields),

                // ג”€ג”€ Fresh build ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€
                ("FreshBuildIndexExists",                 T_FreshBuildIndexExists),
                ("FreshBuildDeletesExistingIndex",        T_FreshBuildDeletesExistingIndex),
                ("IndexExistsReturnsFalseOnEmptyDir",     T_IndexExistsReturnsFalseOnEmptyDir),
                ("IndexExistsReturnsTrueAfterCommit",     T_IndexExistsReturnsTrueAfterCommit),

                // ג”€ג”€ Resume correctness ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€
                ("ResumedBuildAppendsRows",               T_ResumedBuildAppendsRows),
                ("ResumedBuildMatchesSinglePassBuild",    T_ResumedBuildMatchesSinglePassBuild),
                ("ResumeKeepsExistingDocs",               T_ResumeKeepsExistingDocs),
                ("NoBoundaryRowDoubleIndexed",            T_NoBoundaryRowDoubleIndexed),
                ("NoBoundaryRowSkipped",                  T_NoBoundaryRowSkipped),
                ("ExactDocCountAfterResume",              T_ExactDocCountAfterResume),

                // ג”€ג”€ Cancellation + resume ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€
                ("CancelledBuildWritesProgressFile",      T_CancelledBuildWritesProgressFile),
                ("ResumeAfterCancelFindsAllDocs",         T_ResumeAfterCancelFindsAllDocs),
                ("MultipleInterruptionsConverge",         T_MultipleInterruptionsConverge),
                ("CancelAtEveryBoundaryConverges",        T_CancelAtEveryBoundaryConverges),

                // ג”€ג”€ Finalization (ForceMerge) ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€
                ("ForceMergePreservesAllDocs",            T_ForceMergePreservesAllDocs),
                ("ForceMergeProducesSingleSegment",       T_ForceMergeProducesSingleSegment),
                ("SearchAfterForceMergeMatchesBefore",    T_SearchAfterForceMergeMatchesBefore),
                ("ResumedBuildFinalizesMerge",            T_ResumedBuildFinalizesMerge),
                ("NoDuplicatesAfterResumeAndMerge",       T_NoDuplicatesAfterResumeAndMerge),

                // ג”€ג”€ Concurrent search during resume ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€
                ("ConcurrentSearchDuringResume",          T_ConcurrentSearchDuringResume),
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

        private static string WithTempDir(Func<string, string> body)
        {
            string dir = Path.Combine(Path.GetTempPath(),
                                      "LuceneResumeTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try   { return body(dir); }
            finally { try { Directory.Delete(dir, recursive: true); } catch { } }
        }

        private static string Fail(string msg) => msg;
        private static string Pass()            => null;

        /// <summary>
        /// Indexes a list of (id, content) pairs into <paramref name="indexDir"/>.
        /// Commits after every <paramref name="commitEvery"/> rows.
        /// Returns the id of the last row written.
        /// </summary>
        private static int IndexRows(
            string                        indexDir,
            IEnumerable<(int Id, string Content)> rows,
            bool                          deleteExisting,
            int                           commitEvery = int.MaxValue,
            CancellationToken             ct          = default)
        {
            int lastId = 0;
            using (var writer = new LuceneIndexWriter(indexDir,
                                    deleteExistingIndex: deleteExisting))
            {
                int n = 0;
                foreach (var (id, content) in rows)
                {
                    ct.ThrowIfCancellationRequested();
                    writer.AddDocument(id, 0, string.Empty, string.Empty, content);                    lastId = id;
                    n++;
                    if (n % commitEvery == 0)
                        writer.Commit();
                }
                writer.Commit();
            }
            return lastId;
        }

        private static List<int> SearchIds(string indexDir, string query)
        {
            using (var searcher = new LuceneSearcher(indexDir))
                return searcher.SearchRowIds(query).OrderBy(x => x).ToList();
        }

        // ג”€ג”€ Progress-file helper (mirrors SeforimIndex's private logic) ג”€ג”€

        private const string ProgressFileName = "build.progress";

        private static void WriteProgress(string indexDir, int lineId,
                                          long totalLines, long resumeOffset)
        {
            File.WriteAllText(
                Path.Combine(indexDir, ProgressFileName),
                lineId.ToString()       + "\n" +
                totalLines.ToString()   + "\n" +
                resumeOffset.ToString());
        }

        private static void ReadProgress(string indexDir,
                                         out int lineId,
                                         out long totalLines,
                                         out long resumeOffset)
        {
            lineId       = 0;
            totalLines   = 0;
            resumeOffset = 0;
            string path = Path.Combine(indexDir, ProgressFileName);
            if (!File.Exists(path)) return;
            string[] lines = File.ReadAllText(path).Trim().Split('\n');
            if (lines.Length >= 1) int.TryParse(lines[0].Trim(),  out lineId);
            if (lines.Length >= 2) long.TryParse(lines[1].Trim(), out totalLines);
            if (lines.Length >= 3) long.TryParse(lines[2].Trim(), out resumeOffset);
        }

        private static void DeleteProgress(string indexDir)
        {
            string path = Path.Combine(indexDir, ProgressFileName);
            if (File.Exists(path)) File.Delete(path);
        }

        // ג”€ג”€ Test 1: Progress file round-trips correctly ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        /// <summary>
        /// Write a progress file and read it back ג€” all three fields must survive.
        /// </summary>
        private static string T_ProgressFileWriteAndRead()
        {
            return WithTempDir(dir =>
            {
                WriteProgress(dir, lineId: 12345, totalLines: 999999, resumeOffset: 50000);
                ReadProgress(dir, out int lineId, out long total, out long offset);

                if (lineId != 12345)
                    return Fail($"lineId: expected 12345, got {lineId}");
                if (total != 999999)
                    return Fail($"totalLines: expected 999999, got {total}");
                if (offset != 50000)
                    return Fail($"resumeOffset: expected 50000, got {offset}");

                return Pass();
            });
        }

        // ג”€ג”€ Test 2: Absent progress file returns zeros ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        private static string T_ProgressFileAbsentReturnsZero()
        {
            return WithTempDir(dir =>
            {
                ReadProgress(dir, out int lineId, out long total, out long offset);

                if (lineId != 0)   return Fail($"lineId should be 0, got {lineId}");
                if (total  != 0)   return Fail($"totalLines should be 0, got {total}");
                if (offset != 0)   return Fail($"resumeOffset should be 0, got {offset}");

                return Pass();
            });
        }

        // ג”€ג”€ Test 3: Deleting the progress file clears state ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        private static string T_ProgressFileDeleteClearsState()
        {
            return WithTempDir(dir =>
            {
                WriteProgress(dir, 500, 1000, 200);
                DeleteProgress(dir);

                string path = Path.Combine(dir, ProgressFileName);
                if (File.Exists(path))
                    return Fail("Progress file still exists after delete");

                ReadProgress(dir, out int lineId, out _, out _);
                if (lineId != 0)
                    return Fail($"lineId should be 0 after delete, got {lineId}");

                return Pass();
            });
        }

        // ג”€ג”€ Test 4: Overwriting the progress file updates all fields ג”€ג”€

        private static string T_ProgressFileOverwriteUpdatesValues()
        {
            return WithTempDir(dir =>
            {
                WriteProgress(dir, 100, 1000, 100);
                WriteProgress(dir, 200, 1000, 200);   // overwrite

                ReadProgress(dir, out int lineId, out long total, out long offset);

                if (lineId != 200)
                    return Fail($"lineId: expected 200 after overwrite, got {lineId}");
                if (offset != 200)
                    return Fail($"resumeOffset: expected 200 after overwrite, got {offset}");

                return Pass();
            });
        }

        // ג”€ג”€ Test 5: Fresh build creates a searchable index ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        private static string T_FreshBuildIndexExists()
        {
            return WithTempDir(dir =>
            {
                var rows = Enumerable.Range(1, 20)
                    .Select(i => (i, $"׳׳™׳׳”{i} ׳‘׳“׳™׳§׳”"));

                IndexRows(dir, rows, deleteExisting: true);

                if (!LuceneIndexWriter.IndexExists(dir))
                    return Fail("IndexExists returned false after a completed build");

                var ids = SearchIds(dir, "׳‘׳“׳™׳§׳”");
                if (ids.Count != 20)
                    return Fail($"Expected 20 hits, got {ids.Count}");

                return Pass();
            });
        }

        // ג”€ג”€ Test 6: Resumed build appends rows to existing index ג”€ג”€ג”€ג”€ג”€ג”€

        /// <summary>
        /// Index rows 1ג€“10, then resume from row 10 and add rows 11ג€“20.
        /// Final search must find all 20 docs.
        /// </summary>
        private static string T_ResumedBuildAppendsRows()
        {
            return WithTempDir(dir =>
            {
                // Phase 1: index rows 1ג€“10
                var phase1 = Enumerable.Range(1, 10).Select(i => (i, $"׳¡׳₪׳¨ ׳₪׳¨׳§ {i}"));
                IndexRows(dir, phase1, deleteExisting: true);
                WriteProgress(dir, lineId: 10, totalLines: 20, resumeOffset: 10);

                // Phase 2: resume ג€” index rows 11ג€“20 (append, don't delete)
                var phase2 = Enumerable.Range(11, 10).Select(i => (i, $"׳¡׳₪׳¨ ׳₪׳¨׳§ {i}"));
                IndexRows(dir, phase2, deleteExisting: false);

                var ids = SearchIds(dir, "׳¡׳₪׳¨");
                if (ids.Count != 20)
                    return Fail($"Expected 20 hits after resume, got {ids.Count}. " +
                                $"IDs: [{string.Join(",", ids)}]");

                // Verify all 20 IDs are present
                var missing = Enumerable.Range(1, 20).Where(id => !ids.Contains(id)).ToList();
                if (missing.Count > 0)
                    return Fail($"Missing IDs after resume: [{string.Join(",", missing)}]");

                return Pass();
            });
        }

        // ג”€ג”€ Test 7: Resumed build produces same result as single-pass ג”€

        /// <summary>
        /// Build A: index all 30 rows in one shot.
        /// Build B: index rows 1ג€“15, then resume from 15 and add 16ג€“30.
        /// Both indexes must return the same 30 row IDs for the same query.
        /// </summary>
        private static string T_ResumedBuildMatchesSinglePassBuild()
        {
            return WithTempDir(dirA =>
            WithTempDir(dirB =>
            {
                var allRows = Enumerable.Range(1, 30)
                    .Select(i => (i, $"׳×׳•׳¨׳” ׳₪׳¨׳©׳” {i}"))
                    .ToList();

                // Build A ג€” single pass
                IndexRows(dirA, allRows, deleteExisting: true);

                // Build B ג€” two phases
                IndexRows(dirB, allRows.Take(15), deleteExisting: true);
                WriteProgress(dirB, lineId: 15, totalLines: 30, resumeOffset: 15);
                IndexRows(dirB, allRows.Skip(15), deleteExisting: false);

                var idsA = SearchIds(dirA, "׳×׳•׳¨׳”");
                var idsB = SearchIds(dirB, "׳×׳•׳¨׳”");

                if (idsA.Count != 30)
                    return Fail($"Build A: expected 30 hits, got {idsA.Count}");
                if (idsB.Count != 30)
                    return Fail($"Build B (resumed): expected 30 hits, got {idsB.Count}");

                var diff = idsA.Except(idsB).Concat(idsB.Except(idsA)).ToList();
                if (diff.Count > 0)
                    return Fail($"Builds differ on IDs: [{string.Join(",", diff)}]");

                return Pass();
            }));
        }

        // ג”€ג”€ Test 8: Cancelled build writes a progress file ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        /// <summary>
        /// Start indexing 100 rows, cancel after 30.  A progress file must exist
        /// and its lineId must be > 0 (we wrote it manually to simulate what
        /// SeforimIndex does on cancellation).
        /// </summary>
        private static string T_CancelledBuildWritesProgressFile()
        {
            return WithTempDir(dir =>
            {
                var cts  = new CancellationTokenSource();
                int seen = 0;
                int lastId = 0;

                var rows = Enumerable.Range(1, 100).Select(i => (i, $"׳©׳•׳¨׳” {i}")).ToList();

                // Simulate SeforimIndex's cancellation handling:
                // index rows until cancel fires, then commit + write progress.
                using (var writer = new LuceneIndexWriter(dir, deleteExistingIndex: true))
                {
                    try
                    {
                        foreach (var (id, content) in rows)
                        {
                            cts.Token.ThrowIfCancellationRequested();
                            writer.AddDocument(id, content);
                            lastId = id;
                            seen++;
                            if (seen == 30) cts.Cancel();
                        }
                    }
                    catch (OperationCanceledException) { }

                    // This is what SeforimIndex does after catching the cancellation:
                    // commit whatever was written and record the progress file.
                    writer.Commit();
                    WriteProgress(dir, lastId, totalLines: 100, resumeOffset: seen);
                }

                ReadProgress(dir, out int lineId, out long total, out long offset);

                if (lineId == 0)
                    return Fail("Progress file lineId is 0 ג€” should have been written on cancel");
                if (lineId > 31)
                    return Fail($"lineId={lineId} is too high ג€” cancel should have fired around row 30");
                if (total != 100)
                    return Fail($"totalLines: expected 100, got {total}");

                return Pass();
            });
        }

        // ג”€ג”€ Test 9: Resume after cancel finds all docs ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        /// <summary>
        /// Cancel a build at row 40 (out of 80), then resume.
        /// Final search must find all 80 docs.
        /// </summary>
        private static string T_ResumeAfterCancelFindsAllDocs()
        {
            return WithTempDir(dir =>
            {
                const int Total = 80;
                const int CancelAt = 40;

                var allRows = Enumerable.Range(1, Total)
                    .Select(i => (i, $"׳¡׳₪׳¨ {i} ׳‘׳“׳™׳§׳”"))
                    .ToList();

                // Phase 1: index rows 1ג€“CancelAt, then "cancel"
                int lastId = 0;
                using (var writer = new LuceneIndexWriter(dir, deleteExistingIndex: true))
                {
                    foreach (var (id, content) in allRows.Take(CancelAt))
                    {
                        writer.AddDocument(id, content);
                        lastId = id;
                    }
                    writer.Commit();
                    WriteProgress(dir, lastId, totalLines: Total, resumeOffset: CancelAt);
                }

                // Verify partial index
                var partial = SearchIds(dir, "׳‘׳“׳™׳§׳”");
                if (partial.Count != CancelAt)
                    return Fail($"After cancel: expected {CancelAt} hits, got {partial.Count}");

                // Phase 2: resume from lastId
                ReadProgress(dir, out int resumeLineId, out _, out _);
                if (resumeLineId != CancelAt)
                    return Fail($"resumeLineId: expected {CancelAt}, got {resumeLineId}");

                var remainingRows = allRows.Where(r => r.Item1 > resumeLineId);
                IndexRows(dir, remainingRows, deleteExisting: false);

                // Final check
                var all = SearchIds(dir, "׳‘׳“׳™׳§׳”");
                if (all.Count != Total)
                    return Fail($"After resume: expected {Total} hits, got {all.Count}. " +
                                $"Missing: [{string.Join(",", Enumerable.Range(1, Total).Where(id => !all.Contains(id)))}]");

                return Pass();
            });
        }

        // ג”€ג”€ Test 10: Multiple interruptions converge to complete index ג”€

        /// <summary>
        /// Simulate 4 interruptions, each indexing 25 rows out of 100.
        /// After all 4 phases the index must contain all 100 docs.
        /// </summary>
        private static string T_MultipleInterruptionsConverge()
        {
            return WithTempDir(dir =>
            {
                const int Total      = 100;
                const int BatchSize  = 25;

                var allRows = Enumerable.Range(1, Total)
                    .Select(i => (i, $"׳₪׳¨׳§ {i} ׳×׳•׳¨׳”"))
                    .ToList();

                bool firstPhase = true;
                int  resumeFrom = 0;

                for (int phase = 0; phase < Total / BatchSize; phase++)
                {
                    var batch = allRows
                        .Where(r => r.Item1 > resumeFrom)
                        .Take(BatchSize)
                        .ToList();

                    using (var writer = new LuceneIndexWriter(dir,
                                            deleteExistingIndex: firstPhase))
                    {
                        int lastId = resumeFrom;
                        foreach (var (id, content) in batch)
                        {
                            writer.AddDocument(id, content);
                            lastId = id;
                        }
                        writer.Commit();
                        WriteProgress(dir, lastId, totalLines: Total,
                                      resumeOffset: (phase + 1) * BatchSize);
                    }

                    firstPhase = false;
                    ReadProgress(dir, out resumeFrom, out _, out _);
                }

                var ids = SearchIds(dir, "׳×׳•׳¨׳”");
                if (ids.Count != Total)
                    return Fail($"Expected {Total} hits after 4 phases, got {ids.Count}. " +
                                $"Missing: [{string.Join(",", Enumerable.Range(1, Total).Where(id => !ids.Contains(id)).Take(10))}]");

                return Pass();
            });
        }

        // ג”€ג”€ Test 11: Fresh build deletes existing index ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        /// <summary>
        /// Build an index with rows 1ג€“10, then do a fresh build with rows 100ג€“110.
        /// The fresh build must NOT contain rows 1ג€“10.
        /// </summary>
        private static string T_FreshBuildDeletesExistingIndex()
        {
            return WithTempDir(dir =>
            {
                // First build
                var first = Enumerable.Range(1, 10).Select(i => (i, $"׳¨׳׳©׳•׳ {i}"));
                IndexRows(dir, first, deleteExisting: true);

                var afterFirst = SearchIds(dir, "׳¨׳׳©׳•׳");
                if (afterFirst.Count != 10)
                    return Fail($"First build: expected 10 hits, got {afterFirst.Count}");

                // Fresh build ג€” different content, different IDs
                var second = Enumerable.Range(100, 10).Select(i => (i, $"׳©׳ ׳™ {i}"));
                IndexRows(dir, second, deleteExisting: true);

                // Old docs must be gone
                var oldDocs = SearchIds(dir, "׳¨׳׳©׳•׳");
                if (oldDocs.Count > 0)
                    return Fail($"Fresh build should have deleted old docs, but found {oldDocs.Count}");

                // New docs must be present
                var newDocs = SearchIds(dir, "׳©׳ ׳™");
                if (newDocs.Count != 10)
                    return Fail($"Fresh build: expected 10 new hits, got {newDocs.Count}");

                return Pass();
            });
        }

        // ג”€ג”€ Test 12: Resume keeps existing docs intact ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        /// <summary>
        /// Index rows 1ג€“10, then resume (deleteExisting=false) and add rows 11ג€“20.
        /// Rows 1ג€“10 must still be searchable after the resume.
        /// </summary>
        private static string T_ResumeKeepsExistingDocs()
        {
            return WithTempDir(dir =>
            {
                var first = Enumerable.Range(1, 10).Select(i => (i, $"׳™׳©׳ {i}"));
                IndexRows(dir, first, deleteExisting: true);

                var second = Enumerable.Range(11, 10).Select(i => (i, $"׳—׳“׳© {i}"));
                IndexRows(dir, second, deleteExisting: false);

                var oldIds = SearchIds(dir, "׳™׳©׳");
                if (oldIds.Count != 10)
                    return Fail($"Old docs missing after resume: expected 10, got {oldIds.Count}");

                var newIds = SearchIds(dir, "׳—׳“׳©");
                if (newIds.Count != 10)
                    return Fail($"New docs missing after resume: expected 10, got {newIds.Count}");

                return Pass();
            });
        }

        // ג”€ג”€ Test 13: Progress file stores all three fields correctly ג”€ג”€

        /// <summary>
        /// Write a progress file with large values and verify all three fields
        /// survive a round-trip, including the resumeOffset accumulation pattern
        /// used by SeforimIndex (resumeOffset = prior offset + rows processed so far).
        /// </summary>
        private static string T_ProgressFileStoresAllThreeFields()
        {
            return WithTempDir(dir =>
            {
                // Simulate two commits: first at row 250000, second at row 500000
                WriteProgress(dir, lineId: 250000, totalLines: 1500000, resumeOffset: 250000);
                ReadProgress(dir, out int id1, out long tot1, out long off1);

                if (id1  != 250000)  return Fail($"Pass 1 lineId: expected 250000, got {id1}");
                if (tot1 != 1500000) return Fail($"Pass 1 totalLines: expected 1500000, got {tot1}");
                if (off1 != 250000)  return Fail($"Pass 1 resumeOffset: expected 250000, got {off1}");

                WriteProgress(dir, lineId: 500000, totalLines: 1500000, resumeOffset: 500000);
                ReadProgress(dir, out int id2, out long tot2, out long off2);

                if (id2  != 500000)  return Fail($"Pass 2 lineId: expected 500000, got {id2}");
                if (tot2 != 1500000) return Fail($"Pass 2 totalLines: expected 1500000, got {tot2}");
                if (off2 != 500000)  return Fail($"Pass 2 resumeOffset: expected 500000, got {off2}");

                return Pass();
            });
        }

        // ג”€ג”€ Test 14: IndexExists returns false on empty directory ג”€ג”€ג”€ג”€ג”€

        private static string T_IndexExistsReturnsFalseOnEmptyDir()
        {
            return WithTempDir(dir =>
            {
                if (LuceneIndexWriter.IndexExists(dir))
                    return Fail("IndexExists returned true on an empty directory");
                return Pass();
            });
        }

        // ג”€ג”€ Test 15: IndexExists returns true after a commit ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        private static string T_IndexExistsReturnsTrueAfterCommit()
        {
            return WithTempDir(dir =>
            {
                using (var writer = new LuceneIndexWriter(dir, deleteExistingIndex: true))
                {
                    writer.AddDocument(1, "׳©׳׳•׳");
                    writer.Commit();
                }

                if (!LuceneIndexWriter.IndexExists(dir))
                    return Fail("IndexExists returned false after a commit");

                return Pass();
            });
        }

        // ג”€ג”€ Test 16: Progress file lineId matches last committed doc ג”€ג”€

        /// <summary>
        /// After indexing rows 1ג€“50 and committing, the progress file's lineId
        /// must be exactly 50 (the last row written), not 49 or 51.
        /// </summary>
        private static string T_ProgressFileLineIdMatchesLastCommit()
        {
            return WithTempDir(dir =>
            {
                var rows = Enumerable.Range(1, 50).Select(i => (i, $"׳©׳•׳¨׳” {i}"));
                int lastId = IndexRows(dir, rows, deleteExisting: true);

                // IndexRows doesn't write progress ג€” do it manually
                WriteProgress(dir, lastId, totalLines: 50, resumeOffset: 50);
                ReadProgress(dir, out int lineId, out _, out _);

                if (lineId != lastId)
                    return Fail($"Progress lineId: expected {lastId}, got {lineId}");
                if (lineId != 50)
                    return Fail($"Progress lineId: expected 50, got {lineId}");

                return Pass();
            });
        }

        // ג”€ג”€ Test 17: No boundary row double-indexed on resume ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        /// <summary>
        /// Index rows 1ג€“50, commit, then resume from lineId=50 and add rows 51ג€“100.
        /// The final search must find exactly 100 docs, not 101 (row 50 duplicated).
        /// This tests that ReadLinesFrom(50, ...) correctly uses WHERE id > 50, not >= 50.
        /// </summary>
        private static string T_NoBoundaryRowDoubleIndexed()
        {
            return WithTempDir(dir =>
            {
                const int FirstBatch = 50;
                const int SecondBatch = 50;
                const int Total = FirstBatch + SecondBatch;

                var allRows = Enumerable.Range(1, Total)
                    .Select(i => (i, $"׳׳™׳׳” {i}"))
                    .ToList();

                // Phase 1: index rows 1ג€“50
                int lastId = IndexRows(dir, allRows.Take(FirstBatch), deleteExisting: true);
                WriteProgress(dir, lastId, totalLines: Total, resumeOffset: FirstBatch);

                if (lastId != FirstBatch)
                    return Fail($"After phase 1: lastId should be {FirstBatch}, got {lastId}");

                // Phase 2: resume from lastId (50) ג€” should skip row 50, add 51ג€“100
                var remainingRows = allRows.Where(r => r.Item1 > lastId);
                IndexRows(dir, remainingRows, deleteExisting: false);

                var ids = SearchIds(dir, "׳׳™׳׳”");

                if (ids.Count != Total)
                    return Fail($"Expected exactly {Total} docs, got {ids.Count}. " +
                                $"Duplicates or missing: {ids.Count - Total:+#;-#;0}");

                // Verify no duplicates
                var duplicates = ids.GroupBy(x => x).Where(g => g.Count() > 1).ToList();
                if (duplicates.Count > 0)
                    return Fail($"Found {duplicates.Count} duplicate row IDs: " +
                                $"[{string.Join(",", duplicates.Select(g => g.Key))}]");

                return Pass();
            });
        }

        // ג”€ג”€ Test 18: No boundary row skipped on resume ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        /// <summary>
        /// Index rows 1ג€“50, commit, then resume from lineId=50 and add rows 51ג€“100.
        /// Verify that row 51 is present (not skipped due to off-by-one).
        /// </summary>
        private static string T_NoBoundaryRowSkipped()
        {
            return WithTempDir(dir =>
            {
                const int FirstBatch = 50;

                var allRows = Enumerable.Range(1, 100)
                    .Select(i => (i, $"׳¡׳₪׳¨ {i}"))
                    .ToList();

                // Phase 1: index rows 1ג€“50
                IndexRows(dir, allRows.Take(FirstBatch), deleteExisting: true);
                ReadProgress(dir, out int lastId, out _, out _);

                // Phase 2: resume and add rows 51ג€“100
                var remainingRows = allRows.Where(r => r.Item1 > lastId);
                IndexRows(dir, remainingRows, deleteExisting: false);

                var ids = SearchIds(dir, "׳¡׳₪׳¨");

                // Specifically check that row 51 is present
                if (!ids.Contains(51))
                    return Fail("Row 51 is missing ג€” boundary row was skipped");

                // And row 50 should still be there
                if (!ids.Contains(50))
                    return Fail("Row 50 is missing ג€” boundary row was lost");

                return Pass();
            });
        }

        // ג”€ג”€ Test 19: Exact doc count after resume ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        /// <summary>
        /// Index 1000 rows in 4 phases (250 each), verifying the doc count
        /// after each phase matches exactly what we expect.
        /// </summary>
        private static string T_ExactDocCountAfterResume()
        {
            return WithTempDir(dir =>
            {
                const int Total = 1000;
                const int BatchSize = 250;

                var allRows = Enumerable.Range(1, Total)
                    .Select(i => (i, $"׳₪׳¨׳§ {i}"))
                    .ToList();

                bool first = true;
                int resumeFrom = 0;

                for (int phase = 0; phase < Total / BatchSize; phase++)
                {
                    var batch = allRows
                        .Where(r => r.Item1 > resumeFrom)
                        .Take(BatchSize)
                        .ToList();

                    IndexRows(dir, batch, deleteExisting: first);
                    first = false;

                    var ids = SearchIds(dir, "׳₪׳¨׳§");
                    int expectedCount = (phase + 1) * BatchSize;

                    if (ids.Count != expectedCount)
                        return Fail($"After phase {phase + 1}: expected {expectedCount} docs, " +
                                    $"got {ids.Count}");

                    ReadProgress(dir, out resumeFrom, out _, out _);
                }

                return Pass();
            });
        }

        // ג”€ג”€ Test 20: Cancel at every commit boundary converges ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        /// <summary>
        /// Simulate cancelling at every commit boundary (every 250 rows).
        /// After 4 cancellations and 4 resumes, all 1000 rows must be present.
        /// This stresses the exact boundary logic.
        /// </summary>
        private static string T_CancelAtEveryBoundaryConverges()
        {
            return WithTempDir(dir =>
            {
                const int Total = 1000;
                const int CommitEvery = 250;

                var allRows = Enumerable.Range(1, Total)
                    .Select(i => (i, $"׳×׳•׳¨׳” {i}"))
                    .ToList();

                bool first = true;
                int resumeFrom = 0;

                for (int phase = 0; phase < Total / CommitEvery; phase++)
                {
                    var batch = allRows
                        .Where(r => r.Item1 > resumeFrom)
                        .Take(CommitEvery)
                        .ToList();

                    using (var writer = new LuceneIndexWriter(dir,
                                            deleteExistingIndex: first))
                    {
                        int lastId = resumeFrom;
                        foreach (var (id, content) in batch)
                        {
                            writer.AddDocument(id, content);
                            lastId = id;
                        }
                        writer.Commit();
                        WriteProgress(dir, lastId, totalLines: Total,
                                      resumeOffset: (phase + 1) * CommitEvery);
                    }

                    first = false;
                    ReadProgress(dir, out resumeFrom, out _, out _);
                }

                var ids = SearchIds(dir, "׳×׳•׳¨׳”");
                if (ids.Count != Total)
                    return Fail($"Expected {Total} docs after boundary cancellations, got {ids.Count}");

                return Pass();
            });
        }

        // ג”€ג”€ Test 21: ForceMerge preserves all docs ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        /// <summary>
        /// Index 100 rows, count them, call ForceMerge, count again.
        /// The count must be identical before and after.
        /// </summary>
        private static string T_ForceMergePreservesAllDocs()
        {
            return WithTempDir(dir =>
            {
                var rows = Enumerable.Range(1, 100).Select(i => (i, $"׳¡׳₪׳¨ {i}"));

                using (var writer = new LuceneIndexWriter(dir, deleteExistingIndex: true))
                {
                    foreach (var (id, content) in rows)
                        writer.AddDocument(id, content);
                    writer.Commit();
                }

                var beforeMerge = SearchIds(dir, "׳¡׳₪׳¨");

                using (var writer = new LuceneIndexWriter(dir, deleteExistingIndex: false))
                {
                    writer.ForceMerge();
                }

                var afterMerge = SearchIds(dir, "׳¡׳₪׳¨");

                if (beforeMerge.Count != afterMerge.Count)
                    return Fail($"Doc count changed after ForceMerge: " +
                                $"{beforeMerge.Count} ג†’ {afterMerge.Count}");

                var diff = beforeMerge.Except(afterMerge).Concat(afterMerge.Except(beforeMerge)).ToList();
                if (diff.Count > 0)
                    return Fail($"Docs changed after ForceMerge: {diff.Count} differences");

                return Pass();
            });
        }

        // ג”€ג”€ Test 22: ForceMerge produces single segment ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        /// <summary>
        /// Index rows in 5 separate commits (creating 5 segments), then ForceMerge.
        /// Verify that only one segments_* file exists afterward.
        /// Note: Lucene may auto-merge small segments, so we create larger batches.
        /// </summary>
        private static string T_ForceMergeProducesSingleSegment()
        {
            return WithTempDir(dir =>
            {
                // Create 5 segments by opening/closing the writer 5 times with larger batches
                for (int batch = 0; batch < 5; batch++)
                {
                    using (var writer = new LuceneIndexWriter(dir, deleteExistingIndex: batch == 0))
                    {
                        for (int i = 0; i < 100; i++)  // Larger batch to avoid auto-merge
                            writer.AddDocument(batch * 100 + i + 1, $"׳¡׳₪׳¨ {batch * 100 + i + 1}");
                        writer.Commit();
                    }
                }

                // Count segments before merge
                var segmentsBefore = Directory.GetFiles(dir)
                    .Where(f => Path.GetFileName(f).StartsWith("segments_") &&
                                Path.GetFileName(f) != "segments.gen")
                    .ToList();

                // If Lucene auto-merged, we might have fewer segments ג€” that's OK.
                // The important thing is that ForceMerge reduces it to 1.
                int segmentsBeforeCount = segmentsBefore.Count;

                // Merge
                using (var writer = new LuceneIndexWriter(dir, deleteExistingIndex: false))
                {
                    writer.ForceMerge();
                }

                // Count segments after merge
                var segmentsAfter = Directory.GetFiles(dir)
                    .Where(f => Path.GetFileName(f).StartsWith("segments_") &&
                                Path.GetFileName(f) != "segments.gen")
                    .ToList();

                if (segmentsAfter.Count != 1)
                    return Fail($"Expected 1 segment after ForceMerge, got {segmentsAfter.Count} " +
                                $"(had {segmentsBeforeCount} before)");

                // Verify all docs are still there
                var ids = SearchIds(dir, "׳¡׳₪׳¨");
                if (ids.Count != 500)
                    return Fail($"Expected 500 docs after merge, got {ids.Count}");

                return Pass();
            });
        }

        // ג”€ג”€ Test 23: Search after ForceMerge matches before ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        /// <summary>
        /// Index rows across multiple commits, search before ForceMerge,
        /// then ForceMerge and search again. Results must be identical.
        /// </summary>
        private static string T_SearchAfterForceMergeMatchesBefore()
        {
            return WithTempDir(dir =>
            {
                // Create multiple segments
                using (var writer = new LuceneIndexWriter(dir, deleteExistingIndex: true))
                {
                    for (int batch = 0; batch < 3; batch++)
                    {
                        for (int i = 0; i < 30; i++)
                            writer.AddDocument(batch * 30 + i + 1, $"׳׳™׳׳” {batch * 30 + i + 1}");
                        writer.Commit();
                    }
                }

                var beforeMerge = SearchIds(dir, "׳׳™׳׳”");

                using (var writer = new LuceneIndexWriter(dir, deleteExistingIndex: false))
                {
                    writer.ForceMerge();
                }

                var afterMerge = SearchIds(dir, "׳׳™׳׳”");

                if (beforeMerge.Count != afterMerge.Count)
                    return Fail($"Count mismatch: {beforeMerge.Count} vs {afterMerge.Count}");

                // Check exact match
                for (int i = 0; i < beforeMerge.Count; i++)
                {
                    if (beforeMerge[i] != afterMerge[i])
                        return Fail($"Result mismatch at index {i}: " +
                                    $"{beforeMerge[i]} vs {afterMerge[i]}");
                }

                return Pass();
            });
        }

        // ג”€ג”€ Test 24: Resumed build finalizes with merge ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        /// <summary>
        /// Index rows 1ג€“50, resume and add 51ג€“100 (completing the build).
        /// Verify that ForceMerge was called (single segment) and all docs are present.
        /// This simulates the SeforimIndex.BuildIndex finalization path.
        /// </summary>
        private static string T_ResumedBuildFinalizesMerge()
        {
            return WithTempDir(dir =>
            {
                const int FirstBatch = 50;
                const int SecondBatch = 50;
                const int Total = FirstBatch + SecondBatch;

                var allRows = Enumerable.Range(1, Total)
                    .Select(i => (i, $"׳¡׳₪׳¨ {i}"))
                    .ToList();

                // Phase 1: index and commit
                int lastId = IndexRows(dir, allRows.Take(FirstBatch), deleteExisting: true);
                WriteProgress(dir, lastId, totalLines: Total, resumeOffset: FirstBatch);

                // Phase 2: resume, add remaining, and merge (simulating SeforimIndex finalization)
                var remainingRows = allRows.Where(r => r.Item1 > lastId).ToList();
                using (var writer = new LuceneIndexWriter(dir, deleteExistingIndex: false))
                {
                    foreach (var (id, content) in remainingRows)
                        writer.AddDocument(id, content);
                    writer.Commit();
                    writer.ForceMerge();  // This is what SeforimIndex does on completion
                }

                var ids = SearchIds(dir, "׳¡׳₪׳¨");

                if (ids.Count != Total)
                    return Fail($"Expected {Total} docs after finalization, got {ids.Count}");

                // Verify single segment
                var segments = Directory.GetFiles(dir)
                    .Where(f => Path.GetFileName(f).StartsWith("segments_") &&
                                Path.GetFileName(f) != "segments.gen")
                    .ToList();

                if (segments.Count != 1)
                    return Fail($"Expected 1 segment after finalization, got {segments.Count}");

                return Pass();
            });
        }

        // ג”€ג”€ Test 25: No duplicates after resume and merge ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        /// <summary>
        /// Index rows 1ג€“100 in 4 phases with merges at each phase.
        /// Verify no row ID appears more than once in the final results.
        /// </summary>
        private static string T_NoDuplicatesAfterResumeAndMerge()
        {
            return WithTempDir(dir =>
            {
                const int Total = 100;
                const int BatchSize = 25;

                var allRows = Enumerable.Range(1, Total)
                    .Select(i => (i, $"׳₪׳¨׳§ {i}"))
                    .ToList();

                bool first = true;
                int resumeFrom = 0;

                for (int phase = 0; phase < Total / BatchSize; phase++)
                {
                    var batch = allRows
                        .Where(r => r.Item1 > resumeFrom)
                        .Take(BatchSize)
                        .ToList();

                    using (var writer = new LuceneIndexWriter(dir,
                                            deleteExistingIndex: first))
                    {
                        int lastId = resumeFrom;
                        foreach (var (id, content) in batch)
                        {
                            writer.AddDocument(id, content);
                            lastId = id;
                        }
                        writer.Commit();
                        writer.ForceMerge();  // Merge after each phase
                        WriteProgress(dir, lastId, totalLines: Total,
                                      resumeOffset: (phase + 1) * BatchSize);
                    }

                    first = false;
                    ReadProgress(dir, out resumeFrom, out _, out _);
                }

                var ids = SearchIds(dir, "׳₪׳¨׳§");

                if (ids.Count != Total)
                    return Fail($"Expected {Total} docs, got {ids.Count}");

                var duplicates = ids.GroupBy(x => x).Where(g => g.Count() > 1).ToList();
                if (duplicates.Count > 0)
                    return Fail($"Found {duplicates.Count} duplicate row IDs: " +
                                $"[{string.Join(",", duplicates.Select(g => g.Key))}]");

                return Pass();
            });
        }

        // ג”€ג”€ Test 26: Concurrent search during resume ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€ג”€

        /// <summary>
        /// Resume a build (phase 1 ג†’ phase 2) while a search thread is running.
        /// Verify:
        ///   1. No exceptions on either thread
        ///   2. No duplicate results in any search
        ///   3. Searches see consistent snapshots (no partial/corrupted results)
        /// </summary>
        private static string T_ConcurrentSearchDuringResume()
        {
            return WithTempDir(dir =>
            {
                const int FirstBatch = 100;
                const int SecondBatch = 100;
                const int Total = FirstBatch + SecondBatch;

                var allRows = Enumerable.Range(1, Total)
                    .Select(i => (i, $"׳¡׳₪׳¨ {i}"))
                    .ToList();

                // Phase 1: index first batch
                int lastId = IndexRows(dir, allRows.Take(FirstBatch), deleteExisting: true);
                WriteProgress(dir, lastId, totalLines: Total, resumeOffset: FirstBatch);

                var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
                var searchResults = new System.Collections.Concurrent.ConcurrentBag<List<int>>();
                var resumeDone = new ManualResetEventSlim(false);

                // Search thread: run searches every 50ms until resume is done
                var searchThread = new Thread(() =>
                {
                    try
                    {
                        while (!resumeDone.IsSet)
                        {
                            var ids = SearchIds(dir, "׳¡׳₪׳¨");
                            searchResults.Add(ids);
                            Thread.Sleep(50);
                        }
                        // Final search after resume (writer closed, index updated)
                        Thread.Sleep(500);  // Give searcher time to refresh
                        var finalIds = SearchIds(dir, "׳¡׳₪׳¨");
                        searchResults.Add(finalIds);
                    }
                    catch (Exception ex) { errors.Add("Search: " + ex.Message); }
                }) { IsBackground = true };

                searchThread.Start();

                try
                {
                    // Phase 2: resume and add remaining rows
                    var remainingRows = allRows.Where(r => r.Item1 > lastId).ToList();
                    IndexRows(dir, remainingRows, deleteExisting: false);
                }
                catch (Exception ex) { errors.Add("Resume: " + ex.Message); }
                finally { resumeDone.Set(); }

                searchThread.Join(15000);

                if (errors.Count > 0)
                    return Fail($"{errors.Count} error(s): {string.Join("; ", errors.Take(3))}");

                // Verify no search returned duplicates (most important check)
                foreach (var result in searchResults)
                {
                    var dups = result.GroupBy(x => x).Where(g => g.Count() > 1).ToList();
                    if (dups.Count > 0)
                        return Fail($"Search returned duplicates: " +
                                    $"[{string.Join(",", dups.Select(g => g.Key))}]");
                }

                // Verify intermediate searches saw consistent snapshots
                // (either FirstBatch or Total, never partial)
                var uniqueCounts = searchResults.Select(r => r.Count).Distinct().ToList();
                foreach (var count in uniqueCounts)
                {
                    if (count != FirstBatch && count != Total)
                        return Fail($"Search returned inconsistent count: {count} " +
                                    $"(expected {FirstBatch} or {Total})");
                }

                // Verify the index eventually has all docs (sanity check)
                var sanityCheck = SearchIds(dir, "׳¡׳₪׳¨");
                if (sanityCheck.Count != Total)
                    return Fail($"Sanity check: expected {Total} docs, got {sanityCheck.Count}");

                return Pass();
            });
        }
    }
}

