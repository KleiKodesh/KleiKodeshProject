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
    /// FULL-TIER expansion A/B for the trigram sidecar. Runs the wildcard + fuzzy EXPANSION
    /// (Phase A only — the part trigrams accelerate) across a real multi-segment index, twice:
    /// once with NO sidecars (baseline SQLite LIKE scan) and once with sidecars built via
    /// BuildFromDb, asserting the expanded term SET is identical and reporting per-case speedup.
    /// No ZayitDb / SeforimIndex needed — this isolates the expand cost on the segments in place.
    ///
    /// This is where the "drive intersection off the rarest trigram" + TrigramCandidateCap guards
    /// get stressed: on the full index the common trigrams have far larger posting lists than the
    /// 500k sample. NOTE (measured gap, see fuzzy rows): FuzzyExpander still uses its own
    /// LIKE '%ngram%' OR-scan and is NOT yet wired to the sidecar — fuzzy rows are expected to
    /// show ~1x here; only wildcard rows exercise the sidecar today.
    ///
    /// Usage:  FtsLibTest.exe trgmfull &lt;index dir&gt;
    ///         (e.g. ...\FtsLibTest\bin\Release\index_full)
    /// </summary>
    internal static class TrgmFull
    {
        sealed class Case { public string Label, Query; public int Fuzzy; public Case(string l, string q, int f = 0) { Label = l; Query = q; Fuzzy = f; } }

        static readonly Case[] Cases =
        {
            new Case("wild suffix",        "*ישראל"),
            new Case("wild infix",         "*אבר*"),
            new Case("wild infix common",  "*תור*"),
            new Case("wild infix broad",   "*אל*"),      // large candidate set → cap guard
            new Case("wild infix path",    "*כי*"),      // 2-char anchor → cannot trigram → scan
            new Case("wild suffix long",   "*הוריות"),
            new Case("wild infix rare",    "*שויתי*"),
            new Case("wild prefix",        "תורה*"),     // B-tree range — sidecar irrelevant, ~1x
            new Case("fuzzy d1",           "יצחק",  1),
            new Case("fuzzy d1 common",    "תורה",  1),
            new Case("fuzzy d2",           "יסראל", 2),
            new Case("fuzzy d1 3-letter",  "אנב",   1),
        };

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length < 2) { Console.WriteLine("usage: trgmfull <index dir>"); return; }
            string dir = args[1];
            var dats = Directory.GetFiles(dir, "seg_*.dat"); Array.Sort(dats);
            if (dats.Length == 0) { Console.WriteLine("no segments in " + dir); return; }
            Console.WriteLine($"index: {dir}   segments: {dats.Length}");

            // Remove any stale sidecars so the baseline truly scans.
            foreach (var d in dats) { string t = TrigramIndex.SidecarPath(d); if (File.Exists(t)) File.Delete(t); }

            HebrewWildcardExpander.MaxExpandedTerms = 0;   // uncapped so parity can't be masked
            FuzzyExpander.MaxExpandedTerms = 0;

            // ── Phase 1: baseline (no sidecars) ──
            Console.WriteLine("\n── baseline (no sidecar) ──");
            var baseRes = new Dictionary<string, HashSet<string>>();
            var baseMs = new Dictionary<string, double>();
            RunAll(dir, dats, baseRes, baseMs);

            // ── Build sidecars (as ForceMerger would, per segment) ──
            Console.WriteLine("\n── building sidecars (BuildFromDb per segment) ──");
            long totalTgm = 0; var swb = Stopwatch.StartNew();
            foreach (var d in dats)
            {
                string db = Path.ChangeExtension(d, ".db");
                string t = TrigramIndex.SidecarPath(d);
                TrigramIndex.BuildFromDb(db, t);
                long sz = new FileInfo(t).Length; totalTgm += sz;
                Console.WriteLine($"  {Path.GetFileName(t)}: {sz / 1024.0 / 1024:F1} MB  (db {new FileInfo(db).Length / 1024.0 / 1024:F0} MB)");
            }
            swb.Stop();
            Console.WriteLine($"  built {dats.Length} sidecars in {swb.ElapsedMilliseconds} ms, {totalTgm / 1024.0 / 1024:F1} MB total");

            // ── Phase 2: with sidecars ──
            Console.WriteLine("\n── with sidecar ──");
            var idxRes = new Dictionary<string, HashSet<string>>();
            var idxMs = new Dictionary<string, double>();
            RunAll(dir, dats, idxRes, idxMs);

            // ── Report ──
            Console.WriteLine($"\n{"case",-18}{"terms",8}{"base ms",10}{"tgm ms",10}{"speedup",9}  parity");
            int mm = 0; double tb = 0, ti = 0;
            foreach (var c in Cases)
            {
                var a = baseRes[c.Label]; var b = idxRes[c.Label];
                bool ok = a.SetEquals(b); if (!ok) mm++;
                double mb = baseMs[c.Label], mi = idxMs[c.Label]; tb += mb; ti += mi;
                string note = ok ? "OK" : $"MISMATCH {a.Count}/{b.Count}";
                Console.WriteLine($"{c.Label,-18}{a.Count,8:N0}{mb,10:F2}{mi,10:F2}{mb / Math.Max(mi, 1e-6),8:F0}x  {note}");
            }
            Console.WriteLine($"\ntotals: baseline {tb:F0} ms   sidecar {ti:F0} ms   overall {tb / Math.Max(ti, 1e-6):F1}x");
            Console.WriteLine(mm == 0
                ? "\n✓ FULL-TIER PARITY: every case identical baseline vs sidecar."
                : $"\n✗ {mm} case(s) diverged.");

            // clean up sidecars we created (leave the tree as we found it)
            foreach (var d in dats) { string t = TrigramIndex.SidecarPath(d); if (File.Exists(t)) File.Delete(t); }
            Console.WriteLine("(sidecars removed — index dir restored)");
        }

        static void RunAll(string dir, string[] dats,
                           Dictionary<string, HashSet<string>> outRes, Dictionary<string, double> outMs)
        {
            foreach (var c in Cases)
            {
                // Fresh handles each case so the lazy Trigram probe reflects current file state.
                Func<List<string>> run = () =>
                {
                    var segs = new List<SegmentHandle>();
                    foreach (var d in dats) { string db = Path.ChangeExtension(d, ".db"); if (File.Exists(db)) segs.Add(new SegmentHandle(d, db)); }
                    try
                    {
                        return c.Fuzzy > 0
                            ? FuzzyExpander.Expand(c.Query, c.Fuzzy, segs)
                            : HebrewWildcardExpander.Expand(c.Query, segs);
                    }
                    finally { foreach (var s in segs) s.Dispose(); }
                };
                var res = run();
                double best = 1e9;
                for (int i = 0; i < 3; i++) { var sw = Stopwatch.StartNew(); run(); sw.Stop(); best = Math.Min(best, sw.Elapsed.TotalMilliseconds); }
                outRes[c.Label] = new HashSet<string>(res, StringComparer.Ordinal);
                outMs[c.Label] = best;
                Console.WriteLine($"  {c.Label,-18} {res.Count,7:N0} terms  {best,8:F2} ms");
            }
        }
    }
}
