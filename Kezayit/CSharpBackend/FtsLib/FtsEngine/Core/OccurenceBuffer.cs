using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FtsEngine.Core
{
    /// <summary>
    /// Fixed-size RAM buffer for (term, docId) occurrences.
    ///
    /// Internally uses Dictionary&lt;term, List&lt;docId&gt;&gt; so each term string is stored
    /// once regardless of how many documents contain it. This keeps memory low and makes
    /// the flush sort O(U log U) over unique terms U rather than O(N log N) over all pairs N.
    ///
    /// When full, Sort() + Flush() writes a sorted binary run file to disk and clears the buffer.
    ///
    /// Run file format (binary, little-endian):
    ///   Per record: [int16 termByteLen][UTF-8 term bytes][int32 docId]
    ///   Records are written in ascending (term, docId) order.
    /// </summary>
    internal sealed class OccurenceBuffer
    {
        private const int MaxBytes = 512 * 1024 * 1024; // 512 MB — fewer runs = faster merge

        private readonly Dictionary<string, List<int>> _map;
        private int _estimatedBytes;

        public int  Count          => _estimatedBytes; // proxy — used only for IsFull
        public bool IsFull         => _estimatedBytes >= MaxBytes;

        public OccurenceBuffer()
        {
            _map = new Dictionary<string, List<int>>(1_000_000, StringComparer.Ordinal);
        }

        public void Add(string term, int docId)
        {
            if (!_map.TryGetValue(term, out var list))
            {
                list = new List<int>(4);
                _map[term] = list;
                // New term: charge string overhead + ~2 bytes/char UTF-16 + list overhead
                _estimatedBytes += 48 + term.Length * 2;
            }
            list.Add(docId);
            // Per docId: 4 bytes
            _estimatedBytes += 4;
        }

        /// <summary>
        /// Sorts unique terms, then writes all (term, docId) pairs in order to a run file.
        /// Returns the path of the written run file.
        /// </summary>
        public string Flush(string runDir, int runIndex)
        {
            // Sort only unique terms — O(U log U) where U << N
            var terms = new string[_map.Count];
            _map.Keys.CopyTo(terms, 0);
            Array.Sort(terms, StringComparer.Ordinal);

            string path = Path.Combine(runDir, $"run_{runIndex:D4}.bin");

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write,
                                           FileShare.None, 4 * 1024 * 1024)) // 4 MB write buffer
            using (var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: false))
            {
                foreach (var term in terms)
                {
                    byte[] termBytes = Encoding.UTF8.GetBytes(term);
                    var docIds = _map[term];
                    docIds.Sort(); // sort docIds within term — small list, fast
                    foreach (var docId in docIds)
                    {
                        bw.Write((short)termBytes.Length);
                        bw.Write(termBytes);
                        bw.Write(docId);
                    }
                }
            }

            Clear();
            return path;
        }

        public void Clear()
        {
            _map.Clear();
            _estimatedBytes = 0;
        }
    }
}
