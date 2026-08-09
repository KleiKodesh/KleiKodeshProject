using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Text;

namespace KitveiHakodeshLib.Search
{
    /// <summary>
    /// Related-forms query expansion for the hosted FTS path — the net48 twin
    /// of the service's SearchExpansionService (see that class for the design
    /// and the artifact provenance; keep the two in sync).
    ///
    /// Each plain Hebrew query word becomes "word | alt1 | alt2 …" using
    /// FtsLib's native OR syntax, so expanded terms flow through the ordinary
    /// parse/normalise/highlight path with zero FtsLib changes.
    ///
    /// DB resolution: SEARCH_EXPANSION_DB env var, else
    /// "SearchExpansion/expansion-routed.db" next to the host binary. Absent
    /// file = inert (queries pass through unchanged).
    /// </summary>
    internal static class SearchExpansion
    {
        internal const int PerTermLimit = 5;

        private static readonly string DbPath = ResolveDbPath();

        private static string ResolveDbPath()
        {
            string env = Environment.GetEnvironmentVariable("SEARCH_EXPANSION_DB");
            if (!string.IsNullOrWhiteSpace(env)) return env;
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                "SearchExpansion", "expansion-routed.db");
        }

        internal static bool IsAvailable => File.Exists(DbPath);

        internal static string RewriteQuery(string query, int perTerm = PerTermLimit)
        {
            if (string.IsNullOrWhiteSpace(query) || !IsAvailable) return query;
            if (query.IndexOf('|') >= 0) return query;

            string[] tokens = query.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder(query.Length * 2);
            bool changed = false;

            using (var con = new SQLiteConnection(
                       new SQLiteConnectionStringBuilder { DataSource = DbPath, ReadOnly = true }
                           .ConnectionString))
            {
                con.Open();
                using (var foldCmd = new SQLiteCommand("SELECT lemma FROM fold WHERE surface = @s", con))
                using (var expCmd = new SQLiteCommand(
                           "SELECT form, channel, source FROM exp WHERE lemma = @l ORDER BY rank", con))
                {
                    var foldP = foldCmd.Parameters.Add("@s", System.Data.DbType.String);
                    var expP = expCmd.Parameters.Add("@l", System.Data.DbType.String);

                    foreach (string tok in tokens)
                    {
                        if (sb.Length > 0) sb.Append(' ');

                        // Peel affix markers so lookup sees the bare word; the
                        // markers are re-applied to each alternative below.
                        string lead, trail;
                        string core = PeelMarkers(tok, out lead, out trail);

                        string bare = BareHebrew(core);
                        if (bare.Length < 2)
                        {
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
                                // forms the query parser would drop must never
                                // reach the query (see the service twin)
                                string bareForm = BareHebrew(form);
                                if (bareForm.Length < 2 || bareForm.Length > 29 || bareForm.Length != form.Length) continue;
                                alts.Add(form);
                            }
                        }

                        sb.Append(tok);
                        foreach (string a in alts)
                        {
                            // re-wrap so alternatives carry the same
                            // grammar/fuzzy semantics as the source token
                            sb.Append(" | ").Append(lead).Append(a).Append(trail);
                            changed = true;
                        }
                    }
                }
            }

            return changed ? sb.ToString() : query;
        }

        /// <summary>
        /// Splits a token into leading markers + bare word + trailing markers,
        /// mirroring FtsLib's QueryParser.ParseToken ('%' grammar markers first,
        /// then a trailing fuzzy '~'/'~N'). Wildcard tokens are returned unpeeled
        /// and are therefore never expanded. See the service twin for rationale.
        /// </summary>
        private static string PeelMarkers(string tok, out string lead, out string trail)
        {
            lead = "";
            trail = "";
            if (tok.IndexOf('*') >= 0 || tok.IndexOf('?') >= 0) return tok;

            string core = tok;

            if (core.StartsWith("%")) lead = "%";
            if (core.Length > 1 && core.EndsWith("%")) trail = "%";
            if (lead.Length > 0 || trail.Length > 0) core = core.Trim('%');

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

        private static string BareHebrew(string tok)
        {
            var sb = new StringBuilder(tok.Length);
            foreach (char c in tok)
            {
                if (c >= 'א' && c <= 'ת') sb.Append(c);
                else if (c >= '֑' && c <= 'ׇ' && c != '־') continue; // nikud/teamim (maqaf is a separator)
                else if (c == '"' || c == '\'' || c == '׳' || c == '״') continue;
                else return "";
            }
            return sb.ToString();
        }
    }
}
