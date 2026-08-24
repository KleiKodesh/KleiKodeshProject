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
interface LinesContentRef {
  scrollToLineId: (
    lineId: number,
    lineIndex?: number,
    options?: { skipIfVisible?: boolean },
  ) => void
}

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
  /**
   * Called synchronously the instant before commentaryLineId changes, i.e. at the
   * only moment when every panel's currently-shown group is still valid AND the
   * change is certain to happen.
   *
   * Section navigation runs DB queries first, so a caller that staged its pins
   * before awaiting left them staged across the round trip: any other path that
   * changed commentaryLineId meanwhile (a line tap, the auto-select timer) consumed
   * them, and a navigation that found no target left them staged indefinitely for
   * some later unrelated change to pick up. Staging here closes both windows.
   */
  onBeforeNavigate: (
    targetLineId: number,
    commentaryBookId: number,
    /**
     * False when the resolved target IS the current anchor, so no watcher will
     * fire: the caller must then apply the pin itself instead of staging one that
     * nothing would consume.
     */
    anchorChanges: boolean,
  ) => void = () => {},
) {
  // Navigations are serialized. Each run reads the CURRENT anchor to decide where
  // "next" is, so two fast clicks that overlap both read the pre-click anchor,
  // resolve to the SAME target, and the user advances one section instead of two.
  // Worse, the second afterNavigate then assigns commentaryLineId a value it
  // already holds - no watcher fires, and the pin it staged is left behind for
  // some later, unrelated anchor change to consume. Chaining makes each click
  // start from where the previous one landed.
  let navChain: Promise<void> = Promise.resolve()
  let queued = 0
  // A held-down or hammered button must not build an unbounded backlog that keeps
  // scrolling after the user stops; a few queued steps is all that stays useful.
  const MAX_QUEUED = 4

  function onNavigateSection(direction: 'next' | 'prev', commentaryBookId: number): Promise<void> {
    if (queued >= MAX_QUEUED) return navChain
    queued += 1
    navChain = navChain
      .then(() => runNavigation(direction, commentaryBookId))
      .catch(() => { /* a failed step must not break the chain for later clicks */ })
      .then(() => { queued -= 1 })
    return navChain
  }

  async function runNavigation(direction: 'next' | 'prev', commentaryBookId: number) {
    if (selectedLineId.value == null || bookId == null) return
    // A panel with no active group reports bookId 0; navigating "the sections of
    // book 0" can only fail, and staging a pin for it would poison the panel.
    if (!commentaryBookId) return

    function afterNavigate(targetLineId: number) {
      onBeforeNavigate(targetLineId, commentaryBookId, commentaryLineId.value !== targetLineId)
      onClearManualSelection()
      selectedLineId.value = targetLineId
      commentaryLineId.value = targetLineId
      commentaryVisible.value = true
      // Only needs the line on screen — leave the reader where they are if it already is.
      linesContentRef()?.scrollToLineId(targetLineId, undefined, { skipIfVisible: true })
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
