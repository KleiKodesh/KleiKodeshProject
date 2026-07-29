/**
 * Section navigation for the commentary panel — next/prev section buttons.
 *
 * Supports three modes:
 *   - Multi-select mode: the user has shift-selected a range of lines; navigates to
 *     the single line immediately after the last selected line (next) or before the
 *     first selected line (prev), then clears the multi-select.
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
import type { TocEntry } from '@/webview-host/queries.types'
interface LinesContentRef { scrollToLineId: (lineId: number, lineIndex?: number) => void }

export function useCommentaryNavigation(
  bookId: number | undefined,
  selectedLineId: Ref<number | null>,
  commentaryLineId: Ref<number | null>,
  commentaryVisible: Ref<boolean>,
  lines: () => LineItem[],
  tocEntries: () => TocEntry[],
  linesContentRef: () => LinesContentRef | null,
  manualSelectionLineIds: () => number[] | null,
  onClearManualSelection: () => void,
) {
  async function onNavigateSection(direction: 'next' | 'prev', commentaryBookId: number) {
    if (selectedLineId.value == null || bookId == null) return

    function afterNavigate(targetLineId: number) {
      onClearManualSelection()
      selectedLineId.value = targetLineId
      commentaryLineId.value = targetLineId
      commentaryVisible.value = true
      linesContentRef()?.scrollToLineId(targetLineId)
    }

    // Multi-select mode: navigate to the line immediately after the last selected
    // line (next) or before the first selected line (prev), ignoring commentary
    // availability — the user explicitly chose this range.
    const multiIds = manualSelectionLineIds()
    if (multiIds != null && multiIds.length > 0) {
      const allLines = lines()
      if (direction === 'next') {
        const lastSelectedId = multiIds[multiIds.length - 1]!
        const lastLine = allLines.find((l) => l.id === lastSelectedId)
        if (lastLine == null) return
        const targetLine = allLines.find(
          (l) => l.lineIndex === lastLine.lineIndex + 1 && l.content !== null,
        )
        if (targetLine == null) return
        afterNavigate(targetLine.id)
      } else {
        const firstSelectedId = multiIds[0]!
        const firstLine = allLines.find((l) => l.id === firstSelectedId)
        if (firstLine == null) return
        const targetLine = allLines.find(
          (l) => l.lineIndex === firstLine.lineIndex - 1 && l.content !== null,
        )
        if (targetLine == null) return
        afterNavigate(targetLine.id)
      }
      return
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
