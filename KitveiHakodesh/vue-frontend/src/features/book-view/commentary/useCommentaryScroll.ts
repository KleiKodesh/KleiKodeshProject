import { computed, ref, watch, nextTick } from 'vue'
import { setCurrentMark } from '../lines/useBookViewLineRenderer'
import { commentaryScrollTrace as trace } from '@/utils/commentaryScrollTrace'
import type { Virtualizer } from '@tanstack/vue-virtual'

const NAV_HEIGHT = 32

/**
 * How many consecutive frames the target has to need no correction before a goal
 * counts as settled. Frame-based on purpose: frames stretch with machine load, so a
 * slow machine settles later in wall-clock terms - the opposite failure mode of a
 * fixed timeout, which assumes how fast loading finishes and gives up early on slow
 * environments. NOTHING in this file assumes a load duration.
 */
const SETTLE_FRAMES = 30
/** Frames the scroller may be missing mid-goal (v-if flicker) before the goal dies.
 * A restore gets a far larger budget: it is requested BEFORE the panel's v-if body
 * has rendered, so its scroller legitimately does not exist yet. */
const LOST_SCROLLER_FRAMES = 60
const LOST_SCROLLER_FRAMES_RESTORE = 600
/** A restore-intent placeholder only has to survive the tick between the claim and
 * the restore call that follows it. If the restore never comes (component died in
 * that tick), expire quickly - frame-based, so slow machines get proportionally
 * more real time. */
const INTENT_FRAMES = 120
/**
 * Last-resort valve so an unachievable goal cannot run forever (e.g. a search mark
 * whose query was cleared). Deliberately generous: it is NOT a load-time estimate,
 * and a goal that is still making progress keeps correcting right up to it. On
 * expiry the goal applies its best-effort position once and ends.
 */
const SAFETY_MS = 30000

/**
 * What the panel is currently trying to show. Exactly ONE goal exists at a time -
 * this is the whole point. Six independent code paths used to scroll the panel
 * directly, coordinated through shared boolean flags, and every new panel or new
 * load-timing multiplied their races. Now every path REQUESTS a goal and one loop
 * executes whatever the current goal is.
 */
type Goal =
  | { kind: 'restore-intent' }
  | { kind: 'restore'; scrollIndex: number; scrollOffset: number; resolve: () => void; resolved: boolean }
  | { kind: 'group'; bookId: number; sectionLabel?: string; subSectionLabel?: string; reason: string; entered?: boolean }
  | { kind: 'flatIndex'; flatIndex: number; occurrence: number; phase: 'position' | 'mark'; entered?: boolean }

/** Requests from these scrollToGroup reasons follow state around (pin-follow); all
 * other reasons are direct user actions and take priority. */
const AUTO_REASONS = new Set(['groups-reload', 'pin-arrived-late', 'panel-mounted', 'already-restored'])

/**
 * Manages scroll behavior for commentary: sticky header tracking, scroll position
 * capture/restore, and scroll-to-group navigation.
 *
 * All programmatic positioning goes through the single-goal positioner below.
 * Completion is CONDITION-based (target measured, content present, position stable
 * for SETTLE_FRAMES), never a wall-clock window - fixed windows are exactly what
 * kept breaking on slow environments and heavy chapters, because they encode an
 * assumption about how long loading takes.
 */
export function useCommentaryScroll(
  flatItems: () => any[],
  visibleGroups: () => any[],
  virtualizer: () => Virtualizer<any, any>,
  scrollerEl: () => HTMLElement | null,
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

  const stickyHeader = computed(() => {
    let active: any = null
    for (const m of virtualizer().measurementsCache) {
      const item = flatItems()[m.index]
      if (item?.type !== 'header') continue
      // Switch only when the header's bottom edge has scrolled past the nav
      if (m.end <= scrollTop.value + NAV_HEIGHT + 5) active = item
      else break
    }
    return active
  })

  const activeHeader = computed(
    () =>
      stickyHeader.value ??
      (flatItems().find((i) => i.type === 'header') as any) ??
      null,
  )

  const activePinnedGroup = computed<any>(() => {
    const header = activeHeader.value
    if (!header) return null
    return {
      bookId: header.bookId,
      sectionLabel: header.sectionLabel ?? '',
      subSectionLabel: header.subSectionLabel ?? '',
    }
  })

  /**
   * True once the reader has manually moved this panel (wheel/touch/pointer/key)
   * since the last programmatic positioning. Reset whenever a goal is installed:
   * a goal repositions the panel, so whatever the sticky header says afterwards is
   * the POSITIONER's doing, not the reader's.
   */
  let userAdjusted = false
  function markUserAdjusted() { userAdjusted = true }

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
   */
  const activePinnedGroupForCapture = computed<any>(() => {
    if (goal !== null || !userAdjusted) return null
    return activePinnedGroup.value
  })

  function onScroll(emitScroll: (scrollIndex: number, scrollOffset: number) => void) {
    scrollTop.value = scrollerEl()?.scrollTop ?? 0
    const pos = captureScrollPos()
    if (pos) emitScroll(pos.scrollIndex, pos.scrollOffset)
  }

  // ── The positioner ──────────────────────────────────────────────────────────

  let goal: Goal | null = null
  /**
   * True from the moment a goal is installed until it FIRST reaches its target
   * (not until fully settled - corrections continue silently). The panel shows its
   * loading overlay while this is true, so the reader never watches the content
   * sitting at the wrong offset while the two-phase backfill shifts it around.
   * Turned off at first arrival rather than at settle so the fast path (local dev,
   * small chapters) never pays a visible delay.
   */
  const isPositioning = ref(false)
  /** Bumped on every goal change; the running loop checks it each frame and stops
   * the moment it is stale. Replaces the old cancellation token, restore flag AND
   * restore-intent latch - one mechanism instead of three. */
  let goalSeq = 0
  let userCancelEl: HTMLElement | null = null

  function flowFor(g: Goal): string {
    return g.kind === 'restore' || g.kind === 'restore-intent' ? FLOW_RESTORE : FLOW_SCROLL
  }

  function detachUserCancel() {
    if (!userCancelEl) return
    userCancelEl.removeEventListener('wheel', cancelByUser)
    userCancelEl.removeEventListener('touchstart', cancelByUser)
    userCancelEl.removeEventListener('pointerdown', cancelByUser)
    userCancelEl.removeEventListener('keydown', cancelByUser)
    userCancelEl = null
  }

  function cancelByUser() {
    if (!goal) return
    endGoal('cancelled_by_user')
  }

  function endGoal(status: string) {
    if (!goal) return
    trace.push(flowFor(goal), `goal_${status}`, { kind: goal.kind })
    if (goal.kind === 'restore' && !goal.resolved) { goal.resolved = true; goal.resolve() }
    goal = null
    goalSeq++
    isPositioning.value = false
    detachUserCancel()
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
    // reader's own arrangement until they next touch the panel.
    userAdjusted = false
    const seq = ++goalSeq
    trace.begin(flowFor(g), { kind: g.kind, ...describe(g), flatItems: flatItems().length, hasEl: !!scrollerEl() })
    runLoop(seq)
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
    const exact = items.findIndex(
      (item) =>
        item.type === 'header' &&
        item.bookId === bookId &&
        (sectionLabel == null || item.sectionLabel === sectionLabel) &&
        (subSectionLabel == null || item.subSectionLabel === subSectionLabel),
    )
    if (exact !== -1 || (sectionLabel == null && subSectionLabel == null)) return exact
    // The labels only disambiguate a book that appears in several sections; the
    // reader asked for a COMMENTATOR. A pin carries the labels captured on the
    // PREVIOUS line, and the same book can sit under a different section there,
    // so an exact-only match refused to scroll and read as "the panel lost its
    // place". Fall back to book identity.
    return items.findIndex((item) => item.type === 'header' && item.bookId === bookId)
  }

  /**
   * The one loop. Runs one evaluation per animation frame while a goal is active:
   * derive the goal's target from LIVE measurements, correct if off, count stable
   * frames, finish when settled. Re-deriving every frame is what makes it immune to
   * the two-phase backfill - items above the target keep growing as content lands,
   * and each growth just moves the target for the next frame's correction. The old
   * design did the same correction but stopped after a fixed window, which silently
   * assumed the backfill would be done by then.
   */
  function runLoop(seq: number) {
    let stableFrames = 0
    let lostScrollerFrames = 0
    let intentFrames = 0
    const startedAt = performance.now()

    function attachUserCancel(el: HTMLElement) {
      if (userCancelEl === el) return
      detachUserCancel()
      userCancelEl = el
      el.addEventListener('wheel', cancelByUser, { passive: true })
      el.addEventListener('touchstart', cancelByUser, { passive: true })
      el.addEventListener('pointerdown', cancelByUser, { passive: true })
      // The scroller is focusable and arrow keys scroll it; without this the
      // correction would fight keyboard navigation.
      el.addEventListener('keydown', cancelByUser, { passive: true })
    }

    function frame() {
      if (seq !== goalSeq || !goal) return
      const g = goal
      const el = scrollerEl()

      if (g.kind === 'restore-intent' && ++intentFrames > INTENT_FRAMES) {
        endGoal('intent_expired')
        return
      }

      if (!el) {
        // The panel may be mid-mount (restore is requested before the v-if body
        // renders). Tolerate a bounded number of missing-scroller frames.
        const budget = g.kind === 'restore' ? LOST_SCROLLER_FRAMES_RESTORE : LOST_SCROLLER_FRAMES
        if (++lostScrollerFrames > budget) {
          endGoal('lost_scroller')
          return
        }
        requestAnimationFrame(frame)
        return
      }
      lostScrollerFrames = 0
      attachUserCancel(el)

      if (performance.now() - startedAt > SAFETY_MS) {
        applyOnce(g, el, /* force */ true)
        endGoal('safety_valve')
        return
      }

      const status = applyOnce(g, el, false)
      if (status === 'stable') {
        // First arrival: the panel is at (or within 2px of) its target. Reveal it -
        // later corrections are small nudges, and hiding content through the whole
        // settle window would cost every fast load a visible delay.
        isPositioning.value = false
        if (++stableFrames >= SETTLE_FRAMES) {
          endGoal('done')
          return
        }
      } else {
        stableFrames = 0
      }
      requestAnimationFrame(frame)
    }

    requestAnimationFrame(frame)
  }

  /**
   * One evaluation of the current goal against live state.
   * Returns 'stable' when no correction was needed, 'position' when it corrected,
   * 'waiting' when the goal cannot be evaluated yet (unmeasured / content pending /
   * mark not rendered) - waiting resets nothing and the loop simply tries again
   * next frame.
   */
  function applyOnce(g: Goal, el: HTMLElement, force: boolean): 'stable' | 'position' | 'waiting' {
    switch (g.kind) {
      case 'restore-intent':
        // A placeholder that only exists to keep auto goals out until the real
        // restore arrives (it is created synchronously; the restore follows after
        // an await). It has no position of its own.
        return 'waiting'

      case 'restore': {
        const len = flatItems().length
        if (!len) return 'waiting'
        // A saved index can exceed the current list (position saved against a longer
        // line's commentary). Clamp to the last item - best effort - instead of
        // waiting for an index that will never exist.
        const idx = Math.min(g.scrollIndex, len - 1)
        if (idx !== g.scrollIndex) { g.scrollIndex = idx; g.scrollOffset = 0 }
        const m = virtualizer().measurementsCache.find((c: any) => c.index === g.scrollIndex)
        if (!m) return 'waiting'
        // Two-phase loader: applying a pixel offset into a not-yet-filled line lands
        // in the wrong group (a 300px offset inside a 20px stub). Wait for the
        // viewport-priority fetch to fill it - condition-based, no attempt cap. The
        // safety valve force-applies if the content genuinely never comes.
        const flatItem = flatItems()[g.scrollIndex]
        const contentPending =
          g.scrollOffset > 0 && flatItem?.type === 'line' && flatItem.lineId > 0 && flatItem.content === ''
        if (contentPending && !force) {
          trace.push(FLOW_RESTORE, 'content_pending', { scrollIndex: g.scrollIndex })
          return 'waiting'
        }
        const maxScrollTop = Math.max(0, el.scrollHeight - el.clientHeight)
        const target = Math.min(Math.max(0, m.start + g.scrollOffset), maxScrollTop)
        if (Math.abs(el.scrollTop - target) > 2) {
          const before = el.scrollTop
          el.scrollTop = target
          scrollTop.value = target
          trace.push(FLOW_RESTORE, 'apply', { before, target, itemStart: m.start })
          // The reader-facing contract resolves on the FIRST successful apply -
          // the panel is now at (approximately) the right place and callers must
          // not wait out the settle confirmation.
          if (!g.resolved) { g.resolved = true; g.resolve() }
          return 'position'
        }
        if (!g.resolved) { g.resolved = true; g.resolve() }
        return 'stable'
      }

      case 'group': {
        const idx = resolveGroupIndex(g.bookId, g.sectionLabel, g.subSectionLabel)
        if (idx < 0) {
          // Mid-swap (empty list): wait - the old code aborted here, which is how
          // "never scrolled at all" happened when a list swap raced the request.
          // But a NON-empty list without the book means it is genuinely absent
          // (filtered out, or gone from this line): end rather than hold the goal
          // - and the loading overlay - for the whole safety window.
          if (!flatItems().length) {
            trace.push(FLOW_SCROLL, 'group_waiting_for_index', { bookId: g.bookId })
            return 'waiting'
          }
          endGoal('index_not_found')
          return 'waiting'
        }
        const m = virtualizer().measurementsCache.find((c: any) => c.index === idx)
        if (!m) return 'waiting'
        const target = Math.max(0, m.start)
        if (Math.abs(el.scrollTop - target) > 2) {
          const before = el.scrollTop
          if (!g.entered) {
            // Once per goal: brings a far-away target into the render window so its
            // measurements become real rather than pure estimates. Re-issuing it on
            // every correction frame would fight the direct scrollTop writes below.
            virtualizer().scrollToIndex(idx, { align: 'start' })
            g.entered = true
          }
          el.scrollTop = target
          scrollTop.value = target
          trace.push(FLOW_SCROLL, 'correct_applied', { before, target, mStart: m.start, idx })
          return 'position'
        }
        return 'stable'
      }

      case 'flatIndex': {
        const m = virtualizer().measurementsCache.find((c: any) => c.index === g.flatIndex)
        if (!m) return 'waiting'
        if (g.phase === 'position') {
          const target = Math.max(0, m.start - NAV_HEIGHT - 8)
          if (Math.abs(el.scrollTop - target) > 2 && !force) {
            if (!g.entered) {
              virtualizer().scrollToIndex(g.flatIndex, { align: 'start' })
              g.entered = true
            }
            el.scrollTop = target
            scrollTop.value = target
            return 'position'
          }
          setCurrentMark(el, g.flatIndex, g.occurrence)
          g.phase = 'mark'
          return 'position'
        }
        // Phase 2: fine-adjust to the current search mark once Vue has re-rendered
        // the line with it. The mark may take several frames to appear (render
        // cache invalidation + re-render); waiting is condition-based.
        const mark = el.querySelector('mark.search-match.current') as HTMLElement | null
        if (!mark) return force ? 'stable' : 'waiting'
        const markRect = mark.getBoundingClientRect()
        const scrollerRect = el.getBoundingClientRect()
        const relativeTop = markRect.top - scrollerRect.top
        const relativeBottom = markRect.bottom - scrollerRect.top
        const visible = relativeTop >= NAV_HEIGHT + 4 && relativeBottom <= scrollerRect.height - 4
        if (!visible) {
          el.scrollTop += relativeTop - NAV_HEIGHT - 8
          scrollTop.value = el.scrollTop
          return 'position'
        }
        return 'stable'
      }
    }
  }

  // ── Public API (same shape the rest of the app already uses) ────────────────

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
    return setGoal(
      { kind: 'group', bookId, sectionLabel, subSectionLabel, reason },
      AUTO_REASONS.has(reason) ? 'auto' : 'user',
    )
  }

  function scrollToFlatIndex(flatIndex: number, occurrence = 0) {
    setGoal({ kind: 'flatIndex', flatIndex, occurrence, phase: 'position' }, 'user')
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

  function restoreCommentaryScrollPos(scrollIndex: number, scrollOffset: number): Promise<void> {
    return new Promise<void>((resolve) => {
      const g: Goal = { kind: 'restore', scrollIndex, scrollOffset, resolve, resolved: false }
      setGoal(g, 'user')
    })
  }

  function captureScrollPos(): { scrollIndex: number; scrollOffset: number } | null {
    const el = scrollerEl()
    if (!el) return null

    const items = virtualizer().getVirtualItems()
    if (!items.length) return null

    const scrollTopValue = el.scrollTop
    const measured = virtualizer().measurementsCache

    let first = measured.find((item) => item.start <= scrollTopValue && scrollTopValue < item.end)

    if (!first) {
      first = items.find((item) => item.start <= scrollTopValue && scrollTopValue < item.end) ?? items[0]
    }

    if (!first) return null

    return {
      scrollIndex: first.index,
      scrollOffset: Math.max(0, scrollTopValue - first.start),
    }
  }

  const topVisibleFlatIndex = computed(() => {
    const st = scrollTop.value + NAV_HEIGHT
    for (const m of virtualizer().measurementsCache) {
      if (m.end > st) return m.index
    }
    return 0
  })

  // When groups reload, scroll back to the pinned group (captured in parent before
  // selectedLineId changes). These watchers only decide WHETHER to request a pin
  // scroll; whether it MAY run (e.g. not while a restore owns the panel) is the
  // positioner's call, reported through setGoal's return value.
  function setupGroupReloadScroll(
    groups: () => any[],
    pinnedGroup: () => any,
    isLoading: () => boolean,
    hasSavedScrollPos: () => boolean = () => false,
  ) {
    let isFirstLoad = true
    let scrollGeneration = 0
    // Set when a load produced groups but no pin was available yet, so the scroll
    // to the pinned group is still owed. Settled by the pinnedGroup watcher below.
    let pinScrollOwed = false
    watch(
      groups,
      async (newGroups) => {
        // Bump FIRST, before any early return. Every groups change must invalidate a
        // callback that is already mid-flight, INCLUDING a change this callback then
        // ignores (empty list, still loading). See the doc's async-watcher rule.
        const generation = ++scrollGeneration

        // A new load is starting; any scroll owed by the previous one is void.
        if (!newGroups.length) {
          pinScrollOwed = false
          return
        }
        // Skip partial loads — only scroll when loading is fully complete.
        if (isLoading()) return
        // Consume the first-load flag only once we are genuinely ready to position
        // the panel. It used to be consumed by the FIRST fire of this watcher, which
        // is the `groups = []` that load() starts with, so the "a restore owns first
        // positioning" skip below never actually applied to a real load.
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
        await nextTick()
        if (generation !== scrollGeneration) return
        const pinned = pinnedGroup()
        if (!pinned) {
          // The pin has not been decided yet — the default-commentator query is
          // still in flight. Record the debt; the watcher below settles it.
          pinScrollOwed = true
          return
        }
        // Re-read LIVE rather than trusting the captured array: `newGroups` is a
        // snapshot from before the await.
        const live = groups()
        if (!live.length) return
        if (!live.some((g: any) => g.bookId === pinned.bookId)) return
        // If a restore owns the panel, the positioner declines and the debt stays
        // paid-off (restore IS the positioning for this load).
        pinScrollOwed = false
        scrollToGroup(pinned.bookId, pinned.sectionLabel, pinned.subSectionLabel, 'groups-reload')
      },
      { flush: 'post' },
    )

    /**
     * A pin that arrives AFTER its groups.
     *
     * usePinnedCommentary awaits a DB query for the book's default commentators, and
     * on the FIRST commentary load of a book that query can still be in flight when
     * the groups land: the watcher above finds no pin and records the debt; this one
     * settles it when the pin appears. Only the first load of a book pays for this
     * (the query result is cached), which is why it read as "the FIRST time I open a
     * chapter it doesn't scroll to the default commentary".
     */
    watch(
      pinnedGroup,
      (pinned) => {
        if (!pinScrollOwed || !pinned) return
        if (isLoading()) return
        const live = groups()
        if (!live.length || !live.some((g: any) => g.bookId === pinned.bookId)) return
        const accepted = scrollToGroup(pinned.bookId, pinned.sectionLabel, pinned.subSectionLabel, 'pin-arrived-late')
        // A restore in progress declines the request; keep the debt so a later
        // pin change can still settle it (matches the old guard's behavior).
        if (accepted) pinScrollOwed = false
      },
      { flush: 'post' },
    )
  }

  return {
    scrollTop,
    activeHeader,
    activePinnedGroup,
    activePinnedGroupForCapture,
    markUserAdjusted,
    isPositioning,
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
