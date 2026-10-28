using System.Collections.Generic;
using MessagePack;

namespace KitveiHakodesh.Core.SeforimDbFullTextSearch
{
    /// <summary>
    /// One full-text hit, ready for the frontend: the engine's match plus its book id. The
    /// row header (TOC path) is fetched by line id for the rows the page renders.
    ///
    /// A wire type — the property names are the payload keys and must not change casually.
    /// The `Ready`/`Done`/`Error` envelopes these ride in stay with each transport. Numbers
    /// only, and the marks array is sized exactly: a broad search sends hundreds of thousands
    /// of these, so there is no list slack and nothing derivable (the matched term ids are
    /// read off <see cref="SnippetMarks"/> by the page).
    /// </summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class FtsHit
    {
        public int LineId { get; set; }
        public int BookId { get; set; }

        public int Score { get; set; }

        /// <summary>Word distance of the tightest window (0 = query words adjacent). The
        /// frontend's relevancy sort key.</summary>
        public int WordDistance { get; set; }

        /// <summary>
        /// The snippet DESCRIPTOR — not its HTML — located once, by the search's own check,
        /// over the MATCHED LINE's text alone. The page fetches the line's text
        /// (<see cref="FtsSnippetText"/>) only for rows it renders and rebuilds the HTML: the
        /// window [<see cref="SnippetStart"/>, <see cref="SnippetEnd"/>) with tags and {X}
        /// markers stripped, each <see cref="SnippetMarks"/> range wrapped in &lt;mark&gt;, and
        /// "…" on each cut side. A <see cref="SnippetFlagShort"/> window additionally gets plain
        /// context from the neighbour lines around it — presentation only, never part of the
        /// descriptor. Keep in step with the page's fullTextSearchSnippets.ts.
        /// </summary>
        /// <remarks>Raw char offsets of the rendered window in the matched line's text.</remarks>
        public int SnippetStart { get; set; }
        public int SnippetEnd { get; set; }

        /// <summary>Highlights as flat (start, length, termId) triples — raw char offsets into
        /// the matched line's text, in document order, plus the id (into the search's
        /// <see cref="FtsTermTable"/>) of the concrete index term that mark matched. The
        /// distinct term ids of a hit (the 1-3 fuzzy/wildcard expansions that actually occur
        /// in this line — what the book view highlights) are every third int here; they are
        /// not sent separately.</summary>
        public int[] SnippetMarks { get; set; } = System.Array.Empty<int>();

        /// <summary>Bit flags: <see cref="SnippetFlagLeadingEllipsis"/> ("…" before the
        /// window — it starts mid-text), <see cref="SnippetFlagTrailingEllipsis"/> ("…" after
        /// it — it ends early), <see cref="SnippetFlagShort"/> (the window holds fewer words
        /// than the requested context: the line itself is too short to fill it, so the page
        /// fetches its neighbour lines with it). An int, not bools: every hit field is a number
        /// so the page's IndexedDB cache and the wire carry numbers only.</summary>
        public int SnippetFlags { get; set; }

        public const int SnippetFlagLeadingEllipsis = 1;
        public const int SnippetFlagTrailingEllipsis = 2;
        public const int SnippetFlagShort = 4;
    }

    /// <summary>
    /// A snippet's TEXT: the matched line's own text and, for a short window only, the plain
    /// context around it — the same book's non-empty lines up to
    /// <see cref="SeforimDbFtsCorpus.SnippetNeighbourLineRadius"/> before and after, each side
    /// joined by single spaces in document order. Empty sides for a line that is not short.
    /// </summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class FtsSnippetText
    {
        public int LineId { get; set; }
        public string Text { get; set; } = "";
        public string Before { get; set; } = "";
        public string After { get; set; } = "";
    }

    /// <summary>
    /// One search's matched-term strings, numbered in first-seen order. Hits carry ids into
    /// it (the term id of each <see cref="FtsHit.SnippetMarks"/> triple); a transport sends the strings once — streamed as
    /// they appear (<see cref="TakeNewTerms"/>) or whole at the end (<see cref="All"/>).
    /// Thread-safe: hits are built across cores. Reads are lock-free — a search has a
    /// handful of distinct terms but millions of marks, so after the first few hits every
    /// <see cref="IdOf"/> is a lookup of a term already there.
    /// </summary>
    public sealed class FtsTermTable
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _ids =
            new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
        private readonly List<string> _terms = new List<string>();
        private int _sent;

        public int IdOf(string term)
        {
            if (_ids.TryGetValue(term, out int id)) return id;   // lock-free: the common case
            lock (_terms)
            {
                if (_ids.TryGetValue(term, out id)) return id;    // added by another core meanwhile
                id = _terms.Count;
                _terms.Add(term);
                _ids[term] = id;
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

        /// <summary>Why the index last started building, and when; null if it never has in
        /// this process.</summary>
        public KitveiHakodesh.Core.Common.IndexBuildReason? BuildReason { get; set; }
    }

    /// <summary>
    /// Substring search candidates inside given line ranges (SeforimDbLineSubstringSearch: the book
    /// view's in-book search, the commentary panel's search). <c>Answered</c> false: the
    /// index cannot answer exactly right now, and the caller reads the lines itself.
    /// Otherwise the three parallel arrays name every line that MAY contain the query —
    /// the caller confirms the exact substring on the text.
    /// </summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class FtsLinesSearchResult
    {
        public bool Answered { get; set; }
        public int[] BookIds { get; set; } = System.Array.Empty<int>();
        public int[] LineIndexes { get; set; } = System.Array.Empty<int>();
        /// <summary>The same lines' ids — to fetch their text by.</summary>
        public int[] LineIds { get; set; } = System.Array.Empty<int>();
    }

    /// <summary>One (book, first lineIndex, last lineIndex) range of a scoped substring search, inclusive.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class FtsLineRange
    {
        public int BookId { get; set; }
        public int FirstLineIndex { get; set; }
        public int LastLineIndex { get; set; }
    }

    /// <summary>FTS result set. <c>Ready</c> is false while the index is still building.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class FtsSearchResult
    {
        public bool Ready { get; set; }
        public string? Error { get; set; }
        public List<FtsHit> Results { get; set; } = new List<FtsHit>();
        /// <summary>The matched-term strings the hits' mark term ids index into.</summary>
        public List<string> Terms { get; set; } = new List<string>();
        /// <summary>Snippet texts for the FIRST hits, so the page can render their snippets
        /// without a fetch. Per result set, never per hit — hits stay numbers only. See
        /// <see cref="FtsStreamChunk.SnippetTexts"/>.</summary>
        public List<FtsSnippetText> SnippetTexts { get; set; } = new List<FtsSnippetText>();
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
        /// appends them to its table, which the hits' mark term ids index into.</summary>
        public List<string> NewTerms { get; set; } = new List<string>();
        /// <summary>Snippet texts riding on the FRAME, for the first hits of the search only
        /// (<see cref="SeforimDbFtsSearcher.SeedSnippetTextHitCount"/>): the page seeds its
        /// snippet-text cache from them before it renders, so the first screen shows snippets
        /// at once instead of placeholders. Later frames carry none. Hits themselves stay
        /// numbers only.</summary>
        public List<FtsSnippetText> SnippetTexts { get; set; } = new List<FtsSnippetText>();
        public bool Done { get; set; }
        public string? Error { get; set; }
        /// <summary>A RESUMED search (ftsSearchStream's resumeAfterLineId) whose resume line could
        /// not be placed any more, so the service runs it from the start instead: the receiver
        /// drops the hits it had. Set on the first frame only.</summary>
        public bool Restarted { get; set; }
        /// <summary>What the engine is doing right now (stable ASCII key: validate, rewrite,
        /// parse, expand, lookup, fetch, snippets, enrich). Until the first results frame, a
        /// stage change is a frame of its own carrying no results; after it, the latest stage
        /// rides on the next results frame.</summary>
        public string? Stage { get; set; }
    }
}
