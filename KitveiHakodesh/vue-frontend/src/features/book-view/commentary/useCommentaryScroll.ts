import { computed, ref, watch, nextTick, onScopeDispose } from 'vue'
import { setCurrentMark } from '../lines/useBookViewLineRenderer'
import { commentaryGroupKey } from './useCommentary'
import { commentaryScrollTrace as trace } from '@/utils/commentaryScrollTrace'
import type { Virtualizer } from '@tanstack/vue-virtual'

const NAV_HEIGHT = 32

/**
 * How many consecutive frames the target has to need no correction before a goal
 * counts as settled. Frame-based on purpose: frames stretch with machine load, so a
 * slow machine settles later in wall-clock terms - the opposite failure mode of a
 * fixed timeout, which assumes how fast loading finishes and gives up early on slow
 * environments. NOTHING in this file assumes a load duration.
 *
 * This is a quiet-period check, NOT the completion test, and on its own it is still
 * a fixed window (~500ms at 60fps) - which is exactly how it once let goals finish
 * mid-backfill and drift. Completion is `stableFrames >= SETTLE_FRAMES &&
 * !contentPendingAbove(goal)`. If a goal drifts, raising this number is the wrong
 * lever every time; read contentPendingAbove.
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
  | { kind: 'restore'; scrollIndex: number; scrollOffset: number; resolve: (applied: boolean) => void; resolved: boolean; applied: boolean }
  | { kind: 'group'; bookId: number; sectionLabel?: string; subSectionLabel?: string; reason: string; entered?: boolean }
  | { kind: 'flatIndex'; flatIndex: number; occurrence: number; phase: 'position' | 'mark'; entered?: boolean }

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
    // a toolbar pick is normally still scrolling when the next click arrives.
    //
    // READ-ONLY: the pick is consumed by consumeExplicitPick, called from
    // applyPendingPins when the snapshot is actually APPLIED to the pins.
    //
    // Capture is not consumption. captureActivePins runs from three places, and
    // the scroll-sync one (useBookViewScrollSync.applyPositionSync, auto-select-
    // top-line) captures a snapshot 120ms BEFORE it applies it - and may never
    // apply it at all, because a newer scroll clears the timer. Consuming on read
    // meant any lines-pane scroll between a toolbar pick and the next line click
    // swallowed the pick, so the click captured null and fell back to the held
    // pin. That is precisely the "sometimes it persists, sometimes not": the
    // outcome depended on whether the lines pane happened to scroll in between.
    // Everything this function can return is by definition the reader's own doing -
    // a toolbar pick, or a group they scrolled to themselves; the guards above
    // reject every derived case. So `chosen` is stamped ONCE here, on the way out,
    // rather than at each source. See PinnedCommentaryGroup for what it decides.
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
  /** Waiting reasons already traced for the current goal (see runLoop). */
  let tracedWaits = new Set<string>()

  /** Trace a waiting reason at most once per goal. */
  function traceWaitOnce(flow: string, event: string, detail: Record<string, unknown>) {
    if (tracedWaits.has(event)) return
    tracedWaits.add(event)
    trace.push(flow, event, detail)
  }

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
    if (goal.kind === 'restore' && !goal.resolved) { goal.resolved = true; goal.resolve(goal.applied) }
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
    //
    // Only a USER-sourced goal may do this. An AUTO goal (pin-follow, debt
    // settling) is fired by state changes the reader did not ask for - and
    // content backfill mutates `groups` in place batch after batch, so
    // settlePinDebt's groups watcher can install one many seconds after the
    // reader has scrolled somewhere else. Clearing the flag there threw away the
    // evidence that they had curated the view, so the next line switch captured
    // null and captureActivePins fell back to the HELD pin: the panel snapped
    // back to the old commentator on every switch and no amount of scrolling
    // could escape it. A pin-follow that the reader has already scrolled away
    // from is precisely the case where their choice must win.
    if (source === 'user') userAdjusted = false
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

  /** A scroll position the element can actually hold. */
  function clampScroll(el: HTMLElement, desired: number): number {
    const max = Math.max(0, el.scrollHeight - el.clientHeight)
    return Math.min(Math.max(0, desired), max)
  }

  /** The flat index a goal is aiming at, or -1 when it has none / cannot resolve one. */
  function targetIndexOf(g: Goal): number {
    switch (g.kind) {
      case 'restore': return g.scrollIndex
      case 'flatIndex': return g.flatIndex
      case 'group': return resolveGroupIndex(g.bookId, g.sectionLabel, g.subSectionLabel)
      default: return -1
    }
  }

  /**
   * Is any line ABOVE this goal's target still waiting for its content?
   *
   * ── The general rule this encodes ───────────────────────────────────────────
   * A position in a virtualized list is only final once everything ABOVE it has
   * its final height. Until then the target is still moving, and any "we have
   * arrived" answer is a guess about the future.
   *
   * This is THE test to reach for whenever positioning lands correctly and then
   * drifts. It replaces a whole family of failed approaches, all of which were
   * really the same mistake - guessing at a duration instead of asking whether
   * the thing being waited on had happened:
   *
   *   - a correction window (800ms -> 2.5s -> 6s, each "fixed" by enlarging it);
   *   - a retry/attempt cap;
   *   - N stable frames, which is just a fixed window denominated in frames
   *     (SETTLE_FRAMES=30 is ~500ms at 60fps) and broke the same way;
   *   - waiting on `loading`, which only covers the STRUCTURE query - content
   *     backfills in batches long after it clears.
   *
   * Every one of those encodes "loading should be done by now". This asks
   * instead, so it is correct at any speed: a fast local load settles the frame
   * the content lands, a slow host or a heavy chapter simply holds longer, and
   * nothing has to be re-tuned per environment. SAFETY_MS stays as the backstop
   * for content that genuinely never arrives.
   *
   * Prefer widening THIS predicate over lengthening any timeout. If a goal drifts
   * again, the question is "what else above the target is still growing that I am
   * not looking at?" - not "how much longer should we wait?".
   * ────────────────────────────────────────────────────────────────────────────
   *
   * Only lines above the target can move it: the two-phase loader renders them as
   * near-empty stubs and they grow when their text arrives, pushing everything
   * below down. Lines at or after the target grow harmlessly off-screen, which is
   * also what keeps this cheap - it never waits for the whole list to fill.
   *
   * Scans the flat list rather than asking the loader, so it needs no new plumbing
   * and stays true for content fetched by ANY path (display-order backfill or the
   * viewport-priority fetch). `lineId > 0` skips the injected placeholder rows
   * (lineId -1), which are never backfilled and would otherwise hold every goal
   * open until the safety valve.
   */
  function contentPendingAbove(g: Goal): boolean {
    const idx = targetIndexOf(g)
    if (idx <= 0) return false
    const items = flatItems()
    const upTo = Math.min(idx, items.length)
    for (let i = 0; i < upTo; i++) {
      const item = items[i]
      if (item?.type === 'line' && item.lineId > 0 && item.content === '') return true
    }
    return false
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
    // A goal can wait for many seconds; tracing the reason once per frame at 60fps
    // wraps the trace buffer and evicts the events being captured. Once per reason
    // per goal is all a reader of the dump needs.
    tracedWaits = new Set<string>()

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
        // Stable frames alone are NOT proof the goal is finished - they only say
        // nothing has moved YET. The content that will move it may still be in
        // flight, so the goal also has to wait on the condition itself: nothing
        // above the target still pending. See contentPendingAbove for why that is
        // the right question and which family of timing hacks it replaces.
        if (++stableFrames >= SETTLE_FRAMES && !contentPendingAbove(g)) {
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
          traceWaitOnce(FLOW_RESTORE, 'content_pending', { scrollIndex: g.scrollIndex })
          return 'waiting'
        }
        const target = clampScroll(el, m.start + g.scrollOffset)
        if (Math.abs(el.scrollTop - target) > 2) {
          const before = el.scrollTop
          el.scrollTop = target
          scrollTop.value = target
          trace.push(FLOW_RESTORE, 'apply', { before, target, itemStart: m.start })
          // The reader-facing contract resolves on the FIRST successful apply -
          // the panel is now at (approximately) the right place and callers must
          // not wait out the settle confirmation.
          g.applied = true
          if (!g.resolved) { g.resolved = true; g.resolve(true) }
          return 'position'
        }
        g.applied = true
        if (!g.resolved) { g.resolved = true; g.resolve(true) }
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
            traceWaitOnce(FLOW_SCROLL, 'group_waiting_for_index', { bookId: g.bookId })
            return 'waiting'
          }
          endGoal('index_not_found')
          return 'waiting'
        }
        const m = virtualizer().measurementsCache.find((c: any) => c.index === idx)
        if (!m) return 'waiting'
        // Clamped: a target past the end of the scroll range (last group, or a list
        // shorter than the viewport where maxScrollTop is 0) can never be reached,
        // the browser clamps every write, and an unclamped compare would report
        // 'position' on every frame forever - holding the positioning mask over the
        // panel until the safety valve. Clamp so arrival is achievable.
        const target = clampScroll(el, m.start)
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
          const target = clampScroll(el, m.start - NAV_HEIGHT - 8)
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
          // The line is in place; only the mark fine-adjust is left. Reveal now -
          // the mark can take many frames to render (or never, if the query was
          // cleared), and masking the panel through that is a visible stall.
          isPositioning.value = false
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
    // A pick from the header toolbar IS the reader's commentator choice - record
    // it before the goal runs, so a line switch during the scroll still captures
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

  /**
   * Drop whatever the panel was trying to reach. Called when the anchor line
   * changes by explicit reader action: the new load installs its own goal, and a
   * goal held over from the previous line is stale by definition. Without this a
   * restore still waiting for content would survive the switch, block the new
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
    /**
     * The line the panel's commentary currently belongs to. Used only to tell "the
     * same load blinking its list empty" apart from "a new line's load starting" -
     * the two are indistinguishable from the groups array alone, and conflating
     * them is what kept dropping the first-load pin scroll.
     */
    anchorId: () => number | null = () => null,
  ) {
    let isFirstLoad = true
    let scrollGeneration = 0
    /**
     * Set when a load produced groups but no pin was available yet, so the scroll to
     * the pinned group is still owed. Settled by `settlePinDebt` below, which is
     * driven by BOTH the pin arriving and the loading flag clearing - either can be
     * the last precondition to fall into place, and whichever is last has to be the
     * one that fires. A single watch on the pin could not do that: when the pin
     * resolved mid-load the guard rejected it and the pin ref never changed again,
     * so nothing re-asked and the panel sat on the first group.
     */
    let pinScrollOwed = false
    /**
     * The anchor the debt belongs to. A debt is only void when a DIFFERENT line's
     * load starts - not merely because the list blinked empty.
     *
     * One line tap runs load() twice (the selectedLineId and selectedLineIds
     * watchers both fire), so the list goes groups -> [] -> groups for the SAME
     * anchor. Voiding on every empty list wiped the first-load debt inside that
     * blink; if the default-commentator query then resolved in that window, the pin
     * watcher found no debt and the panel never scrolled to the default commentator
     * at all - the "first time I open a chapter it ignores the default commentary"
     * report that survived the positioner rewrite.
     */
    let owedForAnchor: number | null = null
    watch(
      groups,
      async (newGroups) => {
        // Bump FIRST, before any early return. Every groups change must invalidate a
        // callback that is already mid-flight, INCLUDING a change this callback then
        // ignores (empty list, still loading). See the doc's async-watcher rule.
        const generation = ++scrollGeneration

        if (!newGroups.length) {
          // Only a genuinely different anchor voids the debt. The same anchor's
          // second load() pass is the same positioning job, still owed.
          if (anchorId() !== owedForAnchor) pinScrollOwed = false
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
          // still in flight. Record the debt against THIS anchor; settlePinDebt
          // pays it when the pin lands (or when loading finally clears).
          pinScrollOwed = true
          owedForAnchor = anchorId()
          return
        }
        // Re-read LIVE rather than trusting the captured array: `newGroups` is a
        // snapshot from before the await.
        const live = groups()
        if (!live.length) return
        if (!live.some((g: any) => g.bookId === pinned.bookId)) {
          // The pin is decided but its book is not in this list yet. usePinnedCommentary
          // fills a default's real labels from the groups it can see, and on a cold
          // load it can publish the pin before this panel's filtered list carries that
          // book. Treat it as owed rather than as "nothing to do": dropping it here
          // left the panel on the first group with no later event to correct it.
          pinScrollOwed = true
          owedForAnchor = anchorId()
          return
        }
        // If a restore owns the panel, the positioner declines and the debt stays
        // paid-off (restore IS the positioning for this load).
        pinScrollOwed = false
        scrollToGroup(pinned.bookId, pinned.sectionLabel, pinned.subSectionLabel, 'groups-reload')
      },
      { flush: 'post' },
    )

    /**
     * Pay an owed pin scroll as soon as ALL its preconditions hold: a debt exists, a
     * pin is decided, the load is complete, and the pinned book is actually in this
     * panel's list.
     *
     * usePinnedCommentary awaits a DB query for the book's default commentators, and
     * on the FIRST commentary load of a book that query can still be in flight when
     * the groups land - the watcher above then finds no pin and records the debt.
     * Only the first load of a book pays for this (the query result is cached),
     * which is why it reads as "the FIRST time I open a chapter it doesn't scroll to
     * the default commentary".
     *
     * Called from watchers on EVERY precondition, not just the pin, because the pin
     * is not reliably the last one to arrive. When it resolved while a partial load
     * was still running, the old single pin-watcher hit its `isLoading()` guard,
     * returned, and was never asked again - the pin ref does not change a second
     * time, so the debt stayed unpaid for the whole load and the panel sat on the
     * first group. Re-asking on each precondition means whichever lands last does
     * the work; the guards make every other call a cheap no-op.
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
    // brought it back - the last precondition in the remaining case.
    //
    // Watch the group STRUCTURE, not the array. Content backfill mutates
    // `groups.value` in place batch after batch (that is the whole two-phase
    // loader), and a plain deep/array watch therefore re-fires this for the
    // entire duration of the fill. Every one of those fires could install an
    // auto goal seconds after the reader had scrolled elsewhere. Only a change
    // in WHICH groups exist can newly satisfy "the pinned book is in the list",
    // which is the only thing this watcher is here to notice.
    watch(
      () => groups().map((g: any) => commentaryGroupKey(g)).join('|'),
      () => settlePinDebt('pin-owed-groups-arrived'),
      { flush: 'post' },
    )
  }

  // Stop a goal's rAF loop and detach its listeners when the panel goes away,
  // rather than letting it tick against a dead instance until it self-expires.
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
