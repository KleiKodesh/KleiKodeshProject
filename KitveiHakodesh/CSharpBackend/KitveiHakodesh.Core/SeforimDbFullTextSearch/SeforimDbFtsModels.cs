using System.Collections.Generic;
using MessagePack;

namespace KitveiHakodesh.Core.SeforimDbFullTextSearch
{
    /// <summary>
    /// One full-text hit, ready for the frontend: the engine's match plus the corpus-side
    /// enrichment (book id, TOC path) the searcher adds, so no consumer ever needs a second
    /// round-trip to display a result.
    ///
    /// A wire type — the property names are the payload keys and must not change casually.
    /// The `Ready`/`Done`/`Error` envelopes these ride in stay with each transport.
    /// </summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class FtsHit
    {
        public int LineId { get; set; }
        public int BookId { get; set; }

        /// <summary>The line's own TOC entry (line_toc), 0 when it has none. The page resolves
        /// the entry's path from the book's TOC on demand — no text travels per hit.</summary>
        public int TocEntryId { get; set; }

        public int Score { get; set; }

        /// <summary>Word distance of the tightest window (0 = query words adjacent). The
        /// frontend's relevancy sort key.</summary>
        public int WordDistance { get; set; }

        /// <summary>
        /// The snippet DESCRIPTOR — not its HTML. The page fetches the texts of
        /// <see cref="SnippetLineIds"/> (getLineContents) only for rows it renders, joins them
        /// with single spaces, and rebuilds the exact HTML the engine would have rendered:
        /// the window [<see cref="SnippetStart"/>, <see cref="SnippetEnd"/>) of that joined
        /// text with tags and {X} markers stripped, each <see cref="SnippetMarks"/> range
        /// wrapped in &lt;mark&gt;, and "…" on each cut side. Keep in step with FtsLib's
        /// SnippetHtmlRenderer (the reference rendering) and the page's fullTextSearchSnippets.ts.
        /// </summary>
        /// <remarks>The matched line alone, or — for a short window extended over its
        /// neighbours — its non-empty same-book neighbour lines around it, in document order.</remarks>
        public List<int> SnippetLineIds { get; set; } = new List<int>();

        /// <summary>Raw char offsets of the rendered window in the joined text.</summary>
        public int SnippetStart { get; set; }
        public int SnippetEnd { get; set; }

        /// <summary>Highlights as flat (start, length, termId) triples — raw char offsets into
        /// the joined text, in document order, plus the id (into the search's
        /// <see cref="FtsTermTable"/>) of the term that mark matched.</summary>
        public List<int> SnippetMarks { get; set; } = new List<int>();

        /// <summary>Bit flags: <see cref="SnippetFlagLeadingEllipsis"/> ("…" before the
        /// window — it starts mid-text), <see cref="SnippetFlagTrailingEllipsis"/> ("…" after
        /// it — it ends early). An int, not two bools: every hit field is a number so the
        /// page's IndexedDB cache and the wire carry numbers only.</summary>
        public int SnippetFlags { get; set; }

        public const int SnippetFlagLeadingEllipsis = 1;
        public const int SnippetFlagTrailingEllipsis = 2;

        /// <summary>
        /// The concrete index terms that occur in THIS line's snippet marks (the fuzzy/wildcard
        /// expansions that actually matched here — typically 1-3, never the query's whole
        /// expansion), distinct, as ids into the search's <see cref="FtsTermTable"/> — the
        /// strings travel once per search (<see cref="FtsStreamChunk.NewTerms"/> /
        /// <see cref="FtsSearchResult.Terms"/>), never per hit. The book view highlights them.
        /// </summary>
        public List<int> TermIds { get; set; } = new List<int>();
    }

    /// <summary>
    /// One search's matched-term strings, numbered in first-seen order. Hits carry ids into
    /// it (<see cref="FtsHit.TermIds"/>); a transport sends the strings once — streamed as
    /// they appear (<see cref="TakeNewTerms"/>) or whole at the end (<see cref="All"/>).
    /// Thread-safe: hits are built across cores.
    /// </summary>
    public sealed class FtsTermTable
    {
        private readonly Dictionary<string, int> _ids = new Dictionary<string, int>();
        private readonly List<string> _terms = new List<string>();
        private int _sent;

        public int IdOf(string term)
        {
            lock (_terms)
            {
                if (!_ids.TryGetValue(term, out int id))
                {
                    id = _terms.Count;
                    _terms.Add(term);
                    _ids[term] = id;
                }
                return id;
            }
        }

        /// <summary>The terms added since the previous call, in id order — ids continue
        /// from where the previous call ended, so a receiver just appends them.</summary>
        public List<string> TakeNewTerms()
        {
            lock (_terms)
            {
                var fresh = _terms.GetRange(_sent, _terms.Count - _sent);
                _sent = _terms.Count;
                return fresh;
            }
        }

        /// <summary>Every term so far, indexed by id.</summary>
        public List<string> All()
        {
            lock (_terms) return new List<string>(_terms);
        }
    }

    /// <summary>
    /// Where background indexing stands — what the frontend's indexing overlay renders.
    /// "Chunks" are lines; the names are historical and on the wire, so they stay.
    /// </summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class FtsIndexStatus
    {
        public bool IsReady { get; set; }
        public bool IsIndexing { get; set; }
        public double Percentage { get; set; }
        public int ProcessedChunks { get; set; }
        public int TotalChunks { get; set; }

        /// <summary>True when no seforim database exists at the resolved path — there is
        /// nothing to index, which is a different message than "still building".</summary>
        public bool DbMissing { get; set; }

        /// <summary>True when the build ended without a searchable index and nothing will retry
        /// it. TERMINAL, like <see cref="IsReady"/> and <see cref="DbMissing"/>: without it a
        /// failed build reported "not ready, not indexing" forever and every progress stream
        /// waited on it.</summary>
        public bool Failed { get; set; }

        /// <summary>True while the build is in its final force-merge phase. That phase has no
        /// line-level progress — <see cref="Percentage"/> necessarily sits at its last value for
        /// minutes — so a host needs to be told it is a distinct phase rather than a stalled
        /// bar.</summary>
        public bool IsMerging { get; set; }

        /// <summary>Force-merge passes committed so far, and the CURRENT ESTIMATE of the total.
        /// The estimate MOVES as the merge proceeds, so it drives a phase indicator and never
        /// an ETA.</summary>
        public int MergePassesDone { get; set; }
        public int MergePassesExpected { get; set; }
    }

    /// <summary>FTS result set. <c>Ready</c> is false while the index is still building.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class FtsSearchResult
    {
        public bool Ready { get; set; }
        public string? Error { get; set; }
        public List<FtsHit> Results { get; set; } = new List<FtsHit>();
        /// <summary>The matched-term strings <see cref="FtsHit.TermIds"/> index into.</summary>
        public List<string> Terms { get; set; } = new List<string>();
        /// <summary>Snippet line texts (line id -> text) for the FIRST hits, so the page can
        /// render their snippets without a fetch. Per result set, never per hit — hits stay
        /// numbers only. See <see cref="FtsStreamChunk.LineTexts"/>.</summary>
        public Dictionary<int, string> LineTexts { get; set; } = new Dictionary<int, string>();
    }

    /// <summary>One pushed frame of a streaming search. The final frame has <c>Done</c> true
    /// (and the connection closes after it). <c>Ready</c> false = index still building —
    /// sent as the first and only frame.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class FtsStreamChunk
    {
        public bool Ready { get; set; } = true;
        public List<FtsHit> Results { get; set; } = new List<FtsHit>();
        /// <summary>Matched-term strings first seen in this frame, in id order: the receiver
        /// appends them to its table, which <see cref="FtsHit.TermIds"/> index into.</summary>
        public List<string> NewTerms { get; set; } = new List<string>();
        /// <summary>Snippet line texts (line id -> text) riding on the FRAME, for the first
        /// hits of the search only (<see cref="SeforimDbFtsSearcher.SeedLineTextHitCount"/>):
        /// the page seeds its line-text cache from them before it renders, so the first
        /// screen shows snippets at once instead of placeholders. Later frames carry none.
        /// Hits themselves stay numbers only.</summary>
        public Dictionary<int, string> LineTexts { get; set; } = new Dictionary<int, string>();
        public bool Done { get; set; }
        public string? Error { get; set; }
        /// <summary>What the engine is doing right now (stable ASCII key: validate, rewrite,
        /// parse, expand, lookup, fetch, snippets, enrich). Until the first results frame, a
        /// stage change is a frame of its own carrying no results; after it, the latest stage
        /// rides on the next results frame.</summary>
        public string? Stage { get; set; }
    }
}
