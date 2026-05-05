namespace FtsLib.Seforim
{
    /// <summary>
    /// The output of <see cref="SeforimIndex.GenerateSnippet"/>.
    /// Immutable — all properties are set at construction time.
    /// </summary>
    public sealed class SnippetResult
    {
        /// <summary>
        /// Ready-to-render HTML snippet with matched query terms wrapped in
        /// highlight tags. Includes leading/trailing "…" when the snippet is
        /// a sub-range of the source line.
        /// Empty string when <see cref="IsMatch"/> is false.
        /// </summary>
        public string Html { get; }

        /// <summary>
        /// Raw character span (rawEnd - rawStart) of the tightest window covering
        /// all query terms. Smaller = terms are closer together in the source text.
        /// int.MaxValue = at least one term absent (no match).
        /// </summary>
        public int Score { get; }

        /// <summary>
        /// Number of tokens (words) between the leftmost and rightmost matched
        /// tokens in the tightest window. 0 = adjacent. int.MaxValue = no match.
        /// </summary>
        public int WordDistance { get; }

        /// <summary>
        /// True when all query terms were found in the line content.
        /// False means the line was a false positive from the index and should
        /// be filtered out by the caller.
        /// </summary>
        public bool IsMatch { get; }

        public SnippetResult(string html, int score, int wordDistance, bool isMatch)
        {
            Html         = html    ?? string.Empty;
            Score        = score;
            WordDistance = wordDistance;
            IsMatch      = isMatch;
        }

        public static readonly SnippetResult NoMatch =
            new SnippetResult(string.Empty, int.MaxValue, int.MaxValue, false);
    }
}
