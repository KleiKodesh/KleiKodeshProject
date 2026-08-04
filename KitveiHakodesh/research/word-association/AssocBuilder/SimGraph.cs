using Microsoft.Data.Sqlite;

namespace AssocBuilder;

/// <summary>
/// Offline pass for ROADMAP item 2: precompute the top-K OVERLAP neighbors of
/// every word and store them as a second static table, so similar() becomes
/// the same indexed lookup as neighbors().
///
/// Measure: raw shared-feature count over each word's stored top-N association
/// rows (JoBimText overlap — measured 2.2x cosine on the gold set, FINDINGS).
/// With an inverted index over the truncated rows, a counting pass gives
///   count[c] = |rows_N(a) ∩ rows_N(c)|
/// exactly, for every c sharing at least one feature — a superset of the
/// query-time two-hop candidate sweep, at Σ_f indeg(f)² increments total.
///
/// The per-feature cap mirrors Rychly &amp; Kilgarriff's contexts-first skip
/// rule: a feature present in the top rows of very many words says nothing
/// about any of them, and skipping such features measured BETTER (+6% P@20,
/// +11% MRR at cap=100 on the Tanach), while bounding the pass at
/// cap × N increments per word.
///
/// Output schema, same DB:
///   sim (a INTEGER, rank INTEGER, b INTEGER, s REAL, PRIMARY KEY (a, rank))
///     WITHOUT ROWID — physically contiguous per word, like assoc.
/// Ties at equal overlap count are broken by the candidate's assoc weight sum
/// being irrelevant — we keep insertion by (count desc, id asc) for
/// determinism.
/// </summary>
internal static class SimGraph
{
    internal static long Build(string dbPath, int simTopK, int profileN,
                               int featureCap, int workers)
    {
        using var con = new SqliteConnection($"Data Source={dbPath}");
        con.Open();
        Exec(con, "pragma journal_mode=OFF; pragma synchronous=OFF; pragma cache_size=-262144;");

        int vocab;
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "select count(*) from word";
            vocab = Convert.ToInt32(cmd.ExecuteScalar());
        }

        // ── load truncated rows: word -> its first profileN association ids ──
        var rows = new int[vocab][];
        {
            var buf = new List<int>(profileN);
            int cur = -1;
            using var cmd = con.CreateCommand();
            cmd.CommandText = "select a, b from assoc where rank < @n order by a, rank";
            cmd.Parameters.AddWithValue("@n", profileN);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                int a = rd.GetInt32(0);
                if (a != cur)
                {
                    if (cur >= 0) rows[cur] = buf.ToArray();
                    buf.Clear();
                    cur = a;
                }
                buf.Add(rd.GetInt32(1));
            }
            if (cur >= 0) rows[cur] = buf.ToArray();
        }

        // ── inverted index over the truncated rows ───────────────────────────
        var indeg = new int[vocab];
        for (int a = 0; a < vocab; a++)
            if (rows[a] is { } r)
                foreach (int b in r)
                    indeg[b]++;
        var inv = new int[vocab][];
        var fill = new int[vocab];
        for (int b = 0; b < vocab; b++)
            inv[b] = new int[indeg[b]];
        for (int a = 0; a < vocab; a++)
            if (rows[a] is { } r)
                foreach (int b in r)
                    inv[b][fill[b]++] = a;

        int skipped = featureCap > 0 ? indeg.Count(d => d > featureCap) : 0;
        Console.WriteLine($"  sim pass: vocab {vocab:N0}, profileN {profileN}, " +
                          $"cap {featureCap} (skips {skipped:N0} hub features), topK {simTopK}");

        // ── counting pass, parallel over word shards ─────────────────────────
        var results = new (int B, int C)[vocab][];
        Parallel.For(0, workers, w =>
        {
            var count = new Dictionary<int, int>(4096);
            for (int a = w; a < vocab; a += workers)
            {
                if (rows[a] is not { Length: > 0 } r) continue;
                count.Clear();
                foreach (int f in r)
                {
                    var members = inv[f];
                    if (featureCap > 0 && members.Length > featureCap) continue;
                    foreach (int c in members)
                        if (c != a)
                            count[c] = count.TryGetValue(c, out int v) ? v + 1 : 1;
                }
                if (count.Count == 0) continue;
                var top = count.OrderByDescending(kv => kv.Value)
                               .ThenBy(kv => kv.Key)
                               .Take(simTopK)
                               .Select(kv => (kv.Key, kv.Value))
                               .ToArray();
                results[a] = top;
            }
        });

        // ── write ────────────────────────────────────────────────────────────
        Exec(con, "drop table if exists sim;");
        Exec(con, """
            create table sim (
                a    INTEGER NOT NULL,
                rank INTEGER NOT NULL,
                b    INTEGER NOT NULL,
                s    REAL    NOT NULL,
                PRIMARY KEY (a, rank)
            ) WITHOUT ROWID;
            """);
        long edges = 0;
        using (var tx = con.BeginTransaction())
        using (var cmd = con.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "insert into sim (a, rank, b, s) values (@a, @r, @b, @s)";
            var pa = cmd.Parameters.Add("@a", SqliteType.Integer);
            var pr = cmd.Parameters.Add("@r", SqliteType.Integer);
            var pb = cmd.Parameters.Add("@b", SqliteType.Integer);
            var ps = cmd.Parameters.Add("@s", SqliteType.Real);
            for (int a = 0; a < vocab; a++)
            {
                if (results[a] is not { } top) continue;
                for (int r = 0; r < top.Length; r++)
                {
                    pa.Value = a; pr.Value = r;
                    pb.Value = top[r].B; ps.Value = (double)top[r].C;
                    cmd.ExecuteNonQuery();
                    edges++;
                }
            }
            tx.Commit();
        }
        using (var tx = con.BeginTransaction())
        using (var cmd = con.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "insert or replace into meta (key, value) values (@k, @v)";
            var pk = cmd.Parameters.Add("@k", SqliteType.Text);
            var pv = cmd.Parameters.Add("@v", SqliteType.Text);
            void M(string k, object v) { pk.Value = k; pv.Value = v.ToString()!; cmd.ExecuteNonQuery(); }
            M("sim_topk", simTopK);
            M("sim_profile_n", profileN);
            M("sim_feature_cap", featureCap);
            M("sim_measure", "overlap");
            M("sim_edges", edges);
            tx.Commit();
        }
        Exec(con, "analyze;");
        return edges;
    }

    private static void Exec(SqliteConnection con, string sql)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
