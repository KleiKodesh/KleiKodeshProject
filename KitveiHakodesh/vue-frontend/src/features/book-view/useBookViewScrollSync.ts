/**
 * Syncs the active TOC entry and auto-selects commentary as the user scrolls.
 *
 * - Updates activeTocEntryId and the tab's tocPath on every scroll event
 *   (unless a programmatic TOC scroll is in progress).
 * - When autoSelectTopLine is enabled, selects the top visible line and
 *   triggers commentary load after a short debounce.
 */
import { ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useBookViewStore } from '@/stores/bookViewStore'
import type { Ref } from 'vue'
import type { LineItem } from './lines/useBookViewLinesTable'
import type { TocEntry } from '@/webview-host/queries.types'
export function useBookViewScrollSync(
  lines: () => LineItem[],
  activeTocEntryId: Ref<number | undefined>,
  selectedLineId: Ref<number | null>,
  commentaryLineId: Ref<number | null>,
  checkTocScrollProgress: (lineIndex: number) => boolean,
  getActiveTocEntry: (lineIndex: number) => TocEntry | null,
  getTocPath: (entry: TocEntry) => string,
  setPendingPin: (group: { bookId: number; sectionLabel: string; subSectionLabel: string } | null) => void,
  getActivePinnedGroup: () => { bookId: number; sectionLabel: string; subSectionLabel: string } | null,
) {
  const paneNavigation = usePaneNavigation()
  const bookViewStore = useBookViewStore()
  const { autoSelectTopLine } = storeToRefs(bookViewStore)

  const currentScrollLineIndex = ref(0)
  const currentFullLineIndex = ref(0)
  let autoSelectCommentaryTimer: ReturnType<typeof setTimeout> | null = null

  function onLinesScrolled(lineIndex: number, fullLineIndex: number) {
    currentScrollLineIndex.value = lineIndex
    currentFullLineIndex.value = fullLineIndex

    if (checkTocScrollProgress(lineIndex)) return

    const entry = getActiveTocEntry(lineIndex)
    if (entry && entry.id !== activeTocEntryId.value) {
      activeTocEntryId.value = entry.id
      paneNavigation.updateActiveTab({ tocPath: getTocPath(entry) })
    }

    if (!autoSelectTopLine.value) return
    const line = lines().find((l) => l.lineIndex === currentFullLineIndex.value)
    if (line && line.content !== null) {
      selectedLineId.value = line.id
      // Capture the active pinned group synchronously now — groups are still loaded
      // and activePinnedGroup is valid. By the time the timer fires and sets
      // commentaryLineId (triggering a load + groups clear), this value is gone.
      const capturedPin = getActivePinnedGroup()
      if (autoSelectCommentaryTimer) clearTimeout(autoSelectCommentaryTimer)
      autoSelectCommentaryTimer = setTimeout(() => {
        setPendingPin(capturedPin)
        commentaryLineId.value = line.id
      }, 120)
    }
  }

  /**
   * Forces a one-time TOC path sync for a known line index without waiting for
   * a scroll event. Used by session restore so the breadcrumb is populated
   * immediately when a book reloads to its saved position.
   */
  function syncTocPathForLineIndex(lineIndex: number) {
    const entry = getActiveTocEntry(lineIndex)
    if (entry) {
      activeTocEntryId.value = entry.id
      paneNavigation.updateActiveTab({ tocPath: getTocPath(entry) })
    }
  }

  watch(autoSelectTopLine, (enabled) => {
    if (!enabled && autoSelectCommentaryTimer) {
      clearTimeout(autoSelectCommentaryTimer)
      autoSelectCommentaryTimer = null
    }
  })

  return { currentScrollLineIndex, currentFullLineIndex, onLinesScrolled, syncTocPathForLineIndex }
}
