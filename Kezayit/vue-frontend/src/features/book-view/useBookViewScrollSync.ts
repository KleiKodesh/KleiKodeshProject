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
import { useTabStore } from '@/stores/tabStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import type { Ref } from 'vue'
import type { LineItem } from './useBookViewLinesTable'
import type { TocEntry } from './useBookViewToc'

export function useBookViewScrollSync(
  lines: () => LineItem[],
  activeTocEntryId: Ref<number | undefined>,
  selectedLineId: Ref<number | null>,
  commentaryLineId: Ref<number | null>,
  bottomVisible: Ref<boolean>,
  checkTocScrollProgress: (lineIndex: number) => boolean,
  getActiveTocEntry: (lineIndex: number) => TocEntry | null,
  getTocPath: (entry: TocEntry) => string,
) {
  const tabStore = useTabStore()
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
      tabStore.updateActiveTab({ tocPath: getTocPath(entry) })
    }

    if (!autoSelectTopLine.value) return
    const line = lines().find((l) => l.lineIndex === currentFullLineIndex.value)
    if (line && line.id > 0) {
      selectedLineId.value = line.id
      bottomVisible.value = true
      if (autoSelectCommentaryTimer) clearTimeout(autoSelectCommentaryTimer)
      autoSelectCommentaryTimer = setTimeout(() => {
        commentaryLineId.value = line.id
      }, 120)
    }
  }

  watch(autoSelectTopLine, (enabled) => {
    if (!enabled && autoSelectCommentaryTimer) {
      clearTimeout(autoSelectCommentaryTimer)
      autoSelectCommentaryTimer = null
    }
  })

  return { currentScrollLineIndex, currentFullLineIndex, onLinesScrolled }
}
