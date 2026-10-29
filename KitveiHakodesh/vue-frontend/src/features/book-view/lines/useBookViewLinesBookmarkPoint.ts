/**
 * A bookmark's exact point in the book view lines: a line plus a position in its text,
 * not just the line - a line can run for pages.
 *
 * Reading: the reader's text selection when it sits in the lines (the bookmark then
 * covers exactly that text), otherwise the word at the top of the view.
 * Restoring: the line is landed by the ONE line scroller, then nudged so the point sits
 * at the top edge, and a bookmarked selection is selected again.
 *
 * Positions count LETTERS AND DIGITS only, never raw characters. The rendered text is
 * the content after the clean-text options (nikud, teamim, punctuation, maqaf) and the
 * divine-name censor ran, and every one of those adds or drops marks and punctuation -
 * a raw character offset taken with nikud shown lands short once nikud is hidden. The
 * letters survive all of them, so a count of letters points at the same word either way.
 * Counting text nodes, not pixels, also makes it survive zoom and pane width.
 */
import { nextTick } from 'vue'
import type { WindowedScrollerApi } from '@/components/windowedScrollerTypes'
import { SCROLL_LANDING_GAP_PX, SEARCH_BAR_INSET_PX } from './useBookViewLinesNavigation'

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
const SNIPPET_LENGTH = 50

const LETTER = /[\p{L}\p{N}]/u
const LETTERS = /[\p{L}\p{N}]/gu

function countLetters(text: string): number {
  return text.match(LETTERS)?.length ?? 0
}

function snippetOf(text: string): string | undefined {
  const collapsed = text.replace(/\s+/g, ' ').trim()
  if (!collapsed) return undefined
  return collapsed.length > SNIPPET_LENGTH ? `${collapsed.slice(0, SNIPPET_LENGTH).trimEnd()}…` : collapsed
}

function rowElement(scrollerElement: HTMLElement, lineIndex: number): HTMLElement | null {
  return scrollerElement.querySelector<HTMLElement>(`[data-index="${lineIndex}"]`)
}

function rowOf(node: Node, scrollerElement: HTMLElement): HTMLElement | null {
  const element = node instanceof Element ? node : node.parentElement
  const row = element?.closest<HTMLElement>('[data-index]') ?? null
  return row && scrollerElement.contains(row) ? row : null
}

/** The row's text from its start up to (node, offset). */
function textBefore(row: HTMLElement, node: Node, offset: number): string {
  const range = document.createRange()
  range.setStart(row, 0)
  range.setEnd(node, offset)
  return range.toString()
}

/**
 * The DOM position of a letter count. 'start' = just before the next letter (where a
 * point or a selection begins), 'end' = just after the last counted one (where a
 * selection ends). Clamped to the row's end.
 */
function positionAtLetterCount(
  row: HTMLElement, letterCount: number, edge: 'start' | 'end',
): { node: Text; offset: number } | null {
  const walker = document.createTreeWalker(row, NodeFilter.SHOW_TEXT)
  let remaining = letterCount
  let last: Text | null = null
  for (let text = walker.nextNode() as Text | null; text; text = walker.nextNode() as Text | null) {
    const content = text.textContent ?? ''
    for (let index = 0; index < content.length; index++) {
      if (!LETTER.test(content[index]!)) continue
      if (edge === 'start' && remaining === 0) return { node: text, offset: index }
      remaining--
      if (edge === 'end' && remaining === 0) return { node: text, offset: index + 1 }
    }
    last = text
  }
  return last ? { node: last, offset: last.textContent?.length ?? 0 } : null
}

function caretAt(x: number, y: number): { node: Node; offset: number } | null {
  const withPosition = document as Document & {
    caretPositionFromPoint?: (x: number, y: number) => { offsetNode: Node; offset: number } | null
  }
  const position = withPosition.caretPositionFromPoint?.(x, y)
  if (position) return { node: position.offsetNode, offset: position.offset }
  const range = document.caretRangeFromPoint?.(x, y)
  return range ? { node: range.startContainer, offset: range.startOffset } : null
}

export function useBookViewLinesBookmarkPoint(
  scroller: () => WindowedScrollerApi | null,
  searchBarVisible: () => boolean,
  scrollToLine: (lineIndex: number, options: { force?: boolean }) => Promise<void>,
  lineIndexForId: (lineId: number, fallbackLineIndex?: number) => Promise<number | undefined>,
  currentScrollGeneration: () => number,
  suppressPositionSave: () => void,
) {
  function readSelectionPoint(scrollerElement: HTMLElement): BookViewLinesBookmarkPoint | null {
    const selection = window.getSelection()
    if (!selection || selection.rangeCount === 0 || selection.isCollapsed) return null
    const range = selection.getRangeAt(0)
    const row = rowOf(range.startContainer, scrollerElement)
    if (!row) return null
    const before = textBefore(row, range.startContainer, range.startOffset)
    // A selection running past its line is cut at the line's end - the bookmark is one line's.
    const endsInRow = rowOf(range.endContainer, scrollerElement) === row
    const selectedText = endsInRow ? range.toString() : (row.textContent ?? '').slice(before.length)
    const snippet = snippetOf(selectedText)
    if (!snippet) return null
    return {
      lineIndex: Number(row.dataset.index),
      letterOffset: countLetters(before),
      selectionLetterCount: countLetters(selectedText),
      snippet,
    }
  }

  function readTopOfViewPoint(scrollerElement: HTMLElement): BookViewLinesBookmarkPoint | null {
    const api = scroller()
    const reserved = searchBarVisible() ? SEARCH_BAR_INSET_PX : 0
    const first = api?.readFirstVisible(reserved + SCROLL_LANDING_GAP_PX + 4) ?? api?.readFirstVisible(0)
    if (!first) return null
    const row = rowElement(scrollerElement, first.index)
    if (!row) return { lineIndex: first.index, letterOffset: 0 }
    const rowText = row.textContent ?? ''
    const pointAt = (characterOffset: number): BookViewLinesBookmarkPoint => ({
      lineIndex: first.index,
      letterOffset: countLetters(rowText.slice(0, characterOffset)),
      snippet: snippetOf(rowText.slice(characterOffset, characterOffset + SNIPPET_LENGTH * 2)),
    })
    const scrollerRect = scrollerElement.getBoundingClientRect()
    const rowRect = row.getBoundingClientRect()
    // The first text line showing below the top edge (or the search bar), probed mid-row.
    const y = Math.max(rowRect.top, scrollerRect.top + reserved + SCROLL_LANDING_GAP_PX) + 4
    const caret = caretAt(rowRect.left + rowRect.width / 2, y)
    if (!caret || rowOf(caret.node, scrollerElement) !== row) return pointAt(0)
    // Back to the start of the word the caret landed in, so the point is a whole word.
    let wordStart = textBefore(row, caret.node, caret.offset).length
    while (wordStart > 0 && !/\s/.test(rowText[wordStart - 1]!)) wordStart--
    return pointAt(wordStart)
  }

  /** Where a bookmark made now should point. */
  function readBookmarkPoint(): BookViewLinesBookmarkPoint | null {
    const scrollerElement = scroller()?.scrollerElement
    if (!scrollerElement) return null
    return readSelectionPoint(scrollerElement) ?? readTopOfViewPoint(scrollerElement)
  }

  /**
   * Lands the line, then the point at the top edge; reselects a bookmarked selection. The
   * line is found by id first: a version switch or a replaced library can move it to
   * another index, and the stored index is only the fallback.
   */
  async function scrollToBookmarkPoint(
    lineId: number, fallbackLineIndex: number, letterOffset: number, selectionLetterCount = 0,
  ) {
    const lineIndex = await lineIndexForId(lineId, fallbackLineIndex)
    if (lineIndex == null) return
    const landing = scrollToLine(lineIndex, { force: true })
    // Read right after the scroll began: any later jump (another bookmark, the TOC,
    // section keys) bumps it, and this landing's follow-up must not act on that view.
    const generation = currentScrollGeneration()
    const isStale = () => generation !== currentScrollGeneration()
    await landing
    await nextTick()
    if (isStale()) return
    const scrollerElement = scroller()?.scrollerElement
    const row = scrollerElement && rowElement(scrollerElement, lineIndex)
    if (!scrollerElement || !row) return
    const start = positionAtLetterCount(row, letterOffset, 'start')
    if (!start) return
    const range = document.createRange()
    range.setStart(start.node, start.offset)
    const end = selectionLetterCount > 0
      ? positionAtLetterCount(row, letterOffset + selectionLetterCount, 'end')
      : null
    if (end) range.setEnd(end.node, end.offset)
    else range.collapse(true)

    const pointRect = range.getClientRects()[0] ?? range.getBoundingClientRect()
    const reserved = searchBarVisible() ? SEARCH_BAR_INSET_PX : 0
    const shift = pointRect.top - scrollerElement.getBoundingClientRect().top - reserved - SCROLL_LANDING_GAP_PX
    if (shift > 0) {
      suppressPositionSave()
      scrollerElement.scrollTop += shift
    }
    if (end) {
      const selection = window.getSelection()
      selection?.removeAllRanges()
      selection?.addRange(range)
    }
  }

  return { readBookmarkPoint, scrollToBookmarkPoint }
}
