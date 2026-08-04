using System.Text;
using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.SeforimDb;

/// <summary>
/// Related-forms query expansion for full-text search, backed by the offline
/// expansion artifact (expansion-routed.db — see research/word-association,
/// FINDINGS 26/27: inflections of the same lexeme, sense-gated dictionary
/// synonyms, and Targum bridge pairs; every channel human-audited).
///
/// The rewrite uses FtsLib's native OR syntax: each plain query word becomes
/// "word | alt1 | alt2 …" — OR groups break at the next non-piped token, so
/// AND-of-ORs semantics is preserved and expanded terms flow through the
/// ordinary parse/normalise/highlight path with zero FtsLib changes.
///
/// DB resolution: SEARCH_EXPANSION_DB env var, else
/// "SearchExpansion/expansion-routed.db" next to the service binary. When the
/// file is absent the service is inert and queries pass through unchanged.
///
/// Schema: fold(surface PK, lemma, source), exp(lemma, rank, form, channel,
/// source, PK(lemma,rank)). Policy (stored in the artifact's meta and
/// enforced here): synonym rows are trusted only from the validated 'tanach'
/// side; inflection/bridge rows from both sides.
/// </summary>
public sealed class SearchExpansionService(ILogger<SearchExpansionService> logger)
{
    /// <summary>Max expansion terms added per query word. Expansion breadth is a
    /// per-term knob — it is NOT a result cap (results are never capped).</summary>
    public const int PerTermLimit = 5;

    private static readonly string DbPath = ResolveDbPath();

    private static string ResolveDbPath()
    {
        string? env = Environment.GetEnvironmentVariable("SEARCH_EXPANSION_DB");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        return Path.Combine(AppContext.BaseDirectory, "SearchExpansion", "expansion-routed.db");
    }

    public bool IsAvailable => File.Exists(DbPath);

    private static SqliteConnection Open()
    {
        var con = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        con.Open();
        return con;
    }

    /// <summary>
    /// Rewrites <paramref name="query"/> so each plain Hebrew word gains its
    /// related forms as OR alternatives. Tokens carrying query syntax (wildcard,
    /// fuzzy, grammar, quotes-as-syntax) are left untouched; a query that already
    /// contains an OR pipe is returned unchanged (the user is composing manual
    /// OR groups — injecting more would change their meaning).
    /// </summary>
    public string RewriteQuery(string query, int perTerm = PerTermLimit)
    {
        // Expansion must never break a search: any artifact problem (corrupt,
        // truncated, locked file) degrades to the unexpanded query. On the
        // streaming path an escaping exception would close the socket with no
        // response and read as a service outage on the frontend.
        try { return RewriteCore(query, perTerm); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FTS expansion rewrite failed — searching unexpanded");
            return query;
        }
    }

    private string RewriteCore(string query, int perTerm)
    {
        if (string.IsNullOrWhiteSpace(query) || !IsAvailable) return query;
        if (query.IndexOf('|') >= 0) return query;

        string[] tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder(query.Length * 2);
        bool changed = false;

        using var con = Open();
        using var foldCmd = con.CreateCommand();
        foldCmd.CommandText = "SELECT lemma FROM fold WHERE surface = @s";
        var foldP = foldCmd.Parameters.Add("@s", SqliteType.Text);
        using var expCmd = con.CreateCommand();
        expCmd.CommandText =
            "SELECT form, channel, source FROM exp WHERE lemma = @l ORDER BY rank";
        var expP = expCmd.Parameters.Add("@l", SqliteType.Text);

        foreach (string tok in tokens)
        {
            if (sb.Length > 0) sb.Append(' ');

            string bare = BareHebrew(tok);
            if (bare.Length < 2)
            {
                // carries syntax characters / non-Hebrew — pass through
                sb.Append(tok);
                continue;
            }

            foldP.Value = bare;
            string lemma = foldCmd.ExecuteScalar() as string ?? bare;

            expP.Value = lemma;
            var alts = new List<string>(perTerm);
            using (var rd = expCmd.ExecuteReader())
            {
                while (rd.Read() && alts.Count < perTerm)
                {
                    string form = rd.GetString(0);
                    string channel = rd.GetString(1);
                    string source = rd.GetString(2);
                    if (channel == "syn" && source != "tanach") continue;
                    if (form == bare || alts.Contains(form)) continue;
                    // A form the query parser would DROP (too short/long, non-
                    // Hebrew, whitespace) must never reach the query string: a
                    // dropped OR-alternative silently merges the next query
                    // word into this OR group (parser quirk), turning AND into
                    // OR. Same shape rule as the tokens we expand.
                    string bareForm = BareHebrew(form);
                    if (bareForm.Length < 2 || bareForm.Length > 29 || bareForm.Length != form.Length) continue;
                    alts.Add(form);
                }
            }

            sb.Append(tok);
            foreach (string a in alts)
            {
                sb.Append(" | ").Append(a);
                changed = true;
            }
        }

        if (changed)
            logger.LogInformation("FTS expansion rewrote query ({Tokens} tokens)", tokens.Length);
        return changed ? sb.ToString() : query;
    }

    /// <summary>The token stripped to bare Hebrew letters (final forms kept as
    /// typed). Nikud/cantillation marks (except maqaf, which is a separator) and
    /// intra-word quotes are STRIPPED — text pasted from pointed sources must
    /// still expand. Returns "" when any other character is present so
    /// syntax-bearing tokens are never expanded.</summary>
    private static string BareHebrew(string tok)
    {
        var sb = new StringBuilder(tok.Length);
        foreach (char c in tok)
        {
            if (c >= 'א' && c <= 'ת') sb.Append(c);
            else if (c >= '֑' && c <= 'ׇ' && c != '־') continue; // nikud/teamim
            else if (c == '"' || c == '\'' || c == '׳' || c == '״') continue;
            else return "";
        }
        return sb.ToString();
    }
}
