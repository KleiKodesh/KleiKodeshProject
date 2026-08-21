/**
 * Manages all user annotation state for the book view: highlights, notes,
 * the note bubble overlay, and the selection-to-line-offset conversion logic.
 *
 * Encapsulates:
 * - useBookViewHighlights (load, apply, clear)
 * - useBookViewNotes (lazy viewport-driven loading, create/update/delete)
 * - Active note bubble anchor state (open/close)
 * - extractSelectionOnLine — converts a browser selection to per-line offset ranges
 * - onHighlight / onClearHighlight / onAddNote / onMarkerClick handlers
 */
import { ref, nextTick } from 'vue'
import type { Ref } from 'vue'
import { useBookViewHighlights } from './useBookViewHighlights'
import { useBookViewNotes } from './useBookViewNotes'
import type { Note } from './useBookViewNotes'
import type { LineItem } from './useBookViewLinesTable'

// ── Internal selection types ──────────────────────────────────────────────────

interface SelectionOnLine {
  lineId: number
  lineIndex: number
  startOffset: number
  endOffset: number
}

/**
 * Multi-line selection support for highlights.
 * A selection spanning multiple lines is treated as a series of separate
 * highlights — one per line, each with its own start/end offsets.
 */
interface MultiLineSelection {
  lines: SelectionOnLine[]
}

// ── Composable ────────────────────────────────────────────────────────────────

export function useBookViewAnnotations(
  bookId: number,
  scrollerEl: Ref<HTMLElement | null>,
  lines: () => LineItem[],
  virtualItemsViewportIds: () => number[],
) {
  // ── Highlights ──────────────────────────────────────────────────────────────

  const { getHighlightsForLine, applyHighlight, clearHighlight } = useBookViewHighlights(bookId)

  // ── Notes ───────────────────────────────────────────────────────────────────

  const { notesByLine, getNotesForLine, loadNotesForLines, createNote, updateNote, deleteNote } =
    useBookViewNotes(bookId, virtualItemsViewportIds)

  // ── Note bubble state ───────────────────────────────────────────────────────

  const activeBubbleNote = ref<Note | null>(null)
  const activeBubbleAnchorRect = ref<DOMRect | null>(null)

  function openNoteBubble(note: Note, markerElement: HTMLElement) {
    activeBubbleNote.value = note
    activeBubbleAnchorRect.value = markerElement.getBoundingClientRect()
  }

  function closeNoteBubble() {
    activeBubbleNote.value = null
    activeBubbleAnchorRect.value = null
  }

  // ── Selection → line offsets ────────────────────────────────────────────────

  function extractSelectionOnLine(): MultiLineSelection | null {
    const selection = window.getSelection()
    if (!selection || selection.rangeCount === 0 || selection.isCollapsed) return null
    const range = selection.getRangeAt(0)
    if (!scrollerEl.value) return null
    const lineElements = Array.from(scrollerEl.value.querySelectorAll('.line'))
    const intersected = lineElements.filter((element) => range.intersectsNode(element))
    if (intersected.length === 0) return null

    const selectionLines: SelectionOnLine[] = []

    for (let i = 0; i < intersected.length; i++) {
      const lineElement = intersected[i] as HTMLElement
      const virtualItemElement = lineElement.closest('[data-index]') as HTMLElement | null
      if (!virtualItemElement) continue
      const virtualIndex = parseInt(virtualItemElement.dataset['index'] ?? '', 10)
      const lineItem = lines()[virtualIndex]
      if (!lineItem || lineItem.content == null) continue

      const strippedText = (lineElement.textContent ?? '').replace(/[\u0591-\u05C7]/g, '')

      function countStrippedOffset(node: Node, offsetInNode: number): number {
        const walker = document.createTreeWalker(lineElement, NodeFilter.SHOW_TEXT)
        let strippedCount = 0
        let currentNode: Text | null
        while ((currentNode = walker.nextNode() as Text | null)) {
          if (currentNode === node) {
            const slice = currentNode.textContent?.slice(0, offsetInNode) ?? ''
            strippedCount += slice.replace(/[\u0591-\u05C7]/g, '').length
            return strippedCount
          }
          strippedCount += (currentNode.textContent ?? '').replace(/[\u0591-\u05C7]/g, '').length
        }
        return strippedCount
      }

      const isFirstLine = i === 0
      const isLastLine = i === intersected.length - 1

      let startOffset = 0
      let endOffset = strippedText.length

      if (isFirstLine) startOffset = countStrippedOffset(range.startContainer, range.startOffset)
      if (isLastLine) endOffset = countStrippedOffset(range.endContainer, range.endOffset)

      if (startOffset < endOffset) {
        selectionLines.push({
          lineId: lineItem.id,
          lineIndex: lineItem.lineIndex,
          startOffset,
          endOffset: Math.min(endOffset, strippedText.length),
        })
      }
    }

    if (selectionLines.length === 0) return null
    return { lines: selectionLines }
  }

  // ── Annotation action handlers ──────────────────────────────────────────────

  function onHighlight(colorArgb: number) {
    const selection = extractSelectionOnLine()
    if (!selection) return
    for (const line of selection.lines) {
      applyHighlight(line.lineId, line.startOffset, line.endOffset, colorArgb)
    }
    window.getSelection()?.removeAllRanges()
  }

  function onClearHighlight() {
    const selection = extractSelectionOnLine()
    if (!selection) return
    for (const line of selection.lines) {
      clearHighlight(line.lineId, line.startOffset, line.endOffset)
    }
    window.getSelection()?.removeAllRanges()
  }

  function onAddNote() {
    const selection = extractSelectionOnLine()
    if (!selection || selection.lines.length === 0) return
    const firstLine = selection.lines[0]!
    const rawQuote = window.getSelection()?.toString() ?? ''
    const quote = rawQuote.replace(/[\u0591-\u05C7]/g, '').trim()
    window.getSelection()?.removeAllRanges()
    void createNote(firstLine.lineId, firstLine.startOffset, firstLine.endOffset, quote).then(
      (note) => {
        nextTick(() => {
          const markerElement = scrollerEl.value?.querySelector(
            `[data-note-id="${note.id}"]`,
          ) as HTMLElement | null
          if (markerElement) openNoteBubble(note, markerElement)
        })
      },
    )
  }

  function onMarkerClick(event: MouseEvent) {
    const markerElement = (event.target as HTMLElement).closest(
      '[data-note-id]',
    ) as HTMLElement | null
    if (!markerElement) return
    const noteId = parseInt(markerElement.dataset['noteId'] ?? '', 10)
    if (isNaN(noteId)) return
    event.stopPropagation()
    for (const notes of notesByLine.value.values()) {
      const found = notes.find((note) => note.id === noteId)
      if (found) {
        openNoteBubble(found, markerElement)
        return
      }
    }
  }

  // ── Public API ──────────────────────────────────────────────────────────────

  return {
    // Highlights
    getHighlightsForLine,
    applyHighlight,
    clearHighlight,
    // Note bubble
    activeBubbleNote,
    activeBubbleAnchorRect,
    openNoteBubble,
    closeNoteBubble,
    // Notes
    notesByLine,
    getNotesForLine,
    loadNotesForLines,
    createNote,
    updateNote,
    deleteNote,
    // Handlers
    onHighlight,
    onClearHighlight,
    onAddNote,
    onMarkerClick,
  }
}
