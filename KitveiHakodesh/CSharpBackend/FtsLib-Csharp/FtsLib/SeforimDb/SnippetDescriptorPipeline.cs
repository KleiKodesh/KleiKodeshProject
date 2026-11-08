using System;
using FtsLib.Indexing;
using FtsLib.Search;
using FtsLib.Snippets;
using FtsLib.Tokenization;
using System.Collections.Generic;

namespace FtsLib.SeforimDb
{
    /// <summary>
    /// Locates the snippet window for a single search result line and returns it as a
    /// <see cref="SnippetDescriptor"/> — offsets only, never HTML.
    ///
    /// Pipeline (single pass over the token stream):
    ///   1. Use already-fetched content from <see cref="SearchResult.Content"/> —
    ///      no second DB round-trip.
    ///   2. Tokenize via <see cref="TokenStream"/> (preserves raw char positions).
    ///   3. Find the tightest proximity window covering all query groups
    ///      (OR within each group, AND across groups).
    ///   4. Expand the window by contextWords tokens on each side using the already-built
    ///      token list — no second pass over the source string.
    ///
    /// Descriptors with <see cref="SnippetDescriptor.IsMatch"/> = false mean the line did
    /// not actually contain all query terms (index false positive) and should be dropped.
    ///
    /// A descriptor's mark ranges VIEW the per-thread locator's scratch — copy them before the
    /// next locate on the same thread (see <see cref="SnippetDescriptor.MarkRanges"/>).
    /// </summary>
    internal static class SnippetDescriptorPipeline
    {
        // One locator per thread — it holds only per-call SCRATCH buffers (token stream,
        // group counters, mark buffer), so [ThreadStatic] gives each thread its own
        // instance with zero per-call allocation overhead. All QUERY state (the term→group
        // map) lives in PreparedQueryGroups — immutable, built once per query, shared
        // read-only by every thread.
        public const int DefaultContextWords = 8;

        [System.ThreadStatic]
        private static SnippetWindowLocator _locator;
        [System.ThreadStatic]
        private static int _locatorContextWords;

        private static SnippetWindowLocator GetLocator(int contextWords)
        {
            if (_locator == null || _locatorContextWords != contextWords)
            {
                _locator             = new SnippetWindowLocator(contextWords);
                _locatorContextWords = contextWords;
            }
            return _locator;
        }

        // A locator's scratch buffers grow to the longest line it has seen and never shrink;
        // as a [ThreadStatic] on pool threads it would hold them for the life of the process
        // (measured: 2 x 2 MB token arrays after one search hit a huge line). Past this size,
        // drop the locator after the call so only ordinary-sized buffers stay cached.
        private const int MaxRetainedContentLength = 8192;

        private static void ReleaseLocatorIfOversized(string content)
        {
            if (content.Length > MaxRetainedContentLength) _locator = null;
        }

        // ── Primary path: content already in hand ────────────────────

        /// <summary>
        /// Locates from already-fetched content using pre-computed query groups. Each group
        /// is a set of alternative terms (OR within group, AND across groups) — correctly
        /// handles fuzzy/wildcard expansions. No DB access.
        /// </summary>
        internal static SnippetDescriptor Locate(
            string                                         content,
            IReadOnlyList<IReadOnlyCollection<string>>     queryGroups,
            bool                                           requireOrdered     = false,
            int                                            originalGroupCount = 0,
            int                                            contextWords       = DefaultContextWords)
        {
            if (string.IsNullOrEmpty(content) || queryGroups == null || queryGroups.Count == 0)
                return SnippetDescriptor.NoMatch;

            var descriptor = GetLocator(contextWords).Locate(content, queryGroups, requireOrdered, originalGroupCount);
            ReleaseLocatorIfOversized(content);
            return descriptor;
        }

        /// <summary>
        /// The hot path: locates from a query-level <see cref="PreparedQueryGroups"/>
        /// prepared once per query — per-line cost is independent of how many terms the
        /// query expanded to. Used by
        /// <see cref="FtsLib.Fts5.Fts5SeforimIndex.LocateSnippetWindow(SearchResult, bool, int)"/>.
        /// </summary>
        internal static SnippetDescriptor Locate(
            string              content,
            PreparedQueryGroups prepared,
            bool                requireOrdered     = false,
            int                 originalGroupCount = 0,
            int                 contextWords       = DefaultContextWords,
            int                 maxWordDistance    = int.MaxValue,
            SnippetMatchMode    matchMode          = SnippetMatchMode.BestWindow)
        {
            if (string.IsNullOrEmpty(content) || prepared == null || prepared.IsEmpty)
                return SnippetDescriptor.NoMatch;

            var descriptor = GetLocator(contextWords).Locate(content, prepared, requireOrdered, originalGroupCount, maxWordDistance, matchMode);
            ReleaseLocatorIfOversized(content);
            return descriptor;
        }

        internal static SnippetDescriptor Locate(
            string                content,
            IReadOnlyList<string> queryTerms,
            int                   contextWords = DefaultContextWords)
        {
            if (string.IsNullOrEmpty(content) || queryTerms == null || queryTerms.Count == 0)
                return SnippetDescriptor.NoMatch;

            var descriptor = GetLocator(contextWords).Locate(content, queryTerms);
            ReleaseLocatorIfOversized(content);
            return descriptor;
        }

        /// <summary>
        /// Find in an open text (issues #14 / #23): each word is normalized by the line
        /// tokenizer (its words, in order, each its own group; quotes removed with
        /// <paramref name="ignoreQuotes"/>), then <see cref="SnippetWindowLocator.FindAllMatches"/>
        /// runs over the text. A copy of the (start, length, ordinal) triples. Quotes as the
        /// Rust engine's walks read them (its terms keep quotes): a word typed with a quote
        /// needs it (unless <paramref name="ignoreQuotes"/>), a quote-free word finds a quoted
        /// token. <paramref name="matchMarks"/>: nikud / te'amim must match as typed.
        /// </summary>
        internal static int[] FindAllMatches(string text, IReadOnlyList<string> words, bool phrase, bool ignoreQuotes,
                                             bool matchMarks = false)
        {
            if (string.IsNullOrEmpty(text) || words == null) return System.Array.Empty<int>();
            var terms = new List<string>();
            var wordForms = new List<string>(); // per term: its surface form, null = any
            var stream = new TokenStream();
            foreach (var w in words)
            {
                if (string.IsNullOrEmpty(w)) continue;
                foreach (var t in stream.Tokenize(w))
                {
                    var norm = t.Normalized;
                    if (norm.Length == 0) continue;
                    terms.Add(norm);
                    string form = SurfaceForm.Of(w, t.RawStart, t.RawEnd, !ignoreQuotes, matchMarks);
                    bool constrains = matchMarks || form != SurfaceForm.WithoutQuotes(form);
                    wordForms.Add(constrains ? form : null);
                }
            }
            if (terms.Count == 0) return System.Array.Empty<int>();
            var prepared = PreparedQueryGroups.FromLiteralTerms(terms);
            // per ordinal: the forms its words allow (null when one of them allows any)
            var forms = new HashSet<string>[prepared.TermCount];
            var free = new bool[prepared.TermCount];
            for (int i = 0; i < terms.Count; i++)
            {
                if (!prepared.TryGetTerm(terms[i].AsSpan(), out var entry)) continue;
                int o = entry.Ordinal;
                if (wordForms[i] == null) free[o] = true;
                else (forms[o] ??= new HashSet<string>(System.StringComparer.Ordinal)).Add(wordForms[i]);
            }
            for (int o = 0; o < forms.Length; o++) if (free[o]) forms[o] = null;
            var marks = GetLocator(DefaultContextWords).FindAllMatches(text, prepared, phrase, ignoreQuotes, forms, matchMarks);
            var copy = new int[marks.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = marks[i];
            ReleaseLocatorIfOversized(text);
            return copy;
        }
    }
}
