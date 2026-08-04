using System.Buffers.Binary;
using System.Text;
using Microsoft.Data.Sqlite;

namespace AssocBuilder;

/// <summary>
/// Final stage: merge the LSM segments, score with PPMI, prune to top-K, and
/// write a static SQLite table.
///
/// Streaming is what keeps this bounded. The merge emits keys in (a, b) order,
/// so one word's entire association row arrives contiguously — it is scored,
/// sorted, truncated to top-K, and inserted before the next word begins. Peak
/// memory is one word's row, not the corpus.
///
/// Output schema (static; read-only after the build):
///
///   meta  (key TEXT PRIMARY KEY, value TEXT)
///   word  (id INTEGER PRIMARY KEY, term TEXT UNIQUE, freq INTEGER)
///   assoc (a INTEGER, b INTEGER, w REAL, PRIMARY KEY (a, rank))
///
/// `assoc` is a WITHOUT ROWID table clustered on (a, rank), so one word's
/// associations are physically contiguous and already in descending weight
/// order — a top-N lookup is one index seek plus a short forward scan, which is
/// the SQLite equivalent of the CSR layout the Python version writes.
/// </summary>
internal static class AssocWriter
{
    private const int RecordBytes = 12;

    internal static long Write(string outPath, string[] vocab, int[] counts,
                               List<string> segments, double[] totals,
                               double grand, int topK, double minCooc,
                               double shift, BuildMeta meta,
                               bool pruneByLmi = false, string scorer = "ppmi")
    {
        if (File.Exists(outPath)) File.Delete(outPath);
        foreach (var side in new[] { "-wal", "-shm" })
            if (File.Exists(outPath + side)) File.Delete(outPath + side);

        using var con = new SqliteConnection($"Data Source={outPath}");
        con.Open();
        Exec(con, """
            pragma journal_mode=OFF;
            pragma synchronous=OFF;
            pragma cache_size=-262144;
            pragma temp_store=MEMORY;
            """);
        Exec(con, """
            create table meta (key TEXT PRIMARY KEY, value TEXT) WITHOUT ROWID;
            create table word (
                id   INTEGER PRIMARY KEY,
                term TEXT    NOT NULL,
                freq INTEGER NOT NULL
            );
            create table assoc (
                a    INTEGER NOT NULL,
                rank INTEGER NOT NULL,
                b    INTEGER NOT NULL,
                w    REAL    NOT NULL,
                PRIMARY KEY (a, rank)
            ) WITHOUT ROWID;
            """);

        // ── vocabulary ──────────────────────────────────────────────
        using (var tx = con.BeginTransaction())
        using (var cmd = con.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "insert into word (id, term, freq) values (@i, @t, @f)";
            var pi = cmd.Parameters.Add("@i", SqliteType.Integer);
            var pt = cmd.Parameters.Add("@t", SqliteType.Text);
            var pf = cmd.Parameters.Add("@f", SqliteType.Integer);
            for (int i = 0; i < vocab.Length; i++)
            {
                pi.Value = i; pt.Value = vocab[i]; pf.Value = counts[i];
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }

        // ── associations: merge -> score -> prune -> insert ──────────
        // alpha=0.75 on the context probability is the standard correction for
        // PMI's bias toward rare words (Levy & Goldberg 2015).
        const double Alpha = 0.75;
        var pCtx = new double[totals.Length];
        for (int i = 0; i < totals.Length; i++)
            pCtx[i] = totals[i] > 0 ? Math.Pow(totals[i] / grand, Alpha) : 0.0;

        long edges = 0;
        // (B, stored weight, prune key). With pruneByLmi the prune key is
        // LMI = count x PMI (Evert 2005) — what APSyn and JoBimText rank
        // contexts by. The idea: PMI's top ranks go to barely-attested pairs
        // (a count-3 pair can hit a huge PMI); multiplying by the count keeps
        // the well-SUPPORTED associations instead. The stored value stays PPMI
        // so downstream scoring is unchanged — only WHICH K survive differs.
        var row = new List<(int B, double W, double Key)>(1024);

        using (var tx = con.BeginTransaction())
        using (var cmd = con.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "insert into assoc (a, rank, b, w) values (@a, @r, @b, @w)";
            var pa = cmd.Parameters.Add("@a", SqliteType.Integer);
            var pr = cmd.Parameters.Add("@r", SqliteType.Integer);
            var pb = cmd.Parameters.Add("@b", SqliteType.Integer);
            var pw = cmd.Parameters.Add("@w", SqliteType.Real);

            void FlushRow(int a)
            {
                if (row.Count == 0) return;
                // Select the surviving K by the prune key, then order what is
                // KEPT by the stored weight — rank must reflect the value the
                // reader sees, whichever key chose the survivors.
                row.Sort((x, y) => y.Key.CompareTo(x.Key));
                int keep = Math.Min(topK, row.Count);
                var kept = row.GetRange(0, keep);
                kept.Sort((x, y) => y.W.CompareTo(x.W));
                for (int r = 0; r < keep; r++)
                {
                    pa.Value = a; pr.Value = r;
                    pb.Value = kept[r].B; pw.Value = kept[r].W;
                    cmd.ExecuteNonQuery();
                    edges++;
                }
                row.Clear();
            }

            // PairCounter emits each pair in BOTH directions, so the merge
            // already yields (a -> b) and (b -> a) as separate keys and every
            // word's row arrives complete and contiguous. Nothing to transpose.
            int curA = -1;
            foreach (var (a, b, c) in MergeSegments(segments))
            {
                if (a != curA) { FlushRow(curA); curA = a; }
                if (c < minCooc) continue;
                double ta = totals[a];
                if (ta <= 0 || pCtx[b] <= 0) continue;
                if (scorer == "logdice")
                {
                    // logDice (Rychly 2008): 14 + log2(2*c / (ta + tb)). Bounded
                    // above by 14 and built from ratios only, so scores compare
                    // across corpora of different sizes — PMI's grand-total term
                    // makes it corpus-size dependent. Support (c) is in the
                    // numerator, so no LMI-style prune correction is needed;
                    // rank by the score itself.
                    double ld = 14.0 + Math.Log2(2.0 * c / (ta + totals[b]));
                    if (ld > 0) row.Add((b, ld, ld));
                    continue;
                }
                double pmi = Math.Log2((c / grand) / ((ta / grand) * pCtx[b])) - shift;
                if (pmi > 0) row.Add((b, pmi, pruneByLmi ? c * pmi : pmi));
            }
            FlushRow(curA);
            tx.Commit();
        }

        // ── meta ────────────────────────────────────────────────────
        using (var tx = con.BeginTransaction())
        using (var cmd = con.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "insert into meta (key, value) values (@k, @v)";
            var pk = cmd.Parameters.Add("@k", SqliteType.Text);
            var pv = cmd.Parameters.Add("@v", SqliteType.Text);
            void M(string k, object v) { pk.Value = k; pv.Value = v.ToString()!; cmd.ExecuteNonQuery(); }
            M("corpus", meta.Corpus);
            M("base_only", meta.BaseOnly);
            M("books", meta.Books);
            M("units", meta.Units);
            M("tokens", meta.Tokens);
            M("window", meta.Window);
            M("topk", meta.TopK);
            M("min_count", meta.MinCount);
            M("length_norm_b", meta.LengthNormB);
            M("strip_prefixes", meta.StripPrefixes);
            M("min_stem_freq", meta.MinStemFreq);
            M("folded_forms", meta.FoldedForms);
            M("lemmatize", meta.Lemmatize);
            M("prune_by", meta.PruneByLmi ? "lmi" : "pmi");
            M("scorer", meta.Scorer);
            M("vocab_size", vocab.Length);
            M("edge_count", edges);
            M("builder", "AssocBuilder (C#/net10)");
            M("built_utc", DateTime.UtcNow.ToString("o"));
            tx.Commit();
        }

        Exec(con, "analyze; vacuum;");
        return edges;
    }

    /// <summary>
    /// K-way merge over all segment files, summing weights for equal keys.
    /// Standalone (not tied to one PairSegments instance) because pass 2 shards
    /// each produce their own segments and all of them merge together.
    /// </summary>
    private static IEnumerable<(int A, int B, float W)> MergeSegments(List<string> paths)
    {
        var readers = paths.Select(p => new Reader(p)).ToList();
        try
        {
            var heap = new List<(ulong Key, float W, int R)>(readers.Count);
            for (int r = 0; r < readers.Count; r++)
                if (readers[r].Next(out ulong k, out float w))
                    Push(heap, (k, w, r));

            ulong cur = 0; float acc = 0; bool have = false;
            while (heap.Count > 0)
            {
                var top = Pop(heap);
                if (readers[top.R].Next(out ulong nk, out float nw))
                    Push(heap, (nk, nw, top.R));

                if (have && top.Key == cur) { acc += top.W; continue; }
                if (have)
                {
                    ulong k = cur - 1;
                    yield return ((int)(k >> 32), (int)(uint)k, acc);
                }
                cur = top.Key; acc = top.W; have = true;
            }
            if (have)
            {
                ulong k = cur - 1;
                yield return ((int)(k >> 32), (int)(uint)k, acc);
            }
        }
        finally { foreach (var r in readers) r.Dispose(); }
    }

    private static void Push(List<(ulong Key, float W, int R)> h, (ulong, float, int) item)
    {
        h.Add(item);
        int i = h.Count - 1;
        while (i > 0)
        {
            int p = (i - 1) >> 1;
            if (h[p].Key <= h[i].Key) break;
            (h[p], h[i]) = (h[i], h[p]);
            i = p;
        }
    }

    private static (ulong Key, float W, int R) Pop(List<(ulong Key, float W, int R)> h)
    {
        var top = h[0];
        int last = h.Count - 1;
        h[0] = h[last];
        h.RemoveAt(last);
        int i = 0, n = h.Count;
        while (true)
        {
            int l = 2 * i + 1, r = l + 1, m = i;
            if (l < n && h[l].Key < h[m].Key) m = l;
            if (r < n && h[r].Key < h[m].Key) m = r;
            if (m == i) break;
            (h[m], h[i]) = (h[i], h[m]);
            i = m;
        }
        return top;
    }

    private static void Exec(SqliteConnection con, string sql)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private sealed class Reader : IDisposable
    {
        private readonly FileStream _fs;
        private readonly byte[] _buf = new byte[RecordBytes * 16384];
        private int _len, _pos;

        public Reader(string path) =>
            _fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                                 1 << 20, FileOptions.SequentialScan);

        public bool Next(out ulong key, out float w)
        {
            if (_pos >= _len)
            {
                _len = _fs.Read(_buf, 0, _buf.Length);
                _pos = 0;
                if (_len < RecordBytes) { key = 0; w = 0; return false; }
            }
            key = BinaryPrimitives.ReadUInt64LittleEndian(_buf.AsSpan(_pos));
            w   = BinaryPrimitives.ReadSingleLittleEndian(_buf.AsSpan(_pos + 8));
            _pos += RecordBytes;
            return true;
        }

        public void Dispose() => _fs.Dispose();
    }
}
