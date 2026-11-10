import { computed, ref, watch } from 'vue'
import { setCurrentMark } from '../lines/useBookViewLineRenderer'
import type { WindowedScrollerApi } from '@/components/windowedScrollerTypes'

/**
 * Where a group's header sits below the viewport's top, and where the sticky header
 * hands over: the nav bar's height plus a sliver of the previous row, so a group
 * opens with its header just under the nav rather than flush against it.
 */
const NAV_HEIGHT = 32
const LANDING_GAP_PX = 8

/** A row and how far into it — what a WindowedScroller is created at, or told to go to. */
export interface CommentaryRow { index: number; offsetPx: number }

/** Something specific the reader asked to be shown (a toolbar pick, a search hit, section nav). */
type Request =
  | { kind: 'group'; bookId: number; sectionLabel?: string; subSectionLabel?: string }
  | { kind: 'flatIndex'; flatIndex: number; occurrence: number }

/**
 * Where a commentary panel is and where it opens, over a WindowedScroller.
 *
 * THE MODEL (see WindowedScroller.vue): the scroller shows "the rows from index N",
 * and N is an argument of its creation. Every loaded commentary list gets a NEW
 * scroller (CommentaryView drops it while loading), created AT the row this list
 * should open on — `startRow` below decides it, once, from what is known when the
 * list is ready: something the reader just asked for, the place they were (the same
 * line's list coming back), the pinned commentator, or the top.
 *
 * So there is NO positioning step after a list appears: no goal that waits for its
 * list, no restore that runs after mount, no mask over a list that opened in the
 * wrong place, no retry when something arrives late, and nothing that reader input
 * could cancel. All of those existed here while the list was a virtualizer that
 * could only open at its top and be scrolled afterwards. Do not bring them back: if
 * a list opens in the wrong place, the fix is in what `startRow` is given, or in
 * WHEN CommentaryView creates the scroller (it waits for the pin to be settled).
 *
 * What remains after creation is plain navigation on a live scroller — the reader
 * picks a commentator or steps to a search hit — and that is one `scrollToIndex`.
 */
export function useCommentaryScroll(
  flatItems: () => any[],
  /** Flat index of every group header, ascending — what every header lookup walks instead of the rows. */
  headerIndexes: () => number[],
  scroller: () => WindowedScrollerApi | null,
) {
  const scrollTop = ref(0)

  // ── Where the panel is ──────────────────────────────────────────────────────

  /** Position in `headers` of the last header at or before `index`, or -1. */
  function lastHeaderAtOrBefore(headers: number[], index: number): number {
    let low = 0
    let high = headers.length - 1
    let found = -1
    while (low <= high) {
      const mid = (low + high) >> 1
      if (headers[mid]! <= index) { found = mid; low = mid + 1 } else high = mid - 1
    }
    return found
  }

  /**
   * The header whose group the viewport's top is in: the last header that has reached
   * the top of the viewport — its top at or above the gap a group's header is given
   * below the nav, so a group just opened on is the active one. Mounted headers are
   * measured; a header above the mounted window is the last one before it in the list.
   */
  const stickyHeader = computed(() => {
    const api = scroller()
    const items = flatItems()
    const headers = headerIndexes()
    const top = scrollTop.value + LANDING_GAP_PX + 1
    if (!api) return null
    const { start, end } = api.renderedRange
    let active: any = null
    let h = lastHeaderAtOrBefore(headers, start - 1)
    if (h >= 0) active = items[headers[h]!]
    for (h = h + 1; h < headers.length && headers[h]! < end; h++) {
      const index = headers[h]!
      const row = api.rowElement(index)
      if (!row) continue
      if (row.offsetTop <= top) active = items[index]
      else break
    }
    return active
  })

  const activeHeader = computed(() => {
    if (stickyHeader.value) return stickyHeader.value
    const first = headerIndexes()[0]
    return first == null ? null : (flatItems()[first] as any) ?? null
  })

  const activePinnedGroup = computed<any>(() => {
    const header = activeHeader.value
    if (!header) return null
    return {
      bookId: header.bookId,
      sectionLabel: header.sectionLabel ?? '',
      subSectionLabel: header.subSectionLabel ?? '',
    }
  })

  const topVisibleFlatIndex = computed(() => {
    void scrollTop.value
    const api = scroller()
    if (!api) return 0
    void api.renderedRange
    return api.readFirstVisible(NAV_HEIGHT)?.index ?? 0
  })

  // ── What the reader chose ───────────────────────────────────────────────────

  /**
   * The commentator the reader picked BY NAME from the header toolbar (the
   * next/previous buttons and the search input, which reach scrollToGroup with
   * reason 'header-nav-picker').
   *
   * Recorded as the CHOICE ITSELF: a pick names its book, so it needs no derivation
   * from the sticky header and is correct the instant it happens. Consumed by
   * consumeExplicitPick when the pin capture is APPLIED, and dropped by a later
   * scroll gesture (see markUserAdjusted).
   */
  let explicitPick: { bookId: number; sectionLabel: string; subSectionLabel: string } | null = null

  /**
   * True once the reader has moved this panel themselves (wheel, touch, pointer, a
   * scrolling key) since the app last put it somewhere at their request. It is what
   * makes the sticky header THEIR arrangement, and so a commentator worth pinning.
   * It does not gate, delay or cancel anything.
   */
  let userAdjusted = false
  function markUserAdjusted() {
    userAdjusted = true
    // A scroll gesture supersedes an earlier toolbar pick: pick Rashi, then scroll
    // to another commentator without switching lines, and the sticky header is now
    // the reader's latest word.
    explicitPick = null
  }

  /**
   * The active group AS A PIN-CAPTURE SOURCE, which is stricter than the display
   * version above: it answers null unless the reader has actually curated the
   * view. activeHeader falls back to the FIRST header when no scroller is showing
   * rows — a list that has just been replaced and whose scroller is not open yet —
   * and a click in that moment must not capture the first group as "what the reader
   * was looking at". A DERIVED active group is not a preference; only one the reader
   * made is. Callers fall back to the held pin on null.
   *
   * A FUNCTION, deliberately not a computed: it reads plain variables, and it
   * answers "what is true at this instant", asked imperatively at click time.
   *
   * READ-ONLY: the pick is consumed by consumeExplicitPick, called when the
   * snapshot is actually APPLIED to the pins. Capture is not consumption — the
   * scroll-sync capture may never be applied.
   */
  function activePinnedGroupForCapture(): any {
    const picked =
      explicitPick ?? (isOpen && navigationsInFlight === 0 && userAdjusted ? activePinnedGroup.value : null)
    return picked ? { ...picked, chosen: true } : null
  }

  /** Drop the pick: a captured snapshot has been applied to the pins. */
  function consumeExplicitPick() {
    explicitPick = null
  }

  function onScroll(emitScroll: (scrollIndex: number, scrollOffset: number) => void) {
    scrollTop.value = scroller()?.scrollerElement?.scrollTop ?? 0
    const pos = captureScrollPos()
    if (pos) emitScroll(pos.scrollIndex, pos.scrollOffset)
  }

  function captureScrollPos(): { scrollIndex: number; scrollOffset: number } | null {
    const first = scroller()?.readFirstVisible(0)
    if (!first) return null
    return { scrollIndex: first.index, scrollOffset: Math.max(0, first.offsetWithinRow) }
  }

  // ── Rows ────────────────────────────────────────────────────────────────────

  function resolveGroupIndex(bookId: number, sectionLabel?: string, subSectionLabel?: string): number {
    const items = flatItems()
    const headers = headerIndexes()
    const findHeader = (matches: (item: any) => boolean): number => {
      for (const index of headers) if (matches(items[index])) return index
      return -1
    }
    const exact = findHeader(
      (item) =>
        item?.bookId === bookId &&
        (sectionLabel == null || item.sectionLabel === sectionLabel) &&
        (subSectionLabel == null || item.subSectionLabel === subSectionLabel),
    )
    if (exact !== -1 || (sectionLabel == null && subSectionLabel == null)) return exact
    // The labels only disambiguate a book that appears in several sections; the
    // reader asked for a COMMENTATOR. A pin carries the labels captured on the
    // PREVIOUS line, and the same book can sit under a different section there.
    // Fall back to book identity.
    return findHeader((item) => item?.bookId === bookId)
  }

  /** The row a request names in the CURRENT list, or null when it is not in it. */
  function rowOf(request: Request): CommentaryRow | null {
    if (request.kind === 'flatIndex') {
      return request.flatIndex < flatItems().length
        ? { index: request.flatIndex, offsetPx: -(NAV_HEIGHT + LANDING_GAP_PX) }
        : null
    }
    const index = resolveGroupIndex(request.bookId, request.sectionLabel, request.subSectionLabel)
    return index < 0 ? null : { index, offsetPx: -LANDING_GAP_PX }
  }

  // ── Where a new scroller is created ─────────────────────────────────────────

  /**
   * Something the reader asked for while no scroller could take it: the list was
   * loading, or its scroller's first window was still being built. The next scroller
   * is created there (startRow), and its follow-up runs once the rows are in the DOM
   * (scrollerOpened). One slot — the latest request is the one that counts.
   */
  let requestForNextScroller: Request | null = null

  /**
   * The row a NEW scroller is created at, for the list as it is now. Called once per
   * scroller, by CommentaryView, when the list is ready. In order:
   *   1. what the reader asked for while there was no scroller to show it;
   *   2. `samePlace` — the place this panel was at, when the caller says the list
   *      coming up is the one that was there (a remount, a reopen, the same line
   *      loading again). An index past the end of a list that has since shortened
   *      opens on the last row;
   *   3. the pinned commentator's header;
   *   4. the top.
   */
  function startRow(
    samePlace: { scrollIndex: number; scrollOffset: number } | null,
    pinned: { bookId: number; sectionLabel?: string; subSectionLabel?: string } | null | undefined,
  ): CommentaryRow {
    const count = flatItems().length
    const requested = requestForNextScroller ? rowOf(requestForNextScroller) : null
    if (requested) return requested
    requestForNextScroller = null // it names nothing in this list
    if (samePlace && count > 0) {
      const index = Math.min(samePlace.scrollIndex, count - 1)
      return { index, offsetPx: index === samePlace.scrollIndex ? samePlace.scrollOffset : 0 }
    }
    if (pinned) {
      const index = resolveGroupIndex(pinned.bookId, pinned.sectionLabel, pinned.subSectionLabel)
      if (index >= 0) return { index, offsetPx: -LANDING_GAP_PX }
    }
    return { index: 0, offsetPx: 0 }
  }

  /** The scroller's first window is on screen (its `opened` event): rows can be measured and navigated. */
  let isOpen = false
  function scrollerOpened() {
    isOpen = true
    // A new list, opened where the app decided: its sticky header is not the reader's
    // arrangement until they move the panel again. Whatever they had arranged in the
    // previous list was captured into the pin at the click that replaced it. Left
    // set, the next line click captured this list's derived header as their choice —
    // wrong whenever the list could not put the pinned header at the very top (it is
    // the last group), and the panel drifted to the commentator above it.
    userAdjusted = false
    scrollTop.value = scroller()?.scrollerElement?.scrollTop ?? 0
    const request = requestForNextScroller
    requestForNextScroller = null
    // The scroller was created at this request's row; going there again moves
    // nothing and runs the request's follow-up (the search mark).
    if (request) void goTo(request)
  }
  // The scroller went with its list: nothing is on screen until the next one opens.
  watch(scroller, (api) => { if (!api) isOpen = false }, { flush: 'sync' })

  // ── Navigation on a live scroller ───────────────────────────────────────────

  let navigationsInFlight = 0
  /** Bumped by every navigation: a follow-up belongs to the latest one only. */
  let navigationSeq = 0

  /**
   * Shows what the reader asked for. On an open scroller it goes there now; with no
   * scroller, or one still building its first window, the request becomes where the
   * next scroller is created (see requestForNextScroller). False when the list is
   * showing and does not contain it.
   */
  async function goTo(request: Request): Promise<boolean> {
    // The reader put the panel here: what the sticky header says next is the app
    // carrying out their request, not their own scrolling.
    userAdjusted = false
    const api = scroller()
    if (!api || !isOpen) {
      requestForNextScroller = request
      return true
    }
    const row = rowOf(request)
    if (!row) return false
    const seq = ++navigationSeq
    navigationsInFlight++
    try {
      await api.scrollToIndex(row.index, { offsetPx: row.offsetPx })
    } finally {
      navigationsInFlight--
    }
    if (seq !== navigationSeq) return true
    const element = api.scrollerElement
    if (request.kind === 'flatIndex' && element) {
      // The row is mounted with its text, marks included. Bring the current one
      // into view when the row's own position left it off screen.
      setCurrentMark(element, request.flatIndex, request.occurrence)
      const mark = element.querySelector('mark.search-match.current') as HTMLElement | null
      if (mark) {
        const markRect = mark.getBoundingClientRect()
        const scrollerRect = element.getBoundingClientRect()
        const relativeTop = markRect.top - scrollerRect.top
        const relativeBottom = markRect.bottom - scrollerRect.top
        const visible = relativeTop >= NAV_HEIGHT + 4 && relativeBottom <= scrollerRect.height - 4
        if (!visible) element.scrollTop += relativeTop - NAV_HEIGHT - LANDING_GAP_PX
      }
    }
    scrollTop.value = element?.scrollTop ?? 0
    return true
  }

  /**
   * The reader wants this commentator shown: a toolbar pick, the filter tree, section
   * navigation. (A pinned commentator following a line change is NOT a call to this —
   * the new list's scroller is simply created at the pin.)
   */
  function scrollToGroup(
    bookId: number,
    sectionLabel?: string,
    subSectionLabel?: string,
    reason = 'unknown',
  ): void {
    // A pick from the header toolbar IS the reader's commentator choice - record
    // it at once, so a line switch right after it still captures it.
    if (reason === 'header-nav-picker') {
      explicitPick = {
        bookId,
        sectionLabel: sectionLabel ?? '',
        subSectionLabel: subSectionLabel ?? '',
      }
    }
    void goTo({ kind: 'group', bookId, sectionLabel, subSectionLabel })
  }

  function scrollToFlatIndex(flatIndex: number, occurrence = 0) {
    void goTo({ kind: 'flatIndex', flatIndex, occurrence })
  }

  return {
    scrollTop,
    activeHeader,
    activePinnedGroup,
    activePinnedGroupForCapture,
    consumeExplicitPick,
    markUserAdjusted,
    startRow,
    scrollerOpened,
    onScroll,
    scrollToGroup,
    scrollToFlatIndex,
    captureScrollPos,
    topVisibleFlatIndex,
  }
}
