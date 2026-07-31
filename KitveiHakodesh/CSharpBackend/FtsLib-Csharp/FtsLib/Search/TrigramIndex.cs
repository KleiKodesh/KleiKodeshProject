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
        /// <summary>Overload: posting id = index in the list (used by tests/benchmarks).</summary>
        public static void Build(string path, IReadOnlyList<string> terms) => Build(path, terms, null);

        /// <summary>
        /// Content-binding fingerprint tying a sidecar to the term_index it was built
        /// from, stored in the header's spare u32 and validated when the sidecar is
        /// opened next to its .db (see SegmentHandle). Guards against a stale .tgm
        /// paired with a different term_index — e.g. a backup restore that preserves
        /// file timestamps — which would silently map trigram hits to the WRONG
        /// rowids (candidate loss survives the LIKE confirm). Sidecars written before
        /// this field carry 0 and simply fail validation → LIKE fallback until the
        /// next force merge rebuilds them.
        /// </summary>
        internal static uint ComputeBinding(long termCount, long maxRowId)
            => (uint)((ulong)termCount * 2654435761UL) ^ (uint)((ulong)maxRowId * 0x9E3779B9UL) ^ Magic;

        /// <summary>
        /// Builds the sidecar at <paramref name="path"/>. The posting id of <c>terms[i]</c> is
        /// <c>ids[i]</c> (e.g. the term_index rowid) when <paramref name="ids"/> is supplied,
        /// else <c>i</c>. Intended to run once per segment after force-merge (immutable ⇒
        /// write-once).
        /// </summary>
        public static void Build(string path, IReadOnlyList<string> terms, IReadOnlyList<int> ids)
            => Build(path, terms, ids, binding: 0);

        public static void Build(string path, IReadOnlyList<string> terms, IReadOnlyList<int> ids, uint binding)
        {
            var map = new Dictionary<string, List<int>>(1 << 16, StringComparer.Ordinal);
            var grams = new List<string>(32);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < terms.Count; i++)
            {
                string t = terms[i];
                if (t.Length < MinRun) continue;
                int id = ids != null ? ids[i] : i;
                grams.Clear(); seen.Clear();
                AddTrigrams(t, grams, seen);
                foreach (string g in grams)
                {
                    if (!map.TryGetValue(g, out var l)) { l = new List<int>(); map[g] = l; }
                    l.Add(id);
                }
            }

            int n = map.Count;
            int m = 8; while (m < n * 10 / 6) m <<= 1;   // load factor ~0.6, power of two
            uint mask = (uint)(m - 1);
            var fFinger = new uint[m]; var fOff = new uint[m]; var fCnt = new uint[m]; var fLen = new uint[m];
            var blob = new MemoryStream(); var buf = new byte[8];
            foreach (var kv in map)
            {
                var post = kv.Value; post.Sort();   // ascending ⇒ non-negative deltas
                uint off = (uint)blob.Length; int prev = 0;
                foreach (int id in post) { int d = id - prev; prev = id; int len = VarInt.Encode((uint)d, buf); blob.Write(buf, 0, len); }
                uint blen = (uint)blob.Length - off;
                int slot = (int)(Hash(kv.Key) & mask);
                while (fCnt[slot] != 0) slot = (int)((slot + 1) & mask);
                fFinger[slot] = Finger(kv.Key); fOff[slot] = off; fCnt[slot] = (uint)post.Count; fLen[slot] = blen;
            }

            string tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                var bw = new BinaryWriter(fs);
                bw.Write(Magic); bw.Write(m); bw.Write(n); bw.Write(binding);
                for (int i = 0; i < m; i++) { bw.Write(fFinger[i]); bw.Write(fOff[i]); bw.Write(fCnt[i]); bw.Write(fLen[i]); }
                bw.Flush(); blob.Position = 0; blob.CopyTo(fs);
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path); // atomic-ish publish
        }

        /// <summary>Sidecar path for a segment's .dat file (seg.dat → seg.tgm).</summary>
        public static string SidecarPath(string datPath) => Path.ChangeExtension(datPath, ".tgm");

        /// <summary>
        /// Builds the sidecar for a segment by reading its term_index (rowid = posting id) from
        /// <paramref name="dbPath"/>. Opens its own read-only connection; safe to call after a
        /// force-merge on an immutable segment.
        /// </summary>
        public static void BuildFromDb(string dbPath, string outPath)
        {
            var terms = new List<string>(1 << 16);
            var ids = new List<int>(1 << 16);
            long maxRowId = 0;
            // Pooling=false: a pooled handle outlives this using-block and blocks a
            // later merge's File.Delete of the segment in the same process (e.g.
            // sidecars rebuilt at startup for an interrupted build's L0 segments,
            // then deleted by the resumed build's LSM merge).
            var csb = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            { DataSource = dbPath, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false };
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(csb.ConnectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT rowid, term FROM term_index ORDER BY rowid";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    long id = r.GetInt64(0);
                    if (id > maxRowId) maxRowId = id;
                    ids.Add((int)id); terms.Add(r.GetString(1));
                }
            }
            Build(outPath, terms, ids, ComputeBinding(terms.Count, maxRowId));
        }

        // ── Read (seek+read, no mmap) ─────────────────────────────────
        internal sealed class Reader : IDisposable
        {
            readonly SafeFileHandle _h; readonly int _m; readonly uint _mask; readonly long _postBase;

            /// <summary>Content-binding fingerprint stored at build time — see
            /// <see cref="TrigramIndex.ComputeBinding"/>. 0 in pre-binding sidecars.</summary>
            public uint Binding { get; }

            public Reader(string path)
            {
                _h = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
                Span<byte> hdr = stackalloc byte[16];
                RandomAccess.Read(_h, hdr, 0);
                if (BitConverter.ToUInt32(hdr) != Magic) throw new InvalidDataException("TrigramIndex: bad magic");
                _m = BitConverter.ToInt32(hdr.Slice(4, 4)); _mask = (uint)(_m - 1);
                Binding = BitConverter.ToUInt32(hdr.Slice(12, 4));
                _postBase = 16 + (long)_m * 16;
            }

            /// <summary>
            /// Sorted term-ids whose term contains <paramref name="trigram"/>, or empty.
            ///
            /// Collects EVERY fingerprint-matching slot on the probe path (up to the
            /// terminating empty slot), not just the first: two different trigrams can
            /// share both a probe path and a 32-bit fingerprint (~2⁻³² per pair), and
            /// returning only the first match let the earlier entry SHADOW the true
            /// one — its false-positive ids are LIKE-confirmed away downstream, but
            /// the shadowed trigram's ids were silently lost (false negatives). The
            /// union is a superset, which the caller's LIKE confirm already handles.
            /// </summary>
            public int[] Lookup(string trigram)
            {
                int slot = (int)(Hash(trigram) & _mask); uint fg = Finger(trigram);
                Span<byte> rec = stackalloc byte[16];
                int[] first = null; List<int[]> more = null;
                for (int probe = 0; probe < _m; probe++)
                {
                    RandomAccess.Read(_h, rec, 16 + (long)slot * 16);
                    uint cnt = BitConverter.ToUInt32(rec.Slice(8, 4));
                    if (cnt == 0) break;                                     // empty slot ⇒ end of probe path
                    if (BitConverter.ToUInt32(rec) == fg)
                    {
                        uint off = BitConverter.ToUInt32(rec.Slice(4, 4));
                        uint blen = BitConverter.ToUInt32(rec.Slice(12, 4));
                        var pb = new byte[blen]; RandomAccess.Read(_h, pb, _postBase + off);
                        var ids = new int[cnt]; int pos = 0, prev = 0;
                        for (int k = 0; k < cnt; k++) { prev += (int)VarInt.Read(pb, ref pos, pb.Length); ids[k] = prev; }
                        if (first == null) first = ids;
                        else { (more ??= new List<int[]> { first }).Add(ids); }
                    }
                    slot = (int)((slot + 1) & _mask);
                }

                if (more == null) return first ?? Array.Empty<int>();

                // Fingerprint collision on the probe path — vanishingly rare, so a
                // simple concat + sort + dedup is fine.
                int total = 0; foreach (var a in more) total += a.Length;
                var merged = new int[total]; int w = 0;
                foreach (var a in more) { Array.Copy(a, 0, merged, w, a.Length); w += a.Length; }
                Array.Sort(merged);
                int u = 0;
                for (int k = 0; k < merged.Length; k++)
                    if (k == 0 || merged[k] != merged[k - 1]) merged[u++] = merged[k];
                if (u != merged.Length) Array.Resize(ref merged, u);
                return merged;
            }

            public void Dispose() => _h.Dispose();
        }
    }
}
