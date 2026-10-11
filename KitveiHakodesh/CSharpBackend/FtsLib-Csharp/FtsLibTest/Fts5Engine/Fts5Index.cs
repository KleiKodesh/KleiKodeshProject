using FtsLib;
using FtsLib.Search;
using FtsLib.Tokenization;
using Microsoft.Data.Sqlite;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace FtsLibTest.Fts5Engine
{
    /// <summary>
    /// PROTOTYPE: the FtsLib index storage replaced by SQLite FTS5. Everything else — tokenizer,
    /// query parser, expanders, intersection, snippets — is FtsLib's own code.
    ///
    /// Files in the index directory:
    ///   lines.fts5.db — contentless, detail=none FTS5 table: term → rowid (line id) lists and
    ///                   nothing else. Each row is the line's distinct normalized terms joined by
    ///                   spaces (FtsLib's Tokenizer.Extract), split again by FTS5's 'ascii'
    ///                   tokenizer, which only breaks on the spaces (normalized terms are Hebrew
    ///                   letters and lowercase a-z).
    ///   terms.db      — the term dictionary for expansion, in FtsLib's term_index shape so the
    ///                   PRODUCTION wildcard / fuzzy / grammar expanders run on it unchanged:
    ///                   term + count (= document frequency, from fts5vocab); the chunk-location
    ///                   columns are 0. Rows in ordinal term order.
    ///   terms.dat     — empty; SegmentHandle needs the file to exist.
    ///   terms.tgm     — the production trigram sidecar, built from terms.db.
    ///   terms.fmi     — FM-index over terms.db (FtsLibTest/Benchmarks/FmIndex.cs): exact
    ///                   infix / suffix / fuzzy candidate lookup, keyed by terms.db rowid.
    /// </summary>
    internal static class Fts5Index
    {
        public const string LinesDbName = "lines.fts5.db", TermsDbName = "terms.db",
                            TermsDatName = "terms.dat";

        public static string LinesDb(string dir) => Path.Combine(dir, LinesDbName);
        public static string TermsDb(string dir) => Path.Combine(dir, TermsDbName);
        public static string TermsDat(string dir) => Path.Combine(dir, TermsDatName);
        public static string TermsFmi(string dir) => Path.Combine(dir, "terms.fmi");

        const int LinesPerTransaction = 250_000;

        public static void Build(Func<IFtsCorpus> openCorpus, string dir, int limit = 0)
        {
            Directory.CreateDirectory(dir);
            foreach (string f in new[] { LinesDb(dir), TermsDb(dir), TermsDat(dir), TrigramIndex.SidecarPath(TermsDat(dir)), TermsFmi(dir) })
                if (File.Exists(f)) File.Delete(f);

            var total = Stopwatch.StartNew();
            long lines = 0;
            using (var conn = OpenReadWrite(LinesDb(dir)))
            {
                Execute(conn, "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA cache_size=-262144;");
                Execute(conn, "CREATE VIRTUAL TABLE line_fts USING fts5(terms, content='', detail=none, columnsize=0, tokenize='ascii')");

                var tokenizer = new Tokenizer();
                var text = new StringBuilder(4096);
                var tx = conn.BeginTransaction();
                using var insert = conn.CreateCommand();
                insert.CommandText = "INSERT INTO line_fts(rowid, terms) VALUES ($id, $terms)";
                var pId = insert.Parameters.Add("$id", SqliteType.Integer);
                var pTerms = insert.Parameters.Add("$terms", SqliteType.Text);

                using (var corpus = openCorpus())
                    foreach (var (id, content) in corpus.ReadDocuments(limit, CancellationToken.None))
                    {
                        text.Clear();
                        foreach (string term in tokenizer.Extract(content))
                        {
                            if (text.Length > 0) text.Append(' ');
                            text.Append(term);
                        }
                        pId.Value = id; pTerms.Value = text.ToString();
                        insert.Transaction = tx;
                        insert.ExecuteNonQuery();
                        if (++lines % LinesPerTransaction == 0)
                        {
                            tx.Commit(); tx.Dispose(); tx = conn.BeginTransaction();
                            Console.WriteLine($"  {lines:N0} lines  {total.Elapsed.TotalSeconds:F0}s");
                        }
                    }
                tx.Commit(); tx.Dispose();
                Console.WriteLine($"inserted {lines:N0} lines in {total.Elapsed.TotalSeconds:F0}s");

                var sw = Stopwatch.StartNew();
                Execute(conn, "INSERT INTO line_fts(line_fts) VALUES('optimize')");
                Execute(conn, "VACUUM");
                Console.WriteLine($"optimize + vacuum {sw.Elapsed.TotalSeconds:F0}s");
            }

            var swTerms = Stopwatch.StartNew();
            using (var terms = OpenReadWrite(TermsDb(dir)))
            {
                Execute(terms, "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA page_size=65536;");
                Execute(terms, "CREATE TABLE term_index(term TEXT NOT NULL, skip_offset INTEGER NOT NULL DEFAULT 0, " +
                               "skip_count INTEGER NOT NULL DEFAULT 0, offset INTEGER NOT NULL DEFAULT 0, " +
                               "length INTEGER NOT NULL DEFAULT 0, count INTEGER NOT NULL)");
                Execute(terms, $"ATTACH DATABASE '{LinesDb(dir).Replace("'", "''")}' AS lines");
                Execute(terms, "CREATE VIRTUAL TABLE temp.vocab USING fts5vocab(lines, line_fts, row)");
                // BINARY (UTF-8 memcmp) order = ordinal for BMP text, so rowid order is ordinal
                // term order, like a force-merged FtsLib segment.
                Execute(terms, "INSERT INTO term_index(term, count) SELECT term, doc FROM temp.vocab ORDER BY term");
                Execute(terms, "CREATE UNIQUE INDEX idx_term ON term_index(term)");
                Execute(terms, "ANALYZE");
            }
            File.WriteAllBytes(TermsDat(dir), Array.Empty<byte>());
            TrigramIndex.BuildFromDb(TermsDb(dir), TrigramIndex.SidecarPath(TermsDat(dir)));
            BuildFmSidecar(dir);
            Console.WriteLine($"term dictionary + trigram + FM sidecars {swTerms.Elapsed.TotalSeconds:F0}s");
            Console.WriteLine($"total build {total.Elapsed.TotalSeconds:F0}s");
            SqliteConnection.ClearAllPools();
        }

        /// <summary>FM-index over the term dictionary, rowid = terms.db rowid.</summary>
        public static void BuildFmSidecar(string dir)
        {
            var terms = new System.Collections.Generic.List<string>();
            var rowIds = new System.Collections.Generic.List<int>();
            using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder
                   { DataSource = TermsDb(dir), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ConnectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT rowid, term FROM term_index ORDER BY rowid";
                using var r = cmd.ExecuteReader();
                while (r.Read()) { rowIds.Add((int)r.GetInt64(0)); terms.Add(r.GetString(1)); }
            }
            FmIndex.Build(TermsFmi(dir), terms, rowIds);
        }

        static SqliteConnection OpenReadWrite(string path)
        {
            var c = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ConnectionString);
            c.Open();
            return c;
        }

        static void Execute(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 0;
            cmd.ExecuteNonQuery();
        }
    }
}
