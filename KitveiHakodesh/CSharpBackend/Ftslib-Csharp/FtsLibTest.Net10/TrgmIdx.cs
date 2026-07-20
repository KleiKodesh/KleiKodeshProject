using FtsLib.Search;                 // production TrigramIndex (internal, visible to FtsLibTest)
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// End-to-end verification of the PRODUCTION <see cref="TrigramIndex"/> (FtsLib/Search):
    /// build the sidecar over a segment, exhaustively round-trip every trigram against an
    /// independent oracle map, then A/B the on-disk reader vs SQLite `LIKE '%q%'` (identical
    /// results + speedup). Confirms compactness, correctness, and speed of the lifted class.
    ///
    /// Usage:  FtsLibTest.exe trgmidx [tier=500k]
    /// </summary>
    internal static class TrgmIdx
    {
        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string label = args.Length > 1 ? args[1] : "500k";
            string dir = Path.Combine(AppContext.BaseDirectory, "index_" + label);
            string db = Directory.GetFiles(dir, "seg_*.db").OrderByDescending(f => new FileInfo(f).Length).First();
            string outPath = Path.Combine(dir, "trigram.tgm");
            Console.WriteLine($"segment: {db}");

            var terms = new List<string>(1 << 20);
            using (var c = Open(db)) { var cmd = c.CreateCommand(); cmd.CommandText = "SELECT term FROM term_index ORDER BY rowid"; using var r = cmd.ExecuteReader(); while (r.Read()) terms.Add(r.GetString(0)); }
            Console.WriteLine($"terms: {terms.Count:N0}");

            // independent oracle: trigram -> sorted list indices
            var oracle = new Dictionary<string, List<int>>(1 << 16, StringComparer.Ordinal);
            var grams = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int id = 0; id < terms.Count; id++)
            {
                if (terms[id].Length < TrigramIndex.MinRun) continue;
                grams.Clear(); seen.Clear(); TrigramIndex.AddTrigrams(terms[id], grams, seen);
                foreach (var g in grams) { if (!oracle.TryGetValue(g, out var l)) { l = new List<int>(); oracle[g] = l; } l.Add(id); }
            }

            var sw = Stopwatch.StartNew(); TrigramIndex.Build(outPath, terms); sw.Stop();
            long sz = new FileInfo(outPath).Length, segSz = new FileInfo(db).Length;
            Console.WriteLine($"built {Path.GetFileName(outPath)} in {sw.ElapsedMilliseconds} ms — {sz / 1024.0 / 1024:F1} MB ({oracle.Count:N0} trigrams) vs term_index .db {segSz / 1024.0 / 1024:F0} MB\n");

            using var reader = new TrigramIndex.Reader(outPath);
            int bad = 0;
            foreach (var kv in oracle) if (!reader.Lookup(kv.Key).SequenceEqual(kv.Value)) bad++;
            Console.WriteLine($"round-trip: {oracle.Count:N0} trigrams, {bad} mismatches" + (bad == 0 ? "  ✓ ALL OK" : "  ✗"));

            string[] qs = { "יצח", "אמר", "אברה", "תור", "ביצחק", "שמע", "קדם", "וכו", "מלך", "יצחק", "אלהים", "משפט" };
            using var conn = Open(db);
            var likeCmd = conn.CreateCommand(); likeCmd.CommandText = "SELECT term FROM term_index WHERE term LIKE '%'||@q||'%' ESCAPE '\\'";
            var pq = likeCmd.CreateParameter(); pq.ParameterName = "@q"; likeCmd.Parameters.Add(pq);
            Console.WriteLine($"\n{"query",-9}{"matches",9}{"sqlite ms",11}{"ondisk ms",11}{"speedup",9}  correct");
            double ts = 0, to = 0;
            foreach (var q in qs)
            {
                var sset = new HashSet<string>(StringComparer.Ordinal);
                double sqlMs = Best(() => { sset.Clear(); pq.Value = q; using var r = likeCmd.ExecuteReader(); while (r.Read()) sset.Add(r.GetString(0)); });
                var tset = new HashSet<string>(StringComparer.Ordinal);
                double triMs = Best(() => { tset.Clear(); Search(q, reader, terms, tset); });
                bool ok = sset.SetEquals(tset); ts += sqlMs; to += triMs;
                Console.WriteLine($"{q,-9}{sset.Count,9:N0}{sqlMs,11:F2}{triMs,11:F3}{sqlMs / triMs,8:F0}x  {(ok ? "OK" : "MISMATCH " + sset.Count + "/" + tset.Count)}");
            }
            Console.WriteLine($"\ntotals: sqlite {ts:F1} ms  on-disk trigram {to:F2} ms  overall {ts / to:F0}x");
        }

        static void Search(string q, TrigramIndex.Reader reader, List<string> terms, HashSet<string> outset)
        {
            if (q.Length < TrigramIndex.MinRun) return;              // routing: too short → SQLite
            var grams = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            TrigramIndex.AddTrigrams(q, grams, seen);
            var lists = new List<int[]>(grams.Count);
            foreach (var g in grams) { var l = reader.Lookup(g); if (l.Length == 0) return; lists.Add(l); }
            lists.Sort((a, b) => a.Length.CompareTo(b.Length));      // drive intersection off the rarest trigram
            int[] acc = lists[0];
            for (int k = 1; k < lists.Count; k++) acc = Inter(acc, lists[k]);
            foreach (int id in acc) if (terms[id].IndexOf(q, StringComparison.Ordinal) >= 0) outset.Add(terms[id]);
        }

        static int[] Inter(int[] a, int[] b)
        {
            var r = new List<int>(Math.Min(a.Length, b.Length)); int i = 0, j = 0;
            while (i < a.Length && j < b.Length) { int x = a[i], y = b[j]; if (x == y) { r.Add(x); i++; j++; } else if (x < y) i++; else j++; }
            return r.ToArray();
        }

        static double Best(Action f) { f(); double best = 1e9; for (int i = 0; i < 5; i++) { var sw = Stopwatch.StartNew(); f(); sw.Stop(); best = Math.Min(best, sw.Elapsed.TotalMilliseconds); } return best; }
        static SqliteConnection Open(string p) { var c = new SqliteConnection($"Data Source={p};Mode=ReadOnly;Cache=Shared"); c.Open(); return c; }
    }
}
