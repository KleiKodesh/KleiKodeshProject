import { computed, ref, watch, onScopeDispose } from 'vue'
import { setCurrentMark } from '../lines/useBookViewLineRenderer'
import { commentaryGroupKey } from './useCommentary'
import { commentaryScrollTrace as trace } from '@/utils/commentaryScrollTrace'
import type { WindowedScrollerApi } from '@/components/windowedScrollerTypes'

/**
 * Where a landing puts its target below the viewport's top, and where the sticky
 * header hands over: the nav bar's height plus a sliver of the previous row, so a
 * group opens with its header just under the nav rather than flush against it.
 */
const NAV_HEIGHT = 32
const LANDING_GAP_PX = 8

/**
 * What the panel is currently trying to show. Exactly ONE goal exists at a time:
 * every path that positions the panel REQUESTS a goal, and the goal is carried out
 * by one landing of the WindowedScroller — rows fetched first, mounted around the
 * target, scroll set from the real row. There is nothing to correct afterwards, so
 * there is no loop, no settle window and no safety valve: a goal either lands,
 * waits for the list it needs (a group that has not arrived yet, a scroller that
 * has not mounted), or is superseded.
 */
type Goal =
  | { kind: 'restore-intent' }
  | { kind: 'restore'; scrollIndex: number; scrollOffset: number; resolve: (applied: boolean) => void; resolved: boolean; applied: boolean }
  | { kind: 'group'; bookId: number; sectionLabel?: string; subSectionLabel?: string; reason: string }
  | { kind: 'flatIndex'; flatIndex: number; occurrence: number }

/** Requests from these scrollToGroup reasons follow state around (pin-follow); all
 * other reasons are direct user actions and take priority. */
const AUTO_REASONS = new Set([
  'groups-reload',
  'pin-arrived-late',
  // The same owed pin scroll as 'pin-arrived-late', settled by a different
  // precondition landing last (see settlePinDebt). All of them are pin-follow and
  // must never displace a restore.
  'pin-owed-load-done',
  'pin-owed-groups-arrived',
  'panel-mounted',
  'already-restored',
])

/**
 * Manages scroll behavior for commentary: sticky header tracking, scroll position
 * capture/restore, and scroll-to-group navigation, over a WindowedScroller.
 */
export function useCommentaryScroll(
  flatItems: () => any[],
  visibleGroups: () => any[],
  /** Flat index of every group header, ascending — what every header lookup walks instead of the rows. */
  headerIndexes: () => number[],
  scroller: () => WindowedScrollerApi | null,
  /**
   * Which commentary panel this instance drives (see CommentarySlot). Only used to
   * tag trace flows: panels scroll concurrently, and with a shared flow name their
   * BEGIN calls reset each other's relative clock, making a dump unreadable.
   */
  traceSlot = 'panel',
) {
  const scrollTop = ref(0)
  const FLOW_SCROLL = `scrollToGroup:${traceSlot}`
  const FLOW_RESTORE = `restore:${traceSlot}`

  // ── Where the panel is ──────────────────────────────────────────────────────

  /**
   * The header whose group the viewport's top is in: the last header that has reached
   * the top of the viewport — its top at or above the landing gap a group landing
   * leaves above it, so a group just landed on is the active one. Mounted headers are
   * measured; a header above the mounted window is the last one before it in the list.
   */
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

  /**
   * The commentator the reader picked BY NAME from the header toolbar (the
   * next/previous buttons and the search input, all of which land in
   * scrollToGroup with reason 'header-nav-picker').
   *
   * `userAdjusted` cannot answer for these. It records scroll GESTURES on the
   * scroller, and a toolbar pick never touches the scroller - the reader clicks
   * chrome. Worse, the pick installs a user goal, and a user goal clears
   * `userAdjusted`: the toolbar said "go to Rashi" and in the same breath erased
   * the record that anyone had chosen Rashi. Nothing set it back, so the next
   * line switch captured null and fell back to the held pin - the same snap-back
   * as the scroll case, through a completely different door.
   *
   * Recorded as the CHOICE ITSELF rather than as another "the reader did
   * something" flag. A pick names its book, so it needs no derivation from the
   * sticky header and no waiting for the goal to land - it is correct the
   * instant it happens, even if the goal is still in flight or never arrives.
   * Consumed by consumeExplicitPick when the pin capture is APPLIED, and dropped
   * by a later scroll gesture (see markUserAdjusted).
   */
  let explicitPick: { bookId: number; sectionLabel: string; subSectionLabel: string } | null = null

  /**
   * True once the reader has manually moved this panel (wheel/touch/pointer/key)
   * since the last programmatic positioning. Reset whenever a USER goal is
   * installed: such a goal repositions the panel, so whatever the sticky header
   * says afterwards is the POSITIONER's doing, not the reader's.
   */
  let userAdjusted = false
  function markUserAdjusted() {
    userAdjusted = true
    // A scroll gesture supersedes an earlier toolbar pick: pick Rashi, then scroll
    // to another commentator without switching lines, and the sticky header is now
    // the reader's latest word. Without this the stale pick would outrank it and
    // the switch would land back on Rashi.
    explicitPick = null
    // The reader took the panel: a goal still waiting (for its group to arrive, for
    // its rows to be fetched) must not land on top of where they went.
    if (goal) endGoal('cancelled_by_user')
  }

  /**
   * The active group AS A PIN-CAPTURE SOURCE, which is stricter than the display
   * version above: it answers null unless the reader has actually curated the
   * view. activeHeader falls back to the FIRST header when nothing has scrolled
   * under the nav yet - which is exactly a panel's state mid-transit (list
   * swapped, goal not applied). A click landing in that window used to capture
   * the first group as "what the reader was looking at" and staged it as the pin,
   * permanently switching the panel to a commentator the reader never chose.
   * The doc's rule generalizes: a DERIVED active group is not a preference -
   * only one the reader made. Callers fall back to the held pin on null.
   *
   * A FUNCTION, deliberately not a computed. `goal` and `userAdjusted` are plain
   * variables, so a computed reading them would track no reactive dependency on
   * the guard path and Vue would cache that first `null` for the life of the
   * panel - the capture would never work again. It is also the right shape: this
   * answers "what is true at this instant", asked imperatively at click time.
   */
  function activePinnedGroupForCapture(): any {
    // A pick by name outranks anything derived from the viewport, and is valid
    // even mid-goal: the reader named this book, so there is nothing to infer and
    // nothing to wait for. Checked before the goal guard for exactly that reason -
    // a toolbar pick is normally still landing when the next click arrives.
    //
    // READ-ONLY: the pick is consumed by consumeExplicitPick, called from
    // applyPendingPins when the snapshot is actually APPLIED to the pins. Capture
    // is not consumption: captureActivePins runs from three places, and the
    // scroll-sync one captures a snapshot 120ms BEFORE it applies it - and may
    // never apply it, because a newer scroll clears the timer.
    const picked = explicitPick ?? (goal === null && userAdjusted ? activePinnedGroup.value : null)
    return picked ? { ...picked, chosen: true } : null
  }

  /**
   * Drop the pick, called when a captured snapshot is actually applied to the
   * pins. Separate from reading it so that a capture which is discarded (the
   * auto-select timer being superseded by a newer scroll) leaves the reader's
   * choice intact for the switch that really happens.
   */
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

  // ── The goal ────────────────────────────────────────────────────────────────

  let goal: Goal | null = null
  /**
   * True from the moment a goal is installed until it lands. The panel masks its
   * content while this is true, so the reader never sees the list's top before a
   * restore or a pin-follow brings their place in.
   */
  const isPositioning = ref(false)
  /** Bumped on every goal change; a landing that resolves for a stale goal is dropped. */
  let goalSeq = 0

  function flowFor(g: Goal): string {
    return g.kind === 'restore' || g.kind === 'restore-intent' ? FLOW_RESTORE : FLOW_SCROLL
  }

  function endGoal(status: string) {
    if (!goal) return
    trace.push(flowFor(goal), `goal_${status}`, { kind: goal.kind })
    if (goal.kind === 'restore' && !goal.resolved) { goal.resolved = true; goal.resolve(goal.applied) }
    goal = null
    goalSeq++
    isPositioning.value = false
  }

  /**
   * Install `g` as the panel's goal, replacing the current one under these rules:
   *  - a USER request always wins (the reader acted; follow them);
   *  - a restore always wins (it re-establishes the reader's own saved place);
   *  - an AUTO request (pin-follow after a reload, panel-mount) must NOT displace a
   *    restore or a claimed restore intent - that displacement was the old
   *    nondeterministic "lands on the pinned group instead of my place" race.
   * Returns whether the goal was accepted, so auto callers can keep their debt.
   */
  function setGoal(g: Goal, source: 'user' | 'auto'): boolean {
    if (goal && source === 'auto' && (goal.kind === 'restore' || goal.kind === 'restore-intent')) {
      trace.push(flowFor(g), 'goal_blocked_by_restore', { kind: g.kind })
      return false
    }
    if (goal) endGoal('superseded')
    goal = g
    isPositioning.value = true
    // The positioner owns the viewport again; the sticky header stops being the
    // reader's own arrangement until they next touch the panel. Only a USER goal
    // may say so: an AUTO goal (pin-follow, debt settling) is fired by state the
    // reader did not ask for, and clearing the flag there threw away the evidence
    // that they had curated the view.
    if (source === 'user') userAdjusted = false
    goalSeq++
    trace.begin(flowFor(g), { kind: g.kind, ...describe(g), flatItems: flatItems().length, hasScroller: !!scroller() })
    void runGoal()
    return true
  }

  function describe(g: Goal): Record<string, unknown> {
    switch (g.kind) {
      case 'restore': return { scrollIndex: g.scrollIndex, scrollOffset: g.scrollOffset }
      case 'group': return { bookId: g.bookId, sectionLabel: g.sectionLabel, subSectionLabel: g.subSectionLabel, reason: g.reason }
      case 'flatIndex': return { flatIndex: g.flatIndex, occurrence: g.occurrence }
      default: return {}
    }
  }

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
    // PREVIOUS line, and the same book can sit under a different section there,
    // so an exact-only match refused to scroll and read as "the panel lost its
    // place". Fall back to book identity.
    return findHeader((item) => item?.bookId === bookId)
  }

  /**
   * The row a goal lands on and how far into it, against the CURRENT list — or
   * null while the goal cannot be resolved yet (no list, a group not in it yet).
   */
  function landingFor(g: Goal): { index: number; offsetPx: number } | null {
    const count = flatItems().length
    if (!count) return null
    switch (g.kind) {
      case 'restore-intent':
        return null
      case 'restore': {
        // A saved index can exceed the current list (position saved against a longer
        // line's commentary). Clamp to the last item - best effort.
        const index = Math.min(g.scrollIndex, count - 1)
        return { index, offsetPx: index === g.scrollIndex ? g.scrollOffset : 0 }
      }
      case 'group': {
        const index = resolveGroupIndex(g.bookId, g.sectionLabel, g.subSectionLabel)
        return index < 0 ? null : { index, offsetPx: -LANDING_GAP_PX }
      }
      case 'flatIndex':
        return { index: g.flatIndex, offsetPx: -(NAV_HEIGHT + LANDING_GAP_PX) }
    }
  }

  /**
   * The row the scroller should OPEN on, for its `initialIndex`: when a goal is
   * already waiting as the scroller mounts (a restore claimed before the list
   * arrived, a pin-follow), the first window rendered is the goal's — nothing of
   * the list's top is ever fetched or shown first.
   */
  const openingTarget = computed(() => {
    // Re-evaluated with the list; `goal` itself is plain state, read live.
    void flatItems().length
    return goal ? landingFor(goal) : null
  })

  /** Runs the current goal if its target can be resolved now; otherwise it waits for the list. */
  async function runGoal() {
    const g = goal
    const api = scroller()
    if (!g || !api) return
    const landing = landingFor(g)
    if (!landing) {
      // A group that is not in a NON-empty list is genuinely absent (filtered out,
      // or gone from this line): end rather than hold the mask until it appears.
      if (g.kind === 'group' && flatItems().length) endGoal('index_not_found')
      return
    }
    // The scroller is still opening on this very target (its own initialIndex
    // landing): the `opened` event finishes the goal — see scrollerOpened.
    if (api.renderedRange.end === api.renderedRange.start) return
    const seq = goalSeq
    await api.scrollToIndex(landing.index, { offsetPx: landing.offsetPx })
    if (seq !== goalSeq) return
    finishGoal()
  }

  /** The goal's row is on screen: the follow-ups, then the goal ends. */
  function finishGoal() {
    const g = goal
    const api = scroller()
    if (!g || !api) return
    const element = api.scrollerElement
    if (g.kind === 'restore') {
      g.applied = true
      if (!g.resolved) { g.resolved = true; g.resolve(true) }
    }
    if (g.kind === 'flatIndex' && element) {
      // The row is mounted with its text, marks included. Bring the current one
      // into view when the row's top-of-viewport landing left it off screen.
      setCurrentMark(element, g.flatIndex, g.occurrence)
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
    endGoal('done')
  }

  /**
   * Wired to the scroller's `opened` event: its initialIndex landing is on screen.
   * Re-runs the goal rather than finishing it blind: if the goal changed while the
   * scroller opened, the current one lands now — on rows already mounted, at once.
   */
  function scrollerOpened() {
    void runGoal()
  }

  // A goal can be requested before the list it needs exists (a restore claimed
  // before the load, a pin-follow for a group still arriving) or before the
  // scroller has mounted. Re-try whenever any of those change.
  watch(
    () => [flatItems().length, visibleGroups().map(commentaryGroupKey).join('|'), scroller()?.renderedRange.end] as const,
    () => { void runGoal() },
    { flush: 'post' },
  )

  // ── Public API ──────────────────────────────────────────────────────────────

  /**
   * @param reason which code path asked. AUTO_REASONS follow state (pin-follow);
   *   everything else is a user action and takes priority over any running goal.
   */
  function scrollToGroup(
    bookId: number,
    sectionLabel?: string,
    subSectionLabel?: string,
    reason = 'unknown',
  ): boolean {
    // A pick from the header toolbar IS the reader's commentator choice - record
    // it before the goal runs, so a line switch during the landing still captures
    // it. Only this reason: an auto pin-follow is state catching up, and a
    // search/restore jump moves the viewport without choosing a commentator.
    if (reason === 'header-nav-picker') {
      explicitPick = {
        bookId,
        sectionLabel: sectionLabel ?? '',
        subSectionLabel: subSectionLabel ?? '',
      }
    }
    return setGoal(
      { kind: 'group', bookId, sectionLabel, subSectionLabel, reason },
      AUTO_REASONS.has(reason) ? 'auto' : 'user',
    )
  }

  function scrollToFlatIndex(flatIndex: number, occurrence = 0) {
    setGoal({ kind: 'flatIndex', flatIndex, occurrence }, 'user')
  }

  /**
   * Claim, synchronously, that a restore is about to be requested. Blocks auto
   * pin-follow goals from grabbing the panel during the awaits between "the panel
   * decided to restore" and the actual restore call - the race that used to land
   * the panel on the pinned group instead of the saved position.
   */
  function claimRestoreIntent() {
    setGoal({ kind: 'restore-intent' }, 'user')
  }

  /**
   * Drop whatever the panel was trying to reach. Called when the anchor line
   * changes by explicit reader action: the new load installs its own goal, and a
   * goal held over from the previous line is stale by definition. Without this a
   * restore still waiting for its list would survive the switch, block the new
   * load's pin-follow (auto goals cannot displace a restore) and then apply the
   * OLD line's index into the NEW line's list.
   */
  function cancelPositioning() {
    endGoal('cancelled_anchor_changed')
  }

  /**
   * Resolves with whether the position was actually APPLIED. A restore that dies
   * unapplied (panel closed mid-flight, list never produced the index) must not be
   * recorded as done by the caller, or reopening the panel skips the restore and
   * the reader loses their place.
   */
  function restoreCommentaryScrollPos(scrollIndex: number, scrollOffset: number): Promise<boolean> {
    return new Promise<boolean>((resolve) => {
      const g: Goal = { kind: 'restore', scrollIndex, scrollOffset, resolve, resolved: false, applied: false }
      setGoal(g, 'user')
    })
  }

  // When groups reload, scroll back to the pinned group (captured in parent before
  // selectedLineId changes). These watchers only decide WHETHER to request a pin
  // scroll; whether it MAY run (e.g. not while a restore owns the panel) is the
  // goal's call, reported through setGoal's return value.
  function setupGroupReloadScroll(
    groups: () => any[],
    pinnedGroup: () => any,
    isLoading: () => boolean,
    hasSavedScrollPos: () => boolean = () => false,
    /**
     * The line the panel's commentary currently belongs to. Used only to tell "the
     * same load blinking its list empty" apart from "a new line's load starting" -
     * the two are indistinguishable from the groups array alone, and conflating
     * them is what kept dropping the first-load pin scroll.
     */
    anchorId: () => number | null = () => null,
  ) {
    let isFirstLoad = true
    /**
     * Set when a load produced groups but no pin was available yet, so the scroll to
     * the pinned group is still owed. Settled by `settlePinDebt` below, which is
     * driven by BOTH the pin arriving and the loading flag clearing - either can be
     * the last precondition to fall into place, and whichever is last has to be the
     * one that fires.
     */
    let pinScrollOwed = false
    /**
     * The anchor the debt belongs to. A debt is only void when a DIFFERENT line's
     * load starts - not merely because the list blinked empty: one line tap runs
     * load() twice, so the list goes groups -> [] -> groups for the SAME anchor.
     */
    let owedForAnchor: number | null = null
    watch(
      groups,
      (newGroups) => {
        if (!newGroups.length) {
          // Only a genuinely different anchor voids the debt. The same anchor's
          // second load() pass is the same positioning job, still owed.
          if (anchorId() !== owedForAnchor) pinScrollOwed = false
          return
        }
        // Skip partial loads — only scroll when loading is fully complete.
        if (isLoading()) return
        // Consume the first-load flag only once we are genuinely ready to position
        // the panel — not on the `groups = []` a load starts with.
        if (isFirstLoad) {
          isFirstLoad = false
          // Blank slate (no saved scroll position): nobody else positions the panel
          // on the very first groups load, so scroll to the pinned/default group.
          // With a saved position, the restore path owns first positioning — skip.
          if (hasSavedScrollPos()) {
            pinScrollOwed = false
            return
          }
        }
        const pinned = pinnedGroup()
        if (!pinned || !newGroups.some((g: any) => g.bookId === pinned.bookId)) {
          // The pin has not been decided yet (the default-commentator query is still
          // in flight), or its book is not in this list yet (usePinnedCommentary can
          // publish a default's labels before this panel's filtered list carries the
          // book). Record the debt against THIS anchor; settlePinDebt pays it when the
          // missing precondition lands.
          pinScrollOwed = true
          owedForAnchor = anchorId()
          return
        }
        // If a restore owns the panel, the goal declines and the debt stays
        // paid-off (restore IS the positioning for this load).
        pinScrollOwed = false
        scrollToGroup(pinned.bookId, pinned.sectionLabel, pinned.subSectionLabel, 'groups-reload')
      },
      { flush: 'post' },
    )

    /**
     * Pay an owed pin scroll as soon as ALL its preconditions hold: a debt exists, a
     * pin is decided, the load is complete, and the pinned book is actually in this
     * panel's list. Called from watchers on EVERY precondition, because the pin is
     * not reliably the last one to arrive; the guards make every other call a no-op.
     */
    function settlePinDebt(reason: string) {
      if (!pinScrollOwed) return
      if (isLoading()) return
      const pinned = pinnedGroup()
      if (!pinned) return
      const live = groups()
      if (!live.length || !live.some((g: any) => g.bookId === pinned.bookId)) return
      const accepted = scrollToGroup(pinned.bookId, pinned.sectionLabel, pinned.subSectionLabel, reason)
      // A restore in progress declines the request; keep the debt so a later change
      // can still settle it (restore is the better positioning either way).
      if (accepted) pinScrollOwed = false
    }

    watch(pinnedGroup, () => settlePinDebt('pin-arrived-late'), { flush: 'post' })
    // The pin was ready before the load finished: settle on the loading edge.
    watch(isLoading, (loading) => { if (!loading) settlePinDebt('pin-owed-load-done') }, { flush: 'post' })
    // The pinned book appeared in a later slice of this load, or a filter change
    // brought it back. Watch the group STRUCTURE, not the array: content arrives by
    // mutating line objects in place, and only a change in WHICH groups exist can
    // newly satisfy "the pinned book is in the list".
    watch(
      () => groups().map((g: any) => commentaryGroupKey(g)).join('|'),
      () => settlePinDebt('pin-owed-groups-arrived'),
      { flush: 'post' },
    )
  }

  // A goal must not outlive its panel: a restore left pending would resolve nobody.
  onScopeDispose(() => endGoal('disposed'))

  return {
    scrollTop,
    activeHeader,
    activePinnedGroup,
    activePinnedGroupForCapture,
    consumeExplicitPick,
    cancelPositioning,
    markUserAdjusted,
    isPositioning,
    openingTarget,
    scrollerOpened,
    onScroll,
    scrollToGroup,
    scrollToFlatIndex,
    captureScrollPos,
    restoreCommentaryScrollPos,
    claimRestoreIntent,
    topVisibleFlatIndex,
    setupGroupReloadScroll,
  }
}
