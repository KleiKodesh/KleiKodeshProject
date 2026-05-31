namespace LuceneLib.Snippets
{
    /// <summary>
    /// The output of <see cref="SnippetBuilder.Build"/>.
    /// Mirrors FtsLib.Snippets.SnippetResult exactly.
    /// </summary>
    public readonly struct SnippetResult
    {
        /// <summary>HTML snippet with matched terms wrapped in highlight tags.</summary>
        public readonly string Html;

        /// <summary>
        /// Raw character span of the tightest window covering all query terms.
        /// int.MaxValue = at least one term absent from the document.
        /// </summary>
        public readonly int Score;

        /// <summary>
        /// Number of tokens between the leftmost and rightmost matched tokens.
        /// 0 = adjacent. int.MaxValue = no match.
        /// </summary>
        public readonly int WordDistance;

        /// <summary>True when at least one query term was found in the document.</summary>
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
