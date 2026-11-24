namespace FtsLib.Snippets
{
    /// <summary>
    /// What <see cref="SnippetWindowLocator.Locate(string, PreparedQueryGroups, bool, int, int, SnippetMatchMode)"/>
    /// returns for a line that passes (issues #14 / #23 of SeforimLibrarySearchEngine).
    /// </summary>
    public enum SnippetMatchMode
    {
        /// <summary>The best window (tightest covering window, widened by the context) and the
        /// marks of its occurrences. The default; unchanged.</summary>
        BestWindow = 0,

        /// <summary>The whole text: the window is [0, text length) and the marks are EVERY
        /// token of the text that is a query term (same word rules: HTML, entities, marks,
        /// quotes). The pass / fail decision, Score and WordDistance are the best window's.</summary>
        AllMatches = 1,
    }
}
