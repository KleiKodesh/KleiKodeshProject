using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FtsEngine.Core
{
    /// <summary>
    /// Fixed-size RAM buffer for (term, docId) occurrences.
    ///
    /// When full, Sort() + Flush() writes a sorted binary run file to disk and clears the buffer.
    ///
    /// Run file format (binary, little-endian):
    ///   Per record: [int16 termByteLen][UTF-8 term bytes][int32 docId]
    ///   Records are written in ascending (term, docId) order.
    ///
    /// Buffer size: 100 MB → ~8M pairs per run → ~8 runs for 6M lines × 10 terms.
    /// </summary>
    internal sealed class OccurenceBuffer
    {
        private const int MaxBytes = 100 * 1024 * 1024; // 100 MB

        private readonly List<TermOccurence> _items;
        private int _estimatedBytes;

        public int Count => _items.Count;
        public bool IsFull => _estimatedBytes >= MaxBytes;

        public OccurenceBuffer()
        {
            // Pre-size for ~8M entries (100 MB / ~12 bytes avg)
            _items = new List<TermOccurence>(8_000_000);
        }

        public void Add(string term, int docId)
        {
            _items.Add(new TermOccurence(term, docId));
            // Estimate: 2 bytes overhead + ~2 bytes/char (UTF-16 in memory) + 4 bytes docId
            _estimatedBytes += 6 + term.Length * 2;
        }

        /// <summary>
        /// Sorts the buffer by (term, docId) and writes it as a binary run file.
        /// Returns the path of the written run file.
        /// </summary>
        public string Flush(string runDir, int runIndex)
        {
            // Sort: term ascending, then docId ascending within same term
            _items.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.Term, b.Term);
                return c != 0 ? c : a.DocId.CompareTo(b.DocId);
            });

            string path = Path.Combine(runDir, $"run_{runIndex:D4}.bin");

            using (var fs  = new FileStream(path, FileMode.Create, FileAccess.Write,
                                            FileShare.None, 1 << 20))
            using (var bw  = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: false))
            {
                foreach (var item in _items)
                {
                    byte[] termBytes = Encoding.UTF8.GetBytes(item.Term);
                    bw.Write((short)termBytes.Length);
                    bw.Write(termBytes);
                    bw.Write(item.DocId);
                }
            }

            Clear();
            return path;
        }

        public void Clear()
        {
            _items.Clear();
            _estimatedBytes = 0;
        }
    }
}
