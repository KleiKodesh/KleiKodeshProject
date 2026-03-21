import { normalize } from './normalize'

export interface TocNode {
  id: number
  parentId: number | null
  bookId: number
  text: string
}

export interface TocSearchNode extends TocNode {
  tocSearchPath: string  // normalized, space-joined, for matching
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

/** Normalize a string for TOC search: strip quotes, lowercase, insert spaces around non-letter/digit chars (keeps them as tokens) */
const normalizeToc = (s: string) => normalize(s).replace(/[^\p{L}\p{N}]+/gu, m => ` ${m} `).replace(/\s+/g, ' ').trim()

/** Normalize an array of TOC query words the same way */
export const normalizeTocWords = (words: string[]) =>
  words.flatMap(w => normalizeToc(w).split(' ')).filter(w => w.length > 0)
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
