using FtsLib.Tokenization;
using Microsoft.Data.Sqlite;
using System;
using System.Diagnostics;
using System.IO;

namespace FtsLibTest
{
    /// <summary>
    /// Builds a SQLite FTS5 (detail=none, external-content) index using the REAL
    /// production scanner (TokenStream - every token, in order, repeats kept), so it
    /// can be timed head-to-head against SeforimIndex's
    /// posting-list engine on identical terms — isolating "which posting-list engine
    /// is faster" from "does the tokenizer differ". ids-only (no content fetch), same
    /// as SearchIds, for an apples-to-apples comparison.
    /// </summary>
    internal static class Fts5Compare
    {
        const int Cap = 20_000; // guards against pathological multi-MB rows

        public static void Build(string dbPath, string outDbPath)
        {
            if (File.Exists(outDbPath)) File.Delete(outDbPath);

            using var outConn = new SqliteConnection($"Data Source={outDbPath}");
            outConn.Open();
            Exec(outConn, "PRAGMA journal_mode=OFF");
            Exec(outConn, "PRAGMA synchronous=OFF");
            Exec(outConn, "CREATE TABLE line_search (lineId INTEGER PRIMARY KEY, content_bare TEXT NOT NULL)");
            Exec(outConn, "CREATE VIRTUAL TABLE line_fts USING fts5(content_bare, content='line_search', content_rowid='lineId', detail=none)");

            using var src = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Cache=Shared");
            src.Open();
            using var readCmd = src.CreateCommand();
            readCmd.CommandText = "SELECT id, content FROM line ORDER BY id";

            // TokenStream, not Tokenizer: Tokenizer.Extract returns a HashSet, which would drop
            // repeats and shuffle order, and an FTS5 column built from a de-duplicated term bag
            // has no term frequency left for bm25 to rank on. TokenStream is the same production
            // scanner (the highlighter's), emitting every token in order.
            var tok = new TokenStream();
            var sw  = Stopwatch.StartNew();
            long n  = 0;
            var bare = new System.Text.StringBuilder(1024);

            var tx = outConn.BeginTransaction();
            using var insertCmd = outConn.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText = "INSERT INTO line_search (lineId, content_bare) VALUES ($id, $bare)";
            var pId   = insertCmd.CreateParameter(); pId.ParameterName   = "$id";   insertCmd.Parameters.Add(pId);
            var pBare = insertCmd.CreateParameter(); pBare.ParameterName = "$bare"; insertCmd.Parameters.Add(pBare);

            using (var r = readCmd.ExecuteReader())
            {
                while (r.Read())
                {
                    long id = r.GetInt64(0);
                    string content = r.IsDBNull(1) ? "" : r.GetString(1);
                    if (content.Length > Cap) content = content.Substring(0, Cap);

                    bare.Clear();
                    foreach (var t in tok.Tokenize(content))
                    {
                        if (bare.Length > 0) bare.Append(' ');
                        bare.Append(t.NormSpan);
                    }

                    pId.Value   = id;
                    pBare.Value = bare.ToString();
                    insertCmd.ExecuteNonQuery();
                    n++;

                    if (n % 500_000 == 0)
                    {
                        tx.Commit();
                        tx.Dispose();
                        Console.WriteLine($"  ... {n:N0} rows in {sw.Elapsed.TotalSeconds:F1}s");
                        tx = outConn.BeginTransaction();
                        insertCmd.Transaction = tx;
                    }
                }
            }
            tx.Commit();
            tx.Dispose();
            Console.WriteLine($"line_search populated: {n:N0} rows in {sw.Elapsed.TotalSeconds:F1}s");

            var sw2 = Stopwatch.StartNew();
            Exec(outConn, "INSERT INTO line_fts(rowid, content_bare) SELECT lineId, content_bare FROM line_search");
            Console.WriteLine($"fts populated in {sw2.Elapsed.TotalSeconds:F1}s");

            var sw3 = Stopwatch.StartNew();
            Exec(outConn, "INSERT INTO line_fts(line_fts) VALUES('optimize')");
            Console.WriteLine($"optimized in {sw3.Elapsed.TotalSeconds:F1}s");

            outConn.Close();
            SqliteConnection.ClearAllPools();

            long preVacuum = new FileInfo(outDbPath).Length;
            Console.WriteLine($"pre-vacuum size: {preVacuum / 1024.0 / 1024:F1} MB");

            var sw4 = Stopwatch.StartNew();
            using (var vconn = new SqliteConnection($"Data Source={outDbPath}"))
            {
                vconn.Open();
                Exec(vconn, "VACUUM");
            }
            SqliteConnection.ClearAllPools();
            long finalSize = new FileInfo(outDbPath).Length;
            Console.WriteLine($"FINAL size: {finalSize:N0} bytes ({finalSize / 1024.0 / 1024:F1} MB) after vacuum ({sw4.Elapsed.TotalSeconds:F1}s)");
        }

        public static void Query(string outDbPath, string[] queries)
        {
            using var conn = new SqliteConnection($"Data Source={outDbPath};Mode=ReadOnly");
            conn.Open();
            foreach (var q in queries)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT rowid FROM line_fts WHERE line_fts MATCH $q";
                var p = cmd.CreateParameter(); p.ParameterName = "$q"; p.Value = q; cmd.Parameters.Add(p);

                var sw = Stopwatch.StartNew();
                int cnt = 0;
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) cnt++;
                sw.Stop();
                Console.WriteLine($"{q,-30}  {cnt,10:N0} ids  {sw.ElapsedMilliseconds,7} ms");
            }
        }

        private static void Exec(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }
}
