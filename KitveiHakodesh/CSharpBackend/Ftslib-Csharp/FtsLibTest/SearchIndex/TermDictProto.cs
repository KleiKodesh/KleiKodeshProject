using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Prototype: a compact FRONT-CODED BLOCK term dictionary vs the current SQLite
    /// term_index (.db). Validates the compactness + lookup-speed thesis on a REAL
    /// segment before any production integration.
    ///
    /// Layout (blocks of 16 sorted terms):
    ///   block head term : varint len + UTF-8 bytes, then ABSOLUTE outputs
    ///   next 15 terms   : varint sharedPrefixLen + varint suffixLen + suffix bytes,
    ///                     then DELTA outputs (offset/skipOffset delta vs previous term)
    ///   outputs order   : skipOffset, skipCount, offset, length, count
    /// A parallel int[] blockBlobOffset lets us binary-search block heads (decoded from
    /// the blob) then front-decode within the located block. Offsets are monotonic in
    /// sorted-term order, so delta-coding them is tiny.
    ///
    /// Usage: FtsLibTest.exe termdict [tier]
    /// </summary>
    internal static class TermDictProto
    {
        private const int BlockSize = 16;

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string tier  = args.Length > 1 ? args[1] : "full";
            string label = TestHelpers.ResolveTier(tier).Label;
            string dir   = TestHelpers.IndexDir(label);
            if (!Directory.Exists(dir)) { Console.WriteLine($"No index at {dir}"); return; }

            // Pick the largest segment (most representative of a merged index).
            string bestDb = null; long bestTerms = 0;
            foreach (var dat in Directory.GetFiles(dir, "seg_*.dat"))
            {
                string db = Path.ChangeExtension(dat, ".db");
                if (!File.Exists(db)) continue;
                using (var c = Open(db))
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM term_index";
                    long nn = (long)cmd.ExecuteScalar();
                    if (nn > bestTerms) { bestTerms = nn; bestDb = db; }
                }
            }
            if (bestDb == null) { Console.WriteLine("No segments."); return; }

            long sqliteBytes = new FileInfo(bestDb).Length;
            Console.WriteLine($"=== Term-dict prototype — {Path.GetFileName(bestDb)} ===");
            Console.WriteLine($"  terms: {bestTerms:N0}   SQLite .db: {sqliteBytes:N0} B ({sqliteBytes/1048576.0:F1} MB)");

            // ── Load rows sorted by term ──
            var terms = new List<byte[]>((int)bestTerms);
            var so = new List<long>(); var sc = new List<int>();
            var off = new List<long>(); var ln = new List<int>(); var cn = new List<int>();
            var sw = Stopwatch.StartNew();
            using (var c = Open(bestDb))
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT term,skip_offset,skip_count,offset,length,count FROM term_index ORDER BY term";
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                    {
                        terms.Add(Encoding.UTF8.GetBytes(r.GetString(0)));
                        so.Add(r.GetInt64(1)); sc.Add(r.GetInt32(2));
                        off.Add(r.GetInt64(3)); ln.Add(r.GetInt32(4)); cn.Add(r.GetInt32(5));
                    }
            }
            Console.WriteLine($"  loaded {terms.Count:N0} rows in {sw.Elapsed.TotalSeconds:F1}s");

            // ── Build front-coded blob ──
            var blob = new Buf();
            int n = terms.Count;
            int numBlocks = (n + BlockSize - 1) / BlockSize;
            var blockOff = new int[numBlocks];
            for (int i = 0; i < n; i++)
            {
                bool head = (i % BlockSize) == 0;
                if (head)
                {
                    blockOff[i / BlockSize] = blob.Len;
                    blob.VarUInt((uint)terms[i].Length);
                    blob.Bytes(terms[i], 0, terms[i].Length);
                    blob.VarULong((ulong)so[i]); blob.VarUInt((uint)sc[i]);
                    blob.VarULong((ulong)off[i]); blob.VarUInt((uint)ln[i]); blob.VarUInt((uint)cn[i]);
                }
                else
                {
                    int shared = SharedPrefix(terms[i - 1], terms[i]);
                    blob.VarUInt((uint)shared);
                    blob.VarUInt((uint)(terms[i].Length - shared));
                    blob.Bytes(terms[i], shared, terms[i].Length - shared);
                    blob.VarULong((ulong)(so[i] - so[i - 1])); blob.VarUInt((uint)sc[i]);
                    blob.VarULong((ulong)(off[i] - off[i - 1])); blob.VarUInt((uint)ln[i]); blob.VarUInt((uint)cn[i]);
                }
            }
            byte[] data = blob.ToArray();
            long dictBytes = data.Length + (long)blockOff.Length * 4;
            Console.WriteLine();
            Console.WriteLine($"  front-coded dict: {dictBytes:N0} B ({dictBytes/1048576.0:F1} MB)   blob={data.Length:N0}  index={blockOff.Length*4:N0}");
            Console.WriteLine($"  vs SQLite .db   : {100.0*dictBytes/sqliteBytes:F1}%  ({(sqliteBytes-dictBytes)/1048576.0:F1} MB smaller, {sqliteBytes/(double)dictBytes:F1}x)");

            var dict = new FrontCoded(data, blockOff, terms);

            // ── Correctness: every term looks up to the same 5 outputs ──
            long mism = 0;
            for (int i = 0; i < n; i++)
            {
                if (!dict.Lookup(terms[i], out var o)) { mism++; continue; }
                if (o.so != so[i] || o.sc != sc[i] || o.off != off[i] || o.len != ln[i] || o.cn != cn[i]) mism++;
            }
            Console.WriteLine();
            Console.WriteLine($"  correctness (all {n:N0} exact lookups match SQLite outputs): {(mism == 0 ? "PASS" : $"FAIL ({mism})")}");

            // ── Exact-lookup speed: front-coded vs SQLite point query ──
            var rng = new Random(7);
            int Q = 20000;
            var probe = new byte[Q][];
            for (int i = 0; i < Q; i++) probe[i] = terms[rng.Next(n)];

            long chk = 0;
            var t1 = Time(5, () => { long a = 0; for (int i = 0; i < Q; i++) { if (dict.Lookup(probe[i], out var o)) a += o.off; } chk = a; });
            Console.WriteLine();
            Console.WriteLine($"  EXACT lookup ({Q:N0} random terms):");
            Console.WriteLine($"    front-coded : {t1:F2} ms  ({Q/(t1/1000.0):N0}/s)");

            long chk2 = 0;
            using (var c = Open(bestDb))
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT offset FROM term_index WHERE term=@t";
                var p = cmd.Parameters.Add("@t", System.Data.DbType.String);
                var t2 = Time(3, () =>
                {
                    long a = 0;
                    for (int i = 0; i < Q; i++) { p.Value = Encoding.UTF8.GetString(probe[i]); var v = cmd.ExecuteScalar(); if (v != null) a += Convert.ToInt64(v); }
                    chk2 = a;
                });
                Console.WriteLine($"    SQLite      : {t2:F2} ms  ({Q/(t2/1000.0):N0}/s)   speedup {t2/t1:F1}x   (checksums {(chk==chk2?"match":"DIFFER")})");
            }

            // ── Prefix-scan speed: front-coded vs SQLite range, BOTH materializing terms ──
            // (Real prefix-wildcard expansion needs the term strings, not just a count;
            //  char-aligned 3-char anchors mirror how HebrewWildcardExpander is used.)
            int P = 1000;
            var prefixes = new byte[P][];
            var loStr = new string[P]; var hiStr = new string[P];
            for (int i = 0; i < P; i++)
            {
                string t = Encoding.UTF8.GetString(terms[rng.Next(n)]);
                int L = Math.Min(3, t.Length);
                string pre = t.Substring(0, L);
                prefixes[i] = Encoding.UTF8.GetBytes(pre);
                loStr[i] = pre; hiStr[i] = CharIncrement(pre);
            }

            long pc1 = 0;
            var pt1 = Time(5, () => { long a = 0; for (int i = 0; i < P; i++) a += dict.CountPrefix(prefixes[i]); pc1 = a; });
            Console.WriteLine();
            Console.WriteLine($"  PREFIX scan ({P:N0} random 3-char anchors):  matched {pc1:N0} terms/pass");
            Console.WriteLine($"    front-coded (decode terms): {pt1:F2} ms  ({P/(pt1/1000.0):N0} prefixes/s)");

            long pc2 = 0;
            using (var c = Open(bestDb))
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT term FROM term_index WHERE term>=@lo AND term<@hi";
                var lo = cmd.Parameters.Add("@lo", System.Data.DbType.String);
                var hi = cmd.Parameters.Add("@hi", System.Data.DbType.String);
                var pt2 = Time(3, () =>
                {
                    long a = 0;
                    for (int i = 0; i < P; i++)
                    {
                        lo.Value = loStr[i]; hi.Value = hiStr[i];
                        using (var r = cmd.ExecuteReader())
                            while (r.Read()) { var s = r.GetString(0); a += s.Length; }
                    }
                    pc2 = a;
                });
                // pc1 counts matches; pc2 sums term lengths — not directly equal, so
                // re-count matches on the SQLite side once for a correctness check.
                long sqlMatches = 0;
                cmd.CommandText = "SELECT COUNT(*) FROM term_index WHERE term>=@lo AND term<@hi";
                for (int i = 0; i < P; i++) { lo.Value = loStr[i]; hi.Value = hiStr[i]; sqlMatches += (long)cmd.ExecuteScalar(); }
                Console.WriteLine($"    SQLite range (SELECT term): {pt2:F2} ms  ({P/(pt2/1000.0):N0} prefixes/s)   speedup {pt2/pt1:F1}x");
                Console.WriteLine($"    match-count check: front-coded {pc1:N0}  vs  SQLite {sqlMatches:N0}  {(pc1==sqlMatches?"(match)":"(DIFFER)")}");
            }
        }

        private static string CharIncrement(string s)
        {
            if (s.Length == 0) return s;
            char last = s[s.Length - 1];
            return s.Substring(0, s.Length - 1) + (char)(last + 1);
        }

        // ── Front-coded reader ──
        private sealed class FrontCoded
        {
            private readonly byte[] _d;
            private readonly int[]  _blockOff;
            private readonly int    _numBlocks;
            private readonly List<byte[]> _terms; // only for count (n); not used in lookup

            public FrontCoded(byte[] data, int[] blockOff, List<byte[]> terms)
            { _d = data; _blockOff = blockOff; _numBlocks = blockOff.Length; _terms = terms; }

            public struct Out { public long so, off; public int sc, len, cn; }

            // Decode a block head term (bytes) at block b into buffer, return length.
            private int HeadTerm(int b, byte[] tmp)
            {
                int p = _blockOff[b];
                int len = (int)ReadVarUInt(_d, ref p);
                Array.Copy(_d, p, tmp, 0, len);
                return len;
            }

            public bool Lookup(byte[] term, out Out o)
            {
                o = default;
                // Binary search for rightmost block whose head <= term.
                var tmp = new byte[64];
                int lo = 0, hi = _numBlocks - 1, blk = 0;
                while (lo <= hi)
                {
                    int mid = (lo + hi) >> 1;
                    int hl = HeadTerm(mid, tmp);
                    int cmp = Compare(tmp, hl, term, term.Length);
                    if (cmp <= 0) { blk = mid; lo = mid + 1; }
                    else hi = mid - 1;
                }
                // Scan the block, front-decoding, comparing to term.
                int p = _blockOff[blk];
                int end = (blk + 1 < _numBlocks) ? _blockOff[blk + 1] : _d.Length;
                var cur = new byte[64];
                int curLen = 0;
                long soAcc = 0, offAcc = 0;
                for (int i = 0; i < BlockSize && p < end; i++)
                {
                    int sc2, len2, cn2;
                    if (i == 0)
                    {
                        int tl = (int)ReadVarUInt(_d, ref p);
                        EnsureCap(ref cur, tl); Array.Copy(_d, p, cur, 0, tl); p += tl; curLen = tl;
                        soAcc = (long)ReadVarULong(_d, ref p); sc2 = (int)ReadVarUInt(_d, ref p);
                        offAcc = (long)ReadVarULong(_d, ref p); len2 = (int)ReadVarUInt(_d, ref p); cn2 = (int)ReadVarUInt(_d, ref p);
                    }
                    else
                    {
                        int shared = (int)ReadVarUInt(_d, ref p);
                        int suf = (int)ReadVarUInt(_d, ref p);
                        EnsureCap(ref cur, shared + suf); Array.Copy(_d, p, cur, shared, suf); p += suf; curLen = shared + suf;
                        soAcc += (long)ReadVarULong(_d, ref p); sc2 = (int)ReadVarUInt(_d, ref p);
                        offAcc += (long)ReadVarULong(_d, ref p); len2 = (int)ReadVarUInt(_d, ref p); cn2 = (int)ReadVarUInt(_d, ref p);
                    }
                    int cmp = Compare(cur, curLen, term, term.Length);
                    if (cmp == 0) { o = new Out { so = soAcc, sc = sc2, off = offAcc, len = len2, cn = cn2 }; return true; }
                    if (cmp > 0) return false; // passed it
                }
                return false;
            }

            public int CountPrefix(byte[] prefix)
            {
                var tmp = new byte[64];
                // Find rightmost block whose head <= prefix (matches may start there or next).
                int lo = 0, hi = _numBlocks - 1, blk = 0;
                while (lo <= hi)
                {
                    int mid = (lo + hi) >> 1;
                    int hl = HeadTerm(mid, tmp);
                    int cmp = Compare(tmp, hl, prefix, prefix.Length);
                    if (cmp <= 0) { blk = mid; lo = mid + 1; }
                    else hi = mid - 1;
                }
                int count = 0;
                var cur = new byte[64]; int curLen = 0;
                for (int b = blk; b < _numBlocks; b++)
                {
                    int p = _blockOff[b];
                    int end = (b + 1 < _numBlocks) ? _blockOff[b + 1] : _d.Length;
                    for (int i = 0; i < BlockSize && p < end; i++)
                    {
                        if (i == 0)
                        {
                            int tl = (int)ReadVarUInt(_d, ref p);
                            EnsureCap(ref cur, tl); Array.Copy(_d, p, cur, 0, tl); p += tl; curLen = tl;
                            SkipVars(_d, ref p, 5);
                        }
                        else
                        {
                            int shared = (int)ReadVarUInt(_d, ref p);
                            int suf = (int)ReadVarUInt(_d, ref p);
                            EnsureCap(ref cur, shared + suf); Array.Copy(_d, p, cur, shared, suf); p += suf; curLen = shared + suf;
                            SkipVars(_d, ref p, 5);
                        }
                        int cmp = CompareForPrefix(cur, curLen, prefix);
                        if (cmp == 0) count++;
                        else if (cmp > 0) return count; // past the prefix range
                    }
                }
                return count;
            }
        }

        // ── growable write buffer ──
        private sealed class Buf
        {
            private byte[] _b = new byte[1 << 20];
            public int Len;
            public void Ensure(int extra) { if (Len + extra > _b.Length) { int n = _b.Length * 2; while (n < Len + extra) n *= 2; Array.Resize(ref _b, n); } }
            public void Bytes(byte[] src, int off, int len) { Ensure(len); Array.Copy(src, off, _b, Len, len); Len += len; }
            public void VarUInt(uint v) { Ensure(5); while (v >= 0x80) { _b[Len++] = (byte)(v | 0x80); v >>= 7; } _b[Len++] = (byte)v; }
            public void VarULong(ulong v) { Ensure(10); while (v >= 0x80) { _b[Len++] = (byte)(v | 0x80); v >>= 7; } _b[Len++] = (byte)v; }
            public byte[] ToArray() { var r = new byte[Len]; Array.Copy(_b, r, Len); return r; }
        }

        // ── helpers ──
        private static SQLiteConnection Open(string db)
        { var c = new SQLiteConnection($"Data Source={db};Version=3;Read Only=True;"); c.Open(); return c; }

        private static int SharedPrefix(byte[] a, byte[] b)
        { int m = Math.Min(a.Length, b.Length), i = 0; while (i < m && a[i] == b[i]) i++; return i; }

        private static byte[] Sub(byte[] a, int off, int len) { var r = new byte[len]; Array.Copy(a, off, r, 0, len); return r; }

        private static int Compare(byte[] a, int aLen, byte[] b, int bLen)
        {
            int m = Math.Min(aLen, bLen);
            for (int i = 0; i < m; i++) { int d = a[i] - b[i]; if (d != 0) return d; }
            return aLen - bLen;
        }

        // 0 if `term` starts with prefix; <0 if term<prefix range; >0 if term past prefix range.
        private static int CompareForPrefix(byte[] term, int termLen, byte[] prefix)
        {
            int m = Math.Min(termLen, prefix.Length);
            for (int i = 0; i < m; i++) { int d = term[i] - prefix[i]; if (d != 0) return d; }
            return termLen >= prefix.Length ? 0 : -1; // term shorter than prefix → before range
        }

        private static byte[] PrefixUpper(byte[] p)
        {
            var r = (byte[])p.Clone();
            for (int i = r.Length - 1; i >= 0; i--) { if (r[i] != 0xFF) { r[i]++; return Sub(r, 0, i + 1); } }
            return r; // all 0xFF — unlikely for UTF-8 Hebrew
        }

        private static uint  ReadVarUInt(byte[] b, ref int p) { int s = 0; uint r = 0; while (true) { byte x = b[p++]; r |= (uint)(x & 0x7F) << s; if ((x & 0x80) == 0) break; s += 7; } return r; }
        private static ulong ReadVarULong(byte[] b, ref int p) { int s = 0; ulong r = 0; while (true) { byte x = b[p++]; r |= (ulong)(x & 0x7F) << s; if ((x & 0x80) == 0) break; s += 7; } return r; }
        private static void  SkipVars(byte[] b, ref int p, int count) { for (int k = 0; k < count; k++) while ((b[p++] & 0x80) != 0) { } }

        private static void EnsureCap(ref byte[] a, int len) { if (len > a.Length) Array.Resize(ref a, Math.Max(len, a.Length * 2)); }

        private static double Time(int reps, Action body)
        {
            body(); // warm
            double best = double.MaxValue;
            var sw = new Stopwatch();
            for (int i = 0; i < reps; i++) { sw.Restart(); body(); sw.Stop(); best = Math.Min(best, sw.Elapsed.TotalMilliseconds); }
            return best;
        }
    }
}
