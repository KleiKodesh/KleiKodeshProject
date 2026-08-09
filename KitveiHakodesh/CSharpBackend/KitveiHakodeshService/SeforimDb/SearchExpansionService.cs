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
    /// related forms as OR alternatives. A query that already contains an OR pipe
    /// is returned unchanged (the user is composing manual OR groups — injecting
    /// more would change their meaning).
    ///
    /// Affix markers compose rather than override: a token carrying grammar ('%')
    /// or fuzzy ('~', '~N') markers is peeled down to its bare word for lookup,
    /// and every alternative is re-wrapped in the SAME markers — so "%כי% %יצחק%"
    /// with expansion on yields "%כי% | %alt% … %יצחק% | %alt% …" and both
    /// features apply together. Wildcard tokens ('*', '?') are still left
    /// untouched: the wildcard already denotes an open-ended term set (and
    /// overrides '%'/'~' in the parser), so grafting stem alternatives onto it
    /// would widen the query in a direction the user did not ask for.
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

            // Peel the affix markers the parser understands so the lookup sees the
            // bare word, and keep them to re-apply to every alternative below.
            string core = PeelMarkers(tok, out string lead, out string trail);

            string bare = BareHebrew(core);
            if (bare.Length < 2)
            {
                // wildcard / non-Hebrew / too short — pass through
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
                // Re-wrap in the source token's markers so the alternatives carry
                // the same grammar/fuzzy semantics the user asked for.
                sb.Append(" | ").Append(lead).Append(a).Append(trail);
                changed = true;
            }
        }

        if (changed)
            logger.LogInformation("FTS expansion rewrote query ({Tokens} tokens)", tokens.Length);
        return changed ? sb.ToString() : query;
    }

    /// <summary>
    /// Splits <paramref name="tok"/> into the leading markers, the bare word, and
    /// the trailing markers, mirroring FtsLib's QueryParser.ParseToken so what we
    /// peel is exactly what the parser will later re-read:
    ///   1. '%' at either end (grammar prefix/suffix expansion) — Trim('%').
    ///   2. a trailing fuzzy suffix '~' or '~N', taken at the LAST '~' and only
    ///      when what follows is empty or a single digit 1-9.
    /// A token containing a wildcard ('*' or '?') is returned unpeeled: the
    /// wildcard overrides '%'/'~' in the parser and such tokens are not expanded.
    /// <paramref name="lead"/> + core + <paramref name="trail"/> always
    /// reconstructs the marker shape, so alternatives can be re-wrapped verbatim.
    /// </summary>
    private static string PeelMarkers(string tok, out string lead, out string trail)
    {
        lead = trail = "";
        if (tok.IndexOf('*') >= 0 || tok.IndexOf('?') >= 0) return tok;

        string core = tok;

        // '%' — grammar markers (each side is independent, as Trim('%') implies)
        if (core.StartsWith("%")) lead = "%";
        if (core.Length > 1 && core.EndsWith("%")) trail = "%";
        if (lead.Length > 0 || trail.Length > 0) core = core.Trim('%');

        // '~' / '~N' — fuzzy suffix, innermost (applies to the bare word)
        int tilde = core.LastIndexOf('~');
        if (tilde >= 0)
        {
            string suffix = core.Substring(tilde + 1);
            if (suffix.Length == 0 || (suffix.Length == 1 && suffix[0] >= '1' && suffix[0] <= '9'))
            {
                trail = core.Substring(tilde) + trail;
                core = core.Substring(0, tilde);
            }
        }

        return core;
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
