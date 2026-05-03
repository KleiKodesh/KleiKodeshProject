using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FtsEngine.Core
{
    /// <summary>
    /// Fixed-size RAM buffer for (term, docId) occurrences.
    ///
    /// Internally uses Dictionary&lt;string, PostingStream&gt; — identical to FtsLib's RamIndex.
    /// Each term's posting bytes are already delta+varint encoded in memory.
    /// Flush is O(U log U) over unique terms only — no per-pair sort.
    ///
    /// Flushes when either:
    ///   - Estimated RAM exceeds MaxBytes (512 MB), or
    ///   - Unique term count exceeds MaxUniqueTerms (500k) — dictionary efficiency cap
    ///
    /// Run file format (binary, little-endian), sorted by term:
    ///   Per entry: [int16 termByteLen][UTF-8 term bytes][int32 docCount][int32 byteLen][encoded bytes]
    /// </summary>
    internal sealed class OccurenceBuffer
    {
        private const int MaxBytes       = 512 * 1024 * 1024; // 512 MB RAM cap
        private const int MaxUniqueTerms = 500_000;           // dictionary efficiency cap

        private readonly Dictionary<string, PostingStream> _map;
        private int _estimatedBytes;

        public bool IsFull  => _estimatedBytes >= MaxBytes || _map.Count >= MaxUniqueTerms;
        public bool IsEmpty => _map.Count == 0;

        public OccurenceBuffer()
        {
            _map = new Dictionary<string, PostingStream>(1_500_000, StringComparer.Ordinal);
        }

        public void Add(string term, int docId)
        {
            if (!_map.TryGetValue(term, out var stream))
            {
                stream = new PostingStream();
                _map[term] = stream;
                _estimatedBytes += 48 + term.Length * 2; // string overhead
            }
            stream.Add(docId);
            _estimatedBytes += 2; // avg varint size
        }

        /// <summary>
        /// Sorts unique terms, writes each term's already-encoded posting bytes to a run file.
        /// Stores LastEncoded so the merger can stitch segments without re-decoding.
        ///
        /// Run file format per entry:
        ///   [int16 termByteLen][UTF-8 term bytes][int32 docCount][int32 byteLen][uint32 lastEncoded][encoded bytes]
        /// </summary>
        public string Flush(string runDir, int runIndex, Action<string> onProgress = null)
        {
            var terms = new string[_map.Count];
            _map.Keys.CopyTo(terms, 0);

            onProgress?.Invoke($"Sorting {terms.Length:N0} terms...");
            Array.Sort(terms, StringComparer.Ordinal);

            string path = Path.Combine(runDir, $"run_{runIndex:D4}.bin");
            onProgress?.Invoke($"Writing run {runIndex} ({terms.Length:N0} terms) to disk...");

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write,
                                           FileShare.None, 4 * 1024 * 1024))
            using (var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: false))
            {
                foreach (var term in terms)
                {
                    var stream    = _map[term];
                    byte[] tbytes = Encoding.UTF8.GetBytes(term);
                    bw.Write((short)tbytes.Length);
                    bw.Write(tbytes);
                    bw.Write(stream.Count);
                    bw.Write(stream.ByteLength);
                    bw.Write(stream.LastEncoded);   // needed for cross-run delta stitching
                    bw.Write(stream.Buffer, 0, stream.ByteLength);
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
