using FtsLib.Snippets;
using System;
using System.Collections.Generic;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Issue #4: with a word-distance filter, the marks are only the tokens of full
    /// occurrences (covering windows within the distance) inside the snippet window; a
    /// query word elsewhere in the context is not marked. Mirrors the Rust tests
    /// issue4_* in hebrew-fts/src/snippet.rs (SeforimdbSerachEngine).
    ///
    /// Usage:
    ///   FtsLibTest.exe occmarks
    /// </summary>
    internal static class OccurrenceMarksTest
    {
        const string Moshe  = "משה";
        const string Aharon = "ואהרן";
        const string A = "אב", B = "גד", C = "הו";

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            int passed = 0, failed = 0;

            string line = Moshe + " " + Aharon + " אמרו שלום "
                        + "ואחר כך " + Moshe + " כהן הלך";
            var issue = Groups(new[] { Moshe }, new[] { Aharon });
            foreach (int ctx in new[] { 0, 8 })
            {
                Check(ref passed, ref failed, $"issue line, distance 0, context {ctx}",
                      line, issue, ctx, 0, new[] { Moshe, Aharon });
                Check(ref passed, ref failed, $"issue line, no distance, context {ctx}",
                      line, issue, ctx, int.MaxValue,
                      ctx == 0 ? new[] { Moshe, Aharon } : new[] { Moshe, Aharon, Moshe });
            }
            Check(ref passed, ref failed, "repeated phrase: both occurrences marked",
                  $"{A} {B} x {A} {B} y {A}", Groups(new[] { A }, new[] { B }), 8, 0, new[] { A, B, A, B });
            Check(ref passed, ref failed, "an alternative completing no occurrence stays unmarked",
                  $"{A} {B} x y {C} z w {B}", Groups(new[] { A, C }, new[] { B }), 8, 0, new[] { A, B });

            // In order: the window and the marks follow the query order ("B A B", query
            // A B: the ordered "A B", not the equally tight, earlier "B A").
            string ab = "\u05D0\u05D1", gd = "\u05D2\u05D3";
            var inOrder = Groups(new[] { ab }, new[] { gd });
            foreach (int ctx in new[] { 0, 8 })
                foreach (int dist in new[] { int.MaxValue, 0, 5 })
                    Check(ref passed, ref failed, $"in order, context {ctx}, distance {dist}",
                          $"{gd} {ab} {gd}", inOrder, ctx, dist, new[] { ab, gd }, ordered: true, firstMark: 3);
            Check(ref passed, ref failed, "order off: unchanged",
                  $"{gd} {ab} {gd}", inOrder, 8, int.MaxValue, new[] { gd, ab, gd }, firstMark: 0);
            Check(ref passed, ref failed, "in order, issue line: the ordered occurrence only",
                  line, issue, 8, int.MaxValue, new[] { Moshe, Aharon }, ordered: true);
            {
                var d = new SnippetWindowLocator(8).Locate($"{gd} {ab}", PreparedQueryGroups.FromGroups(inOrder), true);
                bool ok = !d.IsMatch;
                if (ok) passed++; else failed++;
                Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  in order, reversed only: no match");
            }

            Console.WriteLine($"occurrence marks: {passed} passed, {failed} failed");
            if (failed > 0) Environment.Exit(1);
        }

        static IReadOnlyList<IReadOnlyCollection<string>> Groups(params string[][] groups)
        {
            var list = new List<IReadOnlyCollection<string>>();
            foreach (var g in groups) list.Add(g);
            return list;
        }

        static void Check(ref int passed, ref int failed, string name, string text,
                          IReadOnlyList<IReadOnlyCollection<string>> groups, int context,
                          int maxWordDistance, string[] expected, bool ordered = false, int firstMark = -1)
        {
            var locator = new SnippetWindowLocator(context);
            var d = locator.Locate(text, PreparedQueryGroups.FromGroups(groups), ordered, 0, maxWordDistance);
            var got = new List<string>();
            for (int i = 0; i + 2 < d.MarkRanges.Count; i += SnippetDescriptor.MarkStride)
                got.Add(text.Substring(d.MarkRanges[i], d.MarkRanges[i + 1]));
            bool ok = d.IsMatch && string.Join("|", got) == string.Join("|", expected)
                      && (firstMark < 0 || (d.MarkRanges.Count > 0 && d.MarkRanges[0] == firstMark));
            if (ok) passed++; else failed++;
            // marks are counted, not printed (Hebrew stays out of the console)
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}  (marks {got.Count}, expected {expected.Length})");
        }
    }
}
