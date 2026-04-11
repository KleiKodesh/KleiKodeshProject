import { normalize } from './normalizeText'

export interface TocNode {
  id: number
  parentId: number | null
  bookId: number
  text: string
  lineIndex: number | null
}

export interface TocSearchNode extends TocNode {
  tocSearchPath: string // normalized, space-joined, for matching
  tocDisplayPath: string // original text joined with " / ", for display
}

// Chars stripped before fuzzy title comparison: Hebrew geresh/gershayim, ASCII/curly quotes, maqaf, hyphen
const TITLE_STRIP_RE = /["\u05f4\u05f3\u201c\u201d\u2018\u2019\u05be\-]/g

// Minimum ratio: shorter word-set must be >= this fraction of the longer one
const TITLE_RATIO = 0.6

/**
 * Bookids whose root TOC entry is a genuine title variant that the fuzzy rule
 * misses (ratio too low because the root drops a long subtitle like "על שולחן ערוך").
 * Extend this list when new books with the same pattern arrive.
 */
const FORCE_STRIP_BOOK_IDS = new Set([
  6036, // אמרי בינה על שולחן ערוך אורח חיים  →  אמרי בינה אורח חיים
  6037, // אמרי בינה על שולחן ערוך יורה דעה   →  אמרי בינה יורה דעה
  6042, // חידושי הרי"ם על שולחן ערוך אבן העזר →  חידושי הרי"ם אבן העזר
  6043, // חידושי הרי"ם על שולחן ערוך חושן משפט חלק א  →  חידושי הרי"ם חושן משפט חלק א
  6044, // חידושי הרי"ם על שולחן ערוך חושן משפט חלק ב  →  חידושי הרי"ם חושן משפט חלק ב
])

function normTitle(s: string): string[] {
  return s.replace(TITLE_STRIP_RE, '').split(/\s+/).filter(Boolean)
}

function isTitleVariant(bookTitle: string, rootText: string): boolean {
  const bt = normTitle(bookTitle)
  const rt = normTitle(rootText)
  if (!bt.length || !rt.length) return false
  const [shorter, longer] = bt.length <= rt.length ? [bt, rt] : [rt, bt]
  if (shorter.length < longer.length * TITLE_RATIO) return false
  return shorter.every((w) => longer.includes(w))
}

/**
 * Remove root TOC entries whose text is a title variant of the book title.
 * Matching is fuzzy: strips quotes/maqaf, then checks that the shorter word-set
 * is a subset of the longer one with a minimum overlap ratio of 0.6.
 * A small static list of bookIds covers known cases the ratio rule misses.
 *
 * When a root is removed its direct children are re-parented to null.
 * If nodes carry a `level` field it is decremented by 1 for affected descendants.
 *
 * Pass `singleRootOnly: true` to only strip when there is exactly one root.
 */
export function stripTocTitleRoots<T extends { id: number; parentId: number | null; text: string }>(
  nodes: T[],
  bookTitle: string,
  options: { singleRootOnly?: boolean; bookId?: number } = {},
): T[] {
  if (!bookTitle || !nodes.length) return nodes
  const roots = nodes.filter((n) => n.parentId === null)
  if (options.singleRootOnly && roots.length !== 1) return nodes
  const forceStrip = options.bookId != null && FORCE_STRIP_BOOK_IDS.has(options.bookId)
  const rootIds = new Set(
    roots.filter((r) => forceStrip || isTitleVariant(bookTitle, r.text)).map((r) => r.id),
  )
  if (!rootIds.size) return nodes
  const hasLevel = 'level' in nodes[0]!
  return nodes
    .filter((n) => !rootIds.has(n.id))
    .map((n) => {
      const parentWasRoot = n.parentId !== null && rootIds.has(n.parentId)
      const updated = parentWasRoot ? { ...n, parentId: null } : n
      if (hasLevel && parentWasRoot) {
        return { ...updated, level: (updated as unknown as { level: number }).level - 1 }
      }
      return updated
    })
}

/**
 * Given query words, find the longest right-trimmed prefix that matches at least
 * one book's searchPath. Returns { bookWords, tocWords } or null if no split found.
 */
export function splitQuery(
  words: string[],
  matchBooks: (words: string[]) => boolean,
): { bookWords: string[]; tocWords: string[] } | null {
  for (let trim = 1; trim < words.length; trim++) {
    const bookWords = words.slice(0, words.length - trim)
    if (matchBooks(bookWords)) {
      return { bookWords, tocWords: words.slice(words.length - trim) }
    }
  }
  return null
}

/**
 * Replace non-alphanumeric chars with spaces, but keep . and : attached to a preceding
 * letter/digit (e.g. י. or י: as in דף י. / דף י:).
 */
function tokenizePunctuation(s: string): string {
  return s
    .replace(/([^\p{L}\p{N}])/gu, (ch, _m, offset, str) => {
      if ((ch === '.' || ch === ':') && offset > 0 && /[\p{L}\p{N}]/u.test(str[offset - 1]!))
        return ch
      return ' '
    })
    .replace(/\s+/g, ' ')
    .trim()
}

/** Normalize a string for TOC search: strip quotes, lowercase, tokenize punctuation */
const normalizeToc = (s: string) => tokenizePunctuation(normalize(s))

/** Apply only the TOC-specific tokenization step (for already-normalized strings) */
const tocTokenize = (s: string) => tokenizePunctuation(s)

/** Normalize an array of TOC query words — input is already normalize()'d, so skip that step */
export const normalizeTocWords = (words: string[]) =>
  words.flatMap((w) => tocTokenize(w).split(' ')).filter((w) => w.length > 0)

/**
 * Build normalized intra-book TOC search paths for all entries.
 * Walks parentId chain to produce:
 *   tocSearchPath  - normalized, space-separated (for matching)
 *   tocDisplayPath - original text, " / " separated (for display)
 */
export function buildTocSearchPaths(nodes: TocNode[]): TocSearchNode[] {
  const map = new Map<number, TocSearchNode>()
  for (const n of nodes) {
    map.set(n.id, { ...n, tocSearchPath: '', tocDisplayPath: '' })
  }

  function getPaths(id: number): { search: string; display: string } {
    const node = map.get(id)
    if (!node) return { search: '', display: '' }
    if (node.tocSearchPath) return { search: node.tocSearchPath, display: node.tocDisplayPath }
    const parent = node.parentId != null ? getPaths(node.parentId) : { search: '', display: '' }
    node.tocSearchPath = normalizeToc(parent.search ? `${parent.search} ${node.text}` : node.text)
    node.tocDisplayPath = parent.display ? `${parent.display} / ${node.text}` : node.text
    return { search: node.tocSearchPath, display: node.tocDisplayPath }
  }

  for (const node of map.values()) getPaths(node.id)
  return Array.from(map.values())
}

/**
 * Match all words against a path as an ordered subsequence with whole-word boundaries.
 * Each word must appear after the previous match — order matters, adjacency does not.
 * Prevents "ד" from matching inside "יד" or "כד".
 */
export function matchWords(path: string, words: string[]): boolean {
  let pos = 0
  for (const w of words) {
    // Escape regex special chars so e.g. "י." does not treat "." as a wildcard
    const escaped = w.replace(/[$()*+.?[\\\]^{|}]/g, (c) => ['\\', c].join(''))
    const re = new RegExp('(?:^|\\s)' + escaped + '(?:\\s|$)')
    const slice = path.slice(pos)
    const m = re.exec(slice)
    if (!m) return false
    pos += m.index + m[0].length
  }
  return true
}

// ---------------------------------------------------------------------------
// SearchableTree
// ---------------------------------------------------------------------------

export interface SearchableNode {
  id: number
  parentId: number | null
  text: string
  hasChildren: number
}

/**
 * A pre-built search index for a flat node list with parent-child relationships.
 *
 * Each node is indexed as an array of normalized segment token arrays — one entry
 * per ancestor segment plus the node's own segment, from root to leaf:
 *   בראשית / פרק א / פסוק ד  →  [["בראשית"], ["פרק", "א"], ["פסוק", "ד"]]
 *
 * Search algorithm (two passes):
 *
 * Pass 1 — score every node:
 *   Match query words as an ordered subsequence across segments. Track which
 *   segment each query word matched in. Score = sum of token distances between
 *   consecutive matched query words within the same segment (cross-segment
 *   transitions cost 0 — only intra-segment spread is penalized).
 *   Returns Infinity for nodes where any query word has no match.
 *
 * Pass 2 — bond detection + filter:
 *   From the top-scoring result, detect "bonded" consecutive query word pairs —
 *   pairs that landed in the same segment. Any result where a bonded pair lands
 *   in different segments is filtered out.
 *   This ensures "פרק ד" (bonded in top result) rejects "פרק א / פסוק ד".
 *
 * Pass 3 — ancestry deduplication:
 *   If a node matched, all its descendants are suppressed. So "פרק ד" matching
 *   prevents "פרק ד / פסוק א", "פרק ד / פסוק ב" etc. from also appearing.
 */
export class SearchableTree {
  /** segments[nodeId] = array of segments, each segment = array of lowercase tokens */
  private segments: Map<number, string[][]> = new Map()
  /** parentId per node, for ancestry deduplication */
  private parentIds: Map<number, number | null> = new Map()
  /** display path per node id, for rendering */
  readonly displayPaths: Map<number, string> = new Map()

  constructor(nodes: SearchableNode[]) {
    this._build(nodes)
  }

  private _build(nodes: SearchableNode[]) {
    const byId = new Map(nodes.map((n) => [n.id, n]))
    const segCache = new Map<number, string[][]>()
    const displayCache = new Map<number, string>()

    const tokenize = (text: string): string[] =>
      text
        .toLowerCase()
        // Keep . and : attached to a preceding letter/digit (e.g. י. י:)
        .replace(/([^\p{L}\p{N}])/gu, (ch, _m, offset, str) => {
          if ((ch === '.' || ch === ':') && offset > 0 && /[\p{L}\p{N}]/u.test(str[offset - 1]!))
            return ch
          return ' '
        })
        .split(/\s+/)
        .filter((t) => t.length > 0)

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
   * Score a node against query words.
   * Returns Infinity if any word has no match.
   * Score = sum of intra-segment token distances between consecutive matched pairs
   * that land in the same segment. Cross-segment pairs cost 0.
   */
  private _score(nodeId: number, words: string[]): { score: number; segIndices: number[] } {
    const segs = this.segments.get(nodeId)
    if (!segs) return { score: Infinity, segIndices: [] }

    const segIndices: number[] = [] // which segment each word matched in
    const tokenIndices: number[] = [] // which token index within that segment
    let segFrom = 0

    for (const w of words) {
      let found = false
      for (let si = segFrom; si < segs.length; si++) {
        const seg = segs[si]!
        // search within this segment for a token that starts with w
        for (let ti = 0; ti < seg.length; ti++) {
          if (seg[ti]!.startsWith(w)) {
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

    // score = intra-segment token distance + heavy penalty per segment boundary crossed
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
   * Search nodes by query string. Returns matched nodes sorted by score (tighter first).
   * Applies bond detection: consecutive query word pairs that land in the same segment
   * in the best result are required to stay in the same segment across all results.
   */
  /**
   * @param limit - max results to return. Use 100 for the TOC tree panel (in-book view).
   *   Pass Infinity (or omit) for the books-fs search where all results should be shown.
   */
  search(nodes: SearchableNode[], query: string, limit = Infinity): SearchableNode[] {
    const words = query.trim().toLowerCase().split(/\s+/).filter(Boolean)
    if (!words.length) return []

    // Pass 1: score all nodes
    const scored: { node: SearchableNode; score: number; segIndices: number[] }[] = []
    for (const node of nodes) {
      const { score, segIndices } = this._score(node.id, words)
      if (score !== Infinity) scored.push({ node, score, segIndices })
    }
    if (!scored.length) return []

    scored.sort((a, b) => a.score - b.score)

    // Pass 2: detect bonded pairs from the best result
    const best = scored[0]!
    const bonded: boolean[] = [] // bonded[i] = true means words[i] and words[i+1] must share a segment
    for (let i = 0; i < words.length - 1; i++) {
      bonded.push(best.segIndices[i] === best.segIndices[i + 1])
    }

    // Filter: drop results that violate any bonded pair constraint
    const filtered = scored.filter(({ segIndices }) =>
      bonded.every((isBonded, i) => !isBonded || segIndices[i] === segIndices[i + 1]),
    )

    // Pass 3: ancestry deduplication — if a node matched, suppress all its descendants.
    // e.g. if "פרק ד" matched, don't also return "פרק ד / פסוק א", "פרק ד / פסוק ב", etc.
    const matchedIds = new Set(filtered.map(({ node }) => node.id))
    const deduplicated = filtered.filter(({ node }) => {
      let parentId = this.parentIds.get(node.id) ?? null
      while (parentId !== null) {
        if (matchedIds.has(parentId)) return false
        parentId = this.parentIds.get(parentId) ?? null
      }
      return true
    })

    return deduplicated
      .slice(0, limit === Infinity ? deduplicated.length : limit)
      .map(({ node }) => node)
  }
}
