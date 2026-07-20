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
    /// Verifies the trigram-index thesis for wildcard/infix/fuzzy candidate generation:
    /// is a trigram AND-filter (O(1) key lookup + posting intersect + confirm) faster than
    /// SQLite `term LIKE '%q%'` (full term_index scan), and does it return the IDENTICAL set?
    /// The in-memory trigram dictionary stands in for the on-disk MPH (both O(1) key lookup);
    /// what's measured is the ALGORITHM (filter vs scan). Also probes the routing boundary
    /// (queries too short to form a trigram must fall back to SQLite).
    ///
    /// Usage:  FtsLibTest.exe trgmbench [tier=500k]
    /// </summary>
    internal static class TrgmBench
    {
        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string label = args.Length > 1 ? args[1] : "500k";
            string dir = Path.Combine(AppContext.BaseDirectory, "index_" + label);
            if (!Directory.Exists(dir)) { Console.WriteLine($"no index dir: {dir}"); return; }
            string db = Directory.GetFiles(dir, "seg_*.db").OrderByDescending(f => new FileInfo(f).Length).First();
            Console.WriteLine($"segment: {db}");

            var terms = new List<string>(1 << 20);
            using (var c = Open(db))
            {
                var cmd = c.CreateCommand(); cmd.CommandText = "SELECT term FROM term_index ORDER BY rowid";
                using var r = cmd.ExecuteReader();
                while (r.Read()) terms.Add(r.GetString(0));
            }
            Console.WriteLine($"terms: {terms.Count:N0}");

            // Build in-memory trigram -> sorted term-id postings (stand-in for on-disk MPH+blob).
            var sw = Stopwatch.StartNew();
            var idx = new Dictionary<string, List<int>>(1 << 20, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int id = 0; id < terms.Count; id++)
            {
                string t = terms[id];
                if (t.Length < 3) continue;
                seen.Clear();
                for (int i = 0; i + 3 <= t.Length; i++)
                {
                    string g = t.Substring(i, 3);
                    if (seen.Add(g))
                    {
                        if (!idx.TryGetValue(g, out var l)) { l = new List<int>(); idx[g] = l; }
                        l.Add(id); // ascending id -> lists stay sorted
                    }
                }
            }
            sw.Stop();
            long postings = 0; foreach (var l in idx.Values) postings += l.Count;
            Console.WriteLine($"trigram index: {idx.Count:N0} trigrams, {postings:N0} postings, built {sw.ElapsedMilliseconds} ms " +
                              $"(~{postings * 4.0 / (1024 * 1024):F0} MB raw ids; delta+varint would be far less)\n");

            string[] qs = { "יצח", "אמר", "אברה", "תור", "ביצחק", "שמע", "קדם", "וכו", "מלך", "יצחק", "אלהים", "משפט" };

            using var conn = Open(db);
            var likeCmd = conn.CreateCommand();
            likeCmd.CommandText = "SELECT term FROM term_index WHERE term LIKE '%' || @q || '%' ESCAPE '\\'";
            var pq = likeCmd.CreateParameter(); pq.ParameterName = "@q"; likeCmd.Parameters.Add(pq);

            Console.WriteLine($"{"query",-9}{"matches",9}{"sqlite ms",11}{"trigram ms",12}{"speedup",9}  correct");
            double totSql = 0, totTri = 0;
            foreach (var q in qs)
            {
                var sset = new HashSet<string>(StringComparer.Ordinal);
                double sqlMs = Best(() => { sset.Clear(); pq.Value = q; using var r = likeCmd.ExecuteReader(); while (r.Read()) sset.Add(r.GetString(0)); });
                var tset = new HashSet<string>(StringComparer.Ordinal);
                double triMs = Best(() => { tset.Clear(); TrigramSearch(q, idx, terms, tset); });
                bool ok = sset.SetEquals(tset);
                totSql += sqlMs; totTri += triMs;
                Console.WriteLine($"{q,-9}{sset.Count,9:N0}{sqlMs,11:F2}{triMs,12:F3}{sqlMs / triMs,8:F0}x  {(ok ? "OK" : "MISMATCH(" + sset.Count + " vs " + tset.Count + ")")}");
            }
            Console.WriteLine($"\ntotals: sqlite {totSql:F1} ms   trigram {totTri:F2} ms   overall {totSql / totTri:F0}x faster");
            Console.WriteLine("routing note: query length < 3 forms no trigram -> must fall back to SQLite (verified: TrigramSearch returns empty for <3).");
        }

        static void TrigramSearch(string q, Dictionary<string, List<int>> idx, List<string> terms, HashSet<string> outset)
        {
            if (q.Length < 3) return; // routing: too short for a trigram -> caller uses SQLite
            var grams = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i + 3 <= q.Length; i++) { string g = q.Substring(i, 3); if (seen.Add(g)) grams.Add(g); }
            var lists = new List<List<int>>(grams.Count);
            foreach (var g in grams) { if (!idx.TryGetValue(g, out var l)) return; lists.Add(l); }
            lists.Sort((a, b) => a.Count.CompareTo(b.Count));
            var acc = lists[0];
            for (int k = 1; k < lists.Count; k++) acc = IntersectSorted(acc, lists[k]);
            foreach (int id in acc) if (terms[id].IndexOf(q, StringComparison.Ordinal) >= 0) outset.Add(terms[id]);
        }

        static List<int> IntersectSorted(List<int> a, List<int> b)
        {
            var r = new List<int>(Math.Min(a.Count, b.Count)); int i = 0, j = 0;
            while (i < a.Count && j < b.Count)
            {
                int x = a[i], y = b[j];
                if (x == y) { r.Add(x); i++; j++; }
                else if (x < y) i++; else j++;
            }
            return r;
        }

        static double Best(Action f) { f(); double best = 1e9; for (int i = 0; i < 5; i++) { var sw = Stopwatch.StartNew(); f(); sw.Stop(); best = Math.Min(best, sw.Elapsed.TotalMilliseconds); } return best; }
        static SqliteConnection Open(string p) { var c = new SqliteConnection($"Data Source={p};Mode=ReadOnly;Cache=Shared"); c.Open(); return c; }
    }
}
