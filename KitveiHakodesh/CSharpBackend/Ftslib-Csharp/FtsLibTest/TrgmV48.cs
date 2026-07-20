using FtsLib.Search;                 // production TrigramIndex (net48; internal, visible to FtsLibTest)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// net48 verification of the production <see cref="TrigramIndex"/> (FtsLib.Net48/Search):
    ///   1. Build a sidecar from a segment DB via the net48 BuildFromDb.
    ///   2. Round-trip every trigram through the net48 FileStream Reader against an independent
    ///      oracle (0 mismatches).
    ///   3. A/B the net48 on-disk reader vs System.Data.SQLite `LIKE '%q%'` — identical results.
    ///   4. Cross-runtime byte-compare: if a net10-built sidecar for the SAME db exists
    ///      (built by trgmlive's BuildFromDb, rowid ids), assert the files are BYTE-IDENTICAL —
    ///      proving a sidecar written by the service (net10) is readable by the DemoApp (net48).
    ///
    /// Usage:  FtsLibTest.exe trgmv48 &lt;segdb path&gt; [net10-built .tgm to byte-compare]
    /// The db path is explicit because the net48 test has no bundled index; point it at the
    /// net10 bin's index_500k\seg_1_4.db.
    /// </summary>
    internal static class TrgmV48
    {
        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length < 2) { Console.WriteLine("usage: trgmv48 <segdb> [net10.tgm]"); return; }
            string db = args[1];
            string outPath = Path.Combine(Path.GetDirectoryName(db), "seg_net48.tgm");
            Console.WriteLine("segment: " + db);

            var terms = new List<string>(1 << 20);
            var ids = new List<int>(1 << 20);
            using (var c = new System.Data.SQLite.SQLiteConnection(
                string.Format("Data Source={0};Version=3;Read Only=True;", db)))
            {
                c.Open();
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "SELECT rowid, term FROM term_index ORDER BY rowid";
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) { ids.Add((int)r.GetInt64(0)); terms.Add(r.GetString(1)); }
                }
            }
            Console.WriteLine("terms: " + terms.Count.ToString("N0"));

            // independent oracle keyed by rowid (matches BuildFromDb's id assignment)
            var oracle = new Dictionary<string, List<int>>(1 << 16, StringComparer.Ordinal);
            var grams = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < terms.Count; i++)
            {
                if (terms[i].Length < TrigramIndex.MinRun) continue;
                grams.Clear(); seen.Clear(); TrigramIndex.AddTrigrams(terms[i], grams, seen);
                foreach (var g in grams) { if (!oracle.TryGetValue(g, out var l)) { l = new List<int>(); oracle[g] = l; } l.Add(ids[i]); }
            }

            var sw = Stopwatch.StartNew(); TrigramIndex.BuildFromDb(db, outPath); sw.Stop();
            Console.WriteLine("built " + Path.GetFileName(outPath) + " in " + sw.ElapsedMilliseconds + " ms — " +
                              (new FileInfo(outPath).Length / 1024.0 / 1024).ToString("F1") + " MB (" +
                              oracle.Count.ToString("N0") + " trigrams)\n");

            int bad = 0;
            using (var reader = new TrigramIndex.Reader(outPath))
                foreach (var kv in oracle)
                    if (!reader.Lookup(kv.Key).SequenceEqual(kv.Value)) bad++;
            Console.WriteLine("round-trip: " + oracle.Count.ToString("N0") + " trigrams, " + bad +
                              " mismatches" + (bad == 0 ? "  OK" : "  FAIL"));

            // A/B vs SQLite LIKE (correctness of the live confirm route on net48)
            string[] qs = { "יצח", "אמר", "אברה", "תור", "ביצחק", "שמע", "מלך", "משפט" };
            using (var conn = new System.Data.SQLite.SQLiteConnection(
                       string.Format("Data Source={0};Version=3;Read Only=True;", db)))
            using (var reader = new TrigramIndex.Reader(outPath))
            {
                conn.Open();
                var likeCmd = conn.CreateCommand();
                likeCmd.CommandText = "SELECT term FROM term_index WHERE term LIKE '%'||@q||'%' ESCAPE '\\'";
                var pq = likeCmd.CreateParameter(); pq.ParameterName = "@q"; likeCmd.Parameters.Add(pq);
                int q_bad = 0;
                foreach (var q in qs)
                {
                    var sset = new HashSet<string>(StringComparer.Ordinal);
                    pq.Value = q; using (var r = likeCmd.ExecuteReader()) while (r.Read()) sset.Add(r.GetString(0));
                    var tset = new HashSet<string>(StringComparer.Ordinal);
                    Search(q, reader, db, tset);
                    bool ok = sset.SetEquals(tset); if (!ok) q_bad++;
                    Console.WriteLine(q.PadRight(8) + sset.Count.ToString("N0").PadLeft(8) + "  " + (ok ? "OK" : "MISMATCH " + sset.Count + "/" + tset.Count));
                }
                Console.WriteLine("query parity: " + (q_bad == 0 ? "all OK" : q_bad + " mismatch"));
            }

            // Fuzzy routing parity: real net48 FuzzyExpander with sidecar present (routes through
            // the sidecar) vs sidecar renamed away (scans) — must be identical.
            {
                FtsLib.Search.FuzzyExpander.MaxExpandedTerms = 0;
                string dat = Path.ChangeExtension(db, ".dat");
                string tgm = TrigramIndex.SidecarPath(dat);
                // outPath is seg_net48.tgm; the expander looks for <dat>.tgm — build it there.
                if (File.Exists(dat)) TrigramIndex.BuildFromDb(db, tgm);
                string[] fz = { "יצחק", "תורה", "יסראל", "אברהם" };
                int f_bad = 0;
                foreach (var term in fz)
                {
                    List<string> withT, noT;
                    using (var seg = new FtsLib.Indexing.SegmentHandle(dat, db))
                        withT = FtsLib.Search.FuzzyExpander.Expand(term, 2, new[] { seg });
                    string hidden = tgm + ".hidden"; bool moved = File.Exists(tgm);
                    if (moved) File.Move(tgm, hidden);
                    try { using (var seg = new FtsLib.Indexing.SegmentHandle(dat, db)) noT = FtsLib.Search.FuzzyExpander.Expand(term, 2, new[] { seg }); }
                    finally { if (moved) File.Move(hidden, tgm); }
                    bool ok = new HashSet<string>(withT, StringComparer.Ordinal).SetEquals(new HashSet<string>(noT, StringComparer.Ordinal));
                    if (!ok) f_bad++;
                    Console.WriteLine("fuzzy " + term.PadRight(7) + withT.Count.ToString("N0").PadLeft(6) + "  " + (ok ? "OK" : "MISMATCH " + withT.Count + "/" + noT.Count));
                }
                Console.WriteLine("fuzzy parity: " + (f_bad == 0 ? "all OK" : f_bad + " mismatch"));
                if (File.Exists(tgm)) File.Delete(tgm);
            }

            // Cross-runtime byte-compare
            if (args.Length > 2 && File.Exists(args[2]))
            {
                byte[] a = File.ReadAllBytes(outPath), b = File.ReadAllBytes(args[2]);
                bool same = a.Length == b.Length && a.SequenceEqual(b);
                Console.WriteLine("\ncross-runtime byte-compare vs " + Path.GetFileName(args[2]) + ": " +
                                  (same ? "BYTE-IDENTICAL ✓ (net48 build == net10 build)"
                                        : "DIFFER (net48 " + a.Length + " B vs net10 " + b.Length + " B)"));
            }
        }

        // maps candidate rowids back to terms to confirm — the confirm here mirrors what
        // HebrewWildcardExpander does (SQLite LIKE on the candidate rowids).
        static void Search(string q, TrigramIndex.Reader reader, string db, HashSet<string> outset)
        {
            if (q.Length < TrigramIndex.MinRun) return;
            var grams = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            TrigramIndex.AddTrigrams(q, grams, seen);
            int[] acc = null;
            foreach (var g in grams) { var l = reader.Lookup(g); if (l.Length == 0) { acc = new int[0]; break; } acc = acc == null ? l : Inter(acc, l); if (acc.Length == 0) break; }
            if (acc == null || acc.Length == 0) return;
            using (var conn = new System.Data.SQLite.SQLiteConnection(
                       string.Format("Data Source={0};Version=3;Read Only=True;", db)))
            {
                conn.Open();
                var sb = new StringBuilder("SELECT term FROM term_index WHERE rowid IN (");
                for (int i = 0; i < acc.Length; i++) { if (i > 0) sb.Append(','); sb.Append(acc[i]); }
                sb.Append(") AND term LIKE '%'||@q||'%' ESCAPE '\\'");
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sb.ToString();
                    var p = cmd.CreateParameter(); p.ParameterName = "@q"; p.Value = q; cmd.Parameters.Add(p);
                    using (var r = cmd.ExecuteReader()) while (r.Read()) outset.Add(r.GetString(0));
                }
            }
        }

        static int[] Inter(int[] a, int[] b)
        {
            var r = new List<int>(Math.Min(a.Length, b.Length)); int i = 0, j = 0;
            while (i < a.Length && j < b.Length) { int x = a[i], y = b[j]; if (x == y) { r.Add(x); i++; j++; } else if (x < y) i++; else j++; }
            return r.ToArray();
        }
    }
}
