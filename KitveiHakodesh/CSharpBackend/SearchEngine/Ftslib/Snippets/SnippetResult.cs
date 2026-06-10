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

        public SnippetResult(string html, int score, int wordDistance, bool isMatch)
        {
            Html         = html;
            Score        = score;
            WordDistance = wordDistance;
            IsMatch      = isMatch;
        }
    }
}
