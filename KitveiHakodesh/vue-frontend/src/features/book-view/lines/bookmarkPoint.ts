/**
 * The pieces of a bookmark point shared by the book view lines and the commentary pane:
 * letter counting, the snippet, the point a text selection marks, and the stored record.
 * See useBookViewLinesBookmarkPoint for why positions count letters, not characters.
 */
import type { BookBookmark } from '@/stores/bookBookmarks'

export interface BookViewLinesBookmarkPoint {
  lineIndex: number
  /** Letters and digits in the line's text before the point. */
  letterOffset: number
  /** Set when the point came from a selection: how many letters and digits it covers. */
  selectionLetterCount?: number
  /** The text bookmarked - the selection, or the words from the point on - short enough to show. */
  snippet?: string
}

/** How much of the bookmarked text is kept to show under the bookmark's name. */
export const SNIPPET_LENGTH = 50

export const LETTER = /[\p{L}\p{N}]/u
const LETTERS = /[\p{L}\p{N}]/gu

export function countLetters(text: string): number {
  return text.match(LETTERS)?.length ?? 0
}

export function snippetOf(text: string): string | undefined {
  const collapsed = text.replace(/\s+/g, ' ').trim()
  if (!collapsed) return undefined
  return collapsed.length > SNIPPET_LENGTH ? `${collapsed.slice(0, SNIPPET_LENGTH).trimEnd()}…` : collapsed
}

/** The scroller row ([data-index]) holding a node, or null when it is outside the scroller. */
export function rowOf(node: Node, scrollerElement: HTMLElement): HTMLElement | null {
  const element = node instanceof Element ? node : node.parentElement
  const row = element?.closest<HTMLElement>('[data-index]') ?? null
  return row && scrollerElement.contains(row) ? row : null
}

/** The row's text from its start up to (node, offset). */
export function textBefore(row: HTMLElement, node: Node, offset: number): string {
  const range = row.ownerDocument.createRange()
  range.setStart(row, 0)
  range.setEnd(node, offset)
  return range.toString()
}

/**
 * The point a selection marks in its first row, with that row: a selection running past
 * its row is cut at the row's end - a bookmark is one line's. `lineIndex` is the ROW index;
 * a caller whose rows are not book lines maps it. Null for an empty selection.
 */
export function selectionPointInRow(
  range: Range, scrollerElement: HTMLElement,
): { row: HTMLElement; point: BookViewLinesBookmarkPoint } | null {
  const row = rowOf(range.startContainer, scrollerElement)
  if (!row) return null
  const before = textBefore(row, range.startContainer, range.startOffset)
  const endsInRow = rowOf(range.endContainer, scrollerElement) === row
  const selectedText = endsInRow ? range.toString() : (row.textContent ?? '').slice(before.length)
  const snippet = snippetOf(selectedText)
  if (!snippet) return null
  return {
    row,
    point: {
      lineIndex: Number(row.dataset.index),
      letterOffset: countLetters(before),
      selectionLetterCount: countLetters(selectedText),
      snippet,
    },
  }
}

/**
 * The stored bookmark for a point on a line, or null for a placeholder line (a negative or
 * zero id is a line whose real id is not known). The name defaults to the TOC path.
 */
export function bookmarkFromPoint(
  line: { id: number; lineIndex: number },
  point: Omit<BookViewLinesBookmarkPoint, 'lineIndex'> | null,
  tocPath: string | undefined,
  now: number = Date.now(),
): BookBookmark | null {
  if (line.id <= 0 || line.lineIndex < 0) return null
  const path = tocPath?.trim() || undefined
  return {
    id: now,
    lineId: line.id,
    lineIndex: line.lineIndex,
    letterOffset: point?.letterOffset,
    selectionLetterCount: point?.selectionLetterCount,
    label: path ?? `${line.lineIndex + 1}`,
    tocPath: path,
    snippet: point?.snippet,
  }
}
