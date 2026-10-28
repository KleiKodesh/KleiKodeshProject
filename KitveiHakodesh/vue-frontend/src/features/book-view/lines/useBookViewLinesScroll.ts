/**
 * Manages scroll position for the book view lines scroller (a WindowedScroller).
 *
 * Scroll save: captureScrollPos reads the first visible row and the pixel offset
 * into it straight from the DOM — the scroller mounts real rows, so there is no
 * estimated position to mistrust.
 *
 * Scroll restore: the scroller itself opens on the saved line (its `initialIndex`
 * and `initialOffsetPx`, passed by BookViewLinesContent). It fetches the line's
 * chunk, mounts the window around it with its text, and sets the scroll from the
 * row's real position — one render, nothing to correct afterwards. This composable
 * only holds the position save through that landing and runs the follow-ups (focus,
 * the keyboard caret, the search mark, the deep-link flash) once the scroller reports
 * it is open.
 */
import { watch, nextTick, onBeforeUnmount } from 'vue'
import type { Ref } from 'vue'
import { registerPositionCapture } from '@/webview-host/persistenceFlush'
import type { WindowedScrollerApi } from '@/components/windowedScrollerTypes'
import type { LineItem } from './useBookViewLinesTable'
import { SCROLL_LANDING_GAP_PX, SEARCH_BAR_INSET_PX } from './useBookViewLinesNavigation'
import { placeCaretAtRowStart } from './lineCaret'
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
  // Held while the scroller opens on a saved or requested line: its landing's
  // scroll event must not be taken for the reader moving, nor persisted. A
  // requested line (TOC, deep link) is known at setup; a SAVED one arrives from
  // IndexedDB after it — read through a getter, and the hold starts when it lands.
  const openingTarget = () => props.initialLineIndex ?? props.initialScrollIndex
  // Also held until IndexedDB has answered: a flush in that gap would persist the
  // rows filled at the top as the reader's place, ahead of the saved one.
  let saveSuppressed = openingTarget() != null || props.idbResolved === false
  // A hold started for a late-arriving target lapses on its own: the scroller
  // declines an opening once the reader has moved, so `opened` may never come.
  const OPENING_HOLD_SAFETY_MS = 5000
  let openingSafetyTimer: ReturnType<typeof setTimeout> | null = null
  function clearOpeningSafety() {
    if (openingSafetyTimer != null) { clearTimeout(openingSafetyTimer); openingSafetyTimer = null }
  }

  function suppressPositionSave() {
    saveSuppressed = true
    if (saveSuppressTimer) clearTimeout(saveSuppressTimer)
    saveSuppressTimer = setTimeout(() => { saveSuppressed = false }, 300)
  }

  // ── Opening at the saved / requested line ───────────────────────────────────

  let restoreCancelled = false
  let flashTimer: number | null = null

  /** Ends the opening's follow-ups: a jump elsewhere supersedes them, and an unmount must leave nothing behind. */
  function cancelRestore() {
    restoreCancelled = true
    saveSuppressed = false
    clearOpeningSafety()
    if (flashTimer != null) { clearTimeout(flashTimer); flashTimer = null }
  }

  watch(openingTarget, (target) => {
    if (target == null || restoreCancelled) return
    saveSuppressed = true
    clearOpeningSafety()
    openingSafetyTimer = setTimeout(() => { openingSafetyTimer = null; saveSuppressed = false }, OPENING_HOLD_SAFETY_MS)
  })

  /** The scroller's opening landing is on screen — wired to its `opened` event. */
  function onScrollerOpened() {
    const target = openingTarget()
    if (restoreCancelled || target == null) return
    clearOpeningSafety()
    // The landing's scroll event is dispatched at the next frame's start, ahead of
    // its animation callbacks — so the hold covers it and is released just after.
    saveSuppressed = true
    requestAnimationFrame(() => { if (!restoreCancelled) saveSuppressed = false })
    const element = scroller()?.scrollerElement
    element?.focus({ preventScroll: true })
    // Focus says which element the keys reach; it does not say where in the text they
    // start from. Caret browsing (F7) moves the DOCUMENT's caret, and without one in
    // the opened line the browser starts from the top of the mounted window — rows
    // above the landing — so the first arrow key left the line the reader was given.
    // See lineCaret.ts.
    placeCaretAtRowStart(scroller()?.rowElement(target) ?? null)
    // FTS: the highlighted mark is in the rendered row — bring it into view.
    if (props.searchHighlightLineIndex != null) {
      element?.querySelector('mark.search-match')?.scrollIntoView({ block: 'center' })
    }
    // Deep-link open (otzaria:// / zayit://): momentarily flash the target line's
    // background so the user sees where the link landed. One-shot; the class is
    // removed imperatively after the animation.
    if (props.flashLineOnOpen) {
      const row = scroller()?.rowElement(target)?.querySelector('.line')
      if (row) {
        row.classList.add('flash-open')
        flashTimer = window.setTimeout(() => {
          row.classList.remove('flash-open')
          flashTimer = null
        }, FLASH_DURATION_MS)
      }
    }
  }

  // Opening at the top (nothing saved): focus once the book is in. Decided only
  // once IndexedDB has answered — before that "nothing saved" is not known yet.
  {
    const stop = watch(
      () => [lines().length, props.idbResolved, openingTarget()] as const,
      ([count, resolved, target]) => {
        if (!count || !resolved) return
        stop()
        if (target != null) return // the scroller's `opened` focuses after its landing
        saveSuppressed = false // nothing saved: the top is the reader's place
        nextTick(() => {
          const element = scroller()?.scrollerElement
          element?.focus({ preventScroll: true })
          // The caret belongs on the line the reader is looking at here too — the
          // browser's own starting point is only coincidentally the first row.
          placeCaretAtRowStart(scroller()?.rowElement(0) ?? null)
        })
      },
      { flush: 'post' },
    )
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
    cancelRestore()
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

  return {
    captureScrollPos,
    readCurrentPosition,
    cancelRestore,
    onScrollerOpened,
    isPositionSaveSuppressed: () => saveSuppressed,
    suppressPositionSave,
    onScroll,
  }
}
