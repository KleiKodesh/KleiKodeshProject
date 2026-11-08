using FtsLib.Fts5;
using FtsLib.Snippets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FtsLibTest
{
    /// <summary>
    /// SnippetMatchMode.AllMatches and FindAllMatches (SeforimLibrarySearchEngine issues #14 /
    /// #23). Mirrors the Rust tests all_matches_* in hebrew-fts/src/snippet.rs.
    ///
    /// Usage:
    ///   FtsLibTest.exe allmatches
    /// </summary>
    internal static class AllMatchesTest
    {
        const string A = "אב", B = "גד", C = "הו";

        static int passed, failed;

        public static void Run(string[] args)
        {
            // Locate, AllMatches: whole text, every term token; best window unchanged.
            string line = $"{A} x x x x x x x x x x x x {B} {A} {B} y {A}";
            var prepared = PreparedQueryGroups.FromGroups(new List<IReadOnlyCollection<string>> { new[] { A }, new[] { B } });
            var best = new SnippetWindowLocator(1).Locate(line, prepared);
            var best2 = new SnippetWindowLocator(1).Locate(line, prepared, matchMode: SnippetMatchMode.BestWindow);
            Check("best window unchanged", best.SnapStart == best2.SnapStart && best.SnapEnd == best2.SnapEnd
                  && best.MarkRanges.SequenceEqual(best2.MarkRanges));
            var all = new SnippetWindowLocator(1).Locate(line, prepared, matchMode: SnippetMatchMode.AllMatches);
            Check("all: whole text", all.IsMatch && all.SnapStart == 0 && all.SnapEnd == line.Length
                  && !all.LeadingEllipsis && !all.TrailingEllipsis);
            Check("all: every token", Starts(all.MarkRanges).SequenceEqual(Occ(line, A, B)));
            Check("all: score as best", all.Score == best.Score && all.WordDistance == best.WordDistance);
            var none = new SnippetWindowLocator(1).Locate($"{A} only", prepared, matchMode: SnippetMatchMode.AllMatches);
            Check("all: failing line still fails", !none.IsMatch);

            // FindAllMatches: words.
            string t1 = $"{A} {B} {A} <b>{B}</b> {A}";
            var m = Fts5SeforimIndex.FindAllMatches(t1, new[] { A });
            Check("find: repeated word", Starts(m).SequenceEqual(new[] { 0, 6, 19 }) && m[1] == 2 && m[2] == 0);
            m = Fts5SeforimIndex.FindAllMatches(t1, new[] { A, B });
            Check("find: two words, tag", Starts(m).SequenceEqual(new[] { 0, 3, 6, 12, 19 }));
            Check("find: ordinals", Ord(m).SequenceEqual(new[] { 0, 1, 0, 1, 0 }));
            m = Fts5SeforimIndex.FindAllMatches(t1, new[] { $"{A} {B}" }, phrase: true);
            Check("find: phrase", Starts(m).SequenceEqual(new[] { 0, 3, 6, 12 }));
            m = Fts5SeforimIndex.FindAllMatches($"{A} {A} {A} {C}", new[] { A, A }, phrase: true);
            Check("find: overlapping phrase", Starts(m).SequenceEqual(new[] { 0, 3, 6 }));
            m = Fts5SeforimIndex.FindAllMatches($"{A} x {B}", new[] { $"{A} {B}" }, phrase: true);
            Check("find: phrase with a gap does not match", m.Length == 0);
            m = Fts5SeforimIndex.FindAllMatches($"({A}) [{C}] {B}", new[] { $"{C} {B}" }, phrase: true);
            Check("find: phrase over a paired reading", Ord(m).SequenceEqual(new[] { 0, 1 }) && m.Length == 6);
            Check("find: no words", Fts5SeforimIndex.FindAllMatches(t1, new string[0]).Length == 0);
            m = Fts5SeforimIndex.FindAllMatches($"{A}&nbsp;{B}", new[] { B });
            Check("find: entity", Starts(m).SequenceEqual(new[] { 8 }));
            m = Fts5SeforimIndex.FindAllMatches("אָב x", new[] { A });
            Check("find: marks ignored", Starts(m).SequenceEqual(new[] { 0 }) && m[1] == 3);
            string q = "אב\"ג";
            m = Fts5SeforimIndex.FindAllMatches($"x {q}", new[] { "אבג" });
            // FtsLib's snippet tokenizer drops quotes from a token's normalized form: matched anyway
            Check("find: quote-free token", Starts(m).SequenceEqual(new[] { 2 }));
            m = Fts5SeforimIndex.FindAllMatches($"x {q}", new[] { "אבג" }, ignoreQuotes: true);
            Check("find: quotes ignored", Starts(m).SequenceEqual(new[] { 2 }) && m[1] == 4);
            m = Fts5SeforimIndex.FindAllMatches("\U0001F600 " + A, new[] { A });
            Check("find: UTF-16 offsets", Starts(m).SequenceEqual(new[] { 3 }));

            Console.WriteLine($"allmatches: {passed} passed, {failed} failed");
            if (failed > 0) Environment.Exit(1);
        }

        static IEnumerable<int> Starts(IReadOnlyList<int> m) { for (int i = 0; i < m.Count; i += 3) yield return m[i]; }
        static IEnumerable<int> Ord(IReadOnlyList<int> m) { for (int i = 2; i < m.Count; i += 3) yield return m[i]; }

        static IEnumerable<int> Occ(string line, params string[] words)
        {
            int at = 0;
            foreach (var w in line.Split(' '))
            {
                if (words.Contains(w)) yield return at;
                at += w.Length + 1;
            }
        }

        static void Check(string name, bool ok)
        {
            if (ok) passed++; else { failed++; Console.WriteLine("FAIL " + name); }
        }
    }
}
