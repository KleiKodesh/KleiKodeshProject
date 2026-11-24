/**
 * TOC search over heading paths - shared by the book view's TOC and the FastDocView outline:
 * the same normalisation (normalize() + punctuation tokenizing) and whole-word ordered matching.
 */
import { normalize } from '@/utils/normalizeText'

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

