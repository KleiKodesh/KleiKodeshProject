/**
 * Section navigation for the commentary panel — next/prev section buttons.
 *
 * Supports two modes:
 *   - TOC mode: the selected line is a TOC entry line; navigates to the next/prev
 *     TOC entry at the same level that has commentary for the given book.
 *   - Normal mode: navigates to the next/prev line that has commentary for the book.
 *
 * After navigating, the main text scrolls to the target line via scrollToLineId.
 * The commentary panel scrolls to the target book's group via setupGroupReloadScroll
 * in useCommentaryScroll — the same path that fires when a user clicks a line.
 * No manual watch(commentaryLoading) is needed here.
 */
import {
  findNextCommentarySection,
  findPrevCommentarySection,
  findNextTocCommentarySection,
  findPrevTocCommentarySection,
} from './commentaryNavigation'
import type { Ref } from 'vue'
import type { LineItem } from '../lines/useBookViewLinesTable'
import type { TocEntry } from '../toc/useBookViewToc'

interface LinesContentRef { scrollToLineId: (lineId: number, lineIndex?: number) => void }

export function useCommentaryNavigation(
  bookId: number | undefined,
  selectedLineId: Ref<number | null>,
  commentaryLineId: Ref<number | null>,
  commentaryVisible: Ref<boolean>,
  lines: () => LineItem[],
  tocEntries: () => TocEntry[],
  linesContentRef: () => LinesContentRef | null,
) {
  async function onNavigateSection(direction: 'next' | 'prev', commentaryBookId: number) {
    if (selectedLineId.value == null || bookId == null) return

    function afterNavigate(targetLineId: number) {
      selectedLineId.value = targetLineId
      commentaryLineId.value = targetLineId
      commentaryVisible.value = true
      // scrollToLineId uses scrollToIndexWithRetry internally — handles lines that
      // haven't been measured yet without fighting the virtualizer.
      linesContentRef()?.scrollToLineId(targetLineId)
      // Commentary scroll is handled by setupGroupReloadScroll in useCommentaryScroll,
      // which watches groups with flush:'post' and calls scrollToGroup (now backed by
      // scrollToIndexWithRetry) once the reload settles. This is the same path that
      // fires when a user clicks a line — no manual watch(commentaryLoading) needed.
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
