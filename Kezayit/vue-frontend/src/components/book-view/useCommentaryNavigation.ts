/**
 * Section navigation for the commentary panel — next/prev section buttons.
 *
 * Supports two modes:
 *   - TOC mode: the selected line is a TOC entry line; navigates to the next/prev
 *     TOC entry at the same level that has commentary for the given book.
 *   - Normal mode: navigates to the next/prev line that has commentary for the book.
 *
 * After navigating, waits for commentary to finish loading then scrolls the
 * commentary view to the target book's group.
 */
import { watch, nextTick } from 'vue'
import {
  findNextCommentarySection,
  findPrevCommentarySection,
  findNextTocCommentarySection,
  findPrevTocCommentarySection,
} from '@/utils/commentaryNav'
import type { Ref } from 'vue'
import type { LineItem } from './useLinesTable'
import type { TocEntry } from './useToc'

interface LinesContentRef { scrollToLineId: (lineId: number, lineIndex?: number) => void }
interface CommentaryViewRef { scrollToGroup: (bookId: number) => void }

export function useCommentaryNavigation(
  bookId: number | undefined,
  selectedLineId: Ref<number | null>,
  commentaryLineId: Ref<number | null>,
  bottomVisible: Ref<boolean>,
  commentaryLoading: Ref<boolean>,
  lines: () => LineItem[],
  tocEntries: () => TocEntry[],
  linesContentRef: () => LinesContentRef | null,
  commentaryViewRef: () => CommentaryViewRef | null,
) {
  let pendingNavStop: (() => void) | null = null

  async function onNavigateSection(direction: 'next' | 'prev', commentaryBookId: number) {
    if (selectedLineId.value == null || bookId == null) return
    if (pendingNavStop) {
      pendingNavStop()
      pendingNavStop = null
    }

    function afterNavigate(targetLineId: number) {
      selectedLineId.value = targetLineId
      commentaryLineId.value = targetLineId
      bottomVisible.value = true
      linesContentRef()?.scrollToLineId(targetLineId)
      const stop = watch(
        commentaryLoading,
        (loading) => {
          if (loading) return
          if (commentaryLineId.value !== targetLineId) return
          pendingNavStop = null
          stop()
          nextTick(() => commentaryViewRef()?.scrollToGroup(commentaryBookId))
        },
        { flush: 'sync' },
      )
      pendingNavStop = stop
    }

    // TOC mode: navigate to next/prev toc entry at same level that has commentary
    const currentTocEntry = tocEntries().find((e) => e.lineId === selectedLineId.value)
    if (currentTocEntry) {
      const fn = direction === 'next' ? findNextTocCommentarySection : findPrevTocCommentarySection
      const entry = await fn(bookId, commentaryBookId, currentTocEntry, tocEntries())
      if (entry == null || entry.lineId == null) return
      afterNavigate(entry.lineId)
      return
    }

    // Normal mode: navigate to next/prev line with commentary for this book
    const currentLine = lines().find((l) => l.id === selectedLineId.value)
    if (currentLine == null) return
    const fn = direction === 'next' ? findNextCommentarySection : findPrevCommentarySection
    const result = await fn(bookId, commentaryBookId, currentLine.lineIndex)
    if (result == null) return
    afterNavigate(result.id)
  }

  return { onNavigateSection }
}
