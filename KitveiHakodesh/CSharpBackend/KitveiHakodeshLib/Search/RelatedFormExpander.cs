namespace KitveiHakodeshLib.Search
{
    /// <summary>
    /// Related-forms query expansion for the hosted FTS path.
    ///
    /// The rewrite itself is KitveiHakodesh.Core's
    /// <see cref="KitveiHakodesh.Core.SeforimDbFullTextSearch.SeforimDbFtsRelatedFormExpander"/>
    /// — the SAME implementation the service uses, so the hosted and dev legs can no longer
    /// answer one query differently. This used to be a hand-maintained net48 twin of the
    /// service's copy, with a "keep the two in sync" instruction in its remarks; it is now a
    /// single shared instance, and that instruction is retired.
    ///
    /// Artifact resolution moved with it: Core probes for
    /// "SearchExpansion/expansion-routed.db" instead of reading the SEARCH_EXPANSION_DB
    /// environment variable, which nothing in this repo ever set. An absent artifact is a
    /// STATE — the expander is inert and queries pass through unchanged.
    /// </summary>
    internal static class RelatedFormExpander
    {
        /// <summary>One instance for the process: it holds only a path, and finding the
        /// artifact should not be repeated per search.</summary>
        private static readonly KitveiHakodesh.Core.SeforimDbFullTextSearch.SeforimDbFtsRelatedFormExpander
            Expander = new KitveiHakodesh.Core.SeforimDbFullTextSearch.SeforimDbFtsRelatedFormExpander();

        internal const int PerTermLimit =
            KitveiHakodesh.Core.SeforimDbFullTextSearch.SeforimDbFtsRelatedFormExpander.PerTermLimit;

        internal static bool IsAvailable => Expander.IsAvailable;

        /// <summary>Rewrites a query so each plain Hebrew word gains its related forms as OR
        /// alternatives. NEVER throws: expansion is an enhancement, and a search that failed
        /// because of its enhancement would be worse than one without it.</summary>
        internal static string RewriteQuery(string query, int perTerm = PerTermLimit) =>
            Expander.RewriteQuery(query, perTerm);
    }
}
