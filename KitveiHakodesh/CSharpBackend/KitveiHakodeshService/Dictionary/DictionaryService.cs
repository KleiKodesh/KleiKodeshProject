using System.Text;
using KitveiHakodeshService.Ipc;
using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.Dictionary;

/// <summary>
/// Dictionary lookups over the bundled KitveiHakodesh dictionary catalog
/// (Dictionary/Dictionary.db). Ports every query that used to live in the Vue
/// dictionaryDb.sql.ts, so the frontend no longer sends SQL for the dev path.
///
/// Tables: word(id, headword), sense(id, word_id, nikud, text, source_id),
/// source_kind(id, name), link(word_id, target_id, kind_id),
/// link_kind(id, name, explanation). Read-only, Microsoft.Data.Sqlite.
/// </summary>
public sealed class DictionaryService(ILogger<DictionaryService> logger)
{
    private static readonly string DbPath =
        Path.Combine(AppContext.BaseDirectory, "Dictionary", "Dictionary.db");

    // Base sense projection shared by the exact/prefix/contains/abbrev queries.
    private const string SenseSelect =
        "SELECT w.headword, s.nikud, s.text, sk.name AS source, s.source_id " +
        "FROM word w JOIN sense s ON s.word_id = w.id " +
        "LEFT JOIN source_kind sk ON sk.id = s.source_id";

    private const string LinkSelect =
        "FROM link l " +
        "JOIN word w1 ON w1.id = l.word_id " +
        "JOIN word w2 ON w2.id = l.target_id " +
        "JOIN link_kind lk ON lk.id = l.kind_id";

    // Follows a spelling/inflection redirect: a variant word that has NO senses of
    // its own but points (via a 'כתיב' link) at a base entry — resolve to the
    // base's senses. Curated pairs only, so the resolved word is always the SAME
    // word typed in a different spelling/inflection. Same column shape as SenseSelect.
    private const string RedirectSelect =
        "SELECT wbase.headword, s.nikud, s.text, sk.name AS source, s.source_id " +
        "FROM word walias " +
        "JOIN link l ON l.word_id = walias.id " +
        "JOIN link_kind lk ON lk.id = l.kind_id AND lk.name = 'כתיב' " +
        "JOIN word wbase ON wbase.id = l.target_id " +
        "JOIN sense s ON s.word_id = wbase.id " +
        "LEFT JOIN source_kind sk ON sk.id = s.source_id";

    // ── Tier queries (driven by the frontend combinedLookup) ───────────────────

    public DictExactResult Exact(string term)
    {
        var rows = QuerySenses(SenseSelect + " WHERE w.headword = @t LIMIT 100", ("@t", term));
        if (rows.Count > 0) return new DictExactResult { Rows = rows, IsExact = true };
        // No direct senses — follow a curated spelling/inflection redirect to the base entry.
        var redirect = QuerySenses(RedirectSelect + " WHERE walias.headword = @t LIMIT 100", ("@t", term));
        if (redirect.Count > 0) return new DictExactResult { Rows = redirect, IsExact = true };
        bool exists = ScalarExists("SELECT 1 FROM word WHERE headword = @t LIMIT 1", ("@t", term));
        return new DictExactResult { Rows = [], IsExact = exists };
    }

    public List<SenseRow> Prefix(string term) => QuerySenses(
        SenseSelect + " WHERE w.headword LIKE @p AND w.headword != @t LIMIT 100",
        ("@p", term + "%"), ("@t", term));

    public List<SenseRow> Contains(string term) => QuerySenses(
        SenseSelect + " WHERE w.headword LIKE @c AND w.headword NOT LIKE @p LIMIT 100",
        ("@c", "%" + term + "%"), ("@p", term + "%"));

    // ── Related words ───────────────────────────────────────────────────────────

    public List<DictLink> Links(string term)
    {
        var outp = new List<DictLink>();
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT lk.name AS kind, w2.headword AS word " + LinkSelect +
                " WHERE w1.headword = @t AND lk.name != 'כתיב' ORDER BY lk.name, w2.headword";
            cmd.Parameters.AddWithValue("@t", term);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                outp.Add(new DictLink { Kind = r.GetString(0), Word = r.GetString(1) });
        }, "dictLinks");
        return outp;
    }

    public List<string> Synonyms(string term) => QueryStrings(
        "SELECT w2.headword AS word " + LinkSelect +
        " WHERE w1.headword = @t AND lk.name = 'נרדף' ORDER BY w2.headword", ("@t", term));

    public List<string> Variants(string term) => QueryStrings(
        "SELECT w2.headword AS word " + LinkSelect +
        " WHERE w1.headword = @t AND lk.name = 'כתיב' ORDER BY w2.headword", ("@t", term));

    // ── Spelling suggestions ────────────────────────────────────────────────────

    public List<string> SpellCandidates(string term)
    {
        var outp = new List<string>();
        if (string.IsNullOrEmpty(term)) return outp;
        var seen = new HashSet<string>();

        string frag2 = term[..Math.Min(2, term.Length)];
        foreach (var h in QueryStrings(
            "SELECT headword FROM word WHERE headword LIKE @p LIMIT 400", ("@p", frag2 + "%")))
            if (seen.Add(h)) outp.Add(h);

        if (term.Length >= 3)
        {
            string frag3 = term[..3];
            foreach (var h in QueryStrings(
                "SELECT headword FROM word WHERE headword LIKE @p LIMIT 200", ("@p", frag3 + "%")))
                if (seen.Add(h)) outp.Add(h);
        }
        return outp;
    }

    // ── Abbreviation tooltip (book view) ────────────────────────────────────────

    public DictAbbrevResult AbbrevSenses(List<string>? candidates)
    {
        if (candidates is null || candidates.Count == 0) return new DictAbbrevResult();

        // Exact matches first, in candidate order.
        foreach (var cand in candidates)
        {
            var rows = QuerySenses(SenseSelect + " WHERE w.headword = @t LIMIT 100", ("@t", cand));
            if (rows.Count > 0) return new DictAbbrevResult { Matched = cand, Rows = rows };
        }
        // Then %candidate% contains fallbacks.
        foreach (var cand in candidates)
        {
            var rows = QuerySenses(SenseSelect + " WHERE w.headword LIKE @c LIMIT 30", ("@c", "%" + cand + "%"));
            if (rows.Count > 0) return new DictAbbrevResult { Matched = cand, Rows = rows };
        }
        return new DictAbbrevResult();
    }

    // ── Ketiv (spelling) existence check ────────────────────────────────────────

    public List<string> KetivVariants(List<string>? candidates)
    {
        var outp = new List<string>();
        if (candidates is null || candidates.Count == 0) return outp;

        var sb = new StringBuilder("SELECT headword FROM word WHERE headword IN (");
        for (int i = 0; i < candidates.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("@c").Append(i);
        }
        sb.Append(')');

        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sb.ToString();
            for (int i = 0; i < candidates.Count; i++)
                cmd.Parameters.AddWithValue("@c" + i, candidates[i]);
            using var r = cmd.ExecuteReader();
            while (r.Read()) outp.Add(r.GetString(0));
        }, "dictKetivVariants");
        return outp;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private SqliteConnection Open()
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ConnectionString;
        var conn = new SqliteConnection(cs);
        conn.Open();
        return conn;
    }

    private void Run(Action action, string op)
    {
        if (!File.Exists(DbPath)) { logger.LogWarning("Dictionary DB not found at {Path}", DbPath); return; }
        try { action(); }
        catch (Exception ex) { logger.LogError(ex, "{Op} failed", op); }
    }

    private List<SenseRow> QuerySenses(string sql, params (string name, object value)[] ps)
    {
        var list = new List<SenseRow>();
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in ps) cmd.Parameters.AddWithValue(name, value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new SenseRow
                {
                    Headword = r.IsDBNull(0) ? "" : r.GetString(0),
                    Nikud = r.IsDBNull(1) ? null : r.GetString(1),
                    Text = r.IsDBNull(2) ? "" : r.GetString(2),
                    Source = r.IsDBNull(3) ? null : r.GetString(3),
                    SourceId = r.IsDBNull(4) ? null : r.GetInt32(4),
                });
            }
        }, "dict sense query");
        return list;
    }

    private List<string> QueryStrings(string sql, params (string name, object value)[] ps)
    {
        var list = new List<string>();
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in ps) cmd.Parameters.AddWithValue(name, value);
            using var r = cmd.ExecuteReader();
            while (r.Read()) if (!r.IsDBNull(0)) list.Add(r.GetString(0));
        }, "dict string query");
        return list;
    }

    private bool ScalarExists(string sql, params (string name, object value)[] ps)
    {
        bool exists = false;
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in ps) cmd.Parameters.AddWithValue(name, value);
            using var r = cmd.ExecuteReader();
            exists = r.Read();
        }, "dict scalar");
        return exists;
    }
}
