/**
 * Manages scroll position for the book view lines scroller (a WindowedScroller).
 *
 * Scroll save: captureScrollPos reads the first visible row and the pixel offset
 * into it straight from the DOM — the scroller mounts real rows, so there is no
 * estimated position to mistrust.
 *
 * Scroll restore: one scrollToIndex(target, saved offset). The scroller fetches the
 * target's lines, mounts the window around it with their text, and sets the scroll
 * from the row's real position — one render, nothing to correct afterwards.
 */
import { watch, nextTick, onBeforeUnmount } from 'vue'
import type { Ref } from 'vue'
import { registerPositionCapture } from '@/webview-host/persistenceFlush'
import type { WindowedScrollerApi } from '@/components/windowedScrollerTypes'
import type { LineItem } from './useBookViewLinesTable'
import { SCROLL_LANDING_GAP_PX } from '@/utils/scrollToIndexWithRetry'
import { SEARCH_BAR_INSET_PX } from './useBookViewLinesNavigation'
import { COMMENTARY_SLOTS } from '../bookViewTypes'
import type {
  CommentaryPanelLiveStates,
  CommentaryPanelPersistStates,
  CommentaryTreeState,
  CommentaryTreeStatePersist,
  SideEditionPersistState,
  TocPersistState,
} from '../bookViewTypes'
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
  /** True when EITHER commentary panel is open. */
  commentaryVisible?: boolean
  /**
   * Both commentary panels' persistable state, read at save time.
   *
   * A getter rather than eight pass-through props: this composable owns none of it
   * and only ferries it to IDB alongside the scroll position it does own. The
   * panels themselves own it - see useCommentaryPanelSlot.
   */
  commentaryPersistState?: () => CommentaryPanelLiveStates
  /** The TOC panel's state, read at save time like the commentary panels'. */
  tocPersistState?: () => TocPersistState
  /**
   * The alternate version the text is being read in, by versionTitle — read at save
   * time like the panels above. null = the book's merged text.
   */
  versionPersistState?: () => string | null
  /** The side-by-side HebrewBooks edition and its column width, read at save time. */
  sideEditionPersistState?: () => SideEditionPersistState
  /**
   * The alternate version being rendered, or null for the merged text. Part of the
   * line renderer's cache key — a swap changes a line's text while its index and id
   * stay the same, which the cache would otherwise read as "nothing to re-render".
   */
  activeVersionId?: number | null
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

/** Frames allowed for a freshly mounted row to paint before it is looked for. */
const ROW_POLL_FRAMES = 10
/** Matches the `.flash-open` animation in BookViewLinesContent.vue. */
const FLASH_DURATION_MS = 3600

// ── Composable ────────────────────────────────────────────────────────────────

export function useBookViewLinesScroll(
  scroller: () => WindowedScrollerApi | null,
  lines: () => LineItem[],
  props: BookViewLinesScrollProps,
  storeRefs: BookViewLinesScrollStoreRefs,
  emit: (
    event: 'scrolled',
    firstVisible: number,
    firstFull: number,
    isUserScroll: boolean,
  ) => void,
) {
  const { tabStore, bookViewStore, autoSelectTopLine, zoom, tabId, bookId } = storeRefs

  // ── Scroll capture ──────────────────────────────────────────────────────────

  function captureScrollPos() {
    const first = scroller()?.readFirstVisible(0)
    if (!first) return null
    return {
      scrollIndex: first.index,
      scrollOffset: Math.max(0, first.offsetWithinRow),
    }
  }

  // ── Position-save suppression ───────────────────────────────────────────────

  let saveSuppressTimer: ReturnType<typeof setTimeout> | null = null
  let saveSuppressed = false

  function suppressPositionSave() {
    saveSuppressed = true
    if (saveSuppressTimer) clearTimeout(saveSuppressTimer)
    saveSuppressTimer = setTimeout(() => { saveSuppressed = false }, 300)
  }

  // ── Initial scroll on load ──────────────────────────────────────────────────

  // Ends a restore in flight (and its flash / mark follow-ups): a jump elsewhere
  // supersedes it, and an unmount must leave nothing behind.
  let cancelRestore: (() => void) | null = null
  let scrollTeardown = false
  {
    let restored = false
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
        const target: number = targetIndex

        nextTick(() => {
          if (restored || scrollTeardown) return
          restored = true
          void restoreTo(target)
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
          nextTick(() => scroller()?.scrollerElement?.focus({ preventScroll: true }))
        }
      },
      { flush: 'post' },
    )
  }

  async function restoreTo(target: number) {
    let cancelled = false
    let flashTimer: number | null = null
    cancelRestore = () => {
      cancelled = true
      saveSuppressed = false
      if (flashTimer != null) { clearTimeout(flashTimer); flashTimer = null }
    }
    // A saved offset belongs to a session-restore position only — a TOC/deep-link
    // target lands at the line's top.
    const savedOffset = props.initialScrollIndex != null ? (props.initialScrollOffset ?? 0) : 0

    saveSuppressed = true
    await scroller()?.scrollToIndex(target, { offsetPx: savedOffset })
    if (cancelled) return
    saveSuppressed = false
    scroller()?.scrollerElement?.focus({ preventScroll: true })
    scrollToSearchMark()
    flashTargetLine(target)

    // FTS: bring the highlighted mark into view once positioned.
    function scrollToSearchMark() {
      if (props.searchHighlightLineIndex == null) return
      let attempts = 0
      function tryScrollToMark() {
        const element = scroller()?.scrollerElement
        if (cancelled || !element) return
        const mark = element.querySelector('mark.search-match') as HTMLElement | null
        if (mark) { mark.scrollIntoView({ block: 'center' }); return }
        if (++attempts < ROW_POLL_FRAMES) requestAnimationFrame(tryScrollToMark)
      }
      requestAnimationFrame(tryScrollToMark)
    }

    // Deep-link open (otzaria:// / zayit://): momentarily flash the target line's
    // background so the user sees where the link landed. One-shot; the class is
    // removed imperatively after the animation.
    function flashTargetLine(lineIndex: number) {
      if (!props.flashLineOnOpen) return
      let attempts = 0
      function tryFlashLine() {
        if (cancelled) return
        const row = scroller()?.rowElement(lineIndex)?.querySelector('.line') as HTMLElement | null
        if (row) {
          row.classList.add('flash-open')
          flashTimer = window.setTimeout(() => {
            row.classList.remove('flash-open')
            flashTimer = null
          }, FLASH_DURATION_MS)
          return
        }
        if (++attempts < ROW_POLL_FRAMES) requestAnimationFrame(tryFlashLine)
      }
      requestAnimationFrame(tryFlashLine)
    }
  }

  // ── Persist scroll position ─────────────────────────────────────────────────

  let lastKnownPos: { scrollIndex: number; scrollOffset: number } | null = null

  /** Copy for storage: the reader's search input only — see CommentaryTreeStatePersist. */
  function cloneFilterState(state: CommentaryTreeState): CommentaryTreeStatePersist {
    return {
      searchQuery: state.searchQuery,
      tokens: [...state.tokens],
    }
  }

  /** Every panel's state for this save. */
  function commentaryPanelsForSave(): CommentaryPanelPersistStates {
    const live = props.commentaryPersistState?.() ?? {}
    const result: CommentaryPanelPersistStates = {}
    for (const slot of COMMENTARY_SLOTS) {
      const panel = live[slot]
      if (!panel) continue
      result[slot] = {
        ...panel,
        filterState: panel.filterState ? cloneFilterState(panel.filterState) : undefined,
      }
    }
    return result
  }

  // Returns the last-read put so a host flush (persistenceFlush.ts) can wait for it;
  // every other caller is fire-and-forget. The per-tab state put is the same kind of
  // write to the same driver, so one of the two is enough to wait on.
  function savePos(): Promise<void> | undefined {
    // Mid-flight programmatic positions are transient — persisting one would
    // overwrite the reader's real place with wherever a jump happened to be
    // passing through.
    if (saveSuppressed) return
    const position = lastKnownPos ?? captureScrollPos()
    if (!position) return

    const commentaryPanels = commentaryPanelsForSave()
    const toc = props.tocPersistState?.()
    const versionTitle = props.versionPersistState?.() ?? null
    const sideEdition = props.sideEditionPersistState?.()

    tabStore.setBookViewState(tabId, bookId, {
      ...position,
      selectedLineId: props.selectedLineId,
      zoom: zoom.value,
      autoSelectTopLine: autoSelectTopLine.value,
      commentaryPanels,
      toc,
      versionTitle,
      sideEdition,
    })
    return tabStore.setLastReadPos(bookId, {
      ...position,
      selectedLineId: props.selectedLineId,
      // Zoom rides along here too, so reopening a book in a new tab restores the
      // text zoom and not just the commentary panels'.
      zoom: zoom.value,
      commentaryPanels,
      toc,
      versionTitle,
      sideEdition,
    })
  }

  watch(
    () => props.commentaryVisible,
    (visible) => { if (!visible) savePos() },
  )

  // Hidden / reload / close in the browser, window.__khsFlushPersistence in the
  // WebView2 hosts (which fire none of those), and the tab's own close (by tab id)
  // all take the position through this one registration - see persistenceFlush.ts
  // for why the position must be captured BEFORE the coalesced stores flush.
  let lastCaptureSuppressed = false
  const unregisterHostFlush = registerPositionCapture(() => {
    lastCaptureSuppressed = saveSuppressed
    return savePos()
  }, tabId)

  onBeforeUnmount(() => {
    unregisterHostFlush()
    scrollTeardown = true
    cancelRestore?.()
    saveSuppressed = false
    if (saveSuppressTimer) {
      clearTimeout(saveSuppressTimer)
      saveSuppressTimer = null
    }
    // A view leaving while its tab stays open (tab switch, navigation within the
    // tab) has only this save. A closed tab's view unmounts after the tab has left
    // the list, and the close already captured this same position (disposeClosedTab
    // -> capturePositionsForTab): saving again would only repeat the last-read put
    // (the per-tab put the store drops anyway). The one exception is a close that
    // fell inside a suppressed window - that capture wrote nothing, so this is its
    // save, as it was before the close-time capture existed.
    if (tabStore.isOpenTab(tabId) || lastCaptureSuppressed) savePos()
  })

  // ── Scroll handler ──────────────────────────────────────────────────────────

  /**
   * Which line is at the top of the view RIGHT NOW: the first line meaningfully
   * visible below the top — below the search bar when it overlays the scroller, and
   * past a sliver allowance slightly larger than the landing gap.
   *
   * The sliver allowance: a programmatic landing deliberately stops
   * SCROLL_LANDING_GAP_PX above its target, leaving that many pixels of the PREVIOUS
   * line at the top. Counting that sliver would report the previous line, whose TOC
   * entry is the previous section — a click on "daf 142a" answered by a sync
   * announcing "daf 141b". For reader scrolling the allowance is imperceptible.
   *
   * Always current: the scroller mounts the rows around a jump BEFORE setting the
   * scroll, so a scroll event never describes a stale window.
   */
  function readCurrentPosition(): { lineIndex: number; fullLineIndex: number } | null {
    const api = scroller()
    if (!api) return null
    const inset = props.searchBarVisible ? SEARCH_BAR_INSET_PX : 0
    const firstVisible = api.readFirstVisible(inset + SCROLL_LANDING_GAP_PX + 4) ?? api.readFirstVisible(0)
    if (!firstVisible) return null
    // Fully visible means fully visible BELOW the search bar too — a short line
    // hidden entirely under the bar is not something auto-select should act on.
    const firstFull = api.readFirstVisible(inset, true) ?? firstVisible
    return { lineIndex: firstVisible.index, fullLineIndex: firstFull.index }
  }

  // Reports the scroller's position on EVERY scroll, programmatic ones included.
  // saveSuppressed is a statement about saving, not about where the reader is: the
  // breadcrumb, the active TOC entry and auto-select track the CURRENT position and
  // are just as right after a programmatic jump as after a wheel.
  function onScroll() {
    const position = readCurrentPosition()
    if (!position) return
    const { lineIndex, fullLineIndex } = position
    lastKnownPos = captureScrollPos()
    // The third argument says whether the READER moved the view, as opposed to a
    // jump the app performed. saveSuppressed already draws exactly that line and
    // every programmatic scroller sets it — and unlike a `wheel` listener it also
    // catches scrollbar drags and keyboard scrolling.
    emit('scrolled', lineIndex, fullLineIndex, !saveSuppressed)
  }

  // ── Public API ──────────────────────────────────────────────────────────────

  /** Ends a restore in flight: a reader who has jumped elsewhere must not be landed back. */
  function cancelRestoreCorrection() {
    cancelRestore?.()
    cancelRestore = null
  }

  return {
    captureScrollPos,
    readCurrentPosition,
    cancelRestoreCorrection,
    isPositionSaveSuppressed: () => saveSuppressed,
    suppressPositionSave,
    onScroll,
  }
}
