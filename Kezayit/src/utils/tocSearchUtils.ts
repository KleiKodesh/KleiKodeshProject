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

/** Normalize a string for TOC search: strip quotes, lowercase, tokenize punctuation */
const normalizeToc = (s: string) =>
  normalize(s)
    .replace(/[^\p{L}\p{N}]+/gu, (m) => ` ${m} `)
    .replace(/\s+/g, ' ')
    .trim()

/** Apply only the TOC-specific tokenization step (for already-normalized strings) */
const tocTokenize = (s: string) =>
  s
    .replace(/[^\p{L}\p{N}]+/gu, (m) => ` ${m} `)
    .replace(/\s+/g, ' ')
    .trim()

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
    const re = new RegExp(`(?:^|\\s)${w}(?:\\s|$)`)
    const slice = path.slice(pos)
    const m = re.exec(slice)
    if (!m) return false
    pos += m.index + m[0].length
  }
  return true
}

/**
 * Score how tightly query words match a path.
 * Tokenizes the path (splitting on whitespace and " / " separators) into an indexed
 * token array, finds the earliest whole-word match position for each query word in
 * order, then sums the token-index distances between consecutive matched pairs.
 *
 * Lower score = tighter match (words closer together in the path).
 * Returns Infinity if any query word has no whole-word match.
 *
 * Examples (token distance between consecutive matched pairs):
 *   query "פרק ד", path "פרק ד"          → positions [0,1] → distance 1 → score 1
 *   query "פרק ד", path "פרק א / פסוק ד" → positions [0,3] → distance 3 → score 3
 *   query "בראשית פרק ד", path "בראשית רמב"ן / פרק ד"
 *                                          → positions [0,2,3] → distances 2+1 → score 3
 */
export function scoreWords(path: string, words: string[]): number {
  // Tokenize: split on any run of non-letter/non-digit chars (covers spaces and " / ")
  const tokens = path.split(/[^\p{L}\p{N}]+/u).filter((t) => t.length > 0)
  const matchedPositions: number[] = []
  let searchFrom = 0
  for (const w of words) {
    let found = -1
    for (let i = searchFrom; i < tokens.length; i++) {
      if (tokens[i]!.startsWith(w)) {
        found = i
        break
      }
    }
    if (found === -1) return Infinity
    matchedPositions.push(found)
    searchFrom = found + 1
  }
  if (matchedPositions.length < 2) return 0
  let score = 0
  for (let i = 1; i < matchedPositions.length; i++) {
    score += matchedPositions[i]! - matchedPositions[i - 1]!
  }
  return score
}
