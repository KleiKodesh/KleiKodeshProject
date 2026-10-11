using Microsoft.Win32.SafeHandles;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace FtsLibTest
{
    /// <summary>
    /// PROTOTYPE FM-index over a segment's term dictionary — a candidate replacement for the
    /// trigram sidecar (<c>seg.tgm</c>) for infix (<c>*x*</c>) and suffix (<c>*x</c>) wildcards.
    /// Returns the EXACT matching term rowids for any pattern length, fully on disk
    /// (positioned reads, no mmap, only a few hundred bytes of metadata in memory).
    ///
    /// Text: <c>SEP t0 SEP t1 ... SEP t(k-1) SEP END</c>, one code per char
    /// (END=0, SEP=1, real chars 2.. in ordinal order).
    ///
    /// Suffix order: compare codes up to and including the first SEP/END ("stop"); if equal,
    /// by text position. The position tie-break keeps backward search valid when the pattern
    /// ends with SEP (suffix queries), and backward search never steps through any other SEP.
    ///
    /// Two parts:
    ///   * BWT as a Huffman-shaped binary wavelet tree ("FM-index for Dummies", Grabowski et
    ///     al. 2015): one bitvector per internal node, each split into 64-byte rank blocks =
    ///     u32 ones-before-block + 480 data bits, so one rank costs ONE 64-byte read. Frequent
    ///     letters have short codes, so the tree costs ~H0 bits per char plus 6.7%.
    ///     Used only by backward search: Occ = one read per code bit, per pattern char.
    ///   * Row → rowid section: for every suffix-array row, the rowid of the term its suffix
    ///     starts in, bit-packed at ceil(log2(maxRowId+1)) bits. A match is a contiguous row
    ///     range, so locating ALL matches is one sequential read.
    ///
    /// File layout (little-endian):
    ///   header  : magic 'FMI2' u32, textLength u32, sigma u32, termCount u32,
    ///             internalNodes u32, rowIdBits u32, bitvectorBytes u64, reserved u64  [40 B]
    ///   chars   : sigma x u16 (code -> char; 0 for END/SEP)
    ///   C       : (sigma + 1) x u32
    ///   nodes   : internalNodes x { left i32, right i32, bitLength u32, pad u32, offset u64 }
    ///             child &gt;= 0 = internal node index, child &lt; 0 = leaf symbol -(code+1)
    ///   bitvectors : bitvectorBytes
    ///   rowIds  : textLength x rowIdBits, packed, + 8 zero bytes of padding
    /// </summary>
    internal static class FmIndex
    {
        public const uint Magic = 0x32494D46; // 'FMI2'
        public const byte EndCode = 0, SeparatorCode = 1;
        const int HeaderBytes = 40, NodeBytes = 24;
        const int RankBlockBytes = 64, RankBlockDataBits = (RankBlockBytes - 4) * 8; // 480
        const int CodesPerSortKey = 10; // 6 bits each → 60 bits

        // ── Build ─────────────────────────────────────────────────────

        public static void Build(string path, IReadOnlyList<string> terms, IReadOnlyList<int> rowIds)
        {
            // Alphabet in ordinal order (code order == ordinal == SQLite BINARY for BMP text).
            var present = new bool[65536];
            foreach (string t in terms)
            {
                if (t.Length == 0) throw new InvalidDataException("FmIndex: empty term");
                foreach (char ch in t) present[ch] = true;
            }
            var codeOf = new byte[65536];
            var charOf = new List<char> { '\0', '\0' };
            for (int ch = 0; ch < 65536; ch++)
                if (present[ch]) { codeOf[ch] = (byte)charOf.Count; charOf.Add((char)ch); }
            int sigma = charOf.Count;
            if (sigma > 64) throw new NotSupportedException($"FmIndex prototype: sigma {sigma} > 64 (6-bit sort keys)");

            // Text, plus the rowid of the term each position belongs to.
            long textLong = 2;
            foreach (string t in terms) textLong += t.Length + 1;
            if (textLong > int.MaxValue) throw new NotSupportedException("FmIndex prototype: text too long");
            int n = (int)textLong;
            var text = new byte[n];
            var rowIdAtPosition = new int[n]; // 0 on SEP/END
            int maxRowId = 0, w = 0;
            for (int i = 0; i < terms.Count; i++)
            {
                if (rowIds[i] <= 0) throw new NotSupportedException("FmIndex prototype: rowid must be positive");
                maxRowId = Math.Max(maxRowId, rowIds[i]);
                text[w++] = SeparatorCode;
                foreach (char ch in terms[i]) { rowIdAtPosition[w] = rowIds[i]; text[w++] = codeOf[ch]; }
            }
            text[w++] = SeparatorCode;
            text[w] = EndCode;

            int[] suffixArray = BuildSuffixArray(text);

            var bwt = new byte[n];
            for (int row = 0; row < n; row++)
            {
                int p = suffixArray[row];
                bwt[row] = text[p == 0 ? n - 1 : p - 1];
            }

            var symbolCount = new long[sigma];
            foreach (byte b in text) symbolCount[b]++;
            var c = new int[sigma + 1];
            for (int s = 0; s < sigma; s++) c[s + 1] = c[s] + (int)symbolCount[s];

            // Huffman-shaped wavelet tree.
            var tree = HuffmanTree.Build(symbolCount);
            var nodeBits = new BitWriter[tree.Left.Count];
            for (int i = 0; i < nodeBits.Length; i++) nodeBits[i] = new BitWriter();
            foreach (byte sym in bwt)
            {
                var pathNodes = tree.PathNodes[sym]; var pathBits = tree.PathBits[sym];
                for (int d = 0; d < pathNodes.Length; d++) nodeBits[pathNodes[d]].Append(pathBits[d]);
            }

            int rowIdBits = 32 - BitOperations.LeadingZeroCount((uint)maxRowId);

            string tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            using (var bw = new BinaryWriter(fs))
            {
                var nodeOffsets = new long[nodeBits.Length];
                long bitvectorBytes = 0;
                for (int i = 0; i < nodeBits.Length; i++)
                {
                    nodeOffsets[i] = bitvectorBytes;
                    bitvectorBytes += (nodeBits[i].Count / RankBlockDataBits + 1) * (long)RankBlockBytes;
                }

                bw.Write(Magic); bw.Write(n); bw.Write(sigma); bw.Write(terms.Count);
                bw.Write(nodeBits.Length); bw.Write(rowIdBits); bw.Write(bitvectorBytes); bw.Write(0L);
                foreach (char ch in charOf) bw.Write((ushort)ch);
                foreach (int v in c) bw.Write(v);
                for (int i = 0; i < nodeBits.Length; i++)
                {
                    bw.Write(tree.Left[i]); bw.Write(tree.Right[i]);
                    bw.Write((uint)nodeBits[i].Count); bw.Write(0u); bw.Write(nodeOffsets[i]);
                }
                foreach (var bits in nodeBits) WriteRankBlocks(bw, bits);

                // Row → rowid, packed.
                var packed = new BitWriter();
                for (int row = 0; row < n; row++)
                    packed.AppendValue((uint)rowIdAtPosition[suffixArray[row]], rowIdBits);
                bw.Write(packed.ToBytes());
                bw.Write(0L); // padding: the reader decodes with 8-byte loads
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        static void WriteRankBlocks(BinaryWriter bw, BitWriter bits)
        {
            long blocks = bits.Count / RankBlockDataBits + 1;
            var data = new byte[RankBlockBytes - 4];
            uint onesBefore = 0;
            for (long b = 0; b < blocks; b++)
            {
                Array.Clear(data);
                long start = b * RankBlockDataBits;
                long end = Math.Min(bits.Count, start + RankBlockDataBits);
                uint ones = 0;
                for (long i = start; i < end; i++)
                    if (bits.Get(i)) { int j = (int)(i - start); data[j >> 3] |= (byte)(1 << (j & 7)); ones++; }
                bw.Write(onesBefore); bw.Write(data);
                onesBefore += ones;
            }
        }

        sealed class BitWriter
        {
            ulong[] _words = new ulong[1024];
            public long Count { get; private set; }

            public void Append(bool bit)
            {
                if ((Count >> 6) >= _words.Length) Array.Resize(ref _words, _words.Length * 2);
                if (bit) _words[Count >> 6] |= 1UL << (int)(Count & 63);
                Count++;
            }

            public void AppendValue(uint value, int bits)
            {
                for (int k = 0; k < bits; k++) Append(((value >> k) & 1) != 0);
            }

            public bool Get(long i) => (_words[i >> 6] >> (int)(i & 63) & 1) != 0;

            public byte[] ToBytes()
            {
                var bytes = new byte[(Count + 7) / 8];
                for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(_words[i >> 3] >> ((i & 7) * 8));
                return bytes;
            }
        }

        /// <summary>Huffman tree over the codes; internal nodes indexed 0..sigma-2, root last.</summary>
        sealed class HuffmanTree
        {
            public readonly List<int> Left = new List<int>(), Right = new List<int>();
            public int Root;
            public int[][] PathNodes; public bool[][] PathBits;

            public static HuffmanTree Build(long[] frequency)
            {
                var t = new HuffmanTree();
                var queue = new PriorityQueue<int, (long, int)>(); // child encoding, (weight, tie-break)
                int tie = 0;
                for (int s = 0; s < frequency.Length; s++) queue.Enqueue(-(s + 1), (Math.Max(1, frequency[s]), tie++));
                while (queue.Count > 1)
                {
                    queue.TryDequeue(out int a, out var wa);
                    queue.TryDequeue(out int b, out var wb);
                    t.Left.Add(a); t.Right.Add(b);
                    queue.Enqueue(t.Left.Count - 1, (wa.Item1 + wb.Item1, tie++));
                }
                t.Root = t.Left.Count - 1;
                t.ComputePaths(frequency.Length);
                return t;
            }

            public void ComputePaths(int sigma)
            {
                PathNodes = new int[sigma][]; PathBits = new bool[sigma][];
                var nodes = new List<int>(); var bits = new List<bool>();
                void Walk(int node)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        int child = side == 0 ? Left[node] : Right[node];
                        nodes.Add(node); bits.Add(side == 1);
                        if (child < 0) { int sym = -child - 1; PathNodes[sym] = nodes.ToArray(); PathBits[sym] = bits.ToArray(); }
                        else Walk(child);
                        nodes.RemoveAt(nodes.Count - 1); bits.RemoveAt(bits.Count - 1);
                    }
                }
                Walk(Root);
            }
        }

        /// <summary>
        /// Sorts all suffixes under the "compare up to the first stop, then by position" order.
        /// Terms are short (max ~30 chars), so a packed 10-code key settles almost every
        /// comparison; equal-key runs are finished with a direct comparison.
        /// </summary>
        static int[] BuildSuffixArray(byte[] text)
        {
            int n = text.Length;
            var distanceToStop = new int[n]; // codes from i up to and including the first SEP/END
            for (int i = n - 1; i >= 0; i--)
                distanceToStop[i] = text[i] <= SeparatorCode ? 1 : distanceToStop[i + 1] + 1;

            var keys = new ulong[n];
            var suffixArray = new int[n];
            for (int i = 0; i < n; i++)
            {
                int len = Math.Min(distanceToStop[i], CodesPerSortKey);
                ulong key = 0;
                for (int k = 0; k < len; k++) key |= (ulong)text[i + k] << (6 * (CodesPerSortKey - 1 - k));
                keys[i] = key;
                suffixArray[i] = i;
            }
            Array.Sort(keys, suffixArray);

            var tail = Comparer<int>.Create((a, b) =>
            {
                int la = distanceToStop[a], lb = distanceToStop[b];
                int common = Math.Min(la, lb);
                for (int k = CodesPerSortKey; k < common; k++)
                {
                    int d = text[a + k] - text[b + k];
                    if (d != 0) return d;
                }
                return la != lb ? (la < lb ? -1 : 1) : a.CompareTo(b);
            });
            var byPosition = Comparer<int>.Default;
            for (int runStart = 0; runStart < n;)
            {
                int runEnd = runStart + 1;
                while (runEnd < n && keys[runEnd] == keys[runStart]) runEnd++;
                if (runEnd - runStart > 1)
                {
                    bool stopInsideKey = distanceToStop[suffixArray[runStart]] <= CodesPerSortKey;
                    Array.Sort(suffixArray, runStart, runEnd - runStart, stopInsideKey ? byPosition : tail);
                }
                runStart = runEnd;
            }
            return suffixArray;
        }

        // ── Read (on disk: positioned reads, no mmap) ─────────────────

        internal sealed class Reader : IDisposable
        {
            readonly SafeFileHandle _file;
            readonly int _textLength, _rowIdBits;
            readonly int[] _c;
            readonly byte[] _codeOf = new byte[65536]; // 0 = char not in alphabet
            readonly long[] _nodeOffset;               // absolute file offset of each node's rank blocks
            readonly int[][] _pathNodes; readonly bool[][] _pathBits;
            readonly long _rowIdsOffset;

            public long FileBytes { get; }
            public long RowIdSectionBytes => FileBytes - _rowIdsOffset;

            public Reader(string path)
            {
                FileBytes = new FileInfo(path).Length;
                _file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
                try
                {
                    var h = new byte[HeaderBytes];
                    RandomAccess.Read(_file, h, 0);
                    if (BinaryPrimitives.ReadUInt32LittleEndian(h) != Magic) throw new InvalidDataException("FmIndex: bad magic");
                    _textLength = BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(4));
                    int sigma = BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(8));
                    int internalNodes = BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(16));
                    _rowIdBits = BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(20));
                    long bitvectorBytes = BinaryPrimitives.ReadInt64LittleEndian(h.AsSpan(24));

                    var meta = new byte[sigma * 2 + (sigma + 1) * 4 + internalNodes * NodeBytes];
                    RandomAccess.Read(_file, meta, HeaderBytes);
                    var s = meta.AsSpan();
                    int pos = 0;
                    for (int code = 0; code < sigma; code++, pos += 2)
                        if (code > SeparatorCode) _codeOf[BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(pos))] = (byte)code;
                    _c = new int[sigma + 1];
                    for (int i = 0; i <= sigma; i++, pos += 4) _c[i] = BinaryPrimitives.ReadInt32LittleEndian(s.Slice(pos));

                    long bitvectorsStart = HeaderBytes + meta.Length;
                    var tree = new HuffmanTree();
                    _nodeOffset = new long[internalNodes];
                    for (int i = 0; i < internalNodes; i++, pos += NodeBytes)
                    {
                        tree.Left.Add(BinaryPrimitives.ReadInt32LittleEndian(s.Slice(pos)));
                        tree.Right.Add(BinaryPrimitives.ReadInt32LittleEndian(s.Slice(pos + 4)));
                        _nodeOffset[i] = bitvectorsStart + BinaryPrimitives.ReadInt64LittleEndian(s.Slice(pos + 16));
                    }
                    tree.Root = internalNodes - 1;
                    tree.ComputePaths(sigma);
                    _pathNodes = tree.PathNodes; _pathBits = tree.PathBits;
                    _rowIdsOffset = bitvectorsStart + bitvectorBytes;
                }
                catch { _file.Dispose(); throw; }
            }

            /// <summary>Ones in the node's bitvector before position i — one 64-byte read.</summary>
            int Rank1(int node, int i)
            {
                int block = i / RankBlockDataBits, within = i - block * RankBlockDataBits;
                Span<byte> buf = stackalloc byte[RankBlockBytes];
                RandomAccess.Read(_file, buf, _nodeOffset[node] + (long)block * RankBlockBytes);
                int ones = BinaryPrimitives.ReadInt32LittleEndian(buf);
                var data = buf.Slice(4);
                int fullBytes = within >> 3, k = 0;
                for (; k + 8 <= fullBytes; k += 8) ones += BitOperations.PopCount(BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(k)));
                for (; k < fullBytes; k++) ones += BitOperations.PopCount((uint)data[k]);
                int rest = within & 7;
                if (rest != 0) ones += BitOperations.PopCount((uint)(data[fullBytes] & ((1 << rest) - 1)));
                return ones;
            }

            /// <summary>Occurrences of <paramref name="code"/> in BWT[0, row): one rank per code bit.</summary>
            int Occ(byte code, int row)
            {
                var nodes = _pathNodes[code]; var bits = _pathBits[code];
                for (int d = 0; d < nodes.Length; d++)
                {
                    int ones = Rank1(nodes[d], row);
                    row = bits[d] ? ones : row - ones;
                }
                return row;
            }

            /// <summary>Backward search. Returns false when the pattern does not occur.</summary>
            bool FindRowRange(string pattern, bool mustEndTerm, out int first, out int end)
            {
                first = 0; end = _textLength;
                for (int k = pattern.Length - (mustEndTerm ? 0 : 1); k >= 0; k--)
                {
                    byte code;
                    if (k == pattern.Length) code = SeparatorCode;      // only when mustEndTerm
                    else { code = _codeOf[pattern[k]]; if (code == 0) return false; }
                    first = _c[code] + Occ(code, first);
                    end = _c[code] + Occ(code, end);
                    if (first >= end) return false;
                }
                return true;
            }

            /// <summary>
            /// Sorted distinct rowids of terms containing <paramref name="pattern"/>
            /// (<paramref name="mustEndTerm"/>: terms ending with it). Backward search, then ONE
            /// sequential read of the packed row → rowid entries for the whole match range.
            /// </summary>
            public int[] FindRowIds(string pattern, bool mustEndTerm)
            {
                if (pattern.Length == 0 || !FindRowRange(pattern, mustEndTerm, out int first, out int end))
                    return Array.Empty<int>();
                int rows = end - first;
                long firstBit = (long)first * _rowIdBits;
                long startByte = firstBit >> 3;
                long endByte = ((long)end * _rowIdBits + 7) >> 3;
                var raw = new byte[endByte - startByte + 8];  // + padding for 8-byte loads
                RandomAccess.Read(_file, raw, _rowIdsOffset + startByte);
                ulong mask = (1UL << _rowIdBits) - 1;
                var ids = new int[rows];
                long bit = firstBit - (startByte << 3);
                for (int i = 0; i < rows; i++, bit += _rowIdBits)
                    ids[i] = (int)((BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan((int)(bit >> 3))) >> (int)(bit & 7)) & mask);
                Array.Sort(ids);
                int distinct = 0;
                for (int i = 0; i < ids.Length; i++)
                    if (i == 0 || ids[i] != ids[i - 1]) ids[distinct++] = ids[i];
                if (distinct != ids.Length) Array.Resize(ref ids, distinct);
                return ids;
            }

            public void Dispose() => _file.Dispose();
        }
    }
}
