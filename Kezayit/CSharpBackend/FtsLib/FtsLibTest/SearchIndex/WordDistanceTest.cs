using FtsLib.Search;
using FtsLib.SeforimDb;
using FtsLib.Snippets;
using System;
using System.Collections.Generic;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Unit tests for word-distance filtering in the snippet pipeline.
    ///
    /// Tests the full chain:
    ///   SnippetBuilder.Build → WordDistance on SnippetResult
    ///
    /// No index or database required — all content is supplied inline.
    ///
    /// Usage:
    ///   FtsLibTest.exe worddist
    /// </summary>
    internal static class WordDistanceTest
    {
        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine();
            Console.WriteLine("╔══ WORD DISTANCE TESTS ══");

            int passed = 0, failed = 0;

            // ── Helper: build a snippet and return WordDistance ───────

            // We test through SnippetBuilder directly (internal access via
            // same-project reference).  Each test supplies raw HTML content
            // and a list of query groups, then asserts the WordDistance value.

            // ── 1. Two adjacent terms — distance 0 ───────────────────

            Check(ref passed, ref failed,
                name:             "two adjacent terms → distance 0",
                content:          "כי ביצחק יקרא לך זרע",
                groups:           G("כי", "ביצחק"),
                originalCount:    2,
                expectedDistance: 0);

            // ── 2. One word between terms — distance 1 ────────────────

            Check(ref passed, ref failed,
                name:             "one word between terms → distance 1",
                content:          "כי הוא ביצחק יקרא",
                groups:           G("כי", "ביצחק"),
                originalCount:    2,
                expectedDistance: 1);

            // ── 3. Five-word phrase, all consecutive — distance 0 ─────

            Check(ref passed, ref failed,
                name:             "five consecutive query words → distance 0",
                content:          "וידבר משה כן אל בני ישראל",
                groups:           G("וידבר", "משה", "כן", "אל", "בני"),
                originalCount:    5,
                expectedDistance: 0);

            // ── 4. Skipped wildcard: 5 original groups, 4 effective ───
            // Simulates: וידבר משה* כן *ל בני
            // *ל expands to nothing → dropped from effective groups.
            // Effective groups: וידבר, משה, כן, בני (4 groups).
            // In text: וידבר משה כן אל בני — iRight-iLeft = 4.
            // With originalCount=5: wordDist = 4 - (5-1) = 0.

            Check(ref passed, ref failed,
                name:             "skipped wildcard slot: originalCount=5, effective=4, consecutive → distance 0",
                content:          "וידבר משה כן אל בני ישראל",
                groups:           G("וידבר", "משה", "כן", "בני"),   // *ל skipped
                originalCount:    5,
                expectedDistance: 0);

            // ── 5. Skipped wildcard with one extra word ───────────────
            // Same query but text has an extra word between כן and בני.
            // iRight-iLeft = 5, originalCount=5 → wordDist = 5-4 = 1.

            Check(ref passed, ref failed,
                name:             "skipped wildcard + one extra word → distance 1",
                content:          "וידבר משה כן אל גם בני ישראל",
                groups:           G("וידבר", "משה", "כן", "בני"),
                originalCount:    5,
                expectedDistance: 1);

            // ── 6. Terms far apart — large distance ───────────────────

            Check(ref passed, ref failed,
                name:             "terms 10 words apart → distance 10",
                content:          "כי אחד שתים שלש ארבע חמש שש שבע שמונה תשע עשר ביצחק",
                groups:           G("כי", "ביצחק"),
                originalCount:    2,
                expectedDistance: 10);

            // ── 7. No match — IsMatch false ───────────────────────────

            CheckNoMatch(ref passed, ref failed,
                name:    "term not in content → IsMatch false",
                content: "שלום עולם",
                groups:  G("תורה"));

            // ── 8. HTML tags don't count as words ─────────────────────

            Check(ref passed, ref failed,
                name:             "HTML tags between terms don't inflate distance",
                content:          "כי <b>ביצחק</b> יקרא",
                groups:           G("כי", "ביצחק"),
                originalCount:    2,
                expectedDistance: 0);

            // ── 9. Nikud stripped — terms still match ─────────────────

            Check(ref passed, ref failed,
                name:             "nikud in content stripped — terms match",
                content:          "כִּי בְיִצְחָק יִקָּרֵא לְךָ זָרַע",
                groups:           G("כי", "ביצחק"),
                originalCount:    2,
                expectedDistance: 0);

            // ── 10. Three terms, two words between each ───────────────

            Check(ref passed, ref failed,
                name:             "three terms, two extra words between each → distance 4",
                content:          "ראובן אחד שתים שמעון שלש ארבע לוי",
                groups:           G("ראובן", "שמעון", "לוי"),
                originalCount:    3,
                expectedDistance: 4);

            // ── 11. maxWordDistance filter: passes when within limit ──

            CheckFilter(ref passed, ref failed,
                name:             "filter passes: distance 1 ≤ maxWordDistance 2",
                content:          "כי הוא ביצחק",
                groups:           G("כי", "ביצחק"),
                originalCount:    2,
                maxWordDistance:  2,
                expectPass:       true);

            // ── 12. maxWordDistance filter: blocked when over limit ───

            CheckFilter(ref passed, ref failed,
                name:             "filter blocks: distance 3 > maxWordDistance 2",
                content:          "כי אחד שתים שלש ביצחק",
                groups:           G("כי", "ביצחק"),
                originalCount:    2,
                maxWordDistance:  2,
                expectPass:       false);

            // ── 13. maxWordDistance = 0: only adjacent terms pass ─────

            CheckFilter(ref passed, ref failed,
                name:             "maxWordDistance=0: adjacent terms pass",
                content:          "כי ביצחק יקרא",
                groups:           G("כי", "ביצחק"),
                originalCount:    2,
                maxWordDistance:  0,
                expectPass:       true);

            CheckFilter(ref passed, ref failed,
                name:             "maxWordDistance=0: one word between → blocked",
                content:          "כי הוא ביצחק",
                groups:           G("כי", "ביצחק"),
                originalCount:    2,
                maxWordDistance:  0,
                expectPass:       false);

            // ── Summary ───────────────────────────────────────────────

            Console.WriteLine("║");
            string overall = failed == 0
                ? $"✓  All {passed} tests passed"
                : $"✗  {failed} FAILED  /  {passed + failed} total";
            Console.WriteLine($"║  {overall}");
            Console.WriteLine("╚══ WORD DISTANCE TESTS DONE ══");
            Console.WriteLine();

            if (failed > 0)
                Environment.Exit(1);
        }

        // ── Assertion helpers ─────────────────────────────────────────

        /// <summary>
        /// Builds a snippet and asserts the WordDistance value.
        /// </summary>
        private static void Check(
            ref int                                        passed,
            ref int                                        failed,
            string                                         name,
            string                                         content,
            IReadOnlyList<IReadOnlyCollection<string>>     groups,
            int                                            originalCount,
            int                                            expectedDistance)
        {
            var builder = new SnippetBuilder();
            var result  = builder.Build(content, groups, requireOrdered: false, originalGroupCount: originalCount);

            if (!result.IsMatch)
            {
                failed++;
                Console.WriteLine($"║  ✗  {name}");
                Console.WriteLine($"║       IsMatch=false (expected a match)");
                return;
            }

            if (result.WordDistance == expectedDistance)
            {
                passed++;
                Console.WriteLine($"║  ✓  {name}  (dist={result.WordDistance})");
            }
            else
            {
                failed++;
                Console.WriteLine($"║  ✗  {name}");
                Console.WriteLine($"║       WordDistance: expected {expectedDistance}, got {result.WordDistance}");
            }
        }

        /// <summary>
        /// Asserts that the snippet has IsMatch=false.
        /// </summary>
        private static void CheckNoMatch(
            ref int                                    passed,
            ref int                                    failed,
            string                                     name,
            string                                     content,
            IReadOnlyList<IReadOnlyCollection<string>> groups)
        {
            var builder = new SnippetBuilder();
            var result  = builder.Build(content, groups);

            if (!result.IsMatch)
            {
                passed++;
                Console.WriteLine($"║  ✓  {name}  (IsMatch=false as expected)");
            }
            else
            {
                failed++;
                Console.WriteLine($"║  ✗  {name}");
                Console.WriteLine($"║       expected IsMatch=false, got IsMatch=true (dist={result.WordDistance})");
            }
        }

        /// <summary>
        /// Asserts whether a result would pass or be blocked by maxWordDistance.
        /// </summary>
        private static void CheckFilter(
            ref int                                        passed,
            ref int                                        failed,
            string                                         name,
            string                                         content,
            IReadOnlyList<IReadOnlyCollection<string>>     groups,
            int                                            originalCount,
            int                                            maxWordDistance,
            bool                                           expectPass)
        {
            var builder  = new SnippetBuilder();
            var result   = builder.Build(content, groups, requireOrdered: false, originalGroupCount: originalCount);
            bool passes  = result.IsMatch && result.WordDistance <= maxWordDistance;

            if (passes == expectPass)
            {
                passed++;
                string verdict = expectPass ? "passes" : "blocked";
                Console.WriteLine($"║  ✓  {name}  (dist={result.WordDistance}, {verdict})");
            }
            else
            {
                failed++;
                Console.WriteLine($"║  ✗  {name}");
                Console.WriteLine($"║       expected {(expectPass ? "pass" : "block")}, " +
                                  $"got dist={result.WordDistance} → {(passes ? "pass" : "block")}");
            }
        }

        // ── Query group builder ───────────────────────────────────────

        /// <summary>
        /// Builds a list of single-term groups from the given terms.
        /// Each term becomes its own group (AND semantics).
        /// </summary>
        private static IReadOnlyList<IReadOnlyCollection<string>> G(params string[] terms)
        {
            var groups = new List<IReadOnlyCollection<string>>(terms.Length);
            foreach (var t in terms)
                groups.Add(new[] { t });
            return groups;
        }
    }
}
