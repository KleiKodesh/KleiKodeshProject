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
 * BUILD (first search, never the constructor):
 *   Each node's own text is tokenized (lowercase, punctuation-aware) once. A node's
 *   segments are its ancestors' token arrays root to leaf, walked through the parent
 *   chain at search time rather than copied per node. displayPaths are computed only
 *   for the ids asked for (the rows shown).
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
 *   const path = tree.displayPaths.get(nodeId)   // "root · parent · node"
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

// ─── Tokenizer ────────────────────────────────────────────────────────────────

/** Memoized \p{L}\p{N} classification for non-ASCII BMP code units. */
const _wordCharMemo = new Map<number, boolean>()
const _WORD_CHAR_RE = /[\p{L}\p{N}]/u

/**
 * Tokenize one node's text into lowercase tokens.
 * Keeps "." and ":" attached to a preceding letter/digit so Talmud page
 * references like "דף י." and "דף י:" survive as single tokens.
 *
 * Single-pass scanner equivalent to:
 *   text.toLowerCase()
 *     .replace(/([^\p{L}\p{N}])/gu, (ch, _m, offset, str) =>
 *       (ch === '.' || ch === ':') && offset > 0 && /[\p{L}\p{N}]/u.test(str[offset - 1]) ? ch : ' ')
 *     .split(/\s+/)
 *     .filter((t) => t.length > 0)
 * but without the per-character regex callback (this runs on every TOC row of
 * every candidate book during catalog search, so it is hot).
 * Exported for the equivalence test harness.
 */
export function tokenizeSegmentText(text: string): string[] {
  const s = text.toLowerCase()
  const tokens: string[] = []
  let token = ''
  let prevIsWord = false
  let i = 0
  while (i < s.length) {
    const code = s.charCodeAt(i)

    // Surrogate pair (high surrogate followed by a low surrogate) — classify
    // the full code point, like the /u regex did. A "." after the pair saw a
    // lone low surrogate as its previous char in the regex version (never a
    // word char), so prevIsWord stays false. A lone high surrogate falls
    // through to the single-unit path below (never a word char there either).
    if (
      code >= 0xd800 &&
      code <= 0xdbff &&
      i + 1 < s.length &&
      s.charCodeAt(i + 1) >= 0xdc00 &&
      s.charCodeAt(i + 1) <= 0xdfff
    ) {
      const pair = s.slice(i, i + 2)
      if (_WORD_CHAR_RE.test(pair)) {
        token += pair
      } else if (token) {
        tokens.push(token)
        token = ''
      }
      prevIsWord = false
      i += 2
      continue
    }

    let isWord: boolean
    if (
      (code >= 0x05d0 && code <= 0x05ea) || // Hebrew letters (incl. finals)
      (code >= 48 && code <= 57) || // 0-9
      (code >= 97 && code <= 122) || // a-z
      (code >= 65 && code <= 90) // A-Z (defensive — input is lowercased)
    ) {
      isWord = true
    } else if (code < 0x80) {
      isWord = false
    } else {
      let memo = _wordCharMemo.get(code)
      if (memo === undefined) {
        memo = _WORD_CHAR_RE.test(s[i]!)
        _wordCharMemo.set(code, memo)
      }
      isWord = memo
    }

    if (isWord || ((code === 46 || code === 58) && prevIsWord)) {
      token += s[i]
    } else if (token) {
      tokens.push(token)
      token = ''
    }
    prevIsWord = isWord
    i++
  }
  if (token) tokens.push(token)
  return tokens
}

/** Read access to display paths by node id ("root · parent · node"). */
export interface DisplayPathLookup {
  get(id: number): string | undefined
}

/** What a tree view needs from a search: ranked candidates and a path per result. */
export interface TreeSearcher {
  search(nodes: SearchableNode[], query: string, limit?: number): SearchableNode[]
  readonly displayPaths: DisplayPathLookup
}

/** One tree's share of a search across several trees (see searchAcross). */
export interface SearchPart {
  tree: SegmentSearchTree
  nodes: SearchableNode[]
}

interface ScoredHit {
  part: number
  node: SearchableNode
  score: number
  segIndices: number[]
}

const SEGMENT_CROSSING_PENALTY = 10

export class SegmentSearchTree implements TreeSearcher {
  /**
   * Built on first use, never on construction: a tree nobody searches costs only the
   * reference to its nodes. Nodes are addressed by position; `parent[i]` is the position
   * of node i's parent (-1 for a root). A node's segments are its ancestors' tokens,
   * walked through `parent` at search time - never copied into a per-node chain.
   *
   * Everything is flat typed arrays, no per-node objects:
   * - id → position: `sortedIds` (ascending) with `sortedPositions`, by binary search;
   * - tokens: each distinct token once, all joined in `vocabText` (token v is
   *   `vocabText[vocabStart[v] .. vocabStart[v+1])`), and every node's tokens as ids
   *   into it - node i's are `tokenIds[tokenStart[i] .. tokenStart[i+1])`.
   * A search compares a query word against the vocabulary once, then scores nodes by
   * id lookups instead of string compares.
   */
  private sortedIds: Int32Array | null = null
  private sortedPositions: Int32Array | null = null
  private parent: Int32Array | null = null
  private vocabText = ''
  private vocabStart: Int32Array | null = null
  private tokenStart: Int32Array | null = null
  private tokenIds: Uint16Array | Int32Array | null = null

  /** "root · parent · node" per node id, computed when asked for (only shown rows ask). */
  readonly displayPaths: DisplayPathLookup = { get: (id) => this.displayPath(id) }

  private readonly nodes: SearchableNode[]

  constructor(nodes: SearchableNode[]) {
    this.nodes = nodes
  }

  private ensureShape(): void {
    if (this.parent) return
    const nodes = this.nodes
    const positions = new Int32Array(nodes.length)
    for (let i = 0; i < nodes.length; i++) positions[i] = i
    // Ascending id; on a repeated id the later node sorts last and is the one found.
    positions.sort((a, b) => nodes[a]!.id - nodes[b]!.id || a - b)
    const sortedIds = new Int32Array(nodes.length)
    for (let k = 0; k < nodes.length; k++) sortedIds[k] = nodes[positions[k]!]!.id
    this.sortedIds = sortedIds
    this.sortedPositions = positions
    const parent = new Int32Array(nodes.length)
    for (let i = 0; i < nodes.length; i++) {
      const parentId = nodes[i]!.parentId
      parent[i] = parentId == null ? -1 : this.positionOf(parentId)
    }
    this.parent = parent
  }

  /** The position of the node with [id], or -1 (binary search; the last of repeated ids). */
  private positionOf(id: number): number {
    const ids = this.sortedIds!
    let lo = 0
    let hi = ids.length - 1
    let found = -1
    while (lo <= hi) {
      const mid = (lo + hi) >>> 1
      if (ids[mid]! <= id) {
        if (ids[mid] === id) found = mid
        lo = mid + 1
      } else hi = mid - 1
    }
    return found < 0 ? -1 : this.sortedPositions![found]!
  }

  private ensureTokens(): void {
    this.ensureShape()
    if (this.tokenIds) return
    const nodes = this.nodes
    const idOf = new Map<string, number>() // build-time only
    const vocab: string[] = []
    const tokenStart = new Int32Array(nodes.length + 1)
    let ids = new Int32Array(Math.max(16, nodes.length * 4))
    let count = 0
    for (let i = 0; i < nodes.length; i++) {
      tokenStart[i] = count
      for (const token of tokenizeSegmentText(nodes[i]!.text)) {
        let id = idOf.get(token)
        if (id === undefined) {
          id = vocab.length
          idOf.set(token, id)
          vocab.push(token)
        }
        if (count === ids.length) {
          const grown = new Int32Array(ids.length * 2)
          grown.set(ids)
          ids = grown
        }
        ids[count++] = id
      }
    }
    tokenStart[nodes.length] = count
    const vocabStart = new Int32Array(vocab.length + 1)
    let offset = 0
    for (let v = 0; v < vocab.length; v++) {
      vocabStart[v] = offset
      offset += vocab[v]!.length
    }
    vocabStart[vocab.length] = offset
    // One flat string, not one string object per token.
    this.vocabText = vocab.join('')
    this.vocabStart = vocabStart
    this.tokenStart = tokenStart
    this.tokenIds = vocab.length <= 0xffff ? Uint16Array.from(ids.subarray(0, count)) : ids.slice(0, count)
  }

  /**
   * Per query word, which vocabulary ids match it: `prefix[id]` when the token starts
   * with the word, `exact[id]` when it is the word or the word plus one Talmud-page
   * suffix ("." or ":") - so a bare page number is exact against both amud forms.
   */
  private matchersFor(words: string[]): { prefix: Uint8Array; exact: Uint8Array }[] {
    const text = this.vocabText
    const start = this.vocabStart!
    const size = start.length - 1
    return words.map((w) => {
      const prefix = new Uint8Array(size)
      const exact = new Uint8Array(size)
      for (let id = 0; id < size; id++) {
        const from = start[id]!
        const length = start[id + 1]! - from
        if (length < w.length || !text.startsWith(w, from)) continue
        prefix[id] = 1
        if (length === w.length) exact[id] = 1
        else if (length === w.length + 1) {
          const last = text.charCodeAt(from + w.length)
          if (last === 46 || last === 58) exact[id] = 1 // "." or ":"
        }
      }
      return { prefix, exact }
    })
  }

  displayPath(id: number): string | undefined {
    this.ensureShape()
    let i = this.positionOf(id)
    if (i < 0) return undefined
    let path = this.nodes[i]!.text
    for (i = this.parent![i]!; i >= 0; i = this.parent![i]!) path = `${this.nodes[i]!.text} · ${path}`
    return path
  }

  /** Node [index] and its ancestors root-to-leaf, as node positions into [out] (reused across calls). */
  private segmentsOf(index: number, out: number[]): number[] {
    const parent = this.parent!
    let depth = 0
    for (let i = index; i >= 0; i = parent[i]!) depth++
    out.length = depth
    for (let i = index; i >= 0; i = parent[i]!) out[--depth] = i
    return out
  }

  /**
   * Score a single node against the query words (Pass 1 inner loop).
   *
   * Returns { score, segIndices } where:
   *   score      — lower is better; Infinity means no match
   *   segIndices — which segment index each query word matched in
   *
   * When lastWordExact is true, the final word must match a token exactly (see
   * matchersFor for what counts as exact).
   */
  private _score(
    segs: number[],
    matchers: { prefix: Uint8Array; exact: Uint8Array }[],
    lastWordExact: boolean,
  ): { score: number; segIndices: number[] } {
    const tokenStart = this.tokenStart!
    const tokenIds = this.tokenIds!
    const words = matchers
    const segIndices: number[] = []  // segment index where each query word matched
    const tokenIndices: number[] = [] // token index within that segment
    let segFrom = 0 // ordered subsequence: next word must match at segFrom or later

    for (let wi = 0; wi < words.length; wi++) {
      const requireExact = lastWordExact && wi === words.length - 1
      const match = requireExact ? words[wi]!.exact : words[wi]!.prefix
      let found = false

      for (let si = segFrom; si < segs.length; si++) {
        const node = segs[si]!
        const start = tokenStart[node]!
        const end = tokenStart[node + 1]!
        for (let k = start; k < end; k++) {
          const ti = k - start
          if (match[tokenIds[k]!]) {
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
    // plus a heavy penalty for each segment boundary crossed between consecutive pairs,
    // so words found together in one entry rank above the same words spread over the path.
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

  /** True when an ancestor of [id] is in [matchedIds] (Pass 3). */
  private hasMatchedAncestor(id: number, matchedIds: ReadonlySet<number>): boolean {
    const index = this.positionOf(id)
    if (index < 0) return false
    for (let i = this.parent![index]!; i >= 0; i = this.parent![i]!) {
      if (matchedIds.has(this.nodes[i]!.id)) return true
    }
    return false
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
    return SegmentSearchTree.searchAcross([{ tree: this, nodes }], query, limit).map((hit) => hit.node)
  }

  /**
   * One search over several trees, ranked as if they were one tree: every candidate is
   * scored, all are sorted together, the best result decides the bonded word pairs for
   * all, and each tree suppresses descendants of its own matches. Lets two trees be
   * searched as one list without building a third tree of both.
   */
  static searchAcross(parts: SearchPart[], query: string, limit = Infinity): { node: SearchableNode; part: number }[] {
    const words = query.trim().toLowerCase().split(/\s+/).filter(Boolean)
    if (!words.length) return []

    // Pass 1: score all nodes.
    // Try exact-last-word first; fall back to all-prefix if that yields nothing.
    const scratch: number[] = []
    const matchers = parts.map(({ tree }) => {
      tree.ensureTokens()
      return tree.matchersFor(words)
    })
    const scoreAll = (lastWordExact: boolean) => {
      const out: ScoredHit[] = []
      parts.forEach(({ tree, nodes }, part) => {
        // Candidates are usually the tree's own node list: their positions are their indexes.
        const own = nodes === tree.nodes
        for (let n = 0; n < nodes.length; n++) {
          const node = nodes[n]!
          const index = own ? n : tree.positionOf(node.id)
          if (index < 0) continue
          const { score, segIndices } = tree._score(tree.segmentsOf(index, scratch), matchers[part]!, lastWordExact)
          if (score !== Infinity) out.push({ part, node, score, segIndices })
        }
      })
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

    // Pass 3: ancestry deduplication, per tree.
    // If a node matched, suppress all its descendants so the parent result
    // implicitly covers them without cluttering the list.
    const matchedIds = parts.map(() => new Set<number>())
    for (const { part, node } of filtered) matchedIds[part]!.add(node.id)
    const deduplicated = filtered.filter(({ part, node }) => !parts[part]!.tree.hasMatchedAncestor(node.id, matchedIds[part]!))

    const count = limit === Infinity ? deduplicated.length : limit
    return deduplicated.slice(0, count).map(({ node, part }) => ({ node, part }))
  }
}
