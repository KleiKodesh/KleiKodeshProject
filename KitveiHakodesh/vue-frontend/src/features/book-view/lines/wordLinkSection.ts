/**
 * Resolves the TOC section a word-link target sits in, so the hover preview can
 * show the whole section rather than the single anchored line.
 *
 * A range anchor cites a passage, not a line: previewing only `target.lineId`
 * routinely lands mid-sentence, because the DB's line breaks follow the printed
 * edition's typography and not the citation. So the preview loads every line of
 * the enclosing TOC section and scrolls to the cited line inside it — the cited
 * line stays the subject, with its surroundings readable around it.
 *
 * Section bounds come from the book's own TOC, resolved entirely on the frontend:
 *   start = the deepest entry whose lineIndex is at or before the target
 *   end   = the next entry at the same or shallower level (i.e. the next sibling
 *           or the start of an outer section), or the end of the book
 * That yields the same end lineIndex as getSectionEnd in commentaryNavigation.ts
 * and follows the same level convention, but is not the same walk: this one
 * filters to positioned entries first and searches within that list.
 *
 * Deliberately NO new backend query: getAllTocEntries + getLinesPaged already
 * expose everything needed, and getLinesPaged's LIMIT/OFFSET runs over the same
 * lineIndex ordering the TOC entries are expressed in, so a section is one page.
 *
 * A book with no TOC (or a target ahead of every entry) yields null, and the
 * caller falls back to the single-line preview.
 */
import { getAllTocEntries, getLinesPaged } from '@/webview-host/seforimApi'
import type { LineRow, TocEntry } from '@/webview-host/queries.types'

/**
 * Cap on how many lines one preview may load. A TOC section is normally a chapter
 * or a siman — tens of lines — but a book whose TOC only marks its volumes would
 * otherwise pull thousands into a hover panel. Past this the section is abandoned
 * rather than truncated: half a section scrolled to an arbitrary cut is worse than
 * the single line the caller falls back to.
 */
const MAX_SECTION_LINES = 400

export interface WordLinkSection {
  lines: LineRow[]
  /** Text of the TOC entry the section belongs to — headlines the preview. */
  tocPath: string
}

/** The flat entry list is ordered by lineIndex; entries without one are TOC-only headings. */
function positionedEntries(entries: TocEntry[]): (TocEntry & { lineIndex: number })[] {
  return entries.filter((e): e is TocEntry & { lineIndex: number } => e.lineIndex != null)
}

/**
 * The section containing `lineIndex`: the last positioned entry at or before it.
 * Ties (several entries opening on the same line, e.g. chapter + its first siman)
 * resolve to the LAST one — the deepest, hence the most specific section.
 */
function findSectionStart(positioned: (TocEntry & { lineIndex: number })[], lineIndex: number) {
  let found: (TocEntry & { lineIndex: number }) | null = null
  for (const entry of positioned) {
    if (entry.lineIndex > lineIndex) break
    found = entry
  }
  return found
}

/**
 * Where the section ends: the next entry that is not nested inside it. An entry at
 * a deeper level is a subsection and belongs to this section, so it is skipped.
 * Falls through to the book's end when nothing follows.
 */
function findSectionEnd(
  positioned: (TocEntry & { lineIndex: number })[],
  start: TocEntry & { lineIndex: number },
): number | null {
  const idx = positioned.indexOf(start)
  const next = positioned.slice(idx + 1).find((e) => e.level <= start.level)
  return next ? next.lineIndex : null
}

/**
 * The book's TOC, which the section resolution needs before it can ask for lines.
 * Exposed separately so the caller can start it in parallel with its own fetches
 * instead of paying for it in series — it depends on nothing but the book id.
 */
export function loadSectionTocEntries(bookId: number): Promise<TocEntry[]> {
  return getAllTocEntries(bookId)
}

/**
 * Load the full TOC section around a word-link target, given the book's already
 * fetched TOC entries.
 *
 * Returns null whenever the section cannot be established or is too large, which
 * the caller must treat as "preview the single line instead" — never as an error.
 */
export async function loadWordLinkSection(
  bookId: number,
  lineIndex: number,
  entries: TocEntry[],
): Promise<WordLinkSection | null> {
  const positioned = positionedEntries(entries)
  if (!positioned.length) return null

  const start = findSectionStart(positioned, lineIndex)
  if (!start) return null

  const end = findSectionEnd(positioned, start)
  // No following entry means "to the end of the book", whose length is unknown
  // here. Ask for one line past the cap so an oversized tail is still detected
  // by the same length check rather than silently truncated at the cap.
  const limit = end === null ? MAX_SECTION_LINES + 1 : end - start.lineIndex
  if (limit <= 0 || limit > MAX_SECTION_LINES) return null

  const lines = await getLinesPaged(bookId, limit, start.lineIndex)
  if (lines.length > MAX_SECTION_LINES) return null
  if (!lines.length) return null

  // getLinesPaged's OFFSET is POSITIONAL, and lineIndex is being passed as it — which
  // is only the same thing while a book's lineIndex values are dense and 0-based. That
  // holds across the corpus today, but a single gap would shift the whole window and
  // quietly preview the wrong passage. Verify rather than trust: the first row back
  // must be the row the section starts at.
  if (lines[0]!.lineIndex !== start.lineIndex) return null

  return { lines, tocPath: start.text }
}
