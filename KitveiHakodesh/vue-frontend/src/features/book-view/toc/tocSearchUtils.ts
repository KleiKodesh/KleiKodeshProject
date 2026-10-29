export { normalizeTocWords, buildTocSearchPaths, matchWords } from '@/utils/tocSearchPaths'
export type { TocNode, TocSearchNode } from '@/utils/tocSearchPaths'
export { SegmentSearchTree as SearchableTree } from '@/utils/segmentSearchTree'
export type { SearchableNode } from '@/utils/segmentSearchTree'

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
