using FtsLib.Search;                 // VarInt (internal, visible to FtsLibTest)
using Microsoft.Data.Sqlite;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Compact ON-DISK trigram index (seek+read, no mmap) + end-to-end verification vs SQLite.
    ///
    /// File layout (little-endian):
    ///   header  : magic 'TGM1' (u32) | m slots (u32) | n trigrams (u32) | pad (u32)   [16 B]
    ///   slots   : m × { fingerprint u32, postOffset u32, count u32, byteLen u32 }     [m*16 B]
    ///             open-addressed (linear probe); empty slot ⇒ count==0.
    ///   postings: per trigram, delta+varint of its sorted term-ids (via FtsLib VarInt).
    ///
    /// Keys use a fingerprinted static hash (O(1), seek+read). A true minimal-perfect-hash
    /// would trim ~30% off the (already tiny) slot table only — postings dominate size — so it's
    /// an isolated later refinement. Correctness does NOT depend on hash perfection: the caller
    /// confirms candidates with term.Contains(q), so any fingerprint fluke is filtered out.
    ///
    /// Usage:  FtsLibTest.exe trgmidx [tier=500k]
    /// </summary>
    internal static class TrgmIdx
    {
        const uint MAGIC = 0x314D4754; // 'TGM1'

        // ── hashing ──
        static uint Hash(string g) { uint h = 2166136261u; foreach (char c in g) h = (h ^ c) * 16777619u; return h; }
        static uint Finger(string g) { uint h = 2166136261u ^ 0x9E3779B9u; foreach (char c in g) h = (h ^ (uint)(c * 3 + 7)) * 2246822519u; return h; }

        // ── writer ──
        static void Write(string path, Dictionary<string, List<int>> map)
        {
            int n = map.Count;
            int m = 8; while (m < n * 10 / 6) m <<= 1;   // load factor ~0.6, power of two
            uint mask = (uint)(m - 1);
            var fFinger = new uint[m]; var fOff = new uint[m]; var fCnt = new uint[m]; var fLen = new uint[m];
            var blob = new MemoryStream(); var buf = new byte[8];
            foreach (var kv in map)
            {
                var ids = kv.Value; ids.Sort();
                uint off = (uint)blob.Length; int prev = 0;
                foreach (int id in ids) { int d = id - prev; prev = id; int len = VarInt.Encode((uint)d, buf); blob.Write(buf, 0, len); }
                uint blen = (uint)blob.Length - off;
                int slot = (int)(Hash(kv.Key) & mask);
                while (fCnt[slot] != 0) slot = (int)((slot + 1) & mask);
                fFinger[slot] = Finger(kv.Key); fOff[slot] = off; fCnt[slot] = (uint)ids.Count; fLen[slot] = blen;
            }
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
            var bw = new BinaryWriter(fs);
            bw.Write(MAGIC); bw.Write(m); bw.Write(n); bw.Write(0u);
            for (int i = 0; i < m; i++) { bw.Write(fFinger[i]); bw.Write(fOff[i]); bw.Write(fCnt[i]); bw.Write(fLen[i]); }
            bw.Flush(); blob.Position = 0; blob.CopyTo(fs);
        }

        // ── reader (seek+read; only header cached, ~0 RAM) ──
        sealed class Reader : IDisposable
        {
            readonly SafeFileHandle _h; readonly int _m; readonly uint _mask; readonly long _postBase;
            public Reader(string path)
            {
                _h = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
                Span<byte> hdr = stackalloc byte[16]; RandomAccess.Read(_h, hdr, 0);
                if (BitConverter.ToUInt32(hdr) != MAGIC) throw new InvalidDataException("bad magic");
                _m = BitConverter.ToInt32(hdr.Slice(4, 4)); _mask = (uint)(_m - 1);
                _postBase = 16 + (long)_m * 16;
            }
            public int[] Lookup(string g)
            {
                int slot = (int)(Hash(g) & _mask); uint fg = Finger(g);
                Span<byte> rec = stackalloc byte[16];
                for (int probe = 0; probe < _m; probe++)
                {
                    RandomAccess.Read(_h, rec, 16 + (long)slot * 16);
                    uint finger = BitConverter.ToUInt32(rec); uint off = BitConverter.ToUInt32(rec.Slice(4, 4));
                    uint cnt = BitConverter.ToUInt32(rec.Slice(8, 4)); uint blen = BitConverter.ToUInt32(rec.Slice(12, 4));
                    if (cnt == 0) return Array.Empty<int>();          // empty slot ⇒ absent
                    if (finger == fg)
                    {
                        var pb = new byte[blen]; RandomAccess.Read(_h, pb, _postBase + off);
                        var ids = new int[cnt]; int pos = 0, prev = 0;
                        for (int k = 0; k < cnt; k++) { prev += (int)VarInt.Read(pb, ref pos, pb.Length); ids[k] = prev; }
                        return ids;
                    }
                    slot = (int)((slot + 1) & _mask);
                }
                return Array.Empty<int>();
            }
            public void Dispose() => _h.Dispose();
        }

        static int[] Inter(int[] a, int[] b)
        {
            var r = new List<int>(Math.Min(a.Length, b.Length)); int i = 0, j = 0;
            while (i < a.Length && j < b.Length) { int x = a[i], y = b[j]; if (x == y) { r.Add(x); i++; j++; } else if (x < y) i++; else j++; }
            return r.ToArray();
        }

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

            var map = new Dictionary<string, List<int>>(1 << 20, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int id = 0; id < terms.Count; id++)
            {
                string t = terms[id]; if (t.Length < 3) continue; seen.Clear();
                for (int i = 0; i + 3 <= t.Length; i++) { string g = t.Substring(i, 3); if (seen.Add(g)) { if (!map.TryGetValue(g, out var l)) { l = new List<int>(); map[g] = l; } l.Add(id); } }
            }
            var sw = Stopwatch.StartNew(); Write(outPath, map); sw.Stop();
            long sz = new FileInfo(outPath).Length; long segSz = new FileInfo(db).Length;
            Console.WriteLine($"built {outPath} in {sw.ElapsedMilliseconds} ms");
            Console.WriteLine($"trigram index size: {sz / 1024.0 / 1024:F1} MB  ({map.Count:N0} trigrams)  vs term_index .db {segSz / 1024.0 / 1024:F0} MB\n");

            using var reader = new Reader(outPath);

            // exhaustive round-trip correctness: every trigram's postings match the built map
            int bad = 0, chec0 = 0;
            foreach (var kv in map) { var got = reader.Lookup(kv.Key); var exp = kv.Value; if (!got.SequenceEqual(exp)) bad++; chec0++; }
            Console.WriteLine($"round-trip: {chec0:N0} trigrams checked, {bad} mismatches" + (bad == 0 ? "  ✓ ALL OK" : "  ✗"));

            // end-to-end vs SQLite LIKE, via the ON-DISK reader
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

        static void Search(string q, Reader reader, List<string> terms, HashSet<string> outset)
        {
            if (q.Length < 3) return;
            var grams = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i + 3 <= q.Length; i++) { string g = q.Substring(i, 3); if (seen.Add(g)) grams.Add(g); }
            var lists = new List<int[]>(grams.Count);
            foreach (var g in grams) { var l = reader.Lookup(g); if (l.Length == 0) return; lists.Add(l); }
            lists.Sort((a, b) => a.Length.CompareTo(b.Length));
            int[] acc = lists[0];
            for (int k = 1; k < lists.Count; k++) acc = Inter(acc, lists[k]);
            foreach (int id in acc) if (terms[id].IndexOf(q, StringComparison.Ordinal) >= 0) outset.Add(terms[id]);
        }

        static double Best(Action f) { f(); double best = 1e9; for (int i = 0; i < 5; i++) { var sw = Stopwatch.StartNew(); f(); sw.Stop(); best = Math.Min(best, sw.Elapsed.TotalMilliseconds); } return best; }
        static SqliteConnection Open(string p) { var c = new SqliteConnection($"Data Source={p};Mode=ReadOnly;Cache=Shared"); c.Open(); return c; }
    }
}
