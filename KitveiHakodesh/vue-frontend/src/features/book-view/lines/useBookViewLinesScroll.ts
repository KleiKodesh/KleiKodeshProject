/**
 * Manages scroll position for the book view lines scroller.
 *
 * Responsibilities:
 * - captureScrollPos — read the current virtualizer offset into a plain object
 * - restoreScrollPos — scroll the virtualizer to a specific line index + pixel offset,
 *   using a MutationObserver to apply the offset as soon as the item is measured
 * - Initial-scroll-on-load — waits for lines + target index, then restores once
 * - savePos — writes the current position + all relevant sidebar state to tabStore IDB
 * - Saves on: visibilitychange hidden, beforeunload, onBeforeUnmount
 * - onScroll — updates lastKnownPos, emits 'scrolled', calls prioritise
 * - setProgrammaticScroll — marks the next 300 ms as programmatic (suppresses onScroll saves)
 *
 * The programmaticScrolling flag is a plain let — exposed as a getter so callers
 * (scrollToLineId, scrollToLineIndex in the component) can read the current value.
 */
import { watch, nextTick, onBeforeUnmount } from 'vue'
import type { Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import type { Virtualizer, VirtualItem } from '@tanstack/vue-virtual'
import type { LineItem } from './useBookViewLinesTable'
import type { CommentaryTreeState, CommentaryVisibilityItem, PinnedCommentaryGroup } from '../bookViewTypes'
import type { useTabStore } from '@/stores/tabStore'
import type { useBookViewStore } from '@/stores/bookViewStore'
import { bookViewPerf } from '@/utils/bookViewPerf'

// ── Props shape accepted by this composable ───────────────────────────────────

export interface BookViewLinesScrollProps {
  initialLineIndex?: number
  initialScrollIndex?: number
  initialScrollOffset?: number
  searchHighlightLineIndex?: number
  idbResolved?: boolean
  commentaryVisible?: boolean
  commentaryMode?: 'off' | 'bottom' | 'side'
  commentaryFraction?: number
  stackedCommentaryFraction?: number
  commentaryScrollIndex?: number | null
  commentaryScrollOffset?: number | null
  commentaryFilterState?: CommentaryTreeState
  pinnedCommentaryGroup?: PinnedCommentaryGroup | null
  selectedLineId?: number | null
  searchBarVisible?: boolean
}

export interface BookViewLinesScrollStoreRefs {
  tabStore: ReturnType<typeof useTabStore>
  bookViewStore: ReturnType<typeof useBookViewStore>
  autoSelectTopLine: Ref<boolean>
  zoom: Ref<number>
  tabId: string
  bookId: number
}

// ── Composable ────────────────────────────────────────────────────────────────

export function useBookViewLinesScroll(
  scrollerEl: Ref<HTMLElement | null>,
  virtualizer: () => Virtualizer<Element, Element>,
  virtualItems: () => VirtualItem[],
  lines: () => LineItem[],
  props: BookViewLinesScrollProps,
  storeRefs: BookViewLinesScrollStoreRefs,
  emit: (event: 'scrolled', firstVisible: number, firstFull: number) => void,
  prioritise: (lineIndex: number) => void,
) {
  const { tabStore, bookViewStore, autoSelectTopLine, zoom, tabId, bookId } = storeRefs

  // ── Scroll capture ──────────────────────────────────────────────────────────

  function captureScrollPos() {
    const items = virtualItems()
    const first = items[0]
    if (!first || !scrollerEl.value) return null
    return {
      scrollIndex: first.index,
      scrollOffset: Math.max(0, scrollerEl.value.scrollTop - first.start),
    }
  }

  // ── Scroll restore ──────────────────────────────────────────────────────────

  let programmaticScrollTimer: ReturnType<typeof setTimeout> | null = null
  let programmaticScrolling = false

  function restoreScrollPos(lineIndex: number, scrollOffset = 0) {
    programmaticScrolling = true
    if (programmaticScrollTimer) clearTimeout(programmaticScrollTimer)

    // Step 1 — tell the virtualizer to bring the target item into the rendered range.
    virtualizer().scrollToIndex(lineIndex, { align: 'start' })

    // Step 2 — once the virtualizer renders the target item and populates
    // measurementsCache, read the exact position and apply scrollTop + offset.
    // A MutationObserver fires on each DOM update from the virtualizer so we
    // apply as soon as the measurement is available, without guessing rAF counts.
    const scroller = scrollerEl.value
    if (!scroller) {
      requestAnimationFrame(() => { programmaticScrolling = false })
      return
    }

    const scrollerCaptured: HTMLElement = scroller
    let applied = false

    function tryApply(): boolean {
      const item = virtualizer().measurementsCache.find((measurement) => measurement.index === lineIndex)
      if (!item) return false
      scrollerCaptured.scrollTop = item.start + scrollOffset
      return true
    }

    // Fast path — item may already be in the cache (e.g. already in the rendered window).
    requestAnimationFrame(() => {
      if (applied) return
      if (tryApply()) {
        applied = true
        requestAnimationFrame(() => { programmaticScrolling = false })
        return
      }

      // Slow path — item not yet measured. Watch DOM mutations from the virtualizer
      // and apply as soon as the measurement lands in the cache.
      const observer = new MutationObserver(() => {
        if (applied) return
        if (tryApply()) {
          applied = true
          observer.disconnect()
          requestAnimationFrame(() => { programmaticScrolling = false })
        }
      })
      observer.observe(scrollerCaptured, { childList: true, subtree: true })

      // Safety timeout — ensure programmaticScrolling is always cleared even if the
      // target item never renders (e.g. list is empty or item is out of range).
      setTimeout(() => {
        if (!applied) {
          applied = true
          observer.disconnect()
          programmaticScrolling = false
        }
      }, 500)
    })
  }

  // ── Initial scroll on load ──────────────────────────────────────────────────
  // Watches lines and initialScrollIndex together. Waits for:
  //   1. lines to be non-empty (placeholders allocated)
  //   2. a target index to be known (either initialLineIndex from TOC nav, or
  //      initialScrollIndex from IDB session restore — which may arrive after mount)
  // Scrolls immediately — does NOT wait for content to load. restoreScrollPos
  // works with placeholder items because the virtualizer measures them using
  // estimateSize and populates measurementsCache on the first render of that
  // viewport. Waiting for real content caused a visible flash since the scroller
  // sat at line 0 for the entire prefetch round-trip.
  // Stops itself after the first successful restore.
  {
    let restored = false
    let stopContentWatch: (() => void) | null = null
    let stop: (() => void) | null = null

    stop = watch(
      () => [lines(), props.initialScrollIndex] as const,
      ([currentLines]) => {
        if (!currentLines.length) return
        const targetIndex = props.initialLineIndex ?? props.initialScrollIndex
        if (targetIndex == null) return
        if (targetIndex >= currentLines.length) return
        stop?.()
        stop = null
        bookViewPerf.mark(`lines:content:targetKnown (targetIndex=${targetIndex})`)
        prioritise(targetIndex)

        const offset = props.initialScrollIndex != null ? (props.initialScrollOffset ?? 0) : 0

        nextTick(() => {
          if (restored) return
          restored = true
          restoreScrollPos(targetIndex, offset)
          requestAnimationFrame(() =>
            requestAnimationFrame(() => {
              bookViewPerf.mark('lines:content:scrollRestored')
              const scrollTop = scrollerEl.value?.scrollTop ?? 0
              const items = virtualizer().getVirtualItems()
              const firstVisible = items.find((v) => v.start + v.size > scrollTop) ?? items[0]
              const firstFull = items.find((v) => v.start >= scrollTop) ?? firstVisible
              emit(
                'scrolled',
                firstVisible?.index ?? targetIndex,
                firstFull?.index ?? firstVisible?.index ?? targetIndex,
              )
            }),
          )
          if (props.searchHighlightLineIndex != null && scrollerEl.value) {
            nextTick(() => {
              const mark = scrollerEl.value!.querySelector(
                'mark.search-match',
              ) as HTMLElement | null
              mark?.scrollIntoView({ block: 'center' })
            })
          }
          scrollerEl.value?.focus({ preventScroll: true })
        })

        // Once real content loads, re-apply the offset so the position is exact
        // rather than estimate-based (only matters when offset > 0).
        if (offset !== 0) {
          stopContentWatch = watch(
            () => lines()[targetIndex]?.content,
            (content) => {
              if (content == null) return
              stopContentWatch?.()
              bookViewPerf.mark('lines:content:targetLineLoaded')
              nextTick(() => restoreScrollPos(targetIndex, offset))
            },
            { immediate: true, flush: 'post' },
          )
        }
      },
      { flush: 'post', immediate: true },
    )

    // If no target ever arrives (no saved position, no TOC nav), focus the scroller
    // once lines are loaded so keyboard navigation works immediately.
    // Gate on idbResolved so we don't give up before IDB has had a chance to respond.
    watch(
      () => [lines(), props.idbResolved] as const,
      ([currentLines, resolved]) => {
        if (!currentLines.length || restored || !resolved) return
        if (props.initialLineIndex == null && props.initialScrollIndex == null) {
          stop?.()
          stop = null
          bookViewPerf.mark('lines:content:noTargetFocusScroller')
          nextTick(() => scrollerEl.value?.focus({ preventScroll: true }))
        }
      },
      { flush: 'post' },
    )
  }

  // ── Persist scroll position ─────────────────────────────────────────────────

  // Last known good position — updated on every scroll so unmount always has fresh data
  // even if the DOM is already detached when onBeforeUnmount fires (WebView2 behaviour).
  let lastKnownPos: { scrollIndex: number; scrollOffset: number } | null = null

  function savePos() {
    if (programmaticScrolling) return
    const position = lastKnownPos ?? captureScrollPos()
    if (position) {
      // Serialize the reactive proxy to a plain object before writing to IDB.
      // IDB's structured clone algorithm cannot serialize Vue reactive proxies.
      const filterState = props.commentaryFilterState
        ? {
            searchQuery: props.commentaryFilterState.searchQuery,
            tokens: [...props.commentaryFilterState.tokens],
            visibilityList: props.commentaryFilterState.visibilityList.map(
              (item: CommentaryVisibilityItem) => ({ ...item }),
            ),
          }
        : undefined
      const pinnedGroup = props.pinnedCommentaryGroup
        ? {
            bookId: props.pinnedCommentaryGroup.bookId,
            sectionLabel: props.pinnedCommentaryGroup.sectionLabel,
            subSectionLabel: props.pinnedCommentaryGroup.subSectionLabel,
          }
        : null
      tabStore.setBookViewState(tabId, bookId, {
        ...position,
        selectedLineId: props.selectedLineId,
        commentaryScrollIndex: props.commentaryScrollIndex,
        commentaryScrollOffset: props.commentaryScrollOffset,
        commentaryFilterState: filterState,
        zoom: zoom.value,
        commentaryZoom: bookViewStore.getCommentaryZoom(tabId, bookId),
        commentaryVisible: props.commentaryVisible,
        commentaryMode: props.commentaryMode,
        commentaryFraction: props.commentaryFraction,
        stackedCommentaryFraction: props.stackedCommentaryFraction,
        autoSelectTopLine: autoSelectTopLine.value,
        pinnedCommentaryGroup: pinnedGroup,
      })
      tabStore.setLastReadPos(bookId, {
        ...position,
        selectedLineId: props.selectedLineId,
        commentaryScrollIndex: props.commentaryScrollIndex,
        commentaryScrollOffset: props.commentaryScrollOffset,
        commentaryFilterState: filterState,
        commentaryMode: props.commentaryMode,
        commentaryFraction: props.commentaryFraction,
        stackedCommentaryFraction: props.stackedCommentaryFraction,
        pinnedCommentaryGroup: pinnedGroup,
      })
    }
  }

  // Save when the commentary panel closes so the commentary scroll position
  // (which just arrived via prop update from onCommentaryScroll) is flushed to
  // IDB before CommentaryView unmounts and the position would otherwise be lost.
  watch(
    () => props.commentaryVisible,
    (visible) => { if (!visible) savePos() },
  )

  useEventListener(document, 'visibilitychange', () => {
    if (document.visibilityState === 'hidden') savePos()
  })
  useEventListener(window, 'beforeunload', savePos)

  onBeforeUnmount(() => {
    // Force-clear the programmatic flag so savePos is never silently skipped at unmount.
    programmaticScrolling = false
    if (programmaticScrollTimer) {
      clearTimeout(programmaticScrollTimer)
      programmaticScrollTimer = null
    }
    savePos()
  })

  // ── Scroll handler ──────────────────────────────────────────────────────────

  function onScroll() {
    if (!scrollerEl.value || programmaticScrolling) return
    const scrollTop = scrollerEl.value.scrollTop
    const items = virtualItems()
    // For scroll position tracking (TOC, persistence): first item with any part visible
    const firstVisible = items.find((v) => v.start + v.size > scrollTop) ?? items[0]
    const lineIndex = firstVisible?.index ?? 0
    // For auto-select: first fully visible line (top edge at or below scrollTop)
    const firstFull = items.find((v) => v.start >= scrollTop) ?? firstVisible
    const fullLineIndex = firstFull?.index ?? lineIndex
    lastKnownPos = captureScrollPos()
    prioritise(lineIndex)
    emit('scrolled', lineIndex, fullLineIndex)
  }

  // ── Programmatic scroll flag ────────────────────────────────────────────────

  function setProgrammaticScroll() {
    programmaticScrolling = true
    if (programmaticScrollTimer) clearTimeout(programmaticScrollTimer)
    programmaticScrollTimer = setTimeout(() => { programmaticScrolling = false }, 300)
  }

  // ── Public API ──────────────────────────────────────────────────────────────

  return {
    captureScrollPos,
    restoreScrollPos,
    /** Getter — read the current programmaticScrolling flag. */
    isProgrammaticScrolling: () => programmaticScrolling,
    setProgrammaticScroll,
    onScroll,
  }
}
