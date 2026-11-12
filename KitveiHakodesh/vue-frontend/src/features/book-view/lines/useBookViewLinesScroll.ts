/**
 * Manages scroll position for the book view lines scroller (a WindowedScroller).
 *
 * Scroll save: captureScrollPos reads the first visible row and the pixel offset
 * into it straight from the DOM — the scroller mounts real rows, so there is no
 * estimated position to mistrust.
 *
 * Scroll restore: there is no restore STEP. A windowed scroller shows "the rows from
 * index N", and N is an argument of its creation — the saved line (or a requested
 * one) is simply the row the scroller is created with (`initialIndex` /
 * `initialOffsetPx`, passed by BookViewLinesContent). BookViewPage creates this pane
 * only once the saved position has been read, so the start row is final at setup.
 * Never add code here that opens at the top and moves to the saved line afterwards,
 * waits for a position to arrive, or guards a late move against reader input: that
 * is the virtualizer model this pane left behind. This composable only holds the
 * position save until the first window is on screen (then the scroller's isLanding()
 * holds it through its own landings) and runs what needs the rows in
 * the DOM (focus, the keyboard caret, the search mark, the deep-link flash).
 */
import { watch, onBeforeUnmount } from 'vue'
import type { Ref } from 'vue'
import { registerPositionCapture } from '@/webview-host/persistenceFlush'
import type { WindowedScrollerApi } from '@/components/windowedScrollerTypes'
import { SCROLL_LANDING_GAP_PX, SEARCH_BAR_INSET_PX } from './useBookViewLinesNavigation'
import { placeCaretAtViewportStart } from './lineCaret'
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
  /** Which split-view pane these lines are in: a background pane never takes focus on open. */
  paneId: 1 | 2
}

/** Matches the `.flash-open` animation in BookViewLinesContent.vue. */
const FLASH_DURATION_MS = 3600

// ── Composable ────────────────────────────────────────────────────────────────

export function useBookViewLinesScroll(
  scroller: () => WindowedScrollerApi | null,
  props: BookViewLinesScrollProps,
  storeRefs: BookViewLinesScrollStoreRefs,
  emit: (
    event: 'scrolled',
    firstVisible: number,
    firstFull: number,
    isUserScroll: boolean,
  ) => void,
) {
  const { tabStore, bookViewStore, autoSelectTopLine, zoom, tabId, bookId, paneId } = storeRefs

  // ── Scroll capture ──────────────────────────────────────────────────────────

  function captureScrollPos() {
    const first = scroller()?.readFirstVisible(0)
    if (!first) return null
    return {
      scrollIndex: first.index,
      scrollOffset: Math.max(0, first.offsetWithinRow),
    }
  }

  // ── Position-save hold ──────────────────────────────────────────────────────

  // The row this pane opens at: a requested line (TOC, search result, deep link) or
  // the saved one; undefined = the top. FINAL at setup — BookViewPage creates the
  // pane only once the saved position has been read, and the scroller is created
  // with this row as its start. Nothing arrives later, so nothing here waits.
  const startRow = props.initialLineIndex ?? props.initialScrollIndex
  // Until the first window is on screen there is no reader position to save: the
  // rows are not there yet. Opening at the top needs no hold - the top is the
  // reader's place from the start.
  let isAwaitingFirstWindow = startRow != null

  /**
   * Not a moment to persist the position, nor to take a scroll for the reader's: the
   * first window is not built yet, or one of the scroller's own landings (a jump, the
   * first window, a nudge to a mark) is under way or its scroll has not ended. The
   * scroller knows - see its isLanding().
   */
  function isSaveHeld(): boolean {
    return isAwaitingFirstWindow || (scroller()?.isLanding() ?? false)
  }

  // ── Once the first window is on screen ──────────────────────────────────────
  //
  // The scroller opened AT the start row (see WindowedScroller.vue, "The first
  // window") — this composable does not put it anywhere. What follows is only what
  // has to happen once the rows are in the DOM: focus, the caret, the search mark,
  // the deep-link flash.

  let openingFollowUpsCancelled = false
  let flashTimer: number | null = null

  /** Ends the opening's follow-ups: a jump elsewhere supersedes them, and an unmount must leave nothing behind. */
  function cancelOpeningFollowUps() {
    openingFollowUpsCancelled = true
    isAwaitingFirstWindow = false
    if (flashTimer != null) { clearTimeout(flashTimer); flashTimer = null }
  }

  /** The scroller's first window is on screen — wired to its `opened` event (emitted for the top too). */
  function onScrollerOpened() {
    if (openingFollowUpsCancelled) return
    // From here the scroller's own isLanding() holds the save through the first
    // window's scroll, until that scroll has ended.
    isAwaitingFirstWindow = false
    const element = scroller()?.scrollerElement
    // Never from a background pane in split view, nor from an input the reader is
    // typing in (an open search bar); otherwise the keys drive the reader at once.
    const isBackgroundPane = bookViewStore.splitViewEnabled && bookViewStore.focusedPaneId !== paneId
    const active = document.activeElement as HTMLElement | null
    const isTypingElsewhere = !!active && !element?.contains(active) &&
      (active.tagName === 'INPUT' || active.tagName === 'TEXTAREA' || active.isContentEditable)
    const takeFocus = !!element && !isBackgroundPane && !isTypingElsewhere
    if (takeFocus) element?.focus({ preventScroll: true })
    // Focus says which element the keys reach; it does not say where in the text they
    // start from. Caret browsing (F7) moves the DOCUMENT's caret, and without one in
    // the opened line the browser starts from the top of the mounted window — rows
    // above the start row — so the first arrow key left the line the reader was
    // given. At the top too: the browser's own starting point is only coincidentally
    // the first row. See lineCaret.ts.
    if (takeFocus) placeCaretAtViewportStart(element)
    if (startRow == null) return
    const target = startRow
    // FTS: the highlighted mark is in the rendered row — bring it into view.
    // Centred through the scroller's nudge: part of the opening landing, not the reader.
    const mark = props.searchHighlightLineIndex != null ? element?.querySelector('mark.search-match') : null
    if (element && mark) {
      const markRect = mark.getBoundingClientRect()
      const viewportRect = element.getBoundingClientRect()
      scroller()?.nudge(
        (markRect.top + markRect.height / 2) - (viewportRect.top + viewportRect.height / 2),
      )
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
  function savePos(ignoreHold = false): Promise<void> | undefined {
    // Mid-flight programmatic positions are transient — persisting one would
    // overwrite the reader's real place with wherever a jump happened to be
    // passing through.
    if (!ignoreHold && isSaveHeld()) return
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
    lastCaptureSuppressed = isSaveHeld()
    return savePos()
  }, tabId)

  onBeforeUnmount(() => {
    unregisterHostFlush()
    cancelOpeningFollowUps()
    // A view leaving while its tab stays open (tab switch, navigation within the
    // tab) has only this save. A closed tab's view unmounts after the tab has left
    // the list, and the close already captured this same position (disposeClosedTab
    // -> capturePositionsForTab): saving again would only repeat the last-read put
    // (the per-tab put the store drops anyway). The one exception is a close that
    // fell inside a suppressed window - that capture wrote nothing, so this is its
    // save, as it was before the close-time capture existed.
    // The view is leaving: whatever it shows last is where the reader is, so the
    // hold does not apply (as before, when the unmount released it).
    if (tabStore.isOpenTab(tabId) || lastCaptureSuppressed) savePos(true)
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
  // isSaveHeld() is a statement about saving, not about where the reader is: the
  // breadcrumb, the active TOC entry and auto-select track the CURRENT position and
  // are just as right after a programmatic jump as after a wheel.
  function onScroll() {
    const position = readCurrentPosition()
    if (!position) return
    const { lineIndex, fullLineIndex } = position
    lastKnownPos = captureScrollPos()
    // The third argument says whether the READER moved the view, as opposed to a
    // jump the app performed. The scroller draws exactly that line (isLanding: its
    // own landings and nudges until their scroll ends) — and unlike a `wheel`
    // listener it also catches scrollbar drags and keyboard scrolling.
    emit('scrolled', lineIndex, fullLineIndex, !isSaveHeld())
  }

  return {
    captureScrollPos,
    readCurrentPosition,
    cancelOpeningFollowUps,
    onScrollerOpened,
    isPositionSaveHeld: isSaveHeld,
    onScroll,
  }
}
