/**
 * A bookmark's exact point in the book view lines: a line plus a character offset into
 * its text, not just the line - a line can run for pages.
 *
 * Reading: the reader's text selection when it sits in the lines (the bookmark then
 * covers exactly that text), otherwise the character at the top of the view.
 * Restoring: the line is landed by the ONE line scroller, then nudged so the character
 * sits at the top edge, and a bookmarked selection is selected again.
 *
 * Offsets count the row's text nodes in document order, the same walk both ways, so
 * they survive zoom and pane width - pixels would not.
 */
import { nextTick } from 'vue'
import type { WindowedScrollerApi } from '@/components/windowedScrollerTypes'
import { SCROLL_LANDING_GAP_PX, SEARCH_BAR_INSET_PX } from './useBookViewLinesNavigation'

export interface BookViewLinesBookmarkPoint {
  lineIndex: number
  characterOffset: number
  /** Set when the point came from a selection: its raw length in the row's text. */
  selectionLength?: number
  /** The text bookmarked - the selection, or the words from the point on - short enough to show. */
  snippet?: string
}

/** How much of the bookmarked text is kept to show under the bookmark's name. */
const SNIPPET_LENGTH = 50

function snippetOf(text: string): string | undefined {
  const collapsed = text.replace(/\s+/g, ' ').trim()
  if (!collapsed) return undefined
  return collapsed.length > SNIPPET_LENGTH ? `${collapsed.slice(0, SNIPPET_LENGTH).trimEnd()}…` : collapsed
}

/** The row's text from a character offset on - enough for a snippet. */
function rowTextFrom(row: HTMLElement, characterOffset: number): string {
  return (row.textContent ?? '').slice(characterOffset, characterOffset + SNIPPET_LENGTH * 2)
}

function rowElement(scrollerElement: HTMLElement, lineIndex: number): HTMLElement | null {
  return scrollerElement.querySelector<HTMLElement>(`[data-index="${lineIndex}"]`)
}

function rowOf(node: Node, scrollerElement: HTMLElement): HTMLElement | null {
  const element = node instanceof Element ? node : node.parentElement
  const row = element?.closest<HTMLElement>('[data-index]') ?? null
  return row && scrollerElement.contains(row) ? row : null
}

function textWalker(row: HTMLElement): TreeWalker {
  return document.createTreeWalker(row, NodeFilter.SHOW_TEXT)
}

/** Characters from the row's start up to (node, offset). */
function characterOffsetIn(row: HTMLElement, node: Node, offset: number): number {
  const walker = textWalker(row)
  let total = 0
  for (let text = walker.nextNode(); text; text = walker.nextNode()) {
    if (text === node) return total + offset
    total += text.textContent?.length ?? 0
  }
  // An element boundary rather than a text node: count the text before it.
  const range = document.createRange()
  range.setStart(row, 0)
  range.setEnd(node, offset)
  return range.toString().length
}

/** The (text node, offset) at a character offset; clamped to the row's end. */
function textPositionAt(row: HTMLElement, characterOffset: number): { node: Text; offset: number } | null {
  const walker = textWalker(row)
  let remaining = characterOffset
  let last: Text | null = null
  for (let text = walker.nextNode() as Text | null; text; text = walker.nextNode() as Text | null) {
    const length = text.textContent?.length ?? 0
    if (remaining <= length) return { node: text, offset: remaining }
    remaining -= length
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
  suppressPositionSave: () => void,
) {
  function readSelectionPoint(scrollerElement: HTMLElement): BookViewLinesBookmarkPoint | null {
    const selection = window.getSelection()
    if (!selection || selection.rangeCount === 0 || selection.isCollapsed) return null
    const range = selection.getRangeAt(0)
    const row = rowOf(range.startContainer, scrollerElement)
    if (!row) return null
    const snippet = snippetOf(range.toString())
    if (!snippet) return null
    const characterOffset = characterOffsetIn(row, range.startContainer, range.startOffset)
    // A selection running past its line is cut at the line's end - the bookmark is one line's.
    const endsInRow = rowOf(range.endContainer, scrollerElement) === row
    const end = endsInRow ? characterOffsetIn(row, range.endContainer, range.endOffset) : (row.textContent?.length ?? characterOffset)
    return {
      lineIndex: Number(row.dataset.index),
      characterOffset,
      snippet,
      selectionLength: Math.max(0, end - characterOffset),
    }
  }

  function readTopOfViewPoint(scrollerElement: HTMLElement): BookViewLinesBookmarkPoint | null {
    const api = scroller()
    const reserved = searchBarVisible() ? SEARCH_BAR_INSET_PX : 0
    const first = api?.readFirstVisible(reserved + SCROLL_LANDING_GAP_PX + 4) ?? api?.readFirstVisible(0)
    if (!first) return null
    const row = rowElement(scrollerElement, first.index)
    if (!row) return { lineIndex: first.index, characterOffset: 0 }
    const pointAt = (characterOffset: number): BookViewLinesBookmarkPoint =>
      ({ lineIndex: first.index, characterOffset, snippet: snippetOf(rowTextFrom(row, characterOffset)) })
    const scrollerRect = scrollerElement.getBoundingClientRect()
    const rowRect = row.getBoundingClientRect()
    // The first text line showing below the top edge (or the search bar), probed mid-row.
    const y = Math.max(rowRect.top, scrollerRect.top + reserved + SCROLL_LANDING_GAP_PX) + 4
    const caret = caretAt(rowRect.left + rowRect.width / 2, y)
    if (!caret || rowOf(caret.node, scrollerElement) !== row) return pointAt(0)
    // Back to the start of the word the caret landed in, so the snippet opens on a whole word.
    const offset = characterOffsetIn(row, caret.node, caret.offset)
    const text = row.textContent ?? ''
    let wordStart = offset
    while (wordStart > 0 && !/\s/.test(text[wordStart - 1]!)) wordStart--
    return pointAt(wordStart)
  }

  /** Where a bookmark made now should point. */
  function readBookmarkPoint(): BookViewLinesBookmarkPoint | null {
    const scrollerElement = scroller()?.scrollerElement
    if (!scrollerElement) return null
    return readSelectionPoint(scrollerElement) ?? readTopOfViewPoint(scrollerElement)
  }

  /** Lands the line, then the character at the top edge; reselects a bookmarked selection. */
  async function scrollToBookmarkPoint(lineIndex: number, characterOffset: number, selectionLength = 0) {
    await scrollToLine(lineIndex, { force: true })
    await nextTick()
    const scrollerElement = scroller()?.scrollerElement
    const row = scrollerElement && rowElement(scrollerElement, lineIndex)
    if (!scrollerElement || !row) return
    const start = textPositionAt(row, characterOffset)
    if (!start) return
    const range = document.createRange()
    range.setStart(start.node, start.offset)
    const end = selectionLength > 0 ? textPositionAt(row, characterOffset + selectionLength) : null
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
