using FtsLib.Search;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using KitveiHakodesh.Core.Common;

namespace KitveiHakodesh.Core.SeforimDbFullTextSearch
{
    /// <summary>
    /// Expands a full-text query's plain Hebrew words to their RELATED FORMS — not synonyms in
    /// general, not stemming, not wildcards: inflections of the same lexeme, sense-gated
    /// dictionary synonyms, and Targum bridge pairs, every channel human-audited into the
    /// offline artifact (expansion-routed.db; see research/word-association, FINDINGS 26/27).
    ///
    /// The rewrite uses the engine's own OR syntax — each word becomes "word | alt1 | alt2" —
    /// so OR groups break at the next unpiped token, AND-of-ORs semantics is preserved, and the
    /// expanded terms flow through the ordinary parse/normalise/highlight path with zero engine
    /// changes.
    ///
    /// The artifact ships beside the app and is found by probing (rule 2); when it is absent
    /// this whole class is inert and queries pass through unchanged — a missing artifact is a
    /// state, not an error. The old SEARCH_EXPANSION_DB env override is DELETED, not migrated:
    /// Core reads no environment, and a caller with a special artifact passes its path in.
    ///
    /// Schema: fold(surface, lemma, weight, source, PK(surface, lemma)), exp(lemma, rank, form,
    /// channel, source, PK(lemma, rank)). A surface may READ as several lemmas; weight is that
    /// reading's share (0..1). Older artifacts have fold(surface PK, lemma, source) with no
    /// weight column — one reading per surface at weight 1, detected once per expander. Policy, stored in the artifact's meta and enforced here: synonym rows
    /// are trusted only from the validated 'tanach' side and the human-reviewed 'word-thesaurus'
    /// links; inflection and bridge rows from every source.
    /// </summary>
    public sealed class SeforimDbFtsRelatedFormExpander
    {
        /// <summary>Max expansion terms added per query WORD. This is expansion breadth — it is
        /// NOT a result cap; results are never capped.</summary>
        public const int PerTermLimit = 5;

        // Two one-line statements — consts here, not a SQL file (rule 9 threshold).
        private const string LookupLemmasSql =
            "SELECT lemma, weight FROM fold WHERE surface = @s ORDER BY weight DESC, lemma";
        private const string LookupLemmasNoWeightSql =
            "SELECT lemma, 1.0 FROM fold WHERE surface = @s ORDER BY lemma";
        private const string LookupFormsSql =
            "SELECT form, channel, source FROM exp WHERE lemma = @l ORDER BY rank";

        // Query-shape bounds, mirroring the engine's own token rules — see the drop-guard note
        // inside RewriteCore.
        private const int MinBareLength = 2;
        private const int MaxBareLength = 29;

        /// <summary>A reading whose share is below this is noise and is dropped — unless it is
        /// the surface's ONLY reading, which is always kept.</summary>
        private const double MinReadingWeight = 0.15;

        /// <summary>fold has a weight column (multi-lemma artifact). Read from the schema once,
        /// on first use — never per query.</summary>
        private bool? _foldHasWeight;

        private readonly string? _databasePath;

        /// <summary>Finds the artifact where the app keeps it.</summary>
        public SeforimDbFtsRelatedFormExpander()
            : this(AppFileLocator.FindFile(Path.Combine("SearchExpansion", "expansion-routed.db")))
        {
        }

        /// <param name="databasePath">The artifact, or null for "not installed" — the expander
        /// is then inert rather than broken.</param>
        public SeforimDbFtsRelatedFormExpander(string? databasePath)
        {
            _databasePath = databasePath;
        }

        public bool IsAvailable => !string.IsNullOrWhiteSpace(_databasePath) && File.Exists(_databasePath);

        /// <summary>
        /// Rewrites <paramref name="query"/> so each plain Hebrew word gains its related forms
        /// as OR alternatives. Returns the query unchanged when there is nothing to do — no
        /// artifact, no Hebrew words, or the user already composed OR groups by hand (injecting
        /// more pipes into those would change their meaning).
        ///
        /// NEVER THROWS. A corrupt, truncated or locked artifact degrades to the unexpanded
        /// query, because expansion is an enhancement and a search that fails BECAUSE OF its
        /// enhancement is strictly worse than one without it. This is the documented exception
        /// to "return data or throw", licensed by the same reasoning as the logger's: on the
        /// streaming search path an escaping exception closes the socket with no response and
        /// reads as a service outage.
        /// </summary>
        public string RewriteQuery(string query, int perTerm = PerTermLimit)
        {
            try { return RewriteCore(query, perTerm); }
            catch (Exception) { return query; }
        }

        /// <summary>True when some word carries the per-word related-forms marker ('+', leading,
        /// in any order with the '^' spelling-variants marker).</summary>
        public static bool HasRelatedMarker(string query)
        {
            foreach (string token in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                WordMarkers.Split(token, out _, out bool related, out _);
                if (related) return true;
            }
            return false;
        }


        private string RewriteCore(string query, int perTerm)
        {
            if (string.IsNullOrWhiteSpace(query) || !IsAvailable) return query;
            // Pad every '|' as the parser does, so "a|b" is the tokens a, |, b. A word's forms
            // join its own OR group as more alternatives, which keeps a hand-made group's meaning.
            query = query.Replace("|", " | ");

            string[] tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var rewritten = new StringBuilder(query.Length * 2);
            bool changed = false;

            using var connection = SqliteConnectionFactory.OpenCorpusRead(_databasePath!);
            _foldHasWeight ??= FoldHasWeightColumn(connection);
            using var lemmaCommand = connection.CreateCommand();
            lemmaCommand.CommandText = _foldHasWeight.Value ? LookupLemmasSql : LookupLemmasNoWeightSql;
            var surfaceParameter = lemmaCommand.Parameters.Add("@s", SqliteType.Text);
            using var formsCommand = connection.CreateCommand();
            formsCommand.CommandText = LookupFormsSql;
            var lemmaParameter = formsCommand.Parameters.Add("@l", SqliteType.Text);

            foreach (string token in tokens)
            {
                if (rewritten.Length > 0) rewritten.Append(' ');

                // Peel the affix markers the engine's parser understands, so the lookup sees the
                // bare word — and keep them, so every alternative is re-wrapped in the SAME
                // markers. Markers COMPOSE with expansion rather than being overridden by it: a
                // grammar- or fuzzy-marked word expands to alternatives carrying those markers.
                // Per-word markers: '+' is consumed here; '^' rides on every alternative so
                // they all get the spelling variants the user asked this word for.
                WordMarkers.Split(token, out bool ketiv, out bool related, out string unmarked);
                if (!related)
                {
                    rewritten.Append(token);
                    continue;
                }
                string core = PeelMarkers(unmarked, out string lead, out string trail);
                if (ketiv) lead = "^" + lead;

                string bare = BareHebrew(core);
                if (bare.Length < MinBareLength)
                {
                    rewritten.Append(token);   // wildcard, non-Hebrew, or too short — pass through
                    continue;
                }

                surfaceParameter.Value = bare;
                var readings = ReadLemmas(lemmaCommand, bare);
                // Readings past the budget never get a slot — don't query their forms.
                if (perTerm > 0 && readings.Count > perTerm) readings.RemoveRange(perTerm, readings.Count - perTerm);

                // Each kept reading's forms, policy-filtered, in rank order. A reading never needs
                // more than the whole budget plus what dedupe against the others may eat.
                int perReading = perTerm * readings.Count;
                var candidates = new List<List<string>>(readings.Count);
                foreach (var (lemma, _) in readings)
                {
                    lemmaParameter.Value = lemma;
                    var forms = new List<string>(perReading);
                    using (var reader = formsCommand.ExecuteReader())
                    {
                        while (reader.Read() && forms.Count < perReading)
                        {
                            string form = reader.GetString(0);
                            string channel = reader.GetString(1);
                            string source = reader.GetString(2);

                            // The artifact's trust policy: synonyms only from the validated side
                            // or the human-reviewed Word-thesaurus links.
                            if (channel == "syn" && source != "tanach" && source != "word-thesaurus") continue;
                            if (form == bare || forms.Contains(form)) continue;

                            // DROP-GUARD. A form the query parser would itself drop — too short or
                            // long, non-Hebrew, whitespace — must never reach the query string: a
                            // dropped OR-alternative silently merges the NEXT query word into this
                            // OR group (parser quirk), turning the user's AND into an OR. So a form
                            // must pass the same shape rules as the tokens being expanded.
                            string bareForm = BareHebrew(form);
                            if (bareForm.Length < MinBareLength
                                || bareForm.Length > MaxBareLength
                                || bareForm.Length != form.Length) continue;

                            forms.Add(form);
                        }
                    }
                    candidates.Add(forms);
                }

                var alternatives = MergeByWeight(readings, candidates, perTerm);

                rewritten.Append(token);
                foreach (string alternative in alternatives)
                {
                    rewritten.Append(" | ").Append(lead).Append(alternative).Append(trail);
                    changed = true;
                }
            }

            return changed ? rewritten.ToString() : query;
        }

        private static bool FoldHasWeightColumn(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM pragma_table_info('fold') WHERE name = 'weight'";
            return command.ExecuteScalar() != null;
        }

        /// <summary>
        /// The surface's readings, heaviest first, minus noise readings (below
        /// <see cref="MinReadingWeight"/>) unless that would leave none. No fold row means the
        /// word is its own lemma, a single reading at weight 1.
        /// </summary>
        private static List<(string Lemma, double Weight)> ReadLemmas(SqliteCommand lemmaCommand, string bare)
        {
            var all = new List<(string Lemma, double Weight)>(2);
            using (var reader = lemmaCommand.ExecuteReader())
            {
                while (reader.Read())
                    all.Add((reader.IsDBNull(0) ? bare : reader.GetString(0), reader.IsDBNull(1) ? 1.0 : reader.GetDouble(1)));
            }
            if (all.Count == 0) { all.Add((bare, 1.0)); return all; }
            if (all.Count == 1) return all;

            var kept = new List<(string Lemma, double Weight)>(all.Count);
            foreach (var reading in all)
                if (reading.Weight >= MinReadingWeight) kept.Add(reading);
            if (kept.Count == 0) kept.Add(all[0]);
            return kept;
        }

        /// <summary>
        /// Merges the readings' form lists into one list of at most <paramref name="budget"/>
        /// forms. The budget is split by weight — at least 1 per reading (readings past the
        /// budget get none), remainder to the heaviest — and taken round-robin so the
        /// heaviest reading's best forms come first. A reading that runs short hands its unused
        /// share on to the others. Forms repeated across readings count once.
        /// </summary>
        private static List<string> MergeByWeight(
            List<(string Lemma, double Weight)> readings, List<List<string>> candidates, int budget)
        {
            var merged = new List<string>(budget);
            int count = Math.Min(readings.Count, budget);
            if (count <= 0) return merged;

            var quota = new int[count];
            double total = 0;
            for (int i = 0; i < count; i++) total += readings[i].Weight;
            int allotted = 0;
            for (int i = 0; i < count; i++)
            {
                quota[i] = total > 0 ? Math.Max(1, (int)(budget * readings[i].Weight / total)) : 1;
                allotted += quota[i];
            }
            // Flooring to at least 1 each can overshoot; trim from the lightest end.
            for (int i = count - 1; allotted > budget && i > 0; i--)
                while (quota[i] > 1 && allotted > budget) { quota[i]--; allotted--; }
            quota[0] += budget - allotted;

            var next = new int[count];
            // Pass 1 honours the quotas; pass 2 lets anyone with forms left fill the leftovers.
            for (int pass = 0; pass < 2 && merged.Count < budget; pass++)
            {
                bool progressed = true;
                while (progressed && merged.Count < budget)
                {
                    progressed = false;
                    for (int i = 0; i < count && merged.Count < budget; i++)
                    {
                        if (pass == 0 && quota[i] == 0) continue;
                        var forms = candidates[i];
                        while (next[i] < forms.Count)
                        {
                            string form = forms[next[i]++];
                            if (merged.Contains(form)) continue;
                            merged.Add(form);
                            if (pass == 0) quota[i]--;
                            progressed = true;
                            break;
                        }
                    }
                }
            }
            return merged;
        }

        /// <summary>
        /// Splits a token into leading markers, the bare word, and trailing markers, mirroring
        /// the engine's QueryParser.ParseToken exactly — what is peeled here is what the parser
        /// will later re-read:
        ///   1. '%' at either end (grammar prefix/suffix expansion), each side independent.
        ///   2. a trailing fuzzy suffix '~' or '~N', taken at the LAST '~' and only when what
        ///      follows is empty or a single digit 1-9.
        /// A token containing a wildcard ('*' or '?') comes back unpeeled: the wildcard
        /// overrides '%' and '~' in the parser, and wildcard tokens are not expanded — they
        /// already denote an open-ended term set, and grafting stem alternatives onto one would
        /// widen the query in a direction the user did not ask for.
        /// lead + core + trail always reconstructs the marker shape, so alternatives re-wrap
        /// verbatim.
        /// </summary>
        private static string PeelMarkers(string token, out string lead, out string trail)
        {
            lead = trail = "";
            if (token.IndexOf('*') >= 0 || token.IndexOf('?') >= 0) return token;

            string core = token;

            if (core.StartsWith("%", StringComparison.Ordinal)) lead = "%";
            if (core.Length > 1 && core.EndsWith("%", StringComparison.Ordinal)) trail = "%";
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

        /// <summary>
        /// The token stripped to bare Hebrew letters, final forms kept as typed. Pointing and
        /// cantillation (except maqaf, which is a separator) and intra-word quote glyphs are
        /// STRIPPED — text pasted from pointed sources must still expand. Returns "" the moment
        /// any other character appears, so a syntax-bearing token is never expanded.
        /// </summary>
        private static string BareHebrew(string token)
        {
            var bare = new StringBuilder(token.Length);
            foreach (char c in token)
            {
                if (c >= '\u05D0' && c <= '\u05EA') bare.Append(c);                       // letters
                else if (c >= '\u0591' && c <= '\u05C7' && c != '\u05BE') continue;       // marks; maqaf stays a separator
                else if (c == '"' || c == '\'' || c == '\u05F3' || c == '\u05F4') continue; // quote glyphs
                else return "";
            }
            return bare.ToString();
        }
    }
}
