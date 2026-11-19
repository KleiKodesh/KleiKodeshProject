import type { HomeDailyLearningBookName, HomeDailyLearningTocStep } from './homeDailyLearningTypes'

/**
 * Pure matching for daily-learning cards: which library book a cycle names, and which
 * TOC entry inside it. No service calls here, so it is testable on plain rows.
 */

const NIQQUD_PATTERN = /[֑-ֽֿ-ׇ]/g
const GERESH_PATTERN = /[׳״'"]/g
const MAQAF_PATTERN = /־/g

/** Heading text as the cycles spell it: no niqqud or geresh, maqaf as a space, single spaces. */
export function normalizeHeadingText(text: string): string {
  return text
    .replace(MAQAF_PATTERN, ' ')
    .replace(NIQQUD_PATTERN, '')
    .replace(GERESH_PATTERN, '')
    .replace(/\s+/g, ' ')
    .trim()
}

/**
 * Title folded for comparison: punctuation dropped, and vav/yod dropped after each word's
 * first letter, so plene and defective spellings meet (תהילים = תהלים, מקוואות = מקואות).
 */
export function foldTitle(text: string): string {
  return normalizeHeadingText(text)
    .replace(/[^א-תa-zA-Z0-9]+/g, ' ')
    .trim()
    .split(' ')
    .map((word) => word.charAt(0) + word.slice(1).replace(/[וי]/g, ''))
    .join(' ')
}

export interface MatchableBook {
  id: number
  title: string
  parentPath?: string
}

/** Category path → folded segments ("תלמוד בבלי / סדר מועד" → two segments). */
function pathSegments(path: string | undefined): string[] {
  return path ? path.split(' / ').map(foldTitle) : []
}

/** Books grouped by folded title, built once per book list. */
export type BookTitleIndex<T extends MatchableBook> = Map<string, T[]>

export function buildBookTitleIndex<T extends MatchableBook>(books: readonly T[]): BookTitleIndex<T> {
  const index: BookTitleIndex<T> = new Map()
  for (const book of books) {
    const key = foldTitle(book.title)
    const sameTitle = index.get(key)
    if (sameTitle) sameTitle.push(book)
    else index.set(key, [book])
  }
  return index
}

/**
 * The book a name points at: same folded title and inside `section` when given. The exact
 * spelling beats a folded-only match (שבועות and שביעית fold alike); then the one nearest
 * the section root (a base text sits directly under its order, commentaries in deeper
 * folders); then library order.
 */
export function pickBook<T extends MatchableBook>(index: BookTitleIndex<T>, name: HomeDailyLearningBookName): T | undefined {
  const section = name.section ? pathSegments(name.section) : []
  const exactTitle = normalizeHeadingText(name.title)
  let best: { book: T; rank: number } | undefined
  for (const book of index.get(foldTitle(name.title)) ?? []) {
    const segments = pathSegments(book.parentPath)
    if (section.some((segment, position) => segments[position] !== segment)) continue
    const rank = (normalizeHeadingText(book.title) === exactTitle ? 0 : 1000) + segments.length
    if (!best || rank < best.rank) best = { book, rank }
  }
  return best?.book
}

export interface MatchableTocEntry {
  id: number
  parentId: number | null
  text: string
  lineIndex: number | null
}

/** A LIKE pattern folded like a title, around its `%` wildcards. */
function foldLikePattern(pattern: string): string {
  return pattern
    .split('%')
    .map((part) => {
      const folded = foldTitle(part)
      // Keep the space next to a wildcard ("פרשת X %") that folding trimmed away.
      return (part.startsWith(' ') && folded ? ' ' : '') + folded + (part.endsWith(' ') && folded ? ' ' : '')
    })
    .join('%')
}

/** SQL LIKE pattern (only `%`) → anchored RegExp. */
function likePatternToRegExp(pattern: string): RegExp {
  const body = pattern
    .split('%')
    .map((part) => part.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
    .join('.*')
  return new RegExp(`^${body}$`, 'i')
}

/**
 * Walks `tocPath` down the TOC: each step's patterns (in order) are tried against the
 * entries under the previous step's match, in document order. Returns the deepest entry
 * found, or null when even the first step has no match.
 */
export function findTocPathEntry<T extends MatchableTocEntry>(
  entries: readonly T[],
  tocPath: readonly HomeDailyLearningTocStep[],
): T | null {
  const childrenOf = new Map<number | null, T[]>()
  for (const entry of entries) {
    const siblings = childrenOf.get(entry.parentId)
    if (siblings) siblings.push(entry)
    else childrenOf.set(entry.parentId, [entry])
  }
  const knownIds = new Set(entries.map((entry) => entry.id))
  // Document order under one entry (depth-first); roots are entries whose parent isn't listed.
  const descendants = (parentId: number | null): T[] => {
    const out: T[] = []
    const visit = (id: number | null) => {
      for (const child of childrenOf.get(id) ?? []) {
        out.push(child)
        visit(child.id)
      }
    }
    if (parentId !== null) visit(parentId)
    else for (const [id, children] of childrenOf) if (id === null || !knownIds.has(id)) for (const root of children) {
      out.push(root)
      visit(root.id)
    }
    return out
  }

  let found: T | null = null
  for (const step of tocPath) {
    const anySpelling = !Array.isArray(step)
    const patterns = Array.isArray(step) ? step : step.anySpelling
    const comparable = anySpelling ? foldTitle : normalizeHeadingText
    const scope = descendants(found ? found.id : null)
    let stepMatch: T | undefined
    for (const pattern of patterns) {
      const regExp = likePatternToRegExp(anySpelling ? foldLikePattern(pattern) : pattern)
      stepMatch = scope.find((entry) => regExp.test(comparable(entry.text)))
      if (stepMatch) break
    }
    if (!stepMatch) break
    found = stepMatch
  }
  return found
}
