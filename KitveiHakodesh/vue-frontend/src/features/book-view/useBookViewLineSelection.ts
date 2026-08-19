/**
 * Line selection state for the book view.
 *
 * Owns single-line selection and shift-click multi-select (range selection).
 * The anchor is set on each plain click; subsequent shift-clicks extend the
 * range from the anchor to the clicked line. A new plain click resets the anchor
 * and clears any existing range.
 *
 * Also derives selectedSectionLineIds — the ordered set of line IDs belonging to
 * the active TOC section. Manual multi-select takes priority over the TOC range.
 */
import { ref, computed } from 'vue'
import type { TocEntry } from '@/webview-host/queries.types'
import type { CommentaryPinSnapshot } from './bookViewTypes'
type Line = { id: number; lineIndex: number; content: string | null }

export function useBookViewLineSelection(
  lines: () => Line[],
  tocEntries: () => TocEntry[],
  commentaryLineId: import('vue').Ref<number | null>,
  selectedLineId: import('vue').Ref<number | null>,
  captureActivePins: () => CommentaryPinSnapshot,
  applyPendingPins: (snapshot: CommentaryPinSnapshot) => void,
) {
  const manualSelectionAnchorLineId = ref<number | null>(null)
  const manualSelectionLineIds = ref<number[] | null>(null)

  function clearManualSelection() {
    manualSelectionAnchorLineId.value = null
    manualSelectionLineIds.value = null
  }

  const selectedSectionLineIds = computed<number[] | null>(() => {
    // Manual multi-select takes priority over TOC-derived section range.
    if (manualSelectionLineIds.value != null) return manualSelectionLineIds.value

    if (commentaryLineId.value == null || !tocEntries().length || !lines().length) return null
    const tocEntry = tocEntries().find((e) => e.lineId === commentaryLineId.value)
    if (!tocEntry || tocEntry.lineIndex == null) return null
    const index = tocEntries().indexOf(tocEntry)
    const nextEntry = tocEntries()
      .slice(index + 1)
      .find((e) => e.lineIndex != null && e.level <= tocEntry.level)
    const fromIndex = tocEntry.lineIndex
    const toIndex = nextEntry?.lineIndex ?? lines().length
    // Exclude placeholder lines (content === null) — they haven't loaded from DB yet.
    // Return null instead of a partial list so useCommentary waits for real IDs.
    const ids = lines()
      .filter((l) => l.lineIndex >= fromIndex && l.lineIndex < toIndex && l.content !== null)
      .map((l) => l.id)
    return ids.length > 0 ? ids : null
  })

  /** Returns false when the click changed nothing (plain re-click on the selected line). */
  function onLineSelected(lineId: number, isShiftClick: boolean): boolean {
    // Plain click on the already-selected line is a no-op — but still set the
    // anchor, because selection can arrive here without a prior click (scroll
    // sync, session restore, commentary navigation) and a later shift-click
    // must range from this line.
    if (
      !isShiftClick &&
      manualSelectionLineIds.value == null &&
      selectedLineId.value === lineId &&
      commentaryLineId.value === lineId
    ) {
      manualSelectionAnchorLineId.value = lineId
      return false
    }

    // Capture synchronously before any reactive state changes: every panel's
    // activePinnedGroup is still valid here (groups haven't been cleared yet).
    applyPendingPins(captureActivePins())

    if (isShiftClick && manualSelectionAnchorLineId.value != null) {
      const anchorId = manualSelectionAnchorLineId.value
      const anchorLine = lines().find((l) => l.id === anchorId)
      const clickedLine = lines().find((l) => l.id === lineId)
      if (anchorLine != null && clickedLine != null) {
        const fromIndex = Math.min(anchorLine.lineIndex, clickedLine.lineIndex)
        const toIndex = Math.max(anchorLine.lineIndex, clickedLine.lineIndex)
        const rangeIds = lines()
          .filter((l) => l.lineIndex >= fromIndex && l.lineIndex <= toIndex && l.content !== null)
          .map((l) => l.id)
        if (rangeIds.length > 0) {
          manualSelectionLineIds.value = rangeIds
          // commentaryLineId drives which line commentary loads for — keep the anchor
          // so the first load always comes from the anchor end of the range.
          commentaryLineId.value = anchorId
          selectedLineId.value = lineId
          return true
        }
      }
    }

    // Plain click — reset anchor and clear any existing range selection.
    clearManualSelection()
    manualSelectionAnchorLineId.value = lineId
    selectedLineId.value = lineId
    commentaryLineId.value = lineId
    return true
  }

  return {
    manualSelectionAnchorLineId,
    manualSelectionLineIds,
    selectedSectionLineIds,
    clearManualSelection,
    onLineSelected,
  }
}
