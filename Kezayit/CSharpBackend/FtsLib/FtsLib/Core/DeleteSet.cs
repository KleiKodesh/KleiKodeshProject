using System;
using System.Collections.Generic;
using System.IO;

namespace FtsLib.Core
{
    /// <summary>
    /// Lucene-style delete bitmap: a sorted set of doc IDs that have been logically
    /// deleted from the index.
    ///
    /// Persisted as a sorted varint-delta file (same codec as posting lists).
    /// Loaded by both IndexReader (to filter search results) and SegmentMerger
    /// (to purge deleted IDs permanently during merge).
    ///
    /// Thread-safety: not thread-safe — single-threaded use only, matching the
    /// rest of the library.
    /// </summary>
    internal sealed class DeleteSet
    {
        private readonly HashSet<int> _ids = new HashSet<int>();

        public int Count => _ids.Count;
        public bool IsEmpty => _ids.Count == 0;

        // ── Query ─────────────────────────────────────────────────────

        public bool Contains(int docId) => _ids.Contains(docId);

        // ── Mutation ──────────────────────────────────────────────────

        public void Add(int docId) => _ids.Add(docId);

        public void Clear() => _ids.Clear();

        // ── Persistence ───────────────────────────────────────────────

        /// <summary>
        /// Writes the delete set to <paramref name="path"/> as a sorted varint-delta
        /// stream. Creates or overwrites the file. Does nothing if the set is empty
        /// (removes the file if it exists).
        /// </summary>
        public void Save(string path)
        {
            if (_ids.Count == 0)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            var sorted = new List<int>(_ids);
            sorted.Sort();

            var buf  = new byte[sorted.Count * 5]; // worst-case 5 bytes per varint
            var tmp  = new byte[5];
            int pos  = 0;
            int prev = 0;

            foreach (int id in sorted)
            {
                uint delta  = (uint)((long)id - (long)prev + (long)int.MaxValue + 1);
                int  nBytes = VarInt.Encode(delta, tmp);
                System.Array.Copy(tmp, 0, buf, pos, nBytes);
                pos += nBytes;
                prev = id;
            }

            using (var fs = new FileStream(path, FileMode.Create,
                                           FileAccess.Write, FileShare.None))
                fs.Write(buf, 0, pos);
        }

        /// <summary>
        /// Loads a previously saved delete set from <paramref name="path"/>.
        /// Returns an empty <see cref="DeleteSet"/> if the file does not exist.
        /// </summary>
        public static DeleteSet Load(string path)
        {
            var ds = new DeleteSet();
            if (!File.Exists(path)) return ds;

            byte[] buf = File.ReadAllBytes(path);
            int    pos = 0;
            int    len = buf.Length;
            int    prev = 0;

            while (pos < len)
            {
                uint delta = VarInt.Read(buf, ref pos, len);
                int  id    = (int)((long)delta + (long)prev - (long)int.MaxValue - 1);
                ds._ids.Add(id);
                prev = id;
            }

            return ds;
        }
    }
}
