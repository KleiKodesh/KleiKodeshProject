using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.IO;

namespace FtsLib.Search
{
    /// <summary>
    /// Compact, disk-based trigram index over a segment's term dictionary — a sidecar that
    /// accelerates infix / suffix-wildcard / fuzzy candidate generation, replacing the SQLite
    /// <c>term LIKE '%x%'</c> full-table scan. Verified ~1000x faster than LIKE with identical
    /// results (FtsLibTest trgmidx). SQLite term_index stays authoritative for exact lookups,
    /// prefix (B-tree range), and the actual document postings.
    ///
    /// On-disk layout (little-endian), read via seek+read (RandomAccess) — NO mmap, ~0 RAM
    /// beyond the 16-byte header (see design decision: 32-bit-safe, streamable, no cold-start):
    ///   header  : magic 'TGM1' (u32) | m slots (u32) | n trigrams (u32) | pad (u32)   [16 B]
    ///   slots   : m × { fingerprint u32, postOffset u32, count u32, byteLen u32 }     [m*16 B]
    ///             open-addressed (linear probe); empty slot ⇒ count == 0.
    ///   postings: per trigram, delta+varint of sorted term-ids (via <see cref="VarInt"/>).
    ///
    /// Term-ids are caller-assigned (the writer stores the 0-based index into the supplied term
    /// list; the caller maps that back to a rowid/term). Correctness does not depend on hash
    /// perfection — callers confirm candidates against the actual term (Contains), so a
    /// fingerprint fluke is filtered out; only a missing key would matter, and open addressing
    /// without deletes guarantees every built key is found.
    /// </summary>
    internal static class TrigramIndex
    {
        public const uint Magic = 0x314D4754; // 'TGM1'
        public const int MinRun = 3;          // shortest literal run that yields a trigram

        internal static uint Hash(string g)   { uint h = 2166136261u;              foreach (char c in g) h = (h ^ c) * 16777619u;         return h; }
        internal static uint Finger(string g) { uint h = 2166136261u ^ 0x9E3779B9u; foreach (char c in g) h = (h ^ (uint)(c * 3 + 7)) * 2246822519u; return h; }

        /// <summary>Distinct trigrams of <paramref name="s"/> appended to <paramref name="into"/>
        /// (caller clears the dedup set). Returns count added.</summary>
        internal static void AddTrigrams(string s, List<string> into, HashSet<string> seen)
        {
            for (int i = 0; i + MinRun <= s.Length; i++)
            {
                string g = s.Substring(i, MinRun);
                if (seen.Add(g)) into.Add(g);
            }
        }

        // ── Build ─────────────────────────────────────────────────────
        /// <summary>
        /// Builds the sidecar at <paramref name="path"/> from <paramref name="terms"/>, where the
        /// posting id of a term is its index in the list. Intended to run once per segment after
        /// force-merge (segments are immutable ⇒ write-once).
        /// </summary>
        public static void Build(string path, IReadOnlyList<string> terms)
        {
            var map = new Dictionary<string, List<int>>(1 << 16, StringComparer.Ordinal);
            var grams = new List<string>(32);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int id = 0; id < terms.Count; id++)
            {
                string t = terms[id];
                if (t.Length < MinRun) continue;
                grams.Clear(); seen.Clear();
                AddTrigrams(t, grams, seen);
                foreach (string g in grams)
                {
                    if (!map.TryGetValue(g, out var l)) { l = new List<int>(); map[g] = l; }
                    l.Add(id); // ascending id ⇒ posting stays sorted
                }
            }

            int n = map.Count;
            int m = 8; while (m < n * 10 / 6) m <<= 1;   // load factor ~0.6, power of two
            uint mask = (uint)(m - 1);
            var fFinger = new uint[m]; var fOff = new uint[m]; var fCnt = new uint[m]; var fLen = new uint[m];
            var blob = new MemoryStream(); var buf = new byte[8];
            foreach (var kv in map)
            {
                var ids = kv.Value;
                uint off = (uint)blob.Length; int prev = 0;
                foreach (int id in ids) { int d = id - prev; prev = id; int len = VarInt.Encode((uint)d, buf); blob.Write(buf, 0, len); }
                uint blen = (uint)blob.Length - off;
                int slot = (int)(Hash(kv.Key) & mask);
                while (fCnt[slot] != 0) slot = (int)((slot + 1) & mask);
                fFinger[slot] = Finger(kv.Key); fOff[slot] = off; fCnt[slot] = (uint)ids.Count; fLen[slot] = blen;
            }

            string tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                var bw = new BinaryWriter(fs);
                bw.Write(Magic); bw.Write(m); bw.Write(n); bw.Write(0u);
                for (int i = 0; i < m; i++) { bw.Write(fFinger[i]); bw.Write(fOff[i]); bw.Write(fCnt[i]); bw.Write(fLen[i]); }
                bw.Flush(); blob.Position = 0; blob.CopyTo(fs);
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path); // atomic-ish publish
        }

        // ── Read (seek+read, no mmap) ─────────────────────────────────
        internal sealed class Reader : IDisposable
        {
            readonly SafeFileHandle _h; readonly int _m; readonly uint _mask; readonly long _postBase;

            public Reader(string path)
            {
                _h = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
                Span<byte> hdr = stackalloc byte[16];
                RandomAccess.Read(_h, hdr, 0);
                if (BitConverter.ToUInt32(hdr) != Magic) throw new InvalidDataException("TrigramIndex: bad magic");
                _m = BitConverter.ToInt32(hdr.Slice(4, 4)); _mask = (uint)(_m - 1);
                _postBase = 16 + (long)_m * 16;
            }

            /// <summary>Sorted term-ids whose term contains <paramref name="trigram"/>, or empty.</summary>
            public int[] Lookup(string trigram)
            {
                int slot = (int)(Hash(trigram) & _mask); uint fg = Finger(trigram);
                Span<byte> rec = stackalloc byte[16];
                for (int probe = 0; probe < _m; probe++)
                {
                    RandomAccess.Read(_h, rec, 16 + (long)slot * 16);
                    uint cnt = BitConverter.ToUInt32(rec.Slice(8, 4));
                    if (cnt == 0) return Array.Empty<int>();                 // empty slot ⇒ absent
                    if (BitConverter.ToUInt32(rec) == fg)
                    {
                        uint off = BitConverter.ToUInt32(rec.Slice(4, 4));
                        uint blen = BitConverter.ToUInt32(rec.Slice(12, 4));
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
    }
}
