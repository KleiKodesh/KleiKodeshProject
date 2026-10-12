#if NET6_0_OR_GREATER
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Numerics;

namespace FtsLib.Fts5
{
    /// <summary>
    /// PROTOTYPE FM-index over a segment's term dictionary — a candidate replacement for the
    /// trigram sidecar (<c>seg.tgm</c>) for infix (<c>*x*</c>), suffix (<c>*x</c>) and fuzzy
    /// (Levenshtein) lookups. Returns EXACT matching term rowids. Read through a memory map.
    ///
    /// Text: <c>SEP t0 SEP t1 ... SEP t(k-1) SEP END</c>, one code per char
    /// (END=0, SEP=1, real chars 2.. in ordinal order).
    ///
    /// Suffix order: compare codes up to and including the first SEP/END ("stop"); if equal,
    /// by text position. The position tie-break keeps backward search valid when the pattern
    /// ends with SEP (suffix and fuzzy queries), and backward search never steps through any
    /// other SEP. Rows whose preceding char is SEP are term starts; they sort by term content,
    /// so Occ(SEP, row) - 1 is the term's lexicographic rank (row 0 is the END suffix).
    ///
    /// Parts:
    ///   * BWT as a Huffman-shaped binary wavelet tree ("FM-index for Dummies", Grabowski et
    ///     al. 2015): one bitvector per internal node, each split into 64-byte rank blocks =
    ///     u32 ones-before-block + 480 data bits. ~H0 bits per char plus 6.7%.
    ///   * Row → rowid section: for every suffix-array row, the rowid of the term its suffix
    ///     starts in, bit-packed. A match is a contiguous row range → one sequential pass.
    ///   * Rank → rowid table, only when rowids are not already rank + 1 (force-merged
    ///     segments write terms in sorted order, so normally absent).
    ///
    /// File layout (little-endian):
    ///   header  : magic 'FMI3' u32, textLength u32, sigma u32, termCount u32,
    ///             internalNodes u32, rowIdBits u32, bitvectorBytes u64,
    ///             flags u32 (bit 0: rowid == rank + 1), reserved u32                 [40 B]
    ///   chars   : sigma x u16 (code -> char; 0 for END/SEP)
    ///   C       : (sigma + 1) x u32
    ///   nodes   : internalNodes x { left i32, right i32, bitLength u32, pad u32, offset u64 }
    ///             child &gt;= 0 = internal node index, child &lt; 0 = leaf symbol -(code+1)
    ///   bitvectors : bitvectorBytes
    ///   rowIds  : textLength x rowIdBits, packed, + 8 zero bytes of padding
    ///   rankToRowId : termCount x u32, only when flags bit 0 is clear
    /// </summary>
    internal static class FmIndex
    {
        public const uint Magic = 0x33494D46; // 'FMI3'
        public const byte EndCode = 0, SeparatorCode = 1;
        const int HeaderBytes = 40, NodeBytes = 24;
        const int RankBlockBytes = 64, RankBlockDataBits = (RankBlockBytes - 4) * 8; // 480
        const int CodesPerSortKey = 10; // 6 bits each → 60 bits
        const uint FlagRowIdIsRankPlusOne = 1;

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

            // Rank (lexicographic order of the terms) → rowid.
            var byOrdinal = new int[terms.Count];
            for (int i = 0; i < byOrdinal.Length; i++) byOrdinal[i] = i;
            Array.Sort(byOrdinal, (a, b) => string.CompareOrdinal(terms[a], terms[b]));
            for (int r = 1; r < byOrdinal.Length; r++)
                if (terms[byOrdinal[r]] == terms[byOrdinal[r - 1]]) throw new InvalidDataException("FmIndex: duplicate term");
            bool rowIdIsRankPlusOne = true;
            for (int r = 0; r < byOrdinal.Length; r++)
                if (rowIds[byOrdinal[r]] != r + 1) { rowIdIsRankPlusOne = false; break; }

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
                bw.Write(nodeBits.Length); bw.Write(rowIdBits); bw.Write(bitvectorBytes);
                bw.Write(rowIdIsRankPlusOne ? FlagRowIdIsRankPlusOne : 0u); bw.Write(0u);
                foreach (char ch in charOf) bw.Write((ushort)ch);
                foreach (int v in c) bw.Write(v);
                for (int i = 0; i < nodeBits.Length; i++)
                {
                    bw.Write(tree.Left[i]); bw.Write(tree.Right[i]);
                    bw.Write((uint)nodeBits[i].Count); bw.Write(0u); bw.Write(nodeOffsets[i]);
                }
                foreach (var bits in nodeBits) WriteRankBlocks(bw, bits);

                var packed = new BitWriter();
                for (int row = 0; row < n; row++)
                    packed.AppendValue((uint)rowIdAtPosition[suffixArray[row]], rowIdBits);
                bw.Write(packed.ToBytes());
                bw.Write(0L); // padding: the reader decodes with 8-byte loads

                if (!rowIdIsRankPlusOne)
                    foreach (int i in byOrdinal) bw.Write(rowIds[i]);
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
                // 64 bits at a time: the block's 480 data bits are 7 full words and a half.
                for (int k = 0; start + k * 64L < end; k++)
                {
                    int width = (int)Math.Min(64, end - (start + k * 64L));
                    ulong word = bits.GetBits(start + k * 64L, width);
                    ones += (uint)BitOperations.PopCount(word);
                    for (int byteIndex = 0; byteIndex < 8 && k * 8 + byteIndex < data.Length; byteIndex++)
                        data[k * 8 + byteIndex] = (byte)(word >> (byteIndex * 8));
                }
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

            /// <summary>Appends the low <paramref name="bits"/> bits of value (bits ≤ 32).</summary>
            public void AppendValue(uint value, int bits)
            {
                if (((Count + bits) >> 6) + 1 >= _words.Length) Array.Resize(ref _words, _words.Length * 2);
                ulong v = bits == 64 ? value : value & ((1UL << bits) - 1);
                int offset = (int)(Count & 63);
                _words[Count >> 6] |= v << offset;
                if (offset + bits > 64) _words[(Count >> 6) + 1] |= v >> (64 - offset);
                Count += bits;
            }

            public bool Get(long i) => (_words[i >> 6] >> (int)(i & 63) & 1) != 0;

            /// <summary><paramref name="width"/> bits (1..64) starting at bit <paramref name="start"/>, LSB first.</summary>
            public ulong GetBits(long start, int width)
            {
                int offset = (int)(start & 63);
                long w = start >> 6;
                ulong value = _words[w] >> offset;
                if (offset != 0 && w + 1 < _words.Length) value |= _words[w + 1] << (64 - offset);
                return width == 64 ? value : value & ((1UL << width) - 1);
            }

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

        // ── Read (memory-mapped) ──────────────────────────────────────

        internal sealed unsafe class Reader : IDisposable
        {
            readonly MemoryMappedFile _map;
            readonly MemoryMappedViewAccessor _view;
            readonly byte* _base;
            bool _pointerAcquired, _disposed;
            readonly int _textLength, _rowIdBits;
            readonly int[] _c;
            readonly byte[] _codeOf = new byte[65536]; // 0 = char not in alphabet
            readonly int[] _left, _right;
            readonly int _rootNode;
            readonly long[] _nodeOffset;               // file offset of each node's rank blocks
            readonly int[][] _pathNodes; readonly bool[][] _pathBits;
            readonly long _rowIdsOffset, _rankTableOffset; // _rankTableOffset < 0: rowid == rank + 1

            public long FileBytes { get; }
            public long RowIdSectionBytes { get; }

            public Reader(string path)
            {
                FileBytes = new FileInfo(path).Length;
                var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                try
                {
                    _map = MemoryMappedFile.CreateFromFile(fs, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: false);
                }
                catch { fs.Dispose(); throw; }
                try
                {
                    _view = _map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                    byte* p = null;
                    _view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
                    _pointerAcquired = true;
                    _base = p + _view.PointerOffset;
                    var h = new ReadOnlySpan<byte>(_base, HeaderBytes);
                    if (BinaryPrimitives.ReadUInt32LittleEndian(h) != Magic) throw new InvalidDataException("FmIndex: bad magic");
                    _textLength = BinaryPrimitives.ReadInt32LittleEndian(h.Slice(4));
                    int sigma = BinaryPrimitives.ReadInt32LittleEndian(h.Slice(8));
                    int internalNodes = BinaryPrimitives.ReadInt32LittleEndian(h.Slice(16));
                    _rowIdBits = BinaryPrimitives.ReadInt32LittleEndian(h.Slice(20));
                    long bitvectorBytes = BinaryPrimitives.ReadInt64LittleEndian(h.Slice(24));
                    uint flags = BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(32));

                    long pos = HeaderBytes;
                    for (int code = 0; code < sigma; code++, pos += 2)
                        if (code > SeparatorCode) _codeOf[ReadU16(pos)] = (byte)code;
                    _c = new int[sigma + 1];
                    for (int i = 0; i <= sigma; i++, pos += 4) _c[i] = ReadI32(pos);

                    long bitvectorsStart = pos + (long)internalNodes * NodeBytes;
                    var tree = new HuffmanTree();
                    _nodeOffset = new long[internalNodes];
                    for (int i = 0; i < internalNodes; i++, pos += NodeBytes)
                    {
                        tree.Left.Add(ReadI32(pos)); tree.Right.Add(ReadI32(pos + 4));
                        _nodeOffset[i] = bitvectorsStart + ReadI64(pos + 16);
                    }
                    tree.Root = _rootNode = internalNodes - 1;
                    tree.ComputePaths(sigma);
                    _left = tree.Left.ToArray(); _right = tree.Right.ToArray();
                    _pathNodes = tree.PathNodes; _pathBits = tree.PathBits;
                    _rowIdsOffset = bitvectorsStart + bitvectorBytes;
                    long packedBytes = ((long)_textLength * _rowIdBits + 7) / 8;
                    RowIdSectionBytes = packedBytes + 8;
                    _rankTableOffset = (flags & FlagRowIdIsRankPlusOne) != 0 ? -1 : _rowIdsOffset + packedBytes + 8;
                }
                catch { Dispose(); throw; }
            }

            ushort ReadU16(long offset) => BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(_base + offset, 2));
            int ReadI32(long offset) => BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(_base + offset, 4));
            long ReadI64(long offset) => BinaryPrimitives.ReadInt64LittleEndian(new ReadOnlySpan<byte>(_base + offset, 8));

            /// <summary>Ones in the node's bitvector before position i — one 64-byte block.</summary>
            int Rank1(int node, int i)
            {
                int block = i / RankBlockDataBits, within = i - block * RankBlockDataBits;
                byte* b = _base + _nodeOffset[node] + (long)block * RankBlockBytes;
                int ones = *(int*)b;
                ulong* words = (ulong*)(b + 4);
                int fullWords = within >> 6;
                for (int k = 0; k < fullWords; k++) ones += BitOperations.PopCount(words[k]);
                int rest = within & 63;
                if (rest != 0) ones += BitOperations.PopCount(words[fullWords] & ((1UL << rest) - 1));
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

            int RowIdOfRank(int rank) => _rankTableOffset < 0 ? rank + 1 : ReadI32(_rankTableOffset + rank * 4L);

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
            /// (<paramref name="mustEndTerm"/>: terms ending with it). Backward search, then one
            /// pass over the packed row → rowid entries of the whole match range.
            /// </summary>
            public int[] FindRowIds(string pattern, bool mustEndTerm)
            {
                if (pattern.Length == 0 || !FindRowRange(pattern, mustEndTerm, out int first, out int end))
                    return Array.Empty<int>();
                int rows = end - first;
                ulong mask = (1UL << _rowIdBits) - 1;
                var ids = new int[rows];
                long bit = (long)first * _rowIdBits;
                byte* section = _base + _rowIdsOffset;
                for (int i = 0; i < rows; i++, bit += _rowIdBits)
                    ids[i] = (int)((*(ulong*)(section + (bit >> 3)) >> (int)(bit & 7)) & mask);
                Array.Sort(ids);
                int distinct = 0;
                for (int i = 0; i < ids.Length; i++)
                    if (i == 0 || ids[i] != ids[i - 1]) ids[distinct++] = ids[i];
                if (distinct != ids.Length) Array.Resize(ref ids, distinct);
                return ids;
            }

            // ── Fuzzy ─────────────────────────────────────────────────

            /// <summary>
            /// Every term within Levenshtein distance <paramref name="maxDistance"/> of
            /// <paramref name="query"/>, as (rowid, distance), sorted by rowid.
            ///
            /// Backward search from SEP walks a trie of the REVERSED terms: the range for the
            /// string "x SEP" holds every term ending with x, and its children are the distinct
            /// BWT symbols in that range (found by descending the wavelet tree once). One
            /// Levenshtein DP row per trie level, against the reversed query (edit distance is
            /// unchanged by reversing both strings); a branch dies when its row minimum exceeds
            /// the limit. A SEP child means x itself is a term.
            /// </summary>
            public List<(int RowId, int Distance)> FindFuzzy(string query, int maxDistance)
            {
                int m = query.Length;
                var reversedQuery = new byte[m];
                for (int j = 0; j < m; j++)
                {
                    byte code = _codeOf[query[m - 1 - j]];
                    reversedQuery[j] = code == 0 ? (byte)255 : code; // 255 never equals a BWT symbol
                }
                var search = new FuzzySearch(this, reversedQuery, maxDistance);
                var firstRow = new int[m + 1];
                for (int j = 0; j <= m; j++) firstRow[j] = j;
                search.Visit(_c[SeparatorCode], _c[SeparatorCode + 1], firstRow, depth: 0);
                search.Results.Sort((a, b) => a.RowId.CompareTo(b.RowId));
                return search.Results;
            }

            sealed class FuzzySearch
            {
                readonly Reader _reader; readonly byte[] _query; readonly int _maxDistance, _m;
                public readonly List<(int RowId, int Distance)> Results = new List<(int, int)>();
                // Per-depth scratch, reused across siblings: child symbols with their Occ ranges,
                // and the DP rows handed down to each child.
                // Depth never exceeds m + maxDistance: row[0] == depth, so deeper rows are pruned.
                readonly List<(byte Code, int OccLo, int OccHi)>[] _children;
                readonly int[][] _rows;

                public FuzzySearch(Reader reader, byte[] query, int maxDistance)
                {
                    _reader = reader; _query = query; _maxDistance = maxDistance; _m = query.Length;
                    _children = new List<(byte, int, int)>[_m + maxDistance + 2];
                    _rows = new int[_m + maxDistance + 3][];
                }

                public void Visit(int lo, int hi, int[] row, int depth)
                {
                    var children = _children[depth] ??= new List<(byte, int, int)>(64);
                    children.Clear();
                    EnumerateSymbols(_reader._rootNode, lo, hi, children);
                    var next = _rows[depth + 1] ??= new int[_m + 1];

                    foreach (var (code, occLo, occHi) in children)
                    {
                        if (code == SeparatorCode)
                        {
                            // x is a complete term (exactly one: terms are distinct).
                            if (depth > 0 && row[_m] <= _maxDistance)
                                Results.Add((_reader.RowIdOfRank(occLo - 1), row[_m])); // -1: END row 0
                            continue;
                        }
                        if (code == EndCode) continue;

                        next[0] = row[0] + 1;
                        int min = next[0];
                        for (int j = 1; j <= _m; j++)
                        {
                            int v = row[j - 1] + (_query[j - 1] == code ? 0 : 1);
                            int delete = row[j] + 1, insert = next[j - 1] + 1;
                            if (delete < v) v = delete;
                            if (insert < v) v = insert;
                            next[j] = v;
                            if (v < min) min = v;
                        }
                        if (min > _maxDistance) continue;

                        // The child's own children overwrite _rows[depth + 2], never this row,
                        // and siblings are processed after the child returns — so next is safe.
                        Visit(_reader._c[code] + occLo, _reader._c[code] + occHi, next, depth + 1);
                    }
                }

                /// <summary>Distinct symbols in BWT[lo, hi) with their Occ at lo and hi.</summary>
                void EnumerateSymbols(int node, int lo, int hi, List<(byte, int, int)> into)
                {
                    int onesLo = _reader.Rank1(node, lo), onesHi = _reader.Rank1(node, hi);
                    int zerosLo = lo - onesLo, zerosHi = hi - onesHi;
                    if (zerosHi > zerosLo) Descend(_reader._left[node], zerosLo, zerosHi, into);
                    if (onesHi > onesLo) Descend(_reader._right[node], onesLo, onesHi, into);
                }

                void Descend(int child, int lo, int hi, List<(byte, int, int)> into)
                {
                    if (child < 0) into.Add(((byte)(-child - 1), lo, hi));
                    else EnumerateSymbols(child, lo, hi, into);
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                if (_pointerAcquired) _view.SafeMemoryMappedViewHandle.ReleasePointer();
                _view?.Dispose();
                _map?.Dispose();
            }
        }
    }
}
#endif
