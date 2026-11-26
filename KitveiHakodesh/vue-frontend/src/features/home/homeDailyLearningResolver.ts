import { bookTitle, requestBookTitles } from '@/stores/bookSummaries'
import {
  findBooksByName,
  findLineByHeRefSuffix,
  findLineByPageMarker,
  getAllAltTocEntries,
  getAllTocEntries,
  getAltTocStructures,
} from '@/webview-host/seforimApi'
import { findTocPathEntry } from './homeDailyLearningBookMatch'
import type { HomeDailyLearningTarget } from './homeDailyLearningTypes'

/** A daily-learning target resolved against the library: the book to open and, when found, the TOC entry. */
export interface HomeDailyLearningResolvedTarget {
  bookId: number
  bookTitle: string
  tocEntryId?: number
  tocLineIndex?: number
}

/**
 * Resolve a daily-learning target BY NAME (the service's findBooksByName: the same folding
 * and ranking as homeDailyLearningBookMatch.pickBook), then walk its TOC path. Returns null
 * only when the book is not in the library (the card is hidden); a missing TOC entry still
 * opens the book at its start. Throws when the library or the service can't answer, so
 * callers can tell "not there" from "try again".
 */
export async function resolveHomeDailyLearningTarget(
  target: HomeDailyLearningTarget,
): Promise<HomeDailyLearningResolvedTarget | null> {
  const [bookId] = await findBooksByName([target.books])
  if (!bookId) return null
  await requestBookTitles([bookId])
  const found = { bookId, bookTitle: bookTitle(bookId) }
  const book = { id: bookId }
  if (!target.tocPath.length) return withLineAnchor(found, target)

  const mainEntries = await getAllTocEntries(book.id)
  const tocEntry = findTocPathEntry(mainEntries, target.tocPath)
  if (tocEntry) return { ...found, tocEntryId: tocEntry.id, tocLineIndex: tocEntry.lineIndex ?? undefined }

  // Headings that live only in an alt TOC (Torah parshiyot, Yerushalmi dapim). Alt entry
  // ids aren't main-TOC ids, so only the line index is returned.
  const structures = (await getAltTocStructures(book.id)).filter(
    (structure) => !target.altTocKey || structure.key === target.altTocKey,
  )
  for (const structure of structures) {
    const altEntry = findTocPathEntry(await getAllAltTocEntries(structure.id), target.tocPath)
    if (altEntry?.lineIndex != null) return { ...found, tocLineIndex: altEntry.lineIndex }
  }
  if (target.fallbackTocPath?.length) {
    const fallbackEntry = findTocPathEntry(mainEntries, target.fallbackTocPath)
    if (fallbackEntry) return { ...found, tocEntryId: fallbackEntry.id, tocLineIndex: fallbackEntry.lineIndex ?? undefined }
  }
  return withLineAnchor(found, target)
}

/**
 * Last resort when no TOC has the place: the target's line anchor (a page marker in the text,
 * or a heRef ending), one small query over this book only. The book start when that misses.
 */
async function withLineAnchor(
  found: HomeDailyLearningResolvedTarget,
  target: HomeDailyLearningTarget,
): Promise<HomeDailyLearningResolvedTarget> {
  const anchor = target.lineAnchor
  if (!anchor) return found
  if (anchor.pageMarker) {
    const lineIndex = await findLineByPageMarker(found.bookId, anchor.pageMarker.overlay, anchor.pageMarker.values)
    if (lineIndex >= 0) return { ...found, tocLineIndex: lineIndex }
  }
  if (anchor.heRefSuffix) {
    const lineIndex = await findLineByHeRefSuffix(found.bookId, anchor.heRefSuffix)
    if (lineIndex >= 0) return { ...found, tocLineIndex: lineIndex }
  }
  return found
}

