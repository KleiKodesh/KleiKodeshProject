namespace FtsLib.Snippets
{
    /// <summary>
    /// The output of <see cref="SnippetBuilder.Build"/>.
    /// </summary>
    internal readonly struct SnippetResult
    {
        public readonly string Html;

        /// <summary>
        /// Character span (rawEnd - rawStart) of the tightest window.
        /// int.MaxValue = at least one term absent.
        /// </summary>
        public readonly int Score;

        /// <summary>
        /// Number of tokens (words) between the leftmost and rightmost matched
        /// tokens in the tightest window. 0 = adjacent. int.MaxValue = no match.
        /// </summary>
        public readonly int WordDistance;

        public readonly bool IsMatch;

        /// <summary>
        /// Number of visible words in the rendered snippet window (the expanded
        /// match window after context). Lets the caller decide a snippet is "short"
        /// — fewer words than the requested context — WITHOUT re-scanning the HTML
        /// or touching the DB. 0 when there is no match.
        /// </summary>
        public readonly int WindowWordCount;

        public SnippetResult(string html, int score, int wordDistance, bool isMatch,
            int windowWordCount = 0)
        {
            Html            = html;
            Score           = score;
            WordDistance    = wordDistance;
            IsMatch         = isMatch;
            WindowWordCount = windowWordCount;
        }
    }
}
