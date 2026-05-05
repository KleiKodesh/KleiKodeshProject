using FtsLib.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Unit tests for the ordered-search feature in <see cref="SnippetBuilder"/>.
    ///
    /// All tests are purely in-memory — no index, no database required.
    /// The TokenStream is the heart of the pipeline: every test exercises the
    /// full path: tokenize → window-find → (ordered check) → render.
    ///
    /// NOTE: The tokenizer drops single-character tokens (buffer.Length > 1),
    /// so all test terms use real multi-character Hebrew words.
    ///
    /// Usage:
    ///   FtsLibTest.exe orderedtest
    /// </summary>
    internal static class OrderedSearchTest
    {
        // ── Shared test vocabulary ────────────────────────────────────
        // Real Hebrew words so the tokenizer accepts them.
        private const string W1 = "אברהם";   // Abraham
        private const string W2 = "יצחק";    // Isaac
        private const string W3 = "יעקב";    // Jacob
        private const string W4 = "שלום";    // peace
        private const string W5 = "תורה";    // Torah
        private const string W6 = "עולם";    // world
        private const string WN = "ספר";     // noise word (book)

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine();
            Console.WriteLine("╔══ ORDERED SEARCH TESTS ══");

            int passed = 0, failed = 0;

            // ─────────────────────────────────────────────────────────
            // 1. UNORDERED MODE — baseline: existing behaviour unchanged
            // ─────────────────────────────────────────────────────────

            // 1a. Single term always matches.
            CheckSnippet(ref passed, ref failed,
                name:        "unordered / single term — match",
                html:        $"{W1} {W2} {W3}",
                groups:      G(W1),
                ordered:     false,
                expectMatch: true);

            // 1b. Two terms present in order → match.
            CheckSnippet(ref passed, ref failed,
                name:        "unordered / two terms in order — match",
                html:        $"{W1} {W2} {W3}",
                groups:      G(W1, W2),
                ordered:     false,
                expectMatch: true);

            // 1c. Two terms present but REVERSED in text → unordered still matches.
            CheckSnippet(ref passed, ref failed,
                name:        "unordered / two terms reversed in text — match",
                html:        $"{W2} {W1}",
                groups:      G(W1, W2),
                ordered:     false,
                expectMatch: true);

            // 1d. One term missing → no match.
            CheckSnippet(ref passed, ref failed,
                name:        "unordered / missing term — no match",
                html:        $"{W1} {W3}",
                groups:      G(W1, W2),
                ordered:     false,
                expectMatch: false);

            // 1e. Three terms all present, scrambled order → unordered match.
            CheckSnippet(ref passed, ref failed,
                name:        "unordered / three terms scrambled — match",
                html:        $"{W3} {W1} {W2}",
                groups:      G(W1, W2, W3),
                ordered:     false,
                expectMatch: true);

            // ─────────────────────────────────────────────────────────
            // 2. ORDERED MODE — terms must appear left-to-right
            // ─────────────────────────────────────────────────────────

            // 2a. Single term — ordered mode trivially satisfied.
            CheckSnippet(ref passed, ref failed,
                name:        "ordered / single term — match",
                html:        $"{W1} {W2} {W3}",
                groups:      G(W2),
                ordered:     true,
                expectMatch: true);

            // 2b. Two terms in correct order → ordered match.
            CheckSnippet(ref passed, ref failed,
                name:        "ordered / two terms in correct order — match",
                html:        $"{W1} {W2} {W3}",
                groups:      G(W1, W2),
                ordered:     true,
                expectMatch: true);

            // 2c. Two terms in WRONG order → ordered no-match.
            CheckSnippet(ref passed, ref failed,
                name:        "ordered / two terms in wrong order — no match",
                html:        $"{W2} {W1}",
                groups:      G(W1, W2),
                ordered:     true,
                expectMatch: false);

            // 2d. Three terms in correct order → ordered match.
            CheckSnippet(ref passed, ref failed,
                name:        "ordered / three terms in correct order — match",
                html:        $"{W1} {W2} {W3}",
                groups:      G(W1, W2, W3),
                ordered:     true,
                expectMatch: true);

            // 2e. Three terms, last two swapped → ordered no-match.
            CheckSnippet(ref passed, ref failed,
                name:        "ordered / three terms last two swapped — no match",
                html:        $"{W1} {W3} {W2}",
                groups:      G(W1, W2, W3),
                ordered:     true,
                expectMatch: false);

            // 2f. Three terms, first two swapped → ordered no-match.
            CheckSnippet(ref passed, ref failed,
                name:        "ordered / three terms first two swapped — no match",
                html:        $"{W2} {W1} {W3}",
                groups:      G(W1, W2, W3),
                ordered:     true,
                expectMatch: false);

            // 2g. Three terms fully reversed → ordered no-match.
            CheckSnippet(ref passed, ref failed,
                name:        "ordered / three terms fully reversed — no match",
                html:        $"{W3} {W2} {W1}",
                groups:      G(W1, W2, W3),
                ordered:     true,
                expectMatch: false);

            // ─────────────────────────────────────────────────────────
            // 3. ORDERED MODE — non-adjacent / interleaved terms
            // ─────────────────────────────────────────────────────────

            // 3a. Terms in order with noise words between them → match.
            CheckSnippet(ref passed, ref failed,
                name:        "ordered / terms in order with noise — match",
                html:        $"{W1} {WN} {W2} {WN} {W3}",
                groups:      G(W1, W2, W3),
                ordered:     true,
                expectMatch: true);

            // 3b. Query [W1, W2] in text "W1 W3 W2" — W1 at 0, W2 at 2 → match.
            CheckSnippet(ref passed, ref failed,
                name:        "ordered / query [W1,W2] in text 'W1 W3 W2' — match",
                html:        $"{W1} {W3} {W2}",
                groups:      G(W1, W2),
                ordered:     true,
                expectMatch: true);

            // 3c. Query [W1, W2, W3] in text "W2 W1 W3" — W1 appears after W2,
            //     so no valid ordered sequence W1→W2→W3 exists.
            CheckSnippet(ref passed, ref failed,
                name:        "ordered / query [W1,W2,W3] in text 'W2 W1 W3' — no match",
                html:        $"{W2} {W1} {W3}",
                groups:      G(W1, W2, W3),
                ordered:     true,
                expectMatch: false);

            // 3d. Repeated occurrences: "W2 W1 W2 W3" — query [W1, W2, W3].
            //     First W2 is before W1, but second W2 is after W1 → match.
            CheckSnippet(ref passed, ref failed,
                name:        "ordered / repeated term: 'W2 W1 W2 W3' query [W1,W2,W3] — match",
                html:        $"{W2} {W1} {W2} {W3}",
                groups:      G(W1, W2, W3),
                ordered:     true,
                expectMatch: true);

            // 3e. "W3 W1 W3 W2" — query [W1, W2, W3].
            //     W1 at 1, then need W2 after it: W2 at 3, then need W3 after 3 — none → no match.
            CheckSnippet(ref passed, ref failed,
                name:        "ordered / 'W3 W1 W3 W2' query [W1,W2,W3] — no match",
                html:        $"{W3} {W1} {W3} {W2}",
                groups:      G(W1, W2, W3),
                ordered:     true,
                expectMatch: false);

            // ─────────────────────────────────────────────────────────
            // 4. ORDERED MODE — HTML tags are transparent
            // ─────────────────────────────────────────────────────────

            // 4a. Terms in order, separated by inline HTML tags → match.
            CheckSnippet(ref passed, ref failed,
                name:        "ordered / terms in order through inline tags — match",
                html:        $"<b>{W1}</b> <i>{W2}</i> <span>{W3}</span>",
                groups:      G(W1, W2, W3),
                ordered:     true,
                expectMatch: true);

            // 4b. Terms in wrong order through block tags → no match.
            CheckSnippet(ref passed, ref failed,
                name:        "ordered / terms in wrong order through block tags — no match",
                html:        $"<p>{W3}</p><p>{W2}</p><p>{W1}</p>",
                groups:      G(W1, W2, W3),
                ordered:     true,
                expectMatch: false);

            // 4c. Terms in order through block tags → match.
            CheckSnippet(ref passed, ref failed,
                name:        "ordered / terms in order through block tags — match",
                html:        $"<p>{W1}</p><p>{W2}</p><p>{W3}</p>",
                groups:      G(W1, W2, W3),
                ordered:     true,
                expectMatch: true);

            // ─────────────────────────────────────────────────────────
            // 5. ORDERED MODE — OR groups (multi-alternative groups)
            // ─────────────────────────────────────────────────────────

            // 5a. OR group: text has second alternative of group 0, then group 1 → match.
            //     groups = [[W1|W2], [W3]]; text = "W2 W3"
            CheckSnippet(ref passed, ref failed,
                name:        "ordered / OR group: alt2 of group0 then group1 — match",
                html:        $"{W2} {W3}",
                groups:      new[] { new[] { W1, W2 }, new[] { W3 } },
                ordered:     true,
                expectMatch: true);

            // 5b. OR group: group1 appears before any alternative of group0 → no match.
            //     groups = [[W1|W2], [W3]]; text = "W3 W1"
            CheckSnippet(ref passed, ref failed,
                name:        "ordered / OR group: group1 before group0 alts — no match",
                html:        $"{W3} {W1}",
                groups:      new[] { new[] { W1, W2 }, new[] { W3 } },
                ordered:     true,
                expectMatch: false);

            // 5c. OR group: both alternatives of group0 present, group1 after → match.
            //     groups = [[W1|W2], [W3]]; text = "W1 W2 W3"
            CheckSnippet(ref passed, ref failed,
                name:        "ordered / OR group: both alts present, group1 after — match",
                html:        $"{W1} {W2} {W3}",
                groups:      new[] { new[] { W1, W2 }, new[] { W3 } },
                ordered:     true,
                expectMatch: true);

            // ─────────────────────────────────────────────────────────
            // 6. ORDERED MODE — nikud stripped (tokenizer normalisation)
            // ─────────────────────────────────────────────────────────

            // 6a. Text has nikud, query terms are bare → tokenizer strips nikud → match.
            CheckSnippet(ref passed, ref failed,
                name:        "ordered / nikud in text stripped by tokenizer — match",
                html:        "שָׁלוֹם עוֹלָם",
                groups:      G(W4, W6),   // שלום עולם
                ordered:     true,
                expectMatch: true);

            // 6b. Nikud in text, terms reversed → no match.
            CheckSnippet(ref passed, ref failed,
                name:        "ordered / nikud in text, terms reversed — no match",
                html:        "עוֹלָם שָׁלוֹם",
                groups:      G(W4, W6),   // query: שלום then עולם
                ordered:     true,
                expectMatch: false);

            // ─────────────────────────────────────────────────────────
            // 7. ORDERED vs UNORDERED — same content, different result
            // ─────────────────────────────────────────────────────────

            // 7a. Reversed terms: unordered matches, ordered does not.
            CheckSnippet(ref passed, ref failed,
                name:        "unordered matches reversed terms",
                html:        $"{W2} {W1}",
                groups:      G(W1, W2),
                ordered:     false,
                expectMatch: true);

            CheckSnippet(ref passed, ref failed,
                name:        "ordered rejects reversed terms",
                html:        $"{W2} {W1}",
                groups:      G(W1, W2),
                ordered:     true,
                expectMatch: false);

            // 7b. Correct order: both modes match.
            CheckSnippet(ref passed, ref failed,
                name:        "unordered matches correct order",
                html:        $"{W1} {W2}",
                groups:      G(W1, W2),
                ordered:     false,
                expectMatch: true);

            CheckSnippet(ref passed, ref failed,
                name:        "ordered matches correct order",
                html:        $"{W1} {W2}",
                groups:      G(W1, W2),
                ordered:     true,
                expectMatch: true);

            // ─────────────────────────────────────────────────────────
            // 8. ORDERED MODE — WordDistance filter still applies
            // ─────────────────────────────────────────────────────────

            // 8a. Terms in order and adjacent → IsMatch true, WordDistance small.
            CheckWordDistance(ref passed, ref failed,
                name:            "ordered / adjacent terms — WordDistance <= 1",
                html:            $"{W1} {W2}",
                groups:          G(W1, W2),
                ordered:         true,
                expectMatch:     true,
                maxExpectedDist: 1);

            // 8b. Terms in order but far apart → IsMatch true, WordDistance large.
            CheckWordDistance(ref passed, ref failed,
                name:            "ordered / distant terms — WordDistance > 1",
                html:            $"{W1} {WN} {WN} {WN} {W2}",
                groups:          G(W1, W2),
                ordered:         true,
                expectMatch:     true,
                minExpectedDist: 2);

            // ─────────────────────────────────────────────────────────
            // 9. ORDERED MODE — snippet HTML is produced on match
            // ─────────────────────────────────────────────────────────

            // 9a. Ordered match → Html contains highlight tags.
            CheckSnippetContainsHighlight(ref passed, ref failed,
                name:        "ordered match produces highlighted snippet",
                html:        $"{W4} {W5}",
                groups:      G(W4, W5),
                ordered:     true,
                mustContain: "<mark>");

            // 9b. Ordered no-match (reversed) → IsMatch=false.
            CheckSnippet(ref passed, ref failed,
                name:        "ordered no-match (reversed terms) — IsMatch false",
                html:        $"{W5} {W4}",
                groups:      G(W4, W5),
                ordered:     true,
                expectMatch: false);

            // ─────────────────────────────────────────────────────────
            // Summary
            // ─────────────────────────────────────────────────────────

            Console.WriteLine("║");
            string overall = failed == 0
                ? $"✓  All {passed} tests passed"
                : $"✗  {failed} FAILED  /  {passed + failed} total";
            Console.WriteLine($"║  {overall}");
            Console.WriteLine("╚══ ORDERED SEARCH TESTS DONE ══");
            Console.WriteLine();

            if (failed > 0)
                Environment.Exit(1);
        }

        // ── Helpers ───────────────────────────────────────────────────

        /// <summary>Shorthand: each string becomes a single-alternative group.</summary>
        private static IReadOnlyList<IReadOnlyCollection<string>> G(params string[] terms)
        {
            var groups = new List<IReadOnlyCollection<string>>(terms.Length);
            foreach (var t in terms)
                groups.Add(new[] { t });
            return groups;
        }

        // ── Assertion: IsMatch ────────────────────────────────────────

        private static void CheckSnippet(
            ref int                                    passed,
            ref int                                    failed,
            string                                     name,
            string                                     html,
            IReadOnlyList<IReadOnlyCollection<string>> groups,
            bool                                       ordered,
            bool                                       expectMatch)
        {
            var builder = new SnippetBuilder();
            var result  = builder.Build(html, groups, ordered);

            if (result.IsMatch == expectMatch)
            {
                passed++;
                Console.WriteLine($"║  ✓  {name}");
            }
            else
            {
                failed++;
                Console.WriteLine($"║  ✗  {name}");
                Console.WriteLine($"║       expected IsMatch={expectMatch}, got IsMatch={result.IsMatch}");
                Console.WriteLine($"║       html=\"{html}\"  ordered={ordered}");
                Console.WriteLine($"║       groups=[{FormatGroups(groups)}]");
            }
        }

        // ── Assertion: WordDistance bounds ────────────────────────────

        private static void CheckWordDistance(
            ref int                                    passed,
            ref int                                    failed,
            string                                     name,
            string                                     html,
            IReadOnlyList<IReadOnlyCollection<string>> groups,
            bool                                       ordered,
            bool                                       expectMatch,
            int                                        maxExpectedDist = int.MaxValue,
            int                                        minExpectedDist = 0)
        {
            var builder = new SnippetBuilder();
            var result  = builder.Build(html, groups, ordered);

            var errors = new List<string>();

            if (result.IsMatch != expectMatch)
                errors.Add($"IsMatch: expected {expectMatch}, got {result.IsMatch}");

            if (result.IsMatch)
            {
                if (result.WordDistance > maxExpectedDist)
                    errors.Add($"WordDistance {result.WordDistance} > max {maxExpectedDist}");
                if (result.WordDistance < minExpectedDist)
                    errors.Add($"WordDistance {result.WordDistance} < min {minExpectedDist}");
            }

            if (errors.Count == 0)
            {
                passed++;
                Console.WriteLine($"║  ✓  {name}  (WordDistance={result.WordDistance})");
            }
            else
            {
                failed++;
                Console.WriteLine($"║  ✗  {name}");
                foreach (var e in errors)
                    Console.WriteLine($"║       {e}");
            }
        }

        // ── Assertion: snippet HTML content ──────────────────────────

        private static void CheckSnippetContainsHighlight(
            ref int                                    passed,
            ref int                                    failed,
            string                                     name,
            string                                     html,
            IReadOnlyList<IReadOnlyCollection<string>> groups,
            bool                                       ordered,
            string                                     mustContain)
        {
            var builder = new SnippetBuilder();
            var result  = builder.Build(html, groups, ordered);

            bool ok     = result.IsMatch && result.Html.Contains(mustContain);
            string detail = $"IsMatch={result.IsMatch}, Html contains \"{mustContain}\"={result.Html.Contains(mustContain)}";

            if (ok)
            {
                passed++;
                Console.WriteLine($"║  ✓  {name}");
            }
            else
            {
                failed++;
                Console.WriteLine($"║  ✗  {name}");
                Console.WriteLine($"║       {detail}");
                Console.WriteLine($"║       Html=\"{result.Html}\"");
            }
        }

        // ── Formatting ────────────────────────────────────────────────

        private static string FormatGroups(IReadOnlyList<IReadOnlyCollection<string>> groups)
        {
            var parts = new List<string>(groups.Count);
            foreach (var g in groups)
                parts.Add("[" + string.Join("|", g) + "]");
            return string.Join(", ", parts);
        }
    }
}
