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
import { ref, computed, watch } from 'vue'
import type { TocEntry } from '@/webview-host/queries.types'
import type { CommentaryPinSnapshot } from './bookViewTypes'
import type { LinesTable } from './lines/linesTable'

export function useBookViewLineSelection(
  lines: () => LinesTable,
  tocEntries: () => TocEntry[],
  commentaryLineId: import('vue').Ref<number | null>,
  selectedLineId: import('vue').Ref<number | null>,
  captureActivePins: () => CommentaryPinSnapshot,
  applyPendingPins: (snapshot: CommentaryPinSnapshot) => void,
  /**
   * Fetches the IDS of the lines in [start, end) — no text. Lines load on demand, so a
   * range may have nothing yet; a section or a shift-click range needs ids only.
   */
  ensureLineIds: (start: number, end: number) => Promise<void>,
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
    // Lines load on demand (an unloaded line has a negative id): until every line of
    // the section has its id, return null — the watcher below fetches the ids — so
    // useCommentary waits for the real ones.
    const section = lines().slice(fromIndex, toIndex)
    if (!section.length || section.some((l) => l.id <= 0)) return null
    return section.map((l) => l.id)
  })

  // The section's line ids, fetched (ids only) when a TOC section is selected.
  watch(
    () => {
      // Nothing to fetch into before the line table is sized; reading its length here
      // makes the watcher fire again when it arrives (restore can set the line first).
      if (!lines().length || manualSelectionLineIds.value != null || commentaryLineId.value == null) return null
      const tocEntry = tocEntries().find((e) => e.lineId === commentaryLineId.value)
      if (!tocEntry || tocEntry.lineIndex == null) return null
      const index = tocEntries().indexOf(tocEntry)
      const nextEntry = tocEntries()
        .slice(index + 1)
        .find((e) => e.lineIndex != null && e.level <= tocEntry.level)
      return [tocEntry.lineIndex, nextEntry?.lineIndex ?? lines().length] as const
    },
    (range) => {
      if (range) void ensureLineIds(range[0], range[1]).catch(() => {})
    },
  )

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
      const anchorIndex = lines().indexOfId(anchorId)
      const clickedIndex = lines().indexOfId(lineId)
      if (anchorIndex != null && clickedIndex != null) {
        const fromIndex = Math.min(anchorIndex, clickedIndex)
        const toIndex = Math.max(anchorIndex, clickedIndex)
        const applyRange = () => {
          const rangeIds = lines().slice(fromIndex, toIndex + 1).filter((l) => l.id > 0).map((l) => l.id)
          if (!rangeIds.length) return
          manualSelectionLineIds.value = rangeIds
          // commentaryLineId drives which line commentary loads for — keep the anchor
          // so the first load always comes from the anchor end of the range.
          commentaryLineId.value = anchorId
          selectedLineId.value = lineId
        }
        // The lines between the two ends may never have been scrolled through: their
        // ids (only) are fetched first.
        const complete = lines().slice(fromIndex, toIndex + 1).every((l) => l.id > 0)
        if (complete) applyRange()
        else void ensureLineIds(fromIndex, toIndex + 1).then(applyRange, () => {})
        return true
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
