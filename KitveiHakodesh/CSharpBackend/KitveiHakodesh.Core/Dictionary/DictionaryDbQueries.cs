using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using KitveiHakodesh.Core.Common;

namespace KitveiHakodesh.Core.Dictionary
{
    /// <summary>
    /// Lookups over the bundled dictionary database.
    ///
    /// Schema: word(id, headword), sense(id, word_id, nikud, text, source_id),
    /// source_kind(id, name), link(word_id, target_id, kind_id),
    /// link_kind(id, name, explanation). Opened read-only.
    ///
    /// The SQL lives as constants at the top of this file rather than in a separate one:
    /// eight short statements in a file this size bury nothing, and keeping a query beside
    /// the method that runs it beats a file-jump to read one line.
    ///
    /// A MISSING DATABASE IS A STATE, NOT AN ERROR. The dictionary is optional, and lookups
    /// run on every keystroke, so <see cref="IsAvailable"/> is exposed and the queries return
    /// empty when it is false. SQL failures are NOT caught — those are real faults and the
    /// orchestrator should hear about them (MIGRATION-PLAN rule 4).
    /// </summary>
    public sealed class DictionaryDbQueries
    {
        // ── Link kinds ────────────────────────────────────────────────────────────────
        //
        // Two curated relations in link_kind. Their names are Hebrew words stored in the
        // database, so they are named here rather than repeated inline in five places.

        /// <summary>Spelling / inflection variants: the SAME word written differently.
        /// Drives redirect resolution (a variant carrying no senses of its own resolves to
        /// the base entry), is excluded from the related-words list, and is what
        /// <see cref="Variants"/> returns.</summary>
        private const string LinkKindVariant = "כתיב";

        /// <summary>Synonyms: different words with a shared meaning.</summary>
        private const string LinkKindSynonym = "נרדף";

        // ── SQL ───────────────────────────────────────────────────────────────────────

        /// <summary>Sense projection shared by the exact / prefix / contains / abbreviation
        /// queries — same column order as <see cref="ReadSenses"/> expects.</summary>
        private const string SenseSelect =
            "SELECT w.headword, s.nikud, s.text, sk.name AS source, s.source_id " +
            "FROM word w JOIN sense s ON s.word_id = w.id " +
            "LEFT JOIN source_kind sk ON sk.id = s.source_id";

        private const string LinkFrom =
            "FROM link l " +
            "JOIN word w1 ON w1.id = l.word_id " +
            "JOIN word w2 ON w2.id = l.target_id " +
            "JOIN link_kind lk ON lk.id = l.kind_id";

        /// <summary>
        /// Follows a spelling redirect: a variant word with no senses of its own points at a
        /// base entry, so the base's senses are returned under the BASE headword. Curated
        /// pairs only, so the result is always the same word spelled differently — never a
        /// guess. Same column shape as <see cref="SenseSelect"/>.
        /// </summary>
        private const string RedirectSelect =
            "SELECT wbase.headword, s.nikud, s.text, sk.name AS source, s.source_id " +
            "FROM word walias " +
            "JOIN link l ON l.word_id = walias.id " +
            "JOIN link_kind lk ON lk.id = l.kind_id AND lk.name = @kind " +
            "JOIN word wbase ON wbase.id = l.target_id " +
            "JOIN sense s ON s.word_id = wbase.id " +
            "LEFT JOIN source_kind sk ON sk.id = s.source_id " +
            "WHERE walias.headword = @term LIMIT 100";

        private const string ExactSql =
            SenseSelect + " WHERE w.headword = @term LIMIT 100";

        private const string PrefixSql =
            SenseSelect + " WHERE w.headword LIKE @prefix AND w.headword != @term LIMIT 100";

        private const string ContainsSql =
            SenseSelect + " WHERE w.headword LIKE @contains AND w.headword NOT LIKE @prefix LIMIT 100";

        private const string WordExistsSql =
            "SELECT 1 FROM word WHERE headword = @term LIMIT 1";

        private const string LinksSql =
            "SELECT lk.name AS kind, w2.headword AS word " + LinkFrom +
            " WHERE w1.headword = @term AND lk.name != @kind ORDER BY lk.name, w2.headword";

        private const string LinkedWordsSql =
            "SELECT w2.headword AS word " + LinkFrom +
            " WHERE w1.headword = @term AND lk.name = @kind ORDER BY w2.headword";

        private const string HeadwordPrefixSql =
            "SELECT headword FROM word WHERE headword LIKE @prefix LIMIT @limit";

        private const string AbbreviationContainsSql =
            SenseSelect + " WHERE w.headword LIKE @contains LIMIT 30";

        // ── Construction ──────────────────────────────────────────────────────────────

        private readonly string _databasePath;

        public DictionaryDbQueries(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("databasePath is required", nameof(databasePath));

            _databasePath = databasePath;
        }

        /// <summary>
        /// Finds the bundled dictionary wherever this host keeps it, or null if it is not
        /// installed. Probes both layouts in use — Core's own Resources folder and the
        /// service's Dictionary folder — so a caller never has to know which host it is in.
        /// </summary>
        public static string? Locate() =>
            AppFileLocator.FindFile(Path.Combine("Resources", "Dictionary.db"))
            ?? AppFileLocator.FindFile(Path.Combine("Dictionary", "Dictionary.db"));

        public string DatabasePath => _databasePath;

        /// <summary>False when the dictionary is not installed. Queries return empty rather
        /// than throwing in that case — see the class remarks.</summary>
        public bool IsAvailable => File.Exists(_databasePath);

        // ── Lookup tiers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// The exact-match tier. Returns the word's own senses; failing that, follows a
        /// curated spelling redirect to the base entry. <see cref="DictionaryExactResult.WordExists"/>
        /// distinguishes "no such word" from "the word is known but carries no senses" — the
        /// caller shows different things for those.
        /// </summary>
        public DictionaryExactResult Exact(string term)
        {
            if (!IsAvailable || string.IsNullOrEmpty(term))
                return new DictionaryExactResult(new List<DictionarySense>(), false);

            var direct = QuerySenses(ExactSql, ("@term", term));
            if (direct.Count > 0) return new DictionaryExactResult(direct, true);

            var viaRedirect = QuerySenses(RedirectSelect, ("@term", term), ("@kind", LinkKindVariant));
            if (viaRedirect.Count > 0) return new DictionaryExactResult(viaRedirect, true);

            return new DictionaryExactResult(new List<DictionarySense>(), WordExists(term));
        }

        /// <summary>Words starting with the term, excluding the exact match itself.</summary>
        public List<DictionarySense> Prefix(string term) =>
            !IsAvailable || string.IsNullOrEmpty(term)
                ? new List<DictionarySense>()
                : QuerySenses(PrefixSql, ("@prefix", term + "%"), ("@term", term));

        /// <summary>Words containing the term, excluding those the prefix tier already returned.</summary>
        public List<DictionarySense> Contains(string term) =>
            !IsAvailable || string.IsNullOrEmpty(term)
                ? new List<DictionarySense>()
                : QuerySenses(ContainsSql, ("@contains", "%" + term + "%"), ("@prefix", term + "%"));

        // ── Related words ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Every related word except spelling variants, grouped by relation. Variants are
        /// excluded because they are the same word, not a related one — <see cref="Variants"/>
        /// returns those separately.
        /// </summary>
        public List<DictionaryLink> Links(string term)
        {
            var links = new List<DictionaryLink>();
            if (!IsAvailable || string.IsNullOrEmpty(term)) return links;

            using var connection = SqliteConnectionFactory.OpenCorpusRead(_databasePath);
            using var command = connection.CreateCommand();
            command.CommandText = LinksSql;
            command.Parameters.AddWithValue("@term", term);
            command.Parameters.AddWithValue("@kind", LinkKindVariant);

            using var reader = command.ExecuteReader();
            while (reader.Read())
                links.Add(new DictionaryLink(reader.GetString(0), reader.GetString(1)));

            return links;
        }

        /// <summary>Different words sharing a meaning.</summary>
        public List<string> Synonyms(string term) => LinkedWords(term, LinkKindSynonym);

        /// <summary>The same word spelled or inflected differently.</summary>
        public List<string> Variants(string term) => LinkedWords(term, LinkKindVariant);

        // ── Spelling suggestions ──────────────────────────────────────────────────────

        /// <summary>
        /// Candidate headwords for a term that matched nothing, for the caller to rank.
        /// Two passes, widest first: everything sharing the first two letters, then the first
        /// three. The three-letter pass is narrower but its hits are better, and appending
        /// rather than replacing keeps both while <see cref="HashSet{T}"/> drops repeats.
        /// </summary>
        public List<string> SpellingCandidates(string term)
        {
            var candidates = new List<string>();
            if (!IsAvailable || string.IsNullOrEmpty(term)) return candidates;

            var seen = new HashSet<string>(StringComparer.Ordinal);

            string twoLetters = term.Substring(0, Math.Min(2, term.Length));
            foreach (string headword in QueryHeadwords(twoLetters + "%", 400))
                if (seen.Add(headword)) candidates.Add(headword);

            if (term.Length >= 3)
            {
                foreach (string headword in QueryHeadwords(term.Substring(0, 3) + "%", 200))
                    if (seen.Add(headword)) candidates.Add(headword);
            }

            return candidates;
        }

        // ── Abbreviations ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves an abbreviation to a dictionary entry, given the caller's expansion
        /// candidates in preference order. All exact matches are tried before any partial
        /// one, so a weaker candidate's exact hit never loses to a stronger candidate's
        /// substring hit. Returns the first that resolves.
        /// </summary>
        public DictionaryAbbreviationMatch AbbreviationSenses(IReadOnlyList<string>? candidates)
        {
            if (!IsAvailable || candidates == null || candidates.Count == 0)
                return DictionaryAbbreviationMatch.None;

            foreach (string candidate in candidates)
            {
                var rows = QuerySenses(ExactSql, ("@term", candidate));
                if (rows.Count > 0) return new DictionaryAbbreviationMatch(candidate, rows);
            }

            foreach (string candidate in candidates)
            {
                var rows = QuerySenses(AbbreviationContainsSql, ("@contains", "%" + candidate + "%"));
                if (rows.Count > 0) return new DictionaryAbbreviationMatch(candidate, rows);
            }

            return DictionaryAbbreviationMatch.None;
        }

        /// <summary>
        /// Which of the candidate spellings exist as headwords. One query for the whole set
        /// rather than one per candidate.
        /// </summary>
        public List<string> ExistingHeadwords(IReadOnlyList<string>? candidates)
        {
            var found = new List<string>();
            if (!IsAvailable || candidates == null || candidates.Count == 0) return found;

            var sql = new StringBuilder("SELECT headword FROM word WHERE headword IN (");
            for (int i = 0; i < candidates.Count; i++)
            {
                if (i > 0) sql.Append(',');
                sql.Append("@c").Append(i);
            }
            sql.Append(')');

            using var connection = SqliteConnectionFactory.OpenCorpusRead(_databasePath);
            using var command = connection.CreateCommand();
            command.CommandText = sql.ToString();
            for (int i = 0; i < candidates.Count; i++)
                command.Parameters.AddWithValue("@c" + i, candidates[i]);

            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (!reader.IsDBNull(0)) found.Add(reader.GetString(0));

            return found;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────

        private List<string> LinkedWords(string term, string linkKind)
        {
            var words = new List<string>();
            if (!IsAvailable || string.IsNullOrEmpty(term)) return words;

            using var connection = SqliteConnectionFactory.OpenCorpusRead(_databasePath);
            using var command = connection.CreateCommand();
            command.CommandText = LinkedWordsSql;
            command.Parameters.AddWithValue("@term", term);
            command.Parameters.AddWithValue("@kind", linkKind);

            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (!reader.IsDBNull(0)) words.Add(reader.GetString(0));

            return words;
        }

        private bool WordExists(string term)
        {
            using var connection = SqliteConnectionFactory.OpenCorpusRead(_databasePath);
            using var command = connection.CreateCommand();
            command.CommandText = WordExistsSql;
            command.Parameters.AddWithValue("@term", term);
            using var reader = command.ExecuteReader();
            return reader.Read();
        }

        private List<string> QueryHeadwords(string pattern, int limit)
        {
            var headwords = new List<string>();

            using var connection = SqliteConnectionFactory.OpenCorpusRead(_databasePath);
            using var command = connection.CreateCommand();
            command.CommandText = HeadwordPrefixSql;
            command.Parameters.AddWithValue("@prefix", pattern);
            command.Parameters.AddWithValue("@limit", limit);

            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (!reader.IsDBNull(0)) headwords.Add(reader.GetString(0));

            return headwords;
        }

        private List<DictionarySense> QuerySenses(string sql, params (string Name, object Value)[] parameters)
        {
            using var connection = SqliteConnectionFactory.OpenCorpusRead(_databasePath);
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);

            return ReadSenses(command);
        }

        private static List<DictionarySense> ReadSenses(SqliteCommand command)
        {
            var senses = new List<DictionarySense>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                senses.Add(new DictionarySense(
                    Headword: reader.IsDBNull(0) ? "" : reader.GetString(0),
                    Nikud: reader.IsDBNull(1) ? null : reader.GetString(1),
                    Text: reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Source: reader.IsDBNull(3) ? null : reader.GetString(3),
                    SourceId: reader.IsDBNull(4) ? null : reader.GetInt32(4)));
            }
            return senses;
        }
    }
}
