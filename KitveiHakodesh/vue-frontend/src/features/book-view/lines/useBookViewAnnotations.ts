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
  /** Ids of the lines mounted in the scroller's window — what the lazy note loader watches. */
  renderedLineIds: () => number[],
  /**
   * Re-creates the selection after an annotation re-renders the lines it landed on.
   * Applying a highlight or a note replaces those lines' markup, which destroys the
   * text nodes the selection's boundaries point at — so the selection has to be put
   * back rather than simply left alone (see useVirtualizedTextSelection).
   */
  restoreSelectionFromPinnedRows: () => void,
  /**
   * The row index span of the current selection, from the pinned-row tracker. Lets an
   * annotation reach the selected lines the scroller never mounted.
   */
  selectedRowIndexRange: () => { firstRowIndex: number; lastRowIndex: number } | null,
  /**
   * How many annotation offsets a line's model content spans. Supplied by the caller
   * because it depends on the live diacritics and censor settings — the offset space
   * is the text AFTER those transforms, not the raw content.
   */
  annotationOffsetLengthOf: (content: string) => number,
) {
  // ── Highlights ──────────────────────────────────────────────────────────────

  const { getHighlightsForLine, applyHighlight, clearHighlight } = useBookViewHighlights(bookId)

  // ── Notes ───────────────────────────────────────────────────────────────────

  const { notesByLine, getNotesForLine, getNoteById, loadNotesForLines, createNote, updateNote, deleteNote } =
    useBookViewNotes(bookId, renderedLineIds)

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

    appendUnmountedSelectedLines(selectionLines)

    if (selectionLines.length === 0) return null
    return { lines: selectionLines }
  }

  /**
   * Adds the selected lines that are not in the DOM — the ones that scrolled out of
   * the virtualized window while the selection was being dragged over them.
   *
   * The loop above can only see rendered lines, so without this an annotation applied
   * to a selection spanning more than a screen would silently land on the handful of
   * rendered rows and skip every line between them. Those middle lines are selected in
   * full by definition (only the two ends can be partial, and both are always rendered
   * because they are pinned), so each takes a whole-line offset span measured from the
   * model's own text — the same diacritic-stripped count the rendered walk produces.
   */
  function appendUnmountedSelectedLines(selectionLines: SelectionOnLine[]) {
    const rowIndexRange = selectedRowIndexRange()
    if (!rowIndexRange) return
    const { firstRowIndex, lastRowIndex } = rowIndexRange

    const alreadyCollectedLineIds = new Set(selectionLines.map((line) => line.lineId))
    const allLines = lines()

    for (let rowIndex = firstRowIndex + 1; rowIndex < lastRowIndex; rowIndex++) {
      const line = allLines[rowIndex]
      if (!line || line.content == null) continue
      if (alreadyCollectedLineIds.has(line.id)) continue
      const strippedLength = annotationOffsetLengthOf(line.content)
      if (strippedLength === 0) continue
      selectionLines.push({
        lineId: line.id,
        lineIndex: line.lineIndex,
        startOffset: 0,
        endOffset: strippedLength,
      })
    }

    selectionLines.sort((first, second) => first.lineIndex - second.lineIndex)
  }

  // ── Annotation action handlers ──────────────────────────────────────────────

  function onHighlight(colorArgb: number) {
    const selection = extractSelectionOnLine()
    if (!selection) return
    // Awaited, THEN restored: applyHighlight only mutates the reactive highlight map
    // after its round-trip to the settings database, so the re-render that destroys
    // the selection's text nodes happens long after this function's own microtasks.
    // Restoring on a bare nextTick would run against the pre-render DOM and be lost.
    void Promise.all(
      selection.lines.map((line) =>
        applyHighlight(line.lineId, line.startOffset, line.endOffset, colorArgb),
      ),
    ).then(() => nextTick(restoreSelectionFromPinnedRows))
  }

  function onClearHighlight() {
    const selection = extractSelectionOnLine()
    if (!selection) return
    void Promise.all(
      selection.lines.map((line) =>
        clearHighlight(line.lineId, line.startOffset, line.endOffset),
      ),
    ).then(() => nextTick(restoreSelectionFromPinnedRows))
  }

  function onAddNote() {
    const selection = extractSelectionOnLine()
    if (!selection || selection.lines.length === 0) return
    const firstLine = selection.lines[0]!
    const rawQuote = window.getSelection()?.toString() ?? ''
    const quote = rawQuote.replace(/[\u0591-\u05C7]/g, '').trim()
    void createNote(firstLine.lineId, firstLine.startOffset, firstLine.endOffset, quote).then(
      (note) => {
        nextTick(() => {
          restoreSelectionFromPinnedRows()
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
    const found = getNoteById(noteId)
    if (found) openNoteBubble(found, markerElement)
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
    getNoteById,
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
