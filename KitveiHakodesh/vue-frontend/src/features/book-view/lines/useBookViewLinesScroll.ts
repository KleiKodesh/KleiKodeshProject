/**
 * Manages scroll position for the book view lines scroller.
 *
 * Scroll save:
 * - captureScrollPos saves firstVisible.index (not the overscan item) and the pixel
 *   offset within that item. If the gap between scrollTop and firstVisible.start exceeds
 *   2000px the virtualizer hasn't re-rendered after a programmatic scroll — skip the save.
 *
 * Scroll restore — two stages, virtualizer API only, never touch scrollTop directly:
 * Stage 1 — scrollToIndex(targetIndex, 'start') immediately when placeholders are allocated.
 *   Uses estimated item heights but gets the item on screen fast.
 * Stage 2 — once the target line's chunk loads (real DOM heights), scrollToIndex again.
 *   Tracks item.start in a rAF loop; whenever it shifts (background chunks above load and
 *   push item.start down), re-issues scrollToIndex. Exits once stable for 3 consecutive frames.
 *   After stabilization, applies the saved sub-line pixel offset via scrollToOffset — but only
 *   if the offset fits within the item's actual height (guards against stale saves).
 *   A post-stabilization watch continues tracking in case late-loading chunks cause further drift.
 */
import { watch, nextTick, onBeforeUnmount } from 'vue'
import type { Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import type { Virtualizer, VirtualItem } from '@tanstack/vue-virtual'
import type { LineItem } from './useBookViewLinesTable'
import type { CommentaryTreeState, CommentaryVisibilityItem, PinnedCommentaryGroup } from '../bookViewTypes'
import type { useTabStore } from '@/stores/tabStore'
import type { useBookViewStore } from '@/stores/bookViewStore'

// ── Props shape accepted by this composable ───────────────────────────────────

export interface BookViewLinesScrollProps {
  initialLineIndex?: number
  initialScrollIndex?: number
  initialScrollOffset?: number
  flashLineOnOpen?: boolean
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
    const scrollTop = scrollerEl.value.scrollTop
    const firstVisible = items.find((v) => v.start + v.size > scrollTop) ?? first

    // If firstVisible is more than 2000px above scrollTop the virtualizer hasn't
    // re-rendered after a programmatic scroll yet — skip this save to avoid
    // persisting a stale overscan-item position.
    if (scrollTop - firstVisible.start > 2000) return null

    return {
      scrollIndex: firstVisible.index,
      scrollOffset: Math.max(0, scrollTop - firstVisible.start),
    }
  }

  // ── Scroll restore (public wrapper) ────────────────────────────────────────

  let programmaticScrollTimer: ReturnType<typeof setTimeout> | null = null
  let programmaticScrolling = false

  function restoreScrollPos(lineIndex: number, _scrollOffset = 0) {
    virtualizer().scrollToIndex(lineIndex, { align: 'start' })
  }

  // ── Initial scroll on load ──────────────────────────────────────────────────

  let cancelStabilize: (() => void) | null = null
  // Timer for the deep-link line flash; cleared on cancel/unmount so it never fires
  // against a stale element.
  let flashTimer: number | null = null
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
        prioritise(targetIndex)

        // Capture as const number — TypeScript loses the narrowing across nextTick closures.
        const target: number = targetIndex

        nextTick(() => {
          if (restored) return
          restored = true

          let cancelled = false
          cancelStabilize = () => {
            cancelled = true
            programmaticScrolling = false
            if (flashTimer != null) { clearTimeout(flashTimer); flashTimer = null }
          }
          programmaticScrolling = true

          // Stage 1 — estimated heights, gets item on screen immediately.
          virtualizer().scrollToIndex(target, { align: 'start' })

          // Stage 2 — once the target chunk loads, re-issue scrollToIndex with real heights,
          // then track item.start until stable, then apply the saved sub-line offset.
          stopContentWatch = watch(
            () => lines()[target]?.content,
            (content) => {
              if (content == null) return
              stopContentWatch?.()
              if (cancelled) return
              nextTick(() => {
                if (cancelled) return

                virtualizer().scrollToIndex(target, { align: 'start' })

                let lastStart = virtualizer().measurementsCache.find((m) => m.index === target)?.start ?? -1
                let stableFrames = 0
                let trackAttempts = 0
                const offsetToApply = props.initialScrollIndex != null ? (props.initialScrollOffset ?? 0) : 0

                function trackAndCorrect() {
                  if (cancelled) return
                  const fresh = virtualizer().measurementsCache.find((m) => m.index === target)
                  const freshStart = fresh?.start ?? lastStart
                  if (freshStart !== lastStart) {
                    lastStart = freshStart
                    stableFrames = 0
                    virtualizer().scrollToIndex(target, { align: 'start' })
                  } else {
                    stableFrames++
                  }

                  if (stableFrames >= 3) {
                    // Apply saved sub-line offset — only if it fits within the item's
                    // actual height (guards against stale saves from old code).
                    if (offsetToApply > 0) {
                      const offsetCache = virtualizer().measurementsCache.find((m) => m.index === target)
                      if (offsetCache && offsetToApply < offsetCache.end - offsetCache.start) {
                        programmaticScrolling = true
                        virtualizer().scrollToOffset(offsetCache.start + offsetToApply)
                        requestAnimationFrame(() => { if (!cancelled) programmaticScrolling = false })
                      } else {
                        programmaticScrolling = false
                      }
                    } else {
                      programmaticScrolling = false
                    }

                    // Post-stabilization: keep correcting if late chunks shift item.start.
                    let postLastStart = lastStart
                    const postStopWatch = watch(
                      () => lines(),
                      () => {
                        if (cancelled) { postStopWatch(); return }
                        const postCache = virtualizer().measurementsCache.find((m) => m.index === target)
                        if (!postCache) return
                        if (postCache.start !== postLastStart) {
                          postLastStart = postCache.start
                          virtualizer().scrollToIndex(target, { align: 'start' })
                          if (offsetToApply > 0) {
                            requestAnimationFrame(() => {
                              if (cancelled) return
                              const driftCache = virtualizer().measurementsCache.find((m) => m.index === target)
                              if (driftCache) virtualizer().scrollToOffset(driftCache.start + offsetToApply)
                            })
                          }
                        }
                        if (lines().every(l => l.content !== null)) postStopWatch()
                      },
                      { flush: 'post' },
                    )

                    // FTS: scroll to the highlighted mark once positioned.
                    if (props.searchHighlightLineIndex != null) {
                      let markAttempts = 0
                      function tryScrollToMark() {
                        if (cancelled || !scrollerEl.value) return
                        const mark = scrollerEl.value.querySelector('mark.search-match') as HTMLElement | null
                        if (mark) { mark.scrollIntoView({ block: 'center' }); return }
                        if (++markAttempts < 30) requestAnimationFrame(tryScrollToMark)
                      }
                      requestAnimationFrame(tryScrollToMark)
                    }

                    // Deep-link open (otzaria:// / zayit://): momentarily flash the
                    // target line's background so the user sees where the link landed.
                    // The row may not be in the DOM yet (virtualizer), so poll by
                    // [data-index] with the same rAF-retry pattern as the mark scroll.
                    if (props.flashLineOnOpen) {
                      let flashAttempts = 0
                      function tryFlashLine() {
                        if (cancelled || !scrollerEl.value) return
                        const row = scrollerEl.value.querySelector(
                          `[data-index="${target}"] .line`,
                        ) as HTMLElement | null
                        if (row) {
                          row.classList.add('flash-open')
                          // Remove after the animation finishes (must match the CSS
                          // animation duration, ~3.5s) so the class doesn't linger and
                          // re-fire on virtualizer recycle. Cleared on cancel/unmount.
                          flashTimer = window.setTimeout(() => {
                            row.classList.remove('flash-open')
                            flashTimer = null
                          }, 3600)
                          return
                        }
                        if (++flashAttempts < 30) requestAnimationFrame(tryFlashLine)
                      }
                      requestAnimationFrame(tryFlashLine)
                    }
                    return
                  }

                  if (++trackAttempts < 200) {
                    requestAnimationFrame(trackAndCorrect)
                  } else {
                    programmaticScrolling = false
                  }
                }

                requestAnimationFrame(trackAndCorrect)
              })
            },
            { immediate: true, flush: 'post' },
          )

          // Safety: clear programmaticScrolling if content never loads.
          setTimeout(() => { if (!cancelled) programmaticScrolling = false }, 5000)

          requestAnimationFrame(() =>
            requestAnimationFrame(() => {
              if (cancelled) return
              const scrollTop = scrollerEl.value?.scrollTop ?? 0
              const items = virtualizer().getVirtualItems()
              const firstVisible = items.find((v) => v.start + v.size > scrollTop) ?? items[0]
              const firstFull = items.find((v) => v.start >= scrollTop) ?? firstVisible
              emit(
                'scrolled',
                firstVisible?.index ?? target,
                firstFull?.index ?? firstVisible?.index ?? target,
              )
            }),
          )
          scrollerEl.value?.focus({ preventScroll: true })
        })
      },
      { flush: 'post', immediate: true },
    )

    watch(
      () => [lines(), props.idbResolved] as const,
      ([currentLines, resolved]) => {
        if (!currentLines.length || restored || !resolved) return
        if (props.initialLineIndex == null && props.initialScrollIndex == null) {
          stop?.()
          stop = null
          nextTick(() => scrollerEl.value?.focus({ preventScroll: true }))
        }
      },
      { flush: 'post' },
    )
  }

  // ── Persist scroll position ─────────────────────────────────────────────────

  let lastKnownPos: { scrollIndex: number; scrollOffset: number } | null = null
  // Last known good filter snapshot — captured whenever visibilityList is non-empty.
  // Replayed on save when the commentary panel is closed (visibilityList empty), so we
  // never overwrite a previously valid filter with an empty list.
  let lastValidFilterState: import('../bookViewTypes').CommentaryTreeState | undefined

  function savePos() {
    if (programmaticScrolling) return
    const position = lastKnownPos ?? captureScrollPos()
    if (position) {
      // Snapshot the filter state only when the visibilityList is populated (panel has
      // been opened and syncVisibilityList has run). Store the snapshot in
      // lastValidFilterState so we can replay it when the panel is closed.
      if (
        props.commentaryFilterState &&
        props.commentaryFilterState.visibilityList.length > 0
      ) {
        lastValidFilterState = {
          searchQuery: props.commentaryFilterState.searchQuery,
          tokens: [...props.commentaryFilterState.tokens],
          visibilityList: props.commentaryFilterState.visibilityList.map(
            (item: CommentaryVisibilityItem) => ({ ...item }),
          ),
        }
      }
      // Use the most recent valid snapshot; undefined if the panel was never opened.
      const filterState = lastValidFilterState
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
      // Carry the tab's live breadcrumb into the per-book record so the
      // recently-opened rows can show "title · path" like an open tab does.
      // Omitted while the breadcrumb is still unresolved; setLastReadPos then
      // keeps whatever path was saved before.
      const tocPath = tabStore.tabs.find((t) => t.id === tabId)?.tocPath
      tabStore.setLastReadPos(bookId, {
        ...position,
        ...(tocPath ? { tocPath } : {}),
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

  watch(
    () => props.commentaryFilterState?.visibilityList.length,
    (length) => {
      if (length && props.commentaryFilterState) {
        lastValidFilterState = {
          searchQuery: props.commentaryFilterState.searchQuery,
          tokens: [...props.commentaryFilterState.tokens],
          visibilityList: props.commentaryFilterState.visibilityList.map(
            (item: CommentaryVisibilityItem) => ({ ...item }),
          ),
        }
      }
    },
  )

  watch(
    () => props.commentaryVisible,
    (visible) => { if (!visible) savePos() },
  )

  useEventListener(document, 'visibilitychange', () => {
    if (document.visibilityState === 'hidden') savePos()
  })
  useEventListener(window, 'beforeunload', savePos)

  onBeforeUnmount(() => {
    cancelStabilize?.()
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
    const firstVisible = items.find((v) => v.start + v.size > scrollTop) ?? items[0]
    const lineIndex = firstVisible?.index ?? 0
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
    isProgrammaticScrolling: () => programmaticScrolling,
    setProgrammaticScroll,
    onScroll,
  }
}
