/**
 * Keeps a text selection alive across virtualization. Used by every virtualized list
 * whose text the user can select and copy: the book-view lines pane, the commentary
 * panels, and the full-text-search results list.
 *
 * THE BUG THIS EXISTS FOR
 * A selection dragged past the bottom of the viewport used to collapse. The row
 * holding the selection's anchor scrolls out of the virtualized window, Vue unmounts
 * it, and the browser's Range loses the text node its start boundary points at.
 * Chromium then re-homes that boundary onto the row container at a child index the
 * keyed v-for immediately reshuffles under it, so the selection does not merely
 * shrink — it becomes nonsense.
 *
 * THE FIX
 * The two rows holding the selection's boundaries are PINNED into the rendered
 * range — via WindowedScroller's `pinnedIndexes`. They
 * stay mounted however far off-screen they scroll, so the native Range keeps both
 * boundaries in live text nodes and never breaks.
 *
 * Everything else then follows for free, which is the point of fixing it here rather
 * than by tracking the selection in a parallel model:
 *   - Rows scrolled back INTO view between the two pinned boundaries are inside the
 *     Range by DOM containment, so Chromium paints them selected with no help.
 *   - Chromium's own selection autoscroll keeps working, because the drag never
 *     leaves a valid Range.
 *   - window.getSelection() stays the single source of truth, so the copy paths,
 *     hasActiveTextSelection(), the abbreviation tooltip and useSelectAllInContainer
 *     all keep working untouched.
 *
 * WHAT IS STILL NOT AUTOMATIC
 * Copying: the middle rows genuinely are not in the DOM, so every caller fills them
 * in from its own model rather than serializing the live Range — see
 * selectedRowIndexRange and its callers in the two book-view copy files and in
 * fullTextSearchResultsCopy. Each decides what a row contributes: the reading panes
 * splice partial text for the two end rows, while a search result is emitted whole
 * with its citation. A selection confined to ONE row is left to the native copy
 * everywhere, since the DOM already holds exactly what the user highlighted.
 *
 * Re-rendering a pinned row's markup — which applying a highlight or a note does —
 * replaces its text nodes and collapses the selection, so the boundaries are captured
 * as stripped-text offsets and restored afterwards. Only the annotating panes need
 * that; a list that never re-renders a row under the selection can ignore it.
 */
import { computed, ref, watch, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import {
  domPositionFromStrippedOffset,
  strippedLineText,
  strippedOffsetFromDomPosition,
} from '@/utils/lineTextOffsets'

/**
 * A selection boundary in the coordinate space that survives a re-render: which row,
 * and how many stripped characters into it. Rows are addressed by virtual index
 * because that is what WindowedScroller's `pinnedIndexes` speaks.
 */
interface SelectionBoundary {
  rowIndex: number
  strippedOffset: number
}

export function useVirtualizedTextSelection(
  scrollerElement: Ref<HTMLElement | null>,
  /**
   * A value that changes whenever the row list is rebuilt — a different book, a
   * version swap, a commentary filter change. Pins address rows by INDEX, so after a
   * rebuild index N holds a different line and every pin is stale.
   *
   * Must not be the row count alone: a filter change that swaps one group for another
   * of equal length, or a version overlay that preserves the line count, would leave
   * the pins pointing at rows whose text has changed underneath them. Include the
   * identity of what is being shown, not just how much of it there is.
   */
  rowListIdentity: () => unknown,
) {
  // The rows the current selection starts and ends in. Null when there is no
  // selection inside this scroller.
  const anchorBoundary = ref<SelectionBoundary | null>(null)
  const focusBoundary = ref<SelectionBoundary | null>(null)

  function rowElementFromNode(node: Node | null): HTMLElement | null {
    const element = node instanceof Element ? node : node?.parentElement ?? null
    const row = element?.closest('[data-index]') as HTMLElement | null
    return row && scrollerElement.value?.contains(row) ? row : null
  }

  /**
   * The `.line` element a boundary sits in, when the list has one.
   *
   * Only the reading panes mark their text up that way. A list whose rows carry no
   * `.line` (the search results list) simply gets no character offset — see
   * boundaryFromDomPosition — which costs it nothing, because the offset is used
   * only by restoreSelectionFromPinnedRows and only annotations need that.
   */
  function lineElementFromNode(node: Node | null): HTMLElement | null {
    const element = node instanceof Element ? node : node?.parentElement ?? null
    const line = element?.closest('.line') as HTMLElement | null
    return line && scrollerElement.value?.contains(line) ? line : null
  }

  function boundaryFromDomPosition(node: Node | null, offsetInNode: number): SelectionBoundary | null {
    const row = rowElementFromNode(node)
    if (!row) return null
    const rowIndex = parseInt(row.dataset['index'] ?? '', 10)
    if (isNaN(rowIndex)) return null

    // Offset 0 when there is no `.line` to measure into — either the list does not
    // use them at all, or the boundary landed on the row wrapper rather than inside
    // its text (which happens when a drag crosses the gap between two rows). Pinning
    // the ROW is what keeps the selection alive; the offset only refines a restore.
    const lineElement = lineElementFromNode(node)
    const strippedOffset = lineElement
      ? strippedOffsetFromDomPosition(lineElement, node!, offsetInNode)
      : 0
    return { rowIndex, strippedOffset }
  }

  function clearPinnedRows() {
    anchorBoundary.value = null
    focusBoundary.value = null
  }

  /**
   * Re-reads the pins from the live selection.
   *
   * Only the boundary that is CURRENTLY IN THE DOM can be re-read: once a row is
   * pinned its boundary stays valid, and a boundary whose row somehow left the DOM
   * anyway must keep its last known value rather than be overwritten with the
   * garbage position Chromium re-homes it to. So a boundary that no longer resolves
   * to a row is left alone; only a selection that has genuinely gone away (collapsed,
   * or moved to another element entirely) clears the pins.
   */
  function readPinnedRowsFromSelection() {
    const selection = window.getSelection()
    if (!selection || selection.isCollapsed || selection.rangeCount === 0) {
      clearPinnedRows()
      return
    }

    const nextAnchor = boundaryFromDomPosition(selection.anchorNode, selection.anchorOffset)
    const nextFocus = boundaryFromDomPosition(selection.focusNode, selection.focusOffset)

    // Neither end is in this scroller: the selection belongs to some other element
    // (the other pane, an input, a tooltip). Release our rows.
    if (!nextAnchor && !nextFocus && !anchorBoundary.value && !focusBoundary.value) return
    if (!nextAnchor && !nextFocus) {
      clearPinnedRows()
      return
    }

    if (nextAnchor) anchorBoundary.value = nextAnchor
    if (nextFocus) focusBoundary.value = nextFocus
  }

  // A pointerdown inside a line pins that row BEFORE the drag can scroll it away.
  // selectionchange alone would be too late: it fires asynchronously, so a fast
  // wheel-scroll immediately after the press could unmount the row first.
  useEventListener(scrollerElement, 'pointerdown', (event: PointerEvent) => {
    // PRIMARY BUTTON ONLY. A right-click inside an existing selection keeps that
    // selection (Chromium does not collapse it, and fires no selectionchange), so
    // resetting the pins here would strand a long selection with both pins on the
    // clicked row — and the context menu's copy would then silently fall back to
    // the rendered window. Right-click is the usual way TO the copy menu, so this
    // would defeat the stitching exactly when it is needed.
    if (event.button !== 0) return

    const boundary = boundaryFromDomPosition(event.target as Node, 0)
    if (!boundary) return
    // Shift extends the existing selection, so its anchor must survive the press.
    if (!event.shiftKey) {
      anchorBoundary.value = boundary
      focusBoundary.value = boundary
    } else {
      focusBoundary.value = boundary
    }
  })

  useEventListener(document, 'selectionchange', readPinnedRowsFromSelection)

  // The list was rebuilt under the pins — see the rowListIdentity parameter.
  watch(rowListIdentity, clearPinnedRows)


  /**
   * The same two boundary rows for a WindowedScroller, which takes the indexes to
   * keep mounted as a prop rather than a range extractor.
   */
  const pinnedRowIndexes = computed(() => {
    const indexes: number[] = []
    if (anchorBoundary.value) indexes.push(anchorBoundary.value.rowIndex)
    if (focusBoundary.value) indexes.push(focusBoundary.value.rowIndex)
    return indexes
  })

  /**
   * The rows the selection spans, in ascending row-index order, or null when there is
   * no selection in this scroller. The copy paths use this to fill in the rows that
   * are between the boundaries but not in the DOM.
   *
   * Ascending, not anchor-to-focus: a selection dragged upwards has its anchor below
   * its focus, and copied text must still read in document order.
   */
  function selectedRowIndexRange(): { firstRowIndex: number; lastRowIndex: number } | null {
    const anchorIndex = anchorBoundary.value?.rowIndex
    const focusIndex = focusBoundary.value?.rowIndex
    if (anchorIndex == null || focusIndex == null) return null
    return {
      firstRowIndex: Math.min(anchorIndex, focusIndex),
      lastRowIndex: Math.max(anchorIndex, focusIndex),
    }
  }

  /**
   * Re-creates the selection from the pinned boundaries after the rows' markup was
   * replaced — which is what applying or clearing a highlight, or adding a note,
   * does to the line it lands on.
   *
   * Must run after the re-render has been flushed to the DOM (await nextTick()).
   * Silently does nothing when a boundary row is not currently rendered, since there
   * is then nothing to select against; the pins survive for the next attempt.
   */
  function restoreSelectionFromPinnedRows() {
    const anchor = anchorBoundary.value
    const focus = focusBoundary.value
    const scroller = scrollerElement.value
    if (!anchor || !focus || !scroller) return

    // Ordered before the range is built, never after. A Range is always start-before-
    // end, and setEnd with a boundary that precedes the start does not throw — it
    // silently COLLAPSES the range. So an upward drag has to be rebuilt as its
    // equivalent forward range up front; detecting the collapse afterwards cannot
    // distinguish it from a genuinely empty selection. The two select identical text;
    // only the caret's reported direction is lost, which nothing here reads.
    const isBackwards =
      focus.rowIndex < anchor.rowIndex ||
      (focus.rowIndex === anchor.rowIndex && focus.strippedOffset < anchor.strippedOffset)
    const startBoundary = isBackwards ? focus : anchor
    const endBoundary = isBackwards ? anchor : focus

    const startPosition = domPositionForBoundary(scroller, startBoundary)
    const endPosition = domPositionForBoundary(scroller, endBoundary)
    if (!startPosition || !endPosition) return

    const selection = window.getSelection()
    if (!selection) return
    const range = document.createRange()
    range.setStart(startPosition.node, startPosition.offset)
    range.setEnd(endPosition.node, endPosition.offset)
    selection.removeAllRanges()
    selection.addRange(range)
  }

  function domPositionForBoundary(
    scroller: HTMLElement,
    boundary: SelectionBoundary,
  ): { node: Text; offset: number } | null {
    const row = scroller.querySelector(`[data-index="${boundary.rowIndex}"]`) as HTMLElement | null
    const lineElement = row?.querySelector('.line') as HTMLElement | null
    if (!lineElement) return null
    const clampedOffset = Math.min(boundary.strippedOffset, strippedLineText(lineElement).length)
    return domPositionFromStrippedOffset(lineElement, clampedOffset)
  }

  return {
    pinnedRowIndexes,
    selectedRowIndexRange,
    restoreSelectionFromPinnedRows,
    clearPinnedRows,
  }
}
