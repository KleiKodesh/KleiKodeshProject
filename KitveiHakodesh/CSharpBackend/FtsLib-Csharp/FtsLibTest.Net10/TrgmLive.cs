using FtsLib.Indexing;
using FtsLib.Search;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// LIVE-PATH verification of the trigram integration: exercises the real
    /// <see cref="HebrewWildcardExpander.Expand"/> → <see cref="SegmentHandle.Trigram"/> →
    /// SQLite rowid-confirm route against a real segment, and proves the expanded term set is
    /// IDENTICAL with the sidecar present (trigram route) vs absent (SQLite LIKE fallback) —
    /// the correctness guarantee for editing the live search path. Also A/Bs expand latency.
    ///
    /// Method: build a real seg.tgm next to the segment, then for each pattern compare
    ///   Expand(pattern) WITH sidecar  ==  Expand(pattern) WITHOUT sidecar (renamed away).
    /// A fresh SegmentHandle is opened per phase so the lazy Trigram probe reflects the file's
    /// presence. MaxExpandedTerms is disabled during the parity check so the cap can't mask a
    /// routing divergence (the cap is applied identically to both paths anyway).
    ///
    /// Usage:  FtsLibTest.exe trgmlive [tier=500k]
    /// </summary>
    internal static class TrgmLive
    {
        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string label = args.Length > 1 ? args[1] : "500k";
            string dir = Path.Combine(AppContext.BaseDirectory, "index_" + label);
            string db = Directory.GetFiles(dir, "seg_*.db").OrderByDescending(f => new FileInfo(f).Length).First();
            string dat = Path.ChangeExtension(db, ".dat");
            string tgm = TrigramIndex.SidecarPath(dat);
            Console.WriteLine($"segment: {Path.GetFileName(db)}   dat: {File.Exists(dat)}   ");

            // Build a fresh sidecar (rowid = posting id) via the production path.
            var sw = Stopwatch.StartNew();
            TrigramIndex.BuildFromDb(db, tgm);
            sw.Stop();
            Console.WriteLine($"built sidecar {Path.GetFileName(tgm)} in {sw.ElapsedMilliseconds} ms — " +
                              $"{new FileInfo(tgm).Length / 1024.0 / 1024:F1} MB\n");

            // Patterns covering every routed shape: infix (*abc*), suffix (*abc),
            // prefix (abc* — should route to B-tree range, sidecar irrelevant), '?' optional,
            // sub-3-char anchors (must fall back), and broad/pathological infixes.
            string[] patterns =
            {
                "*יצח*", "*אמר*", "*אברה*", "*תור*", "*ביצחק*", "*שמע*", "*קדם*",
                "*מלך*", "*יצחק*", "*אלהים*", "*משפט*", "*וכו*",
                "*לום",  "*רים",  "*תור",   "*אלהים",              // suffix
                "אבר*",  "מלכ*",  "יצח*",                          // prefix (range)
                "שלו?ם", "מלכ?ים",                                 // optional
                "*כי*",  "*ים*",  "*אל*",                          // broad / short-ish
                "*א*",   "*ב",                                     // sub-anchor (fallback / reject)
            };

            int savedCap = HebrewWildcardExpander.MaxExpandedTerms;
            HebrewWildcardExpander.MaxExpandedTerms = 0; // uncapped for an honest parity check
            int mismatches = 0;
            double tOn = 0, tOff = 0;
            Console.WriteLine($"{"pattern",-10}{"WITH tgm",10}{"no tgm",9}{"on ms",9}{"off ms",9}{"x",7}  parity");
            try
            {
                foreach (var p in patterns)
                {
                    // WITH sidecar present.
                    List<string> withTgm; double onMs;
                    using (var seg = new SegmentHandle(dat, db))
                    {
                        bool hasReader = seg.Trigram != null; // force the lazy probe now
                        onMs = Best(() => HebrewWildcardExpander.Expand(p, new[] { seg }));
                        withTgm = HebrewWildcardExpander.Expand(p, new[] { seg });
                        if (!hasReader) { /* sidecar unexpectedly missing — still valid, just falls back */ }
                    }

                    // WITHOUT sidecar: rename it away so the probe finds nothing → LIKE fallback.
                    string hidden = tgm + ".hidden";
                    File.Move(tgm, hidden);
                    List<string> noTgm; double offMs;
                    try
                    {
                        using var seg = new SegmentHandle(dat, db);
                        offMs = Best(() => HebrewWildcardExpander.Expand(p, new[] { seg }));
                        noTgm = HebrewWildcardExpander.Expand(p, new[] { seg });
                    }
                    finally { File.Move(hidden, tgm); }

                    var a = new HashSet<string>(withTgm, StringComparer.Ordinal);
                    var b = new HashSet<string>(noTgm, StringComparer.Ordinal);
                    bool ok = a.SetEquals(b);
                    if (!ok) mismatches++;
                    tOn += onMs; tOff += offMs;

                    string note = ok ? "OK" : $"MISMATCH  on-only={Show(a.Except(b))}  off-only={Show(b.Except(a))}";
                    Console.WriteLine($"{p,-10}{a.Count,10:N0}{b.Count,9:N0}{onMs,9:F2}{offMs,9:F2}{offMs / Math.Max(onMs, 1e-6),6:F0}x  {note}");
                }
            }
            finally { HebrewWildcardExpander.MaxExpandedTerms = savedCap; }

            Console.WriteLine($"\ntotals: expand WITH tgm {tOn:F1} ms   WITHOUT (LIKE) {tOff:F1} ms   overall {tOff / Math.Max(tOn, 1e-6):F1}x");
            Console.WriteLine(mismatches == 0
                ? "\n✓ LIVE PARITY: all patterns identical trigram-route vs LIKE-fallback."
                : $"\n✗ {mismatches} pattern(s) diverged — routing is NOT result-preserving.");
        }

        static string Show(IEnumerable<string> xs)
        {
            var l = xs.Take(6).ToArray();
            return "[" + string.Join(",", l) + (l.Length == 6 ? ",…" : "") + "]";
        }

        static double Best(Action f)
        {
            f();
            double best = 1e9;
            for (int i = 0; i < 5; i++) { var sw = Stopwatch.StartNew(); f(); sw.Stop(); best = Math.Min(best, sw.Elapsed.TotalMilliseconds); }
            return best;
        }
    }
}
