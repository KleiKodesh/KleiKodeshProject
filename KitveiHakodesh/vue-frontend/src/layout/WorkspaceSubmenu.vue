<script setup lang="ts">
import { ref, computed, watch, nextTick, onBeforeUnmount } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useDropdownClose } from '@/composables/useDropdownClose'
import WorkspaceMenu from './WorkspaceMenu.vue'

/**
 * `WorkspaceMenu` as a real submenu: a panel that flies out sideways from whatever row or
 * button opened it.
 *
 * Both surfaces that offer workspaces render THIS, so the anchoring, the clamping, the
 * hover behaviour and the dismissal are written once. The caller owns only `open` and the
 * anchor element - which side the panel prefers to open toward is the caller's too, since
 * the rail opens inward off the window's edge while a menu row opens along its own menu.
 *
 * It **opens on hover**, like any submenu; the click is there for touch, where there is no
 * hover to open on. The anchor's hover listeners are attached from in here rather than by
 * each caller - see the hover section below for the two timers that make it usable.
 *
 * Teleported to the body and `position: fixed`, like every other floating panel here
 * (AppTitleBarBreadcrumbChevronDropdown): the rail and the hamburger menu are both scroll
 * boxes with their own stacking contexts, and a panel positioned inside either one gets
 * clipped by it. Fixed to the viewport is also what lets it be clamped to the viewport.
 *
 * Clamping is the point of the measure-then-place pass in `place()`. In a task pane a few
 * hundred pixels wide - or a short one - the panel would otherwise hang off the edge with
 * the create box past the bottom, which is exactly where it is least recoverable. So it
 * flips to the other side of the anchor when the preferred side has no room, gives up the
 * flip if neither side fits and simply sits against the roomier edge, and takes a
 * `max-height` from whatever vertical space is actually left, letting its list scroll
 * inside that. It never leaves the viewport, at any size.
 */
const props = withDefaults(
  defineProps<{
    open: boolean
    /** The row or button the panel hangs off. */
    anchor: HTMLElement | null
    /**
     * Which physical side of the anchor to try first. The panel flips to the other side
     * when that one does not fit. RTL: 'left' is inward from a right-docked rail.
     */
    prefer?: 'left' | 'right'
    /**
     * A surface the panel must never cover - the rail or the menu it belongs to. The panel
     * sits beside it, and in a window too narrow for both it gives up its own width rather
     * than the ground it was told to keep clear.
     */
    keepClearOf?: HTMLElement | null
  }>(),
  { prefer: 'left', keepClearOf: null },
)

const emit = defineEmits<{ 'update:open': [boolean]; close: [] }>()

/** Breathing room kept between the panel and the viewport edges. */
const VIEWPORT_MARGIN = 8
/** Gap between the anchor and the panel. */
const ANCHOR_GAP = 2
/**
 * Gap left between the panel and the surface it must keep clear.
 *
 * Bigger than ANCHOR_GAP on purpose: this one separates two floating sheets, each with its
 * own frame and shadow, and touching frames read as one wider panel with a seam down it.
 */
const KEEP_CLEAR_GAP = 6
/**
 * The narrowest band worth keeping clear for. Squeezed below this, the panel overlays the
 * surface instead - the rename and create rows need roughly this much to be workable, and
 * a panel nobody can use defeats the point of staying out of the way.
 */
const MIN_USABLE_WIDTH = 160

const panelRef = ref<HTMLElement | null>(null)
const top = ref(0)
const left = ref(0)
/** Hidden for the first frame: `place()` has to measure the panel before it can place it. */
const placed = ref(false)

// The anchor counts as the toggle, not as "outside": when a click lands on it the
// composable suppresses its own close and leaves the job to the button's `@click`, which
// is `toggle()` below. So `toggle()` deliberately does NOT consult the composable's
// `justClosed` - on this path that flag means "your turn", not "already handled".
//
// That is also why the panel is dismissed by a real close rather than by the guard other
// callers use: those pass a wrapper that CONTAINS their button, so a click on it is inside
// the target and never reaches this branch at all. Here the panel is teleported and the
// anchor lives in another tree, so the branch is the normal path, not the edge case.
useDropdownClose(panelRef, () => close(), {
  toggleButton: computed(() => props.anchor),
  enabled: () => props.open,
})

/**
 * Open or dismiss the panel. Callers wire their button's `@click` straight to this rather
 * than flipping the bound value themselves, so all three entry points - pointer, keyboard,
 * and hover-intent - go through one door.
 *
 * A click on a panel the POINTER ALREADY OPENED does nothing, which is the whole reason
 * this is not a plain flip. With a mouse, clicking a submenu row means "yes, this one" -
 * but the pointer is by definition hovering the row it clicks, so hover has already opened
 * the panel and a toggle would read that click as "close" and shut it in the user's face.
 * The other two dismissals still work: click away, or press Escape.
 *
 * No reopen guard is needed, per the note on `useDropdownClose` above: on the anchor path
 * the composable never closes, so there is never a close for this click to undo.
 */
function toggle() {
  if (props.open && openedByHover) return
  openedByHover = false
  emit('update:open', !props.open)
}

// ── Hover ─────────────────────────────────────────────────────────────────────
//
// A submenu opens on hover; the click is for touch, where there is no hover to have.
// Two timers, because both edges need slack:
//
// - Opening waits out a short intent delay, so dragging the pointer down a column of rail
//   buttons on the way somewhere else does not flash a panel at every one it crosses.
// - Closing waits out a grace period, because the pointer has to cross the gap between the
//   anchor and the panel, and for that moment it is over neither. Closing on the first
//   `pointerleave` would make the panel unreachable.
//
// Both are cancelled by the opposite intent, so the last thing the pointer did wins. A
// pointer that never reports hover (`pointerenter` with `pointerType: 'touch'` fires on
// tap, immediately followed by the click) is ignored here and left to `toggle()`, or the
// tap would open the panel and the click would close it again.

/** How long the pointer must rest on the anchor before the panel opens. */
const HOVER_OPEN_DELAY_MS = 120
/** How long the panel survives the pointer being over neither it nor the anchor. */
const HOVER_CLOSE_GRACE_MS = 220

let openTimer: ReturnType<typeof setTimeout> | undefined
let closeTimer: ReturnType<typeof setTimeout> | undefined
/**
 * Whether the open that is about to happen came from the pointer merely passing over the
 * anchor. Read once by the open watcher, which takes the focus only when it did NOT - see
 * there. Not a ref: nothing renders from it.
 */
let openedByHover = false

function clearHoverTimers() {
  clearTimeout(openTimer)
  clearTimeout(closeTimer)
  openTimer = undefined
  closeTimer = undefined
}

function onHoverEnter(e: PointerEvent) {
  if (e.pointerType === 'touch') return
  clearHoverTimers()
  if (props.open) return
  openTimer = setTimeout(() => {
    openedByHover = true
    emit('update:open', true)
  }, HOVER_OPEN_DELAY_MS)
}

function onHoverLeave(e: PointerEvent) {
  if (e.pointerType === 'touch') return
  clearHoverTimers()
  if (!props.open) return
  closeTimer = setTimeout(() => close(), HOVER_CLOSE_GRACE_MS)
}

// The anchor belongs to the caller, so its hover listeners are attached here rather than
// asking every caller to wire three more handlers onto its button.
useEventListener(
  () => props.anchor,
  'pointerenter',
  (e: PointerEvent) => onHoverEnter(e),
)
useEventListener(
  () => props.anchor,
  'pointerleave',
  (e: PointerEvent) => onHoverLeave(e),
)

onBeforeUnmount(clearHoverTimers)

// `panelEl` is exposed because the panel is teleported: a host menu with its own
// outside-click watcher sees clicks inside this panel as landing outside itself, and needs
// the real element to add to its `ignore` list. `$el` cannot serve - the root here is a
// Teleport, which has no element of its own.
defineExpose({ toggle, panelEl: panelRef })

function close() {
  // A close from any source settles the hover question too: without this, a grace-period
  // timer left running by an earlier `pointerleave` would fire into an already-closed
  // panel and emit a second, spurious `close`.
  clearHoverTimers()
  emit('update:open', false)
  emit('close')
}

function place() {
  const anchorEl = props.anchor
  const panel = panelRef.value
  if (!anchorEl || !panel) return

  const a = anchorEl.getBoundingClientRect()
  // Measured unclamped, so the fit tests below ask about the size the panel WANTS rather
  // than the size a previous, tighter open left it at.
  //
  // The height these tests need is the one the panel WANTS, not the one an earlier pass
  // allowed it - otherwise the clamp is sticky downward: it could only ever shrink across
  // re-places and would never recover the room a resize gave back.
  //
  // So the cap comes off before measuring and goes back on after. Both writes are made
  // straight to the ELEMENT, never through a ref: a ref reaches the DOM on Vue's next
  // flush - i.e. after the reads below - which is what made the measurement stale in the
  // first place. An element write applies immediately, so `offsetHeight` on the next line
  // is the unclamped height.
  //
  // `scrollHeight` cannot substitute for this and save the reflow: this panel is
  // `overflow: hidden` and it is the inner `.ws-list` that scrolls, so the panel itself
  // never overflows and its `scrollHeight` is just its clamped height again.
  // Both caps come off together: the width one narrows the panel, which reflows its rows
  // and changes the height, so measuring either while the other is applied measures a
  // panel shaped by the last pass rather than the one this pass has to place.
  panel.style.maxHeight = ''
  panel.style.maxWidth = ''
  const width = panel.offsetWidth

  const vw = window.innerWidth
  const vh = window.innerHeight

  // ── Horizontal ──
  //
  // The band the panel is allowed to occupy. Normally the viewport less its margins; with
  // `keepClearOf` it is also cut back to one side of that surface, so the panel sits BESIDE
  // the rail or menu it belongs to and never over it. Which side: whichever the anchor is
  // on, since the anchor lives in the surface being kept clear.
  let bandStart = VIEWPORT_MARGIN
  let bandEnd = vw - VIEWPORT_MARGIN
  const clear = props.keepClearOf?.getBoundingClientRect()
  if (clear && clear.width > 0) {
    // `>=`, not `>`, and that matters: a menu ROW is the full width of its menu, so the two
    // centres are equal and the tie has to fall to the first branch. The other branch would
    // leave a sliver between the menu's far edge and the viewport margin - on the wrong
    // side of a menu already docked to that edge.
    const anchorCentre = (a.left + a.right) / 2
    if (anchorCentre >= (clear.left + clear.right) / 2) {
      bandEnd = Math.min(bandEnd, clear.left - KEEP_CLEAR_GAP)
    } else {
      bandStart = Math.max(bandStart, clear.right + KEEP_CLEAR_GAP)
    }
    // Keeping clear is worth a narrower panel, but not a useless one. In the VSTO task
    // pane (~240px wide) the hamburger menu spans most of the window, and the band beside
    // it comes out ~56px - a sliver nobody registers as a panel at all, which read as
    // "hover does nothing". Below a usable width, overlaying the surface beats being
    // invisible beside it, so the band falls back to the whole viewport.
    if (bandEnd - bandStart < MIN_USABLE_WIDTH) {
      bandStart = VIEWPORT_MARGIN
      bandEnd = vw - VIEWPORT_MARGIN
    }
  }

  // A window too narrow for both is the case this is all for: the panel gives up its own
  // width rather than the ground it was told to keep clear, so the rail stays fully
  // visible and the panel scrolls inside what is left.
  const bandWidth = Math.max(0, bandEnd - bandStart)
  const fittedWidth = Math.min(width, bandWidth)
  panel.style.maxWidth = fittedWidth < width ? `${fittedWidth}px` : ''

  const roomLeft = a.left - bandStart
  const roomRight = bandEnd - a.right
  const need = fittedWidth + ANCHOR_GAP
  const preferLeft = props.prefer === 'left'
  const fitsPreferred = (preferLeft ? roomLeft : roomRight) >= need
  const fitsOther = (preferLeft ? roomRight : roomLeft) >= need

  if (fitsPreferred || !fitsOther) {
    // Preferred side, or neither fits and we stay put rather than flipping for nothing.
    left.value = preferLeft ? a.left - fittedWidth - ANCHOR_GAP : a.right + ANCHOR_GAP
  } else {
    left.value = preferLeft ? a.right + ANCHOR_GAP : a.left - fittedWidth - ANCHOR_GAP
  }
  // The final word regardless of which side won: the panel starts no further out than the
  // band allows, so it stays on screen AND off the surface it must keep clear.
  left.value = Math.max(bandStart, Math.min(left.value, bandEnd - fittedWidth))

  // ── Vertical: aligned to the anchor's top, lifted to fit, then capped ──
  //
  // Re-measured, because the width cap above may have narrowed the panel and rewrapped its
  // rows. The height taken before that would be the height of a wider panel.
  const height = panel.offsetHeight
  const available = vh - 2 * VIEWPORT_MARGIN
  // A panel taller than the viewport gets capped and scrolls its list; one that merely
  // hangs off the bottom is lifted until it fits. An empty string removes the cap, which is
  // how a panel clamped by an earlier pass expands again once a resize gives the room back.
  const cap = height > available ? `${available}px` : ''
  // Unconditional, because the measure above already took any cap off - this is the write
  // that puts the right one back, not an extra one.
  //
  // The panel's own ResizeObserver calls `place()`, so this looks like a loop, and is not:
  // the clear and this restore happen in the same synchronous pass, with only layout reads
  // between them. The observer reports the size at the end of the frame, which for a pass
  // that lands on the cap already in force is the size it was already at - no change to
  // report, nothing to re-enter.
  //
  // That symmetry is the invariant: an `await` between the clear and this restore would
  // split them across frames and bring the loop straight back. `place()` stays synchronous.
  panel.style.maxHeight = cap

  const effectiveHeight = Math.min(height, available)
  top.value = Math.max(
    VIEWPORT_MARGIN,
    Math.min(a.top, vh - effectiveHeight - VIEWPORT_MARGIN),
  )

  placed.value = true
}

/**
 * Whether the anchor is still visible inside the surface it belongs to.
 *
 * The dropdown is itself a scroll box with a `max-height`, so in a short window its rows
 * can scroll out of view. A panel that merely re-placed would follow its row up past the
 * menu's top edge and hang there beside a row nobody can see - so it closes instead.
 * Only meaningful with `keepClearOf`; a rail that spans the whole window never clips.
 */
function anchorIsVisible(): boolean {
  const clear = props.keepClearOf?.getBoundingClientRect()
  const a = props.anchor?.getBoundingClientRect()
  if (!clear || !a) return true
  return a.bottom > clear.top && a.top < clear.bottom
}

// Re-place on anything that moves the anchor or changes the room around it. Capture-phase
// scroll: scrolls on inner containers don't bubble, and the anchor may sit in one.
function followAnchor() {
  if (!props.open) return
  place()
}

// A resize only ever re-places: the anchor is still the row the reader picked, and a
// window that got smaller is exactly when the clamping above earns its keep. Closing here
// would take the panel away mid-drag.
useEventListener(window, 'resize', followAnchor)

// A scroll can take the anchor out of its own container, though, and a panel left beside a
// row nobody can see is worse than no panel - so that one closes rather than following.
useEventListener(
  window,
  'scroll',
  () => {
    if (!props.open) return
    if (!anchorIsVisible()) {
      close()
      return
    }
    place()
  },
  true,
)

// The panel's own height changes as rows are added, deleted, or swapped for a rename box
// or a delete confirmation - each of which can push it past the bottom of a short window.
let observer: ResizeObserver | null = null

watch(
  () => props.open,
  async (isOpen) => {
    observer?.disconnect()
    observer = null
    if (!isOpen) {
      placed.value = false
      return
    }
    await nextTick()
    place()
    const panel = panelRef.value
    if (!panel) return
    // Focus the panel so Escape has somewhere to land. Without this the rail has no
    // keyboard dismissal at all: its buttons are `tabindex="-1"`, so after opening one the
    // focus is still on a button in a different DOM tree from this teleported panel, and
    // an Escape keydown there never passes through it.
    //
    // Not on a hover-open, though - the pointer merely passing over a button must not take
    // the focus away from whatever the reader was typing in.
    if (!openedByHover) panel.focus({ preventScroll: true })
    observer = new ResizeObserver(() => props.open && place())
    observer.observe(panel)
  },
)

onBeforeUnmount(() => observer?.disconnect())
</script>

<template>
  <Teleport to="body">
    <div
      v-if="open"
      ref="panelRef"
      class="ws-submenu"
      :style="{
        top: `${top}px`,
        left: `${left}px`,
        visibility: placed ? 'visible' : 'hidden',
      }"
      tabindex="-1"
      @click.stop
      @pointerenter="onHoverEnter"
      @pointerleave="onHoverLeave"
      @keydown.escape.stop="close()"
    >
      <WorkspaceMenu @close="close" />
    </div>
  </Teleport>
</template>

<style scoped>
/* Its own sheet: unlike the rail and the menu it hangs off, this one floats, so it is the
   one that casts the shadow (see CommentaryPanelHost - in this app only floating panels
   do). `left`/`top` are set inline by `place()`. */
.ws-submenu {
  position: fixed;
  /* 9999, matching AppTitleBarBreadcrumbChevronDropdown - the closest peer, a teleported
     dropdown off this same title bar. A number picked against the hamburger menu's 200
     would be wrong: teleporting to the body takes the panel out of that menu's stacking
     context and puts it up against the app's body-level layers, which run to 10001. */
  z-index: 9999;
  display: flex;
  flex-direction: column;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.18);
  overflow: hidden;
  direction: rtl;
}

/* The clamp lands on this element, so the menu inside it has to be the thing that shrinks
   - its list scrolls, and the create box below the list stays put, since that is the one
   row that must always be reachable no matter how short the window is. */
.ws-submenu :deep(.ws-menu) {
  flex: 1;
  min-height: 0;
  /* The menu's preferred 200px is a preference, not a floor: in a window too narrow for
     both the panel and the surface it must keep clear, the width cap on the panel is what
     wins, and the menu has to be able to follow it down. */
  min-width: 0;
}
</style>
