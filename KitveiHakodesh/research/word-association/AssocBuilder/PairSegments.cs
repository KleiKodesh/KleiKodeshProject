using System.Buffers.Binary;

namespace AssocBuilder;

/// <summary>
/// LSM-style pair accumulator — the same shape FtsLib uses for postings:
/// RAM buffer -> flush a sorted segment -> merge segments -> final merge.
///
/// Why not a SQLite upsert per pair
/// --------------------------------
/// The whole corpus generates ~5 billion pair increments. `ON CONFLICT DO
/// UPDATE SET n = n + 1` would mean 5 billion B-tree probes with random page
/// writes against an index far larger than page cache. The final TABLE is
/// small (313k words x ~200 kept associations); it is the counting that is
/// large, and you cannot know which pairs matter until you have counted them.
///
/// So: buffer in RAM, spill SORTED runs (sequential writes), then k-way merge
/// while summing duplicate keys (sequential reads). Peak memory is one buffer.
///
/// Key packing
/// -----------
/// A pair is two int32 word ids packed into one ulong (a &lt;&lt; 32 | b), always
/// with a &lt; b so each unordered pair has exactly one key. Sorting ulongs sorts
/// by (a, b) lexicographically for free, which is what lets the final merge
/// emit one word's whole row contiguously.
/// </summary>
internal sealed class PairSegments : IDisposable
{
    private const int RecordBytes = 12;      // ulong key + float weight

    private readonly string _dir;
    private readonly int    _capacity;
    private readonly string _prefix;

    // Open-addressed hash map, kept as parallel arrays. A Dictionary<ulong,float>
    // costs ~60 B/entry once boxed nodes and the bucket array are counted; this
    // is 16 B/slot and stays in cache far better on the increment path.
    private ulong[] _keys;
    private float[] _vals;
    private int     _mask;
    private int     _count;

    private readonly List<string> _segments = new();

    public IReadOnlyList<string> Segments => _segments;
    public long TotalSpilled { get; private set; }

    public PairSegments(string dir, int capacity, string prefix = "")
    {
        _dir      = dir;
        _prefix   = prefix;
        _capacity = capacity;

        // Power-of-two table, load factor <= 0.7.
        int size = 1024;
        while (size < capacity / 0.7) size <<= 1;
        _keys = new ulong[size];
        _vals = new float[size];
        _mask = size - 1;
        Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// Adds weight to the DIRECTED pair (a -> b), flushing a segment when the
    /// buffer fills.
    ///
    /// Directed, not canonicalized: the caller adds both (a,b) and (b,a) so that
    /// after the merge every word's row is complete and arrives contiguously in
    /// `a` order. Canonicalizing would halve the segment size but force the
    /// writer to transpose, which means random access over the whole table.
    /// </summary>
    public void Add(int a, int b, float w)
    {
        ulong key = ((ulong)(uint)a << 32) | (uint)b;

        // Key 0 would collide with the empty-slot marker; bias by 1 so a real
        // (0,0) pair is representable. Self-pairs are filtered upstream anyway,
        // but relying on that silently would be fragile.
        key++;

        int i = Hash(key) & _mask;
        while (true)
        {
            ulong k = _keys[i];
            if (k == key) { _vals[i] += w; return; }
            if (k == 0)
            {
                _keys[i] = key;
                _vals[i] = w;
                if (++_count >= _capacity) Flush();
                return;
            }
            i = (i + 1) & _mask;
        }
    }

    // Splitmix64 finalizer: cheap, and mixes the high bits (the `a` id) down
    // into the low bits that select the bucket. Without that, sequential word
    // ids cluster badly under linear probing.
    private static int Hash(ulong x)
    {
        x ^= x >> 30; x *= 0xbf58476d1ce4e5b9UL;
        x ^= x >> 27; x *= 0x94d049bb133111ebUL;
        x ^= x >> 31;
        return (int)x & int.MaxValue;
    }

    /// <summary>Writes the buffer as one key-sorted segment file and clears it.</summary>
    public void Flush()
    {
        if (_count == 0) return;

        // Compact live slots, then sort by key. Array.Sort on a ulong key array
        // with a float payload array is a dual-pivot quicksort over primitives —
        // no comparer delegate, no boxing.
        var keys = new ulong[_count];
        var vals = new float[_count];
        int n = 0;
        for (int i = 0; i < _keys.Length; i++)
            if (_keys[i] != 0) { keys[n] = _keys[i]; vals[n] = _vals[i]; n++; }
        Array.Sort(keys, vals);

        string path = Path.Combine(_dir, $"{_prefix}seg{_segments.Count:D4}.bin");
        var buf = new byte[RecordBytes * 8192];
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write,
                                      FileShare.None, 1 << 22, FileOptions.SequentialScan))
        {
            int o = 0;
            for (int i = 0; i < n; i++)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(o), keys[i]);
                BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(o + 8), vals[i]);
                o += RecordBytes;
                if (o == buf.Length) { fs.Write(buf, 0, o); o = 0; }
            }
            if (o > 0) fs.Write(buf, 0, o);
        }

        _segments.Add(path);
        TotalSpilled += n;
        Array.Clear(_keys);
        Array.Clear(_vals);
        _count = 0;
    }

    /// <summary>Segments are consumed by AssocWriter.MergeSegments, which merges
    /// across ALL shards' segments at once. Deleting them is the caller's job
    /// (the temp directory is removed wholesale after the build).</summary>
    public void Dispose() { }
}
