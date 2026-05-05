namespace FtsLib.Core
{
    /// <summary>
    /// The output of <see cref="SnippetBuilder.Build"/>.
    /// </summary>
    internal readonly struct SnippetResult
    {
        /// <summary>
        /// Ready-to-render HTML snippet with query terms wrapped in highlight tags.
        /// Includes leading/trailing "…" when the snippet is a sub-range of the source.
        /// </summary>
        public readonly string Html;

        /// <summary>
        /// Character span (rawEnd - rawStart) of the tightest window that covers all
        /// query terms. Smaller = terms are closer together = stronger match.
        /// <see cref="int.MaxValue"/> means at least one query term was absent entirely.
        /// </summary>
        public readonly int Score;

        /// <summary>
        /// False when at least one query term was not found in the content at all.
        /// When false, <see cref="Score"/> is <see cref="int.MaxValue"/> and
        /// <see cref="Html"/> is the plain (non-highlighted) full content.
        /// </summary>
        public readonly bool IsMatch;

        public SnippetResult(string html, int score, bool isMatch)
        {
            Html    = html;
            Score   = score;
            IsMatch = isMatch;
        }
    }
}
