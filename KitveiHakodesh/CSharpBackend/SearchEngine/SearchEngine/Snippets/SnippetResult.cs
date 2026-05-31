namespace SearchEngine.Snippets
{
    /// <summary>
    /// The output of snippet generation.
    /// Mirrors FtsLib.SeforimDb.SnippetResult exactly — class, not struct,
    /// so callers can use it as a reference type without boxing surprises.
    /// </summary>
    public sealed class SnippetResult
    {
        /// <summary>HTML snippet with matched terms wrapped in highlight tags.</summary>
        public string Html { get; }

        /// <summary>
        /// Raw character span of the tightest window covering all query terms.
        /// int.MaxValue = at least one term absent from the document.
        /// </summary>
        public int Score { get; }

        /// <summary>
        /// Number of tokens between the leftmost and rightmost matched tokens.
        /// 0 = adjacent. int.MaxValue = no match.
        /// </summary>
        public int WordDistance { get; }

        /// <summary>True when at least one query term was found in the document.</summary>
        public bool IsMatch { get; }

        public SnippetResult(string html, int score, int wordDistance, bool isMatch)
        {
            Html         = html ?? string.Empty;
            Score        = score;
            WordDistance = wordDistance;
            IsMatch      = isMatch;
        }

        public static readonly SnippetResult NoMatch =
            new SnippetResult(string.Empty, int.MaxValue, int.MaxValue, false);
    }
}
