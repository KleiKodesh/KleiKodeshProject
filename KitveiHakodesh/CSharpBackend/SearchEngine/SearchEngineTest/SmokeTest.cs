using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SearchEngine.Search;
using SearchEngine.SeforimDb;

namespace SearchEngineTest
{
    /// <summary>
    /// Fast smoke tests that run against the real index in lucene_index/.
    /// Each test completes in milliseconds. Total suite target: under 3 seconds.
    ///
    /// Usage:
    ///   LuceneTest test smoke
    /// </summary>
    internal static class SmokeTest
    {
        private static readonly string IndexDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lucene_index");

        public static int Run()
        {
            Console.WriteLine("=== SMOKE TESTS ===");
            Console.WriteLine($"Index: {IndexDir}");
            Console.WriteLine();

            if (!LuceneSearcher.IndexExists(IndexDir))
            {
                Console.WriteLine("SKIP — no index found. Run 'build' first.");
                return 0;
            }

            var tests = new (string Name, Func<string> Body)[]
            {
                ("IndexExists",              T_IndexExists),
                ("LiteralSearch_HitsFound",  T_LiteralSearch_HitsFound),
                ("TwoWordSearch_HitsFound",  T_TwoWordSearch_HitsFound),
                ("WildcardPrefix",           T_WildcardPrefix),
                ("EmptyQuery_NoResults",     T_EmptyQuery_NoResults),
                ("StoredFieldsPresent",      T_StoredFieldsPresent),
                ("ResultsAreSortedByRowId",  T_ResultsAreSortedByRowId),
                ("SnippetContainsMark",      T_SnippetContainsMark),
            };

            int passed = 0, failed = 0;
            var total = Stopwatch.StartNew();

            foreach (var (name, body) in tests)
            {
                var sw = Stopwatch.StartNew();
                string failure = null;
                try { failure = body(); }
                catch (Exception ex) { failure = "EXCEPTION: " + ex.Message; }
                sw.Stop();

                if (failure == null)
                {
                    Console.WriteLine($"  PASS  {name,-40} ({sw.ElapsedMilliseconds} ms)");
                    passed++;
                }
                else
                {
                    Console.WriteLine($"  FAIL  {name,-40} ({sw.ElapsedMilliseconds} ms)");
                    Console.WriteLine($"        {failure}");
                    failed++;
                }
            }

            total.Stop();
            Console.WriteLine();
            Console.WriteLine($"Results: {passed} passed, {failed} failed  ({total.ElapsedMilliseconds} ms total)");
            return failed;
        }

        // ── Helpers ───────────────────────────────────────────────────

        private static string Pass() => null;
        private static string Fail(string msg) => msg;

        // ── Tests ─────────────────────────────────────────────────────

        private static string T_IndexExists()
        {
            return LuceneSearcher.IndexExists(IndexDir) ? Pass() : Fail("IndexExists returned false");
        }

        private static string T_LiteralSearch_HitsFound()
        {
            using (var searcher = new LuceneSearcher(IndexDir))
            {
                int count = 0;
                // Just check if we get any hits — don't iterate through all results
                foreach (var _ in searcher.Search("תורה"))
                {
                    count = 1; // Found at least one
                    break;
                }
                return count > 0 ? Pass() : Fail("Expected hits for 'תורה', got 0");
            }
        }

        private static string T_TwoWordSearch_HitsFound()
        {
            using (var searcher = new LuceneSearcher(IndexDir))
            {
                int count = 0;
                foreach (var _ in searcher.Search("ספר תורה"))
                {
                    count = 1;
                    break;
                }
                return count > 0 ? Pass() : Fail("Expected hits for 'ספר תורה', got 0");
            }
        }

        private static string T_WildcardPrefix()
        {
            using (var searcher = new LuceneSearcher(IndexDir))
            {
                int count = 0;
                foreach (var _ in searcher.Search("תור*"))
                {
                    count = 1;
                    break;
                }
                return count > 0 ? Pass() : Fail("Expected hits for wildcard 'תור*', got 0");
            }
        }

        private static string T_EmptyQuery_NoResults()
        {
            using (var searcher = new LuceneSearcher(IndexDir))
            {
                int count = 0;
                foreach (var _ in searcher.Search("   "))
                    count++;
                return count == 0 ? Pass() : Fail($"Expected 0 results for whitespace query, got {count}");
            }
        }

        private static string T_StoredFieldsPresent()
        {
            using (var searcher = new LuceneSearcher(IndexDir))
            {
                foreach (var (rowId, bookId, bookTitle, tocPath) in searcher.Search("תורה"))
                {
                    if (rowId <= 0)
                        return Fail($"rowId must be > 0, got {rowId}");
                    if (bookId <= 0)
                        return Fail($"bookId must be > 0, got {bookId}");
                    if (string.IsNullOrEmpty(bookTitle))
                        return Fail("bookTitle is empty — was it indexed?");
                    // tocPath may legitimately be empty for lines with no TOC entry
                    return Pass();
                }
                return Fail("No results to check stored fields");
            }
        }

        private static string T_ResultsAreSortedByRowId()
        {
            using (var searcher = new LuceneSearcher(IndexDir))
            {
                int prev = -1;
                int checked_ = 0;
                foreach (var (rowId, _, __, ___) in searcher.Search("תורה"))
                {
                    if (rowId <= prev)
                        return Fail($"Results not sorted: {prev} followed by {rowId}");
                    prev = rowId;
                    if (++checked_ >= 10) break; // Check just first 10 instead of 50
                }
                return checked_ > 0 ? Pass() : Fail("No results to check ordering");
            }
        }

        private static string T_SnippetContainsMark()
        {
            // Open the DB to provide content for snippet generation.
            // ZayitDb resolves the path from registry/default if null.
            using (var searcher = new LuceneSearcher(IndexDir))
            using (var db = new ZayitDb())
            {
                if (!db.IsOpen) return Pass(); // no DB available in this environment — skip

                foreach (var (_, _, _, _, fragment) in searcher.SearchWithSnippets(
                    "תורה",
                    rowId => db.GetLineById(rowId),
                    minMarks: 1))
                {
                    if (!fragment.Contains("<mark>"))
                        return Fail("Snippet does not contain <mark> tag");
                    return Pass(); // Return immediately after first result
                }
                return Fail("SearchWithSnippets returned no results for 'תורה'");
            }
        }
    }
}
