/**
 * Manages scroll position for the book view lines scroller.
 *
 * Responsibilities:
 * - captureScrollPos — read the current virtualizer offset into a plain object
 * - restoreScrollPos — scroll the virtualizer to a specific line index (virtualizer API only)
 * - Initial-scroll-on-load — two-stage restore using scrollToIndex
 * - savePos — writes the current position + all relevant sidebar state to tabStore IDB
 * - Saves on: visibilitychange hidden, beforeunload, onBeforeUnmount
 * - onScroll — updates lastKnownPos, emits 'scrolled', calls prioritise
 * - setProgrammaticScroll — marks the next 300 ms as programmatic (suppresses onScroll saves)
 *
 * Scroll restore strategy:
 * Stage 1 — scrollToIndex(targetIndex, 'start') immediately when placeholders are allocated.
 *   Uses estimated item heights but gets the item on screen fast.
 * Stage 2 — once the target line's chunk loads (real heights available), scrollToIndex again.
 *   The virtualizer now has DOM-measured heights and places the item accurately.
 *
 * We intentionally do NOT attempt to restore the sub-line pixel offset (scrollOffset).
 * item.start for items far from the viewport is estimated from estimateSize=32 for all
 * un-rendered items above — potentially thousands of items with real heights of 60-200px
 * in long-line books. scrollToOffset(item.start + offset) with a stale item.start would
 * land at the wrong position. scrollToIndex is always accurate regardless of item heights.
 *
 * The programmaticScrolling flag suppresses savePos during the restore window so that a
 * visibilitychange event during restore doesn't overwrite IDB with a mid-restore position.
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
    const firstFull = items.find((v) => v.start >= scrollTop) ?? firstVisible

    // Sanity check: if firstVisible.start is more than 2000px above scrollTop,
    // the virtualizer hasn't re-rendered after a programmatic scroll yet — skip.
    if (scrollTop - firstVisible.start > 2000) {
      console.warn(`[scroll-save] skipping stale capture: scrollTop=${scrollTop}, firstVisible=${firstVisible.index} start=${firstVisible.start}, gap=${scrollTop - firstVisible.start}`)
      return null
    }

    // Save firstVisible (not first rendered) as scrollIndex so restore lands at
    // the line the user was actually looking at, not the overscan item above it.
    const result = {
      scrollIndex: firstVisible.index,
      scrollOffset: Math.max(0, scrollTop - firstVisible.start),
    }
    console.log(`[scroll-save] captureScrollPos: scrollTop=${scrollTop}, firstVisible=${firstVisible.index} (start=${firstVisible.start}), firstFull=${firstFull.index}, saving scrollIndex=${result.scrollIndex} offset=${result.scrollOffset}, content="${lines()[firstVisible.index]?.content?.replace(/<[^>]*>/g, '').slice(0, 50) ?? 'n/a'}"`)
    return result
  }

  // ── Scroll restore (public wrapper) ────────────────────────────────────────

  let programmaticScrollTimer: ReturnType<typeof setTimeout> | null = null
  let programmaticScrolling = false

  function restoreScrollPos(lineIndex: number, _scrollOffset = 0) {
    // Always use scrollToIndex — scrollOffset is ignored because item.start is
    // unreliable for un-rendered items (see module comment).
    virtualizer().scrollToIndex(lineIndex, { align: 'start' })
  }

  // ── Initial scroll on load ──────────────────────────────────────────────────

  let cancelStabilize: (() => void) | null = null
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

        nextTick(() => {
          if (restored) return
          restored = true
          console.log(`[scroll-restore] initial restore: targetIndex=${targetIndex} lines=${lines().length}`)

          let cancelled = false
          cancelStabilize = () => { cancelled = true; programmaticScrolling = false }
          programmaticScrolling = true

          // Stage 1 — estimated heights, gets item on screen immediately.
          const virt1 = virtualizer()
          const cache1 = virt1.measurementsCache.find((m) => m.index === targetIndex)
          console.log(`[scroll-restore] stage1: scrollToIndex(${targetIndex}) — item in cache: ${cache1 != null}, estimated start: ${cache1?.start ?? 'n/a'}, scrollTop before: ${scrollerEl.value?.scrollTop ?? 'n/a'}`)
          virt1.scrollToIndex(targetIndex, { align: 'start' })
          requestAnimationFrame(() => {
            const cache1after = virtualizer().measurementsCache.find((m) => m.index === targetIndex)
            console.log(`[scroll-restore] stage1 after rAF: scrollTop=${scrollerEl.value?.scrollTop ?? 'n/a'}, item.start=${cache1after?.start ?? 'n/a'}, rendered=[${virtualizer().getVirtualItems().map(v => v.index).join(',')}]`)
          })

          // Stage 2 — once the target chunk loads, re-issue scrollToIndex with real heights.
          stopContentWatch = watch(
            () => lines()[targetIndex]?.content,
            (content) => {
              if (content == null) return
              stopContentWatch?.()
              if (cancelled) return
              const virt2 = virtualizer()
              const cache2 = virt2.measurementsCache.find((m) => m.index === targetIndex)
              const rendered2 = virt2.getVirtualItems()
              const placeholders2 = rendered2.filter((v) => lines()[v.index]?.content === null).length
              console.log(`[scroll-restore] stage2 trigger: targetIndex=${targetIndex}, item.start=${cache2?.start ?? 'n/a'}, scrollTop=${scrollerEl.value?.scrollTop ?? 'n/a'}, rendered=[${rendered2[0]?.index ?? '?'}..${rendered2[rendered2.length - 1]?.index ?? '?'}] (${rendered2.length} items, ${placeholders2} placeholders), totalSize=${virt2.getTotalSize()}`)
              nextTick(() => {
                if (cancelled) return

                // Stage 2: initial scrollToIndex to put target at top with real heights.
                virtualizer().scrollToIndex(targetIndex, { align: 'start' })

                // Tracking loop: item.start shifts as background chunks above the target
                // get loaded and DOM-measured, pushing item.start down. Keep re-issuing
                // scrollToIndex whenever item.start changes. Exit once stable for 3 frames.
                let lastStart = virtualizer().measurementsCache.find((m) => m.index === targetIndex)?.start ?? -1
                let stableFrames = 0
                let trackAttempts = 0

                function trackAndCorrect() {
                  if (cancelled) return
                  const fresh = virtualizer().measurementsCache.find((m) => m.index === targetIndex)
                  const freshStart = fresh?.start ?? lastStart
                  if (freshStart !== lastStart) {
                    console.log(`[scroll-restore] tracking: item.start shifted ${lastStart} -> ${freshStart}, re-issuing scrollToIndex (attempt ${trackAttempts})`)
                    lastStart = freshStart
                    stableFrames = 0
                    virtualizer().scrollToIndex(targetIndex, { align: 'start' })
                  } else {
                    stableFrames++
                    console.log(`[scroll-restore] tracking: stable frame ${stableFrames} (item.start=${freshStart}, scrollTop=${scrollerEl.value?.scrollTop ?? 'n/a'})`)
                  }

                  if (stableFrames >= 3) {
                    programmaticScrolling = false
                    const finalCache = virtualizer().measurementsCache.find((m) => m.index === targetIndex)
                    const finalRendered = virtualizer().getVirtualItems()
                    const finalScrollTop = scrollerEl.value?.scrollTop ?? 0
                    const finalFirstVisible = finalRendered.find((v) => v.start + v.size > finalScrollTop)
                    const finalFirstFull = finalRendered.find((v) => v.start >= finalScrollTop)
                    console.log(`[scroll-restore] tracking done after ${trackAttempts} attempts:`)
                    console.log(`  item.start=${finalCache?.start ?? 'n/a'}, scrollTop=${finalScrollTop}, totalSize=${virtualizer().getTotalSize()}`)
                    console.log(`  firstVisible=${finalFirstVisible?.index ?? 'n/a'} start=${finalFirstVisible?.start ?? 'n/a'}`)
                    console.log(`  firstFull=${finalFirstFull?.index ?? 'n/a'} start=${finalFirstFull?.start ?? 'n/a'}`)
                    console.log(`  targetIndex=${targetIndex} content="${lines()[targetIndex]?.content?.replace(/<[^>]*>/g, '').slice(0, 60) ?? 'n/a'}"`)
                    console.log(`  firstVisible content="${lines()[finalFirstVisible?.index ?? -1]?.content?.replace(/<[^>]*>/g, '').slice(0, 60) ?? 'n/a'}"`)
                    console.log(`  firstFull content="${lines()[finalFirstFull?.index ?? -1]?.content?.replace(/<[^>]*>/g, '').slice(0, 60) ?? 'n/a'}"`)
                    // Log 3 lines before and after target for context
                    for (let d = -2; d <= 2; d++) {
                      const idx = targetIndex + d
                      const c = virtualizer().measurementsCache.find((m) => m.index === idx)
                      console.log(`  [${d >= 0 ? '+' : ''}${d}] index=${idx} start=${c?.start ?? 'n/a'} content="${lines()[idx]?.content?.replace(/<[^>]*>/g, '').slice(0, 50) ?? 'n/a'}"`)
                    }

                    // Post-stabilization: apply the saved pixel offset within the line.
                    // item.start is now accurate (real heights from DOM measurement).
                    // Only apply if offset > 0 — scrollToIndex already placed it at the top.
                    const offsetToApply = props.initialScrollIndex != null ? (props.initialScrollOffset ?? 0) : 0
                    if (offsetToApply > 0) {
                      const offsetCache = virtualizer().measurementsCache.find((m) => m.index === targetIndex)
                      if (offsetCache && offsetToApply < offsetCache.end - offsetCache.start) {
                        // Guard: only apply if offset fits within the item's actual height.
                        // A saved offset larger than the item height means a stale save from
                        // an old overscan-based captureScrollPos — discard it.
                        const offsetTarget = offsetCache.start + offsetToApply
                        programmaticScrolling = true  // keep suppressed through the scroll
                        console.log(`[scroll-restore] applying offset: scrollToOffset(${offsetTarget}) = item.start(${offsetCache.start}) + offset(${offsetToApply}), item.height=${offsetCache.end - offsetCache.start}`)
                        virtualizer().scrollToOffset(offsetTarget)
                        requestAnimationFrame(() => { if (!cancelled) programmaticScrolling = false })
                      } else if (offsetCache) {
                        console.warn(`[scroll-restore] offset ${offsetToApply} exceeds item height ${offsetCache.end - offsetCache.start} — discarding stale offset`)
                      }
                    }
                    // Each time, check if item.start shifted — if so, re-issue scrollToIndex.
                    // Stops once all lines have real content (no more chunks loading).
                    let postLastStart = lastStart
                    const postStopWatch = watch(
                      () => lines(),
                      () => {
                        if (cancelled) { postStopWatch(); return }
                        const postCache = virtualizer().measurementsCache.find((m) => m.index === targetIndex)
                        if (!postCache) return
                        if (postCache.start !== postLastStart) {
                          console.log(`[scroll-restore] post-drift: item.start ${postLastStart}->${postCache.start}, re-issuing scrollToIndex`)
                          postLastStart = postCache.start
                          virtualizer().scrollToIndex(targetIndex, { align: 'start' })
                          // Re-apply offset after the drift correction
                          if (offsetToApply > 0) {
                            requestAnimationFrame(() => {
                              if (cancelled) return
                              const driftCache = virtualizer().measurementsCache.find((m) => m.index === targetIndex)
                              if (driftCache) {
                                console.log(`[scroll-restore] post-drift offset re-apply: scrollToOffset(${driftCache.start + offsetToApply})`)
                                virtualizer().scrollToOffset(driftCache.start + offsetToApply)
                              }
                            })
                          }
                        }
                        // Stop once all lines loaded — no more drift possible
                        if (lines().every(l => l.content !== null)) {
                          console.log(`[scroll-restore] post-watch: all lines loaded, stopping`)
                          postStopWatch()
                        }
                      },
                      { flush: 'post' },
                    )

                    if (props.searchHighlightLineIndex != null && !cancelled) {
                      let markAttempts = 0
                      function tryScrollToMark() {
                        if (cancelled || !scrollerEl.value) return
                        const mark = scrollerEl.value.querySelector('mark.search-match') as HTMLElement | null
                        if (mark) {
                          console.log(`[scroll-restore] mark found after ${markAttempts} attempts`)
                          mark.scrollIntoView({ block: 'center' })
                          return
                        }
                        if (++markAttempts < 30) requestAnimationFrame(tryScrollToMark)
                        else console.warn('[scroll-restore] mark not found after 30 attempts')
                      }
                      requestAnimationFrame(tryScrollToMark)
                    }
                    return
                  }

                  if (++trackAttempts < 200) {
                    requestAnimationFrame(trackAndCorrect)
                  } else {
                    console.warn(`[scroll-restore] tracking gave up after 200 attempts, item.start=${lastStart}, scrollTop=${scrollerEl.value?.scrollTop ?? 'n/a'}`)
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

          // Periodic audit: check if position drifts after stabilization.
          // Fires at 1s, 2s, 3s, 4s, 5s — catches late drift from slow background chunks.
          let auditTargetIndex = -1
          ;[1000, 2000, 3000, 4000, 5000].forEach((delay) => {
            setTimeout(() => {
              if (cancelled) return
              const virtA = virtualizer()
              const cacheA = virtA.measurementsCache.find((m) => m.index === targetIndex)
              const renderedA = virtA.getVirtualItems()
              const scrollTopA = scrollerEl.value?.scrollTop ?? 0
              const firstVisibleA = renderedA.find((v) => v.start + v.size > scrollTopA)
              const firstFullA = renderedA.find((v) => v.start >= scrollTopA)
              const chunksLoaded = lines().filter(l => l.content !== null).length
              const isCorrect = firstFullA?.index === targetIndex || firstVisibleA?.index === targetIndex
              console.log(`[scroll-restore] ${delay}ms audit: scrollTop=${scrollTopA}, item.start=${cacheA?.start ?? 'n/a'}, firstVisible=${firstVisibleA?.index ?? 'n/a'} (start=${firstVisibleA?.start ?? 'n/a'}), firstFull=${firstFullA?.index ?? 'n/a'}, targetIndex=${targetIndex}, totalSize=${virtA.getTotalSize()}, chunksLoaded=${chunksLoaded}/${lines().length}, CORRECT=${isCorrect}`)
              if (!isCorrect) {
                console.warn(`[scroll-restore] ${delay}ms WRONG POSITION — firstVisible content: "${lines()[firstVisibleA?.index ?? -1]?.content?.replace(/<[^>]*>/g, '').slice(0, 60) ?? 'n/a'}"`)
                console.warn(`[scroll-restore] ${delay}ms target content: "${lines()[targetIndex]?.content?.replace(/<[^>]*>/g, '').slice(0, 60) ?? 'n/a'}"`)
              } else {
                console.log(`[scroll-restore] ${delay}ms firstVisible content: "${lines()[firstVisibleA?.index ?? -1]?.content?.replace(/<[^>]*>/g, '').slice(0, 60) ?? 'n/a'}"`)
              }
              if (auditTargetIndex === -1 && isCorrect) {
                auditTargetIndex = delay
              }
            }, delay)
          })

          requestAnimationFrame(() =>
            requestAnimationFrame(() => {
              if (cancelled) return
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
          scrollerEl.value?.focus({ preventScroll: true })
        })
      },
      { flush: 'post', immediate: true },
    )

    // If no target ever arrives (no saved position, no TOC nav), focus the scroller
    // once lines are loaded so keyboard navigation works immediately.
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

  function savePos() {
    if (programmaticScrolling) return
    const position = lastKnownPos ?? captureScrollPos()
    if (position) {
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
