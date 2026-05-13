/**
 * Generic segment-aware search tree for hierarchical node lists.
 *
 * "Segment" means one node's text in the ancestor chain. A node at depth 3 has
 * three segments: grandparent text, parent text, own text. The algorithm matches
 * query words across those segments rather than against a flat concatenated string,
 * which lets it respect hierarchy boundaries.
 *
 * ── Algorithm overview ────────────────────────────────────────────────────────
 *
 * BUILD (constructor):
 *   For every node, walk the parentId chain to collect the full ancestor sequence.
 *   Each ancestor's text is tokenized (lowercase, punctuation-aware) into a segment.
 *   The result is segments[nodeId] = [[tok, tok, ...], [tok, ...], ...] root→leaf.
 *   Also build displayPaths[nodeId] = "root / parent / node" for rendering.
 *
 * SEARCH (three passes):
 *
 *   Pass 1 — score every node:
 *     Walk query words left-to-right. For each word, scan segments from the last
 *     matched segment onward (ordered subsequence — words must appear in order but
 *     need not be adjacent). Within a segment, find the first token that starts with
 *     the word (prefix match). Track which segment index and token index each word
 *     landed in.
 *
 *     Score = sum of intra-segment token distances between consecutive word pairs
 *     that landed in the same segment. Cross-segment transitions cost 0 — only
 *     spread within a single segment is penalized. A heavy per-segment-boundary
 *     penalty (×10) is added for each segment boundary crossed between consecutive
 *     words, so tighter matches rank higher.
 *
 *     Nodes where any query word has no match get score = Infinity and are excluded.
 *
 *     Two-attempt strategy: first try with the last word requiring an exact token
 *     match (not just prefix). If that yields results, use them — this prevents
 *     "פרק ל" from surfacing "פרק לא" when an exact "פרק ל" entry exists. If the
 *     exact-last-word attempt yields nothing, fall back to prefix on all words.
 *     Talmud-page suffix tokens ("ד." / "ד:") count as exact matches for the bare
 *     word ("ד") so both forms are found when the exact attempt succeeds.
 *
 *   Pass 2 — bond detection + filter:
 *     From the best-scoring result, detect "bonded" consecutive word pairs — pairs
 *     where both words landed in the same segment. Any result that breaks a bonded
 *     pair (puts those two words in different segments) is filtered out.
 *     Example: query "פרק ד" bonds in the best result → "פרק א / פסוק ד" is rejected
 *     because "פרק" and "ד" land in different segments there.
 *
 *   Pass 3 — ancestry deduplication:
 *     If a node matched, suppress all its descendants. Example: "פרק ד" matching a
 *     chapter node prevents "פרק ד / פסוק א", "פרק ד / פסוק ב" etc. from also
 *     appearing — the chapter result already covers them.
 *
 * ── Usage ─────────────────────────────────────────────────────────────────────
 *
 *   const tree = new SegmentSearchTree(nodes)
 *   const results = tree.search(leafNodes, query, 100)
 *   const path = tree.displayPaths.get(nodeId)   // "root / parent / node"
 *
 * Pass only the nodes you want as candidates to search() — typically leaf nodes
 * when you want to match books/entries rather than section headers.
 * The full node list (including ancestors) must be passed to the constructor so
 * the segment chains can be built correctly.
 */

export interface SearchableNode {
  id: number
  parentId: number | null
  text: string
  hasChildren: number | boolean
}

export class SegmentSearchTree {
  /**
   * segments[nodeId] = array of segments, root→leaf.
   * Each segment = array of lowercase tokens for one ancestor node.
   * Example: node "פסוק ד" with parent "פרק א" with parent "בראשית":
   *   [["בראשית"], ["פרק", "א"], ["פסוק", "ד"]]
   */
  private segments: Map<number, string[][]> = new Map()

  /** parentId per node — used for ancestry deduplication in Pass 3. */
  private parentIds: Map<number, number | null> = new Map()

  /**
   * Human-readable display path per node, built during construction.
   * Format: "root / parent / node" (original text, not lowercased).
   * Used by callers to render the path subtitle under a search result.
   */
  readonly displayPaths: Map<number, string> = new Map()

  constructor(nodes: SearchableNode[]) {
    this._build(nodes)
  }

  private _build(nodes: SearchableNode[]) {
    const byId = new Map(nodes.map((n) => [n.id, n]))
    const segCache = new Map<number, string[][]>()
    const displayCache = new Map<number, string>()

    /**
     * Tokenize one node's text into lowercase tokens.
     * Keeps "." and ":" attached to a preceding letter/digit so Talmud page
     * references like "דף י." and "דף י:" survive as single tokens.
     */
    const tokenize = (text: string): string[] =>
      text
        .toLowerCase()
        .replace(/([^\p{L}\p{N}])/gu, (ch, _m, offset, str) => {
          if ((ch === '.' || ch === ':') && offset > 0 && /[\p{L}\p{N}]/u.test(str[offset - 1]!))
            return ch
          return ' '
        })
        .split(/\s+/)
        .filter((t) => t.length > 0)

    /** Recursively build the segment chain for a node (memoized). */
    const getSegments = (id: number): string[][] => {
      const cached = segCache.get(id)
      if (cached) return cached
      const node = byId.get(id)
      if (!node) return []
      const parentSegs = node.parentId != null ? getSegments(node.parentId) : []
      const result = [...parentSegs, tokenize(node.text)]
      segCache.set(id, result)
      return result
    }

    /** Recursively build the display path for a node (memoized). */
    const getDisplay = (id: number): string => {
      const cached = displayCache.get(id)
      if (cached !== undefined) return cached
      const node = byId.get(id)
      if (!node) return ''
      const parent = node.parentId != null ? getDisplay(node.parentId) : ''
      const result = parent ? `${parent} / ${node.text}` : node.text
      displayCache.set(id, result)
      return result
    }

    for (const node of nodes) {
      this.segments.set(node.id, getSegments(node.id))
      this.displayPaths.set(node.id, getDisplay(node.id))
      this.parentIds.set(node.id, node.parentId)
    }
  }

  /**
   * Score a single node against the query words (Pass 1 inner loop).
   *
   * Returns { score, segIndices } where:
   *   score      — lower is better; Infinity means no match
   *   segIndices — which segment index each query word matched in
   *
   * When lastWordExact is true, the final word must match a token exactly.
   * A token with a trailing Talmud-page punctuation suffix ("." or ":") is
   * treated as an exact match for the bare word — so query "ד" is exact against
   * token "ד." and "ד:", enabling "פסחים דף ד" to find both "דף ד." and "דף ד:".
   */
  private _score(
    nodeId: number,
    words: string[],
    lastWordExact: boolean,
  ): { score: number; segIndices: number[] } {
    const segs = this.segments.get(nodeId)
    if (!segs) return { score: Infinity, segIndices: [] }

    const segIndices: number[] = []  // segment index where each query word matched
    const tokenIndices: number[] = [] // token index within that segment
    let segFrom = 0 // ordered subsequence: next word must match at segFrom or later

    for (let wi = 0; wi < words.length; wi++) {
      const w = words[wi]!
      const requireExact = lastWordExact && wi === words.length - 1
      let found = false

      for (let si = segFrom; si < segs.length; si++) {
        const seg = segs[si]!
        for (let ti = 0; ti < seg.length; ti++) {
          const tok = seg[ti]!
          // A token matches exactly when it equals the query word, or when it is
          // the query word followed by a single Talmud-page suffix ("." or ":").
          // This lets "ד" match both "ד." and "ד:" as exact hits so the two-attempt
          // strategy doesn't suppress them when a bare "ד" entry also exists.
          const isTalmudSuffix = tok.length === w.length + 1 && (tok.endsWith('.') || tok.endsWith(':')) && tok.startsWith(w)
          const isExact = tok === w || isTalmudSuffix
          const matches = requireExact ? isExact : tok.startsWith(w)
          if (matches) {
            segIndices.push(si)
            tokenIndices.push(ti)
            segFrom = si // next word may match same or later segment
            found = true
            break
          }
        }
        if (found) break
      }

      if (!found) return { score: Infinity, segIndices: [] }
    }

    // Score = intra-segment token distance for same-segment consecutive pairs,
    // plus a heavy penalty for each segment boundary crossed between consecutive pairs.
    // This ranks "פרק ד" (both words in one segment, distance 1) above
    // "מקורות / פרק ד" (same words but crossing a segment boundary).
    const SEGMENT_CROSSING_PENALTY = 10
    let score = 0
    for (let i = 1; i < words.length; i++) {
      if (segIndices[i] === segIndices[i - 1]) {
        score += tokenIndices[i]! - tokenIndices[i - 1]!
      } else {
        score += (segIndices[i]! - segIndices[i - 1]!) * SEGMENT_CROSSING_PENALTY
      }
    }

    return { score, segIndices }
  }

  /**
   * Search candidate nodes by query string.
   *
   * @param nodes  - candidate nodes to match against (typically leaf nodes only)
   * @param query  - raw query string; split on whitespace, lowercased internally
   * @param limit  - max results to return (default: no limit)
   * @returns matched nodes sorted by score, best first
   */
  search(nodes: SearchableNode[], query: string, limit = Infinity): SearchableNode[] {
    const words = query.trim().toLowerCase().split(/\s+/).filter(Boolean)
    if (!words.length) return []

    // Pass 1: score all nodes.
    // Try exact-last-word first; fall back to all-prefix if that yields nothing.
    const scoreAll = (lastWordExact: boolean) => {
      const out: { node: SearchableNode; score: number; segIndices: number[] }[] = []
      for (const node of nodes) {
        const { score, segIndices } = this._score(node.id, words, lastWordExact)
        if (score !== Infinity) out.push({ node, score, segIndices })
      }
      return out
    }

    let scored = scoreAll(true)
    if (!scored.length) scored = scoreAll(false)
    if (!scored.length) return []

    scored.sort((a, b) => a.score - b.score)

    // Pass 2: bond detection.
    // A "bonded" pair is two consecutive query words that both landed in the same
    // segment in the best result. Any result that breaks a bonded pair is filtered out.
    const best = scored[0]!
    const bonded: boolean[] = []
    for (let i = 0; i < words.length - 1; i++) {
      bonded.push(best.segIndices[i] === best.segIndices[i + 1])
    }

    const filtered = scored.filter(({ segIndices }) =>
      bonded.every((isBonded, i) => !isBonded || segIndices[i] === segIndices[i + 1]),
    )

    // Pass 3: ancestry deduplication.
    // If a node matched, suppress all its descendants so the parent result
    // implicitly covers them without cluttering the list.
    const matchedIds = new Set(filtered.map(({ node }) => node.id))
    const deduplicated = filtered.filter(({ node }) => {
      let parentId = this.parentIds.get(node.id) ?? null
      while (parentId !== null) {
        if (matchedIds.has(parentId)) return false
        parentId = this.parentIds.get(parentId) ?? null
      }
      return true
    })

    const count = limit === Infinity ? deduplicated.length : limit
    return deduplicated.slice(0, count).map(({ node }) => node)
  }
}
