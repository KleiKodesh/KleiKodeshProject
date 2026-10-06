namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// The SQL fragments that differ between a database whose line text sits in `line` and one
    /// whose line text sits in the `line_content` sidecar (SeforimLibrary schema 6+).
    ///
    /// WHY FRAGMENTS RATHER THAN TWO COPIES OF EVERY QUERY: more than thirty statements read
    /// line text, and the only thing that changes between the two shapes is where the column
    /// comes from. Writing each one twice would double the surface that has to stay in step
    /// with the other backend's copy, and the two copies would drift silently - nothing fails
    /// to compile when only one of them is edited. One fragment per shape, applied at the few
    /// points that need it, keeps each query readable as a single statement.
    ///
    /// Every query that selects line text therefore takes the schema and asks for:
    ///   <see cref="ContentColumn"/>  - the expression that yields the text, aliased `content`
    ///   <see cref="ContentJoin"/>    - the JOIN that makes that expression resolvable
    ///
    /// The alias is always `content`, on both shapes, so the readers above are identical.
    /// </summary>
    internal static class SeforimDbLineContentSql
    {
        /// <summary>
        /// The text expression for a line, aliased `content`.
        ///
        /// <paramref name="lineTableAlias"/> is the alias the query gave the `line` table
        /// ("l", "ln", "m", "n"); the sidecar is always joined as that alias + "_content" so
        /// a self-join (two line aliases in one statement) gets a distinct sidecar alias for
        /// each side.
        /// </summary>
        internal static string ContentColumn(SeforimDbSchema schema, string lineTableAlias)
            => schema.HasSplitLineContent
                ? "COALESCE(" + SidecarAlias(lineTableAlias) + ".content, '') AS content"
                : lineTableAlias + ".content";

        /// <summary>
        /// The same expression without the alias, for a query that selects the text alone
        /// (a scalar read) or wraps it in something else.
        /// </summary>
        internal static string ContentExpression(SeforimDbSchema schema, string lineTableAlias)
            => schema.HasSplitLineContent
                ? "COALESCE(" + SidecarAlias(lineTableAlias) + ".content, '')"
                : lineTableAlias + ".content";

        /// <summary>
        /// The raw text expression, WITHOUT the empty-string fallback - for the places that
        /// must tell a missing row from an empty one (version lines, see
        /// SeforimDbSqlStrings.GetVersionLinesPaged).
        /// </summary>
        internal static string RawContentExpression(SeforimDbSchema schema, string lineTableAlias)
            => schema.HasSplitLineContent
                ? SidecarAlias(lineTableAlias) + ".content"
                : lineTableAlias + ".content";

        /// <summary>
        /// The JOIN that brings the text into scope, or an empty string when the text is
        /// already a column of `line`.
        ///
        /// LEFT JOIN, not JOIN: a line with no sidecar row must still come back, as an empty
        /// line rather than a vanished one. The join is on the sidecar's INTEGER PRIMARY KEY,
        /// which is SQLite's rowid, so it costs a direct row lookup rather than an index
        /// search - measured at about 0.02 ms for a page of lines.
        /// </summary>
        internal static string ContentJoin(SeforimDbSchema schema, string lineTableAlias)
            => schema.HasSplitLineContent
                ? " LEFT JOIN line_content " + SidecarAlias(lineTableAlias) +
                  " ON " + SidecarAlias(lineTableAlias) + ".id = " + lineTableAlias + ".id"
                : string.Empty;

        /// <summary>
        /// A LIKE predicate against line text. Kept here because the column it tests moves
        /// with the schema exactly like the ones selected above.
        ///
        /// NOTE: on a compressed database this matches against the COMPRESSED bytes and will
        /// find nothing. Callers that search text must go through the full-text index
        /// instead - see SeforimDbSchema.HasCompressedContent and the guard in
        /// SeforimDbQueries.
        /// </summary>
        internal static string ContentLikePredicate(SeforimDbSchema schema, string lineTableAlias, string parameterName)
            => RawContentExpression(schema, lineTableAlias) + " LIKE " + parameterName;

        /// <summary>The sidecar alias paired with a given `line` alias.</summary>
        private static string SidecarAlias(string lineTableAlias) => lineTableAlias + "_content";
    }
}
