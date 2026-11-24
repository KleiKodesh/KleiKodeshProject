using FtsLib.Search;
using System;
using System.Collections.Generic;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Unit tests for <see cref="KetivExpander"/>.
    ///
    /// No index or database required — all assertions are purely in-memory.
    ///
    /// Usage:
    ///   FtsLibTest.exe ketivtest
    /// </summary>
    internal static class KetivExpanderTest
    {
        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine();
            Console.WriteLine("╔══ KETIV EXPANDER TESTS ══");

            int passed = 0, failed = 0;

            // ── 1. ששים is a variant of שישים ────────────────────────
            CheckContains(ref passed, ref failed,
                name:     "שישים → contains ששים",
                term:     "שישים",
                expected: "ששים");

            // ── 2. גברים is a variant of גבורים ─────────────────────
            CheckContains(ref passed, ref failed,
                name:     "גבורים → contains גברים",
                term:     "גבורים",
                expected: "גברים");

            // ── 3. שלחן is a variant of שולחן ────────────────────────
            CheckContains(ref passed, ref failed,
                name:     "שולחן → contains שלחן",
                term:     "שולחן",
                expected: "שלחן");

            // ── 4. Original term is NOT in the result list ────────────
            CheckNotContains(ref passed, ref failed,
                name:     "שישים → does not contain itself",
                term:     "שישים",
                excluded: "שישים");

            CheckNotContains(ref passed, ref failed,
                name:     "גבורים → does not contain itself",
                term:     "גבורים",
                excluded: "גבורים");

            // ── 5. Suffix ים is preserved ─────────────────────────────
            // תורה has suffix ה — bare skeleton is תר, variants include תורה (original excluded)
            // but the suffix must be reattached correctly.
            CheckContains(ref passed, ref failed,
                name:     "תורה → contains תרה (bare skeleton + suffix)",
                term:     "תורה",
                expected: "תרה");

            // ── 6. Short word (2 chars) — no expansion ────────────────
            CheckEmpty(ref passed, ref failed,
                name: "כי (2 chars) → no variants",
                term: "כי");

            // ── 7. Single char — no expansion ────────────────────────
            CheckEmpty(ref passed, ref failed,
                name: "א (1 char) → no variants",
                term: "א");

            // ── 8. Empty string — no expansion ───────────────────────
            CheckEmpty(ref passed, ref failed,
                name: "empty string → no variants",
                term: "");

            // ── 9. Word with ים suffix — suffix preserved ─────────────
            // גבורים: stem=גבור, suffix=ים → skeleton=גבר
            // variants include גברים (skeleton+suffix)
            CheckContains(ref passed, ref failed,
                name:     "גבורים → suffix ים preserved in variants",
                term:     "גבורים",
                expected: "גברים");

            // ── 10. Result count is capped at MaxVariants ─────────────
            // Use a long word with many gaps to trigger the cap.
            var longVariants = KetivExpander.Expand("אברהמיצחקיעקב");
            if (longVariants.Count <= KetivExpander.MaxVariants)
            {
                passed++;
                Console.WriteLine($"║  ✓  long word variants capped at MaxVariants ({longVariants.Count} ≤ {KetivExpander.MaxVariants})");
            }
            else
            {
                failed++;
                Console.WriteLine($"║  ✗  long word variants capped at MaxVariants");
                Console.WriteLine($"║       got {longVariants.Count}, max is {KetivExpander.MaxVariants}");
            }

            // ── 11. Live query test: ששים גבורים found via expansion ──
            // Simulate what SearchPipeline does: expand שישים and verify ששים is in the list,
            // expand גבורים and verify גברים is in the list.
            var shishimVariants = KetivExpander.Expand("שישים");
            var giborimVariants = KetivExpander.Expand("גבורים");

            bool shishimHasShasim = shishimVariants.Contains("ששים");
            bool giborimHasGvarim = giborimVariants.Contains("גברים");

            if (shishimHasShasim && giborimHasGvarim)
            {
                passed++;
                Console.WriteLine($"║  ✓  שישים→ששים and גבורים→גברים: pasuk שיר השירים reachable via expansion");
            }
            else
            {
                failed++;
                Console.WriteLine($"║  ✗  pasuk reachability check");
                if (!shishimHasShasim) Console.WriteLine($"║       שישים variants do not contain ששים: [{string.Join(", ", shishimVariants)}]");
                if (!giborimHasGvarim) Console.WriteLine($"║       גבורים variants do not contain גברים: [{string.Join(", ", giborimVariants)}]");
            }

            // ── Summary ───────────────────────────────────────────────
            Console.WriteLine("║");
            string overall = failed == 0
                ? $"✓  All {passed} tests passed"
                : $"✗  {failed} FAILED  /  {passed + failed} total";
            Console.WriteLine($"║  {overall}");
            Console.WriteLine("╚══ KETIV EXPANDER TESTS DONE ══");
            Console.WriteLine();

            if (failed > 0)
                Environment.Exit(1);
        }

        // ── Helpers ───────────────────────────────────────────────────

        private static void CheckContains(ref int passed, ref int failed,
            string name, string term, string expected)
        {
            var variants = KetivExpander.Expand(term);
            if (variants.Contains(expected))
            {
                passed++;
                Console.WriteLine($"║  ✓  {name}");
            }
            else
            {
                failed++;
                Console.WriteLine($"║  ✗  {name}");
                Console.WriteLine($"║       expected \"{expected}\" in variants of \"{term}\"");
                Console.WriteLine($"║       got: [{string.Join(", ", variants)}]");
            }
        }

        private static void CheckNotContains(ref int passed, ref int failed,
            string name, string term, string excluded)
        {
            var variants = KetivExpander.Expand(term);
            if (!variants.Contains(excluded))
            {
                passed++;
                Console.WriteLine($"║  ✓  {name}");
            }
            else
            {
                failed++;
                Console.WriteLine($"║  ✗  {name}");
                Console.WriteLine($"║       \"{excluded}\" should NOT be in variants of \"{term}\"");
            }
        }

        private static void CheckEmpty(ref int passed, ref int failed,
            string name, string term)
        {
            var variants = KetivExpander.Expand(term);
            if (variants.Count == 0)
            {
                passed++;
                Console.WriteLine($"║  ✓  {name}  (0 variants as expected)");
            }
            else
            {
                failed++;
                Console.WriteLine($"║  ✗  {name}");
                Console.WriteLine($"║       expected 0 variants, got {variants.Count}: [{string.Join(", ", variants)}]");
            }
        }
    }
}
