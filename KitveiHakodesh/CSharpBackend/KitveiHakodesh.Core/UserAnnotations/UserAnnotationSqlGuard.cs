using System;
using System.Collections.Generic;
using System.Text;

namespace KitveiHakodesh.Core.UserAnnotations
{
    /// <summary>
    /// The whole boundary between frontend-supplied SQL and arbitrary SQLite.
    ///
    /// <see cref="UserAnnotationStore"/> is the one place the frontend still sends SQL text
    /// rather than calling a typed method, so a compromised page — or a leaked bearer token on
    /// the dev transport — would otherwise reach the file system through it. This runs on
    /// EVERY statement, on BOTH transports, which is why it lives in Core beside the store
    /// rather than in one host: a guard only the service applied would leave the hosted
    /// WebView2 bridge open.
    ///
    /// It is deliberately a FILTER, not a SQL parser: one statement, one allowed verb, only
    /// the two annotation tables, and none of the words that can escape the file. Anything it
    /// cannot understand is rejected rather than passed through. ATTACH is the sharp one —
    /// without it on the list, "edit my highlights" becomes "create and write an arbitrary
    /// SQLite file anywhere this process can write".
    /// </summary>
    internal static class UserAnnotationSqlGuard
    {
        /// <summary>The two annotation tables. Nothing else in the database is reachable
        /// through this path, and the database itself is the only file reachable at all.</summary>
        private static readonly string[] AllowedTables = { "user_highlights", "user_notes" };

        /// <summary>Split read vs write so a read cannot mutate, and a write cannot be used as
        /// an exfiltration channel for a SELECT the frontend was never given.</summary>
        private static readonly string[] ReadVerbs = { "select" };
        private static readonly string[] WriteVerbs = { "insert", "update", "delete" };

        /// <summary>Anything that can reach OUTSIDE the annotations database, change its shape,
        /// or run a second statement.</summary>
        private static readonly string[] ForbiddenWords =
        {
            "attach", "detach", "pragma", "vacuum", "alter", "drop", "create", "reindex",
            "load_extension", "readfile", "writefile",
            "sqlite_master", "sqlite_schema", "sqlite_temp_master", "sqlite_dbpage", "sqlite_stat1",
        };

        /// <param name="write">True for the INSERT/UPDATE/DELETE path, false for SELECT.</param>
        /// <exception cref="InvalidOperationException">The statement is not allowed. The message
        /// says which rule it broke; the host decides what the user sees.</exception>
        public static void Check(string sql, bool write)
        {
            if (string.IsNullOrWhiteSpace(sql)) throw new InvalidOperationException("empty statement");

            string bare = StripLiteralsAndComments(sql);

            // No batching: a second statement would never be seen by the verb check below.
            if (bare.TrimEnd().TrimEnd(';').IndexOf(';') >= 0)
                throw new InvalidOperationException("only one statement is allowed");

            var tokens = Words(bare);
            if (tokens.Count == 0) throw new InvalidOperationException("empty statement");

            string verb = tokens[0];
            string[] allowedVerbs = write ? WriteVerbs : ReadVerbs;
            if (Array.IndexOf(allowedVerbs, verb) < 0)
                throw new InvalidOperationException("statement type '" + verb + "' is not allowed here");

            foreach (string word in tokens)
                if (Array.IndexOf(ForbiddenWords, word) >= 0)
                    throw new InvalidOperationException("'" + word + "' is not allowed in an annotation statement");

            // Every table position — what follows FROM / JOIN / INTO / UPDATE — must be ours.
            for (int i = 0; i < tokens.Count - 1; i++)
            {
                string token = tokens[i];
                if (token != "from" && token != "join" && token != "into" && token != "update") continue;
                if (Array.IndexOf(AllowedTables, tokens[i + 1]) < 0)
                    throw new InvalidOperationException("table '" + tokens[i + 1] + "' is not an annotation table");
            }
        }

        /// <summary>Blanks out string literals and comments so the guard reads STRUCTURE only —
        /// a note whose text happens to contain the word "attach" must not be rejected.</summary>
        private static string StripLiteralsAndComments(string sql)
        {
            var result = new StringBuilder(sql.Length);
            char quote = '\0';

            for (int i = 0; i < sql.Length; i++)
            {
                char c = sql[i];

                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                    result.Append(' ');
                    continue;
                }
                if (c == '\'' || c == '"' || c == '`' || c == '[')
                {
                    quote = c == '[' ? ']' : c;
                    result.Append(' ');
                    continue;
                }
                if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
                {
                    while (i < sql.Length && sql[i] != '\n') i++;
                    result.Append('\n');
                    continue;
                }
                if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/')) i++;
                    i++;
                    result.Append(' ');
                    continue;
                }
                result.Append(c);
            }
            return result.ToString();
        }

        /// <summary>Lower-cased identifier/keyword runs. Hand-rolled rather than a Regex so
        /// nothing here depends on Reflection.Emit under native AOT.</summary>
        private static List<string> Words(string text)
        {
            var words = new List<string>();
            var current = new StringBuilder();

            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c) || c == '_') current.Append(char.ToLowerInvariant(c));
                else if (current.Length > 0) { words.Add(current.ToString()); current.Length = 0; }
            }
            if (current.Length > 0) words.Add(current.ToString());
            return words;
        }
    }
}
