using System.Text;

namespace AssocBuilder;

/// <summary>
/// Pass 2: distance-weighted co-occurrence counting into LSM segments.
///
/// Semantics match build_index.py exactly so the two implementations stay
/// comparable:
///   - windows never cross a text-unit (line) boundary
///   - harmonic distance weighting, 1/d
///   - BM25 length normalization per unit, using the CORPUS-WIDE mean length
///
/// That last point is a correctness requirement under sharding: if each shard
/// normalized against its own mean, the weights would depend on how the work
/// happened to be divided. avgLen is computed in pass 1 and passed in.
/// </summary>
internal sealed class PairCounter
{
    private readonly string _db;
    private readonly Dictionary<string, int> _wordId;
    private readonly Dictionary<string, string> _prefixMap;
    private readonly int    _window;
    private readonly double _lengthNormB;
    private readonly double _avgLen;
    private readonly string _tmpDir;
    private readonly int    _bufferPairs;
    private readonly int    _vocabSize;

    public long TotalPairs { get; private set; }

    public PairCounter(string db, Dictionary<string, int> wordId,
                       Dictionary<string, string> prefixMap, int window,
                       double lengthNormB, double avgLen, string tmpDir,
                       int bufferPairs, int vocabSize)
    {
        _db = db; _wordId = wordId; _prefixMap = prefixMap;
        _window = window; _lengthNormB = lengthNormB; _avgLen = avgLen;
        _tmpDir = tmpDir; _bufferPairs = bufferPairs; _vocabSize = vocabSize;
    }

    /// <summary>
    /// Counts across all books, sharded by book over <paramref name="workers"/>
    /// threads. Returns the segment paths plus the per-word context totals and
    /// document frequencies the PPMI scorer needs.
    /// </summary>
    public (List<string> Segments, double[] Totals, double Grand, int[] DocFreq)
        Run(List<int> books, int workers)
    {
        var shards = Vocabulary.Shard(books, workers);
        var results = new (List<string> Segs, double[] Tot, double Grand, int[] Df, long Pairs)[shards.Count];

        Parallel.For(0, shards.Count,
            new ParallelOptions { MaxDegreeOfParallelism = workers },
            s => results[s] = CountShard(shards[s], s));

        var segments = new List<string>();
        var totals   = new double[_vocabSize];
        var docFreq  = new int[_vocabSize];
        double grand = 0;
        foreach (var r in results)
        {
            segments.AddRange(r.Segs);
            grand += r.Grand;
            TotalPairs += r.Pairs;
            for (int i = 0; i < _vocabSize; i++)
            {
                if (r.Tot[i] != 0) totals[i] += r.Tot[i];
                if (r.Df[i]  != 0) docFreq[i] += r.Df[i];
            }
        }
        return (segments, totals, grand, docFreq);
    }

    /// <summary>
    /// Clamps the requested per-shard buffer to what physical memory allows.
    ///
    /// Each hash slot costs 16 bytes (ulong key + float value) and the table is
    /// sized to a 0.7 load factor, so a slot's true cost is ~23 B. With N shards
    /// running concurrently the real commitment is N x buffer x 23 B — which is
    /// how a "60M" request became 11.5 GB and stalled the machine.
    ///
    /// Budget: at most 55% of currently-available RAM across all shards. The
    /// remainder has to cover the vocabulary map, the lemma map, SQLite's cache,
    /// and the OS. Spilling is a slowdown; exhausting RAM is a failure.
    /// </summary>
    private int BudgetPerShard(int requested)
    {
        const long BytesPerSlot = 23;
        int shards = Math.Max(1, Math.Min(Environment.ProcessorCount - 1, 32));

        long avail;
        try
        {
            // GC's own view of the container/machine limit is the most reliable
            // figure available without P/Invoke.
            var info = GC.GetGCMemoryInfo();
            avail = info.TotalAvailableMemoryBytes - info.MemoryLoadBytes;
            if (avail <= 0) avail = info.TotalAvailableMemoryBytes / 2;
        }
        catch { avail = 4L << 30; }

        long budget = (long)(avail * 0.55);
        int allowed = (int)Math.Max(2_000_000, budget / BytesPerSlot / shards);
        int use = Math.Min(requested, allowed);
        if (use < requested && shardId0Reported == 0 &&
            Interlocked.Exchange(ref shardId0Reported, 1) == 0)
        {
            Console.WriteLine($"    buffer clamped: {requested:N0} -> {use:N0} "
                            + $"pairs/shard ({shards} shards, "
                            + $"{avail / 1e9:F1} GB available)");
        }
        return use;
    }

    private int shardId0Reported;

    private (List<string>, double[], double, int[], long) CountShard(List<int> books, int shardId)
    {
        // The buffer is the ONLY thing standing between this and writing every
        // pair occurrence to disk. Aggregation in RAM is what collapses the
        // ~5 billion pair increments down toward the ~50M DISTINCT pairs that
        // actually exist: a pair seen 400 times should cost one record, not 400.
        //
        // Sizing it is a two-sided trap, and both sides have been hit here:
        //   too small -> 34 GB spilled on the full corpus, hours of extra I/O
        //   too large -> 7 shards x 60M slots exhausted RAM to 0.1 GB free and
        //                the process had to be killed (lemmatization concentrates
        //                pairs onto fewer, denser rows, so the same nominal
        //                budget costs far more)
        //
        // So the request is clamped against what the machine actually has, split
        // across the shards that run concurrently. A build that has to spill is
        // slow; a build that exhausts RAM does not finish at all.
        var segs = new PairSegments(_tmpDir, BudgetPerShard(_bufferPairs),
                                    $"s{shardId:D2}_");
        var totals  = new double[_vocabSize];
        var docFreq = new int[_vocabSize];
        double grand = 0;

        var toks = new List<string>(512);
        var buf  = new StringBuilder(64);
        var ids  = new List<int>(512);
        // Per-unit pair accumulation, so a repeated pair inside one line is
        // summed once before it reaches the segment buffer.
        var local = new Dictionary<long, float>(512);
        var seen  = new HashSet<int>(512);

        foreach (var line in Corpus.ReadLines(_db, books))
        {
            HebrewTokenizer.Tokenize(line, toks, buf);
            if (toks.Count < 2) continue;

            ids.Clear();
            foreach (var w in toks)
            {
                string key = _prefixMap.TryGetValue(w, out var s) ? s : w;
                if (_wordId.TryGetValue(key, out int id)) ids.Add(id);
            }
            if (ids.Count < 2) continue;

            seen.Clear();
            foreach (int id in ids)
                if (seen.Add(id)) docFreq[id]++;

            double norm = 1.0;
            if (_lengthNormB > 0 && _avgLen > 0)
                norm = 1.0 - _lengthNormB + _lengthNormB * (ids.Count / _avgLen);

            local.Clear();
            int n = ids.Count;
            for (int i = 0; i < n; i++)
            {
                int a = ids[i];
                int hi = Math.Min(n, i + _window + 1);
                for (int j = i + 1; j < hi; j++)
                {
                    int b = ids[j];
                    if (a == b) continue;
                    long k = a < b ? ((long)a << 32) | (uint)b
                                   : ((long)b << 32) | (uint)a;
                    float w = 1.0f / (j - i);
                    local[k] = local.TryGetValue(k, out float e) ? e + w : w;
                }
            }

            foreach (var (k, w0) in local)
            {
                float w = (float)(w0 / norm);
                int a = (int)(k >> 32), b = (int)(uint)k;
                // Both directions, so each word's row comes out complete after
                // the merge. Totals/grand accumulate symmetrically, matching the
                // Python reference exactly.
                segs.Add(a, b, w);
                segs.Add(b, a, w);
                totals[a] += w;
                totals[b] += w;
                grand += 2 * w;
            }
        }

        segs.Flush();
        return (segs.Segments.ToList(), totals, grand, docFreq, segs.TotalSpilled);
    }
}
