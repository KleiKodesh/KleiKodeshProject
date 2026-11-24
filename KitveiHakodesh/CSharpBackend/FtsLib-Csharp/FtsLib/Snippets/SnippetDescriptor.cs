using System.Collections.Generic;

namespace FtsLib.Snippets
{
    /// <summary>
    /// WHERE a snippet is in its source text — not its HTML. The output of
    /// <see cref="SnippetWindowLocator.Locate"/>.
    ///
    /// The window [<see cref="SnapStart"/>, <see cref="SnapEnd"/>) and the
    /// <see cref="MarkRanges"/> are raw char offsets into the string the locator was given
    /// (a matched line, or that line joined with its neighbours by single spaces). A consumer
    /// holding that text rebuilds the highlighted snippet from this alone — the page does it
    /// in the browser.
    /// </summary>
    public readonly struct SnippetDescriptor
    {
        /// <summary>
        /// True when every query group was found in the source (and, if ordered matching was
        /// required, in query order). False means the line was an index false positive and
        /// the caller should drop it.
        /// </summary>
        public readonly bool IsMatch;

        /// <summary>
        /// Raw char span (rawEnd - rawStart) of the tightest window covering all query groups.
        /// Smaller = terms closer together. int.MaxValue = at least one group absent.
        /// </summary>
        public readonly int Score;

        /// <summary>
        /// Words between the leftmost and rightmost matched tokens in the tightest window.
        /// 0 = adjacent. int.MaxValue = no match.
        /// </summary>
        public readonly int WordDistance;

        /// <summary>
        /// Visible words the window spans after context expansion. Lets the caller tell a
        /// snippet is "short" (fewer words than the requested per-side context) without
        /// re-scanning the text or touching the DB. 0 when there is no match.
        /// </summary>
        public readonly int WindowWordCount;

        /// <summary>The window as raw char offsets [SnapStart, SnapEnd) into the source.
        /// 0/0 when there is no match.</summary>
        public readonly int SnapStart;
        public readonly int SnapEnd;

        /// <summary>"…" belongs before the window (it starts mid-text) / after it (it ends
        /// before the source does).</summary>
        public readonly bool LeadingEllipsis;
        public readonly bool TrailingEllipsis;

        /// <summary>
        /// Every highlighted token as flat (rawStart, rawLength, termOrdinal) triples —
        /// <see cref="MarkStride"/> ints each — into the source, in document order, length
        /// already clipped to <see cref="SnapEnd"/>. The term ordinal says WHICH query term
        /// the token matched; <see cref="MatchedTermAt"/> resolves it to the term string, so a
        /// consumer learns this line's own 1-3 matched terms without the whole expansion.
        /// Empty when there is no match.
        ///
        /// A VIEW over the locator's reused scratch buffer: valid only until the next
        /// <see cref="SnippetWindowLocator.Locate"/> on the same locator (one per thread in
        /// the pipeline). Copy it out before locating again.
        /// (<see cref="MatchedTermAt"/> reads the query's immutable prepared terms and stays
        /// valid for the query's lifetime.)
        /// </summary>
        public readonly IReadOnlyList<int> MarkRanges;

        /// <summary>Ints per entry in <see cref="MarkRanges"/>: start, length, term ordinal.</summary>
        public const int MarkStride = 3;

        private readonly PreparedQueryGroups _terms;

        internal SnippetDescriptor(bool isMatch, int score, int wordDistance, int windowWordCount,
            int snapStart, int snapEnd, bool leadingEllipsis, bool trailingEllipsis,
            IReadOnlyList<int> markRanges, PreparedQueryGroups terms)
        {
            _terms           = terms;
            IsMatch          = isMatch;
            Score            = score;
            WordDistance     = wordDistance;
            WindowWordCount  = windowWordCount;
            SnapStart        = snapStart;
            SnapEnd          = snapEnd;
            LeadingEllipsis  = leadingEllipsis;
            TrailingEllipsis = trailingEllipsis;
            MarkRanges       = markRanges ?? System.Array.Empty<int>();
        }

        /// <summary>The query term a mark's term ordinal (every third int of
        /// <see cref="MarkRanges"/>) stands for.</summary>
        public string MatchedTermAt(int termOrdinal) => _terms.TermAt(termOrdinal);

        public static readonly SnippetDescriptor NoMatch =
            new SnippetDescriptor(false, int.MaxValue, int.MaxValue, 0, 0, 0, false, false, null, null);
    }
}
