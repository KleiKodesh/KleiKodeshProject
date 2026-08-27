<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import { useEventListener, useResizeObserver } from '@vueuse/core'
import { useDropdownClose } from '@/composables/useDropdownClose'

/**
 * The toolbar buttons a too-narrow pane could not show, as a labelled flyout off the
 * toolbar's "more" button.
 *
 * The split of duties with the toolbar: the toolbar decides WHAT collapsed
 * (useBookViewToolbarOverflow) and renders each collapsed control's row into the default
 * slot, so a row keeps the same component - and therefore the same behaviour - it had on
 * the toolbar. This panel owns only where the sheet lands and when it closes.
 *
 * Teleported to the body like every floating panel here: the toolbar sits inside panes and
 * split-view containers that clip and stack, and a panel positioned inside it would be
 * clipped by them.
 */
const props = defineProps<{
  open: boolean
  /** The toolbar's "more" button. */
  anchor: HTMLElement | null
  /** The toolbar itself - the panel opens beside it, never over it. */
  keepClearOf: HTMLElement | null
  /** Which edge the toolbar is docked to, which decides the side the panel opens toward. */
  toolbarPosition: 'top' | 'bottom' | 'left' | 'right'
}>()

const emit = defineEmits<{ 'update:open': [boolean] }>()

/** Breathing room kept between the panel and the viewport edges. */
const VIEWPORT_MARGIN = 8
/** Gap between the toolbar and the panel - two framed surfaces must not touch. */
const KEEP_CLEAR_GAP = 6

const panelRef = ref<HTMLElement | null>(null)
const top = ref(0)
const left = ref(0)
/** Hidden for the first frame: `place()` has to measure the panel before it can place it. */
const placed = ref(false)

// The anchor is passed as the toggle so the composable leaves a click on it to the
// button's own @click, instead of closing on pointerdown and reopening on click.
useDropdownClose(panelRef, () => close(), {
  toggleButton: computed(() => props.anchor),
  enabled: () => props.open,
})

function close() {
  emit('update:open', false)
}

/**
 * Set while `place()` is writing to the panel, and cleared a frame later.
 *
 * `place()` runs from a ResizeObserver on the panel and writes the panel's `maxHeight`, which
 * is the shape that produces "ResizeObserver loop completed with undelivered notifications".
 * The observation it needs is real - the row count changes with the window - so the answer is
 * to ignore the callbacks its own writes provoke rather than to stop observing. Cleared on a
 * frame boundary because that is when the observer has delivered them.
 *
 * Not a ref: nothing renders from it.
 */
let placing = false

function place() {
  const anchorEl = props.anchor
  const panel = panelRef.value
  if (!anchorEl || !panel) return

  placing = true
  requestAnimationFrame(() => {
    placing = false
  })

  const anchorRect = anchorEl.getBoundingClientRect()
  const barRect = props.keepClearOf?.getBoundingClientRect() ?? anchorRect
  const viewportWidth = window.innerWidth
  const viewportHeight = window.innerHeight

  // The cap comes off before measuring so the height read is the one the panel WANTS, not
  // the one a previous, shorter open left it clamped to, then goes back on. Written straight
  // to the element - a ref would reach the DOM only after these reads.
  //
  // Those two writes are why `place()` guards its own re-entry (see `placing`): it also runs
  // from a ResizeObserver on this very element, and a style write inside that callback
  // re-triggers the observer. Guarding the writes on the value changing does NOT help - the
  // panel only needs a cap when it is taller than the viewport, and in exactly that case both
  // writes change the value every pass, so the guard never fires on the path that loops.
  const availableHeight = viewportHeight - 2 * VIEWPORT_MARGIN
  panel.style.maxHeight = ''
  const wantedHeight = panel.offsetHeight
  panel.style.maxHeight = wantedHeight > availableHeight ? `${availableHeight}px` : ''
  const height = Math.min(wantedHeight, availableHeight)
  const width = panel.offsetWidth

  // Off the toolbar's own edge, on the side the page is: a toolbar docked to the top opens
  // downward, one docked to the left opens rightward, and so on. Clamped into the viewport
  // afterwards, which is what keeps it on screen in the short or narrow window that made
  // the toolbar overflow in the first place.
  const isVertical = props.toolbarPosition === 'left' || props.toolbarPosition === 'right'
  if (isVertical) {
    top.value = anchorRect.top
    left.value =
      props.toolbarPosition === 'right'
        ? barRect.left - KEEP_CLEAR_GAP - width
        : barRect.right + KEEP_CLEAR_GAP
  } else {
    top.value =
      props.toolbarPosition === 'top'
        ? barRect.bottom + KEEP_CLEAR_GAP
        : barRect.top - KEEP_CLEAR_GAP - height
    // Right-aligned to the button: the document is RTL, so a panel wider than its anchor
    // grows toward the page rather than off the edge the toolbar is against.
    left.value = anchorRect.right - width
  }

  top.value = Math.max(VIEWPORT_MARGIN, Math.min(top.value, viewportHeight - height - VIEWPORT_MARGIN))
  left.value = Math.max(VIEWPORT_MARGIN, Math.min(left.value, viewportWidth - width - VIEWPORT_MARGIN))

  placed.value = true
}

// A resize can hand back the room the toolbar was missing, at which point the parent hides
// the "more" button and closes this panel - but until it does, the panel must stay placed
// against wherever its anchor moved to.
useEventListener(window, 'resize', () => props.open && place())

// The panel's row count is not fixed while it is open: a resize that takes more room away
// collapses another control into it, making the sheet taller. Every such change needs the
// clamp run again, or a panel that fitted when it opened hangs off the bottom. Observing
// the element covers that without the toolbar having to announce it - and `placing` is what
// keeps `place()`'s own writes from coming straight back round as another callback.
useResizeObserver(panelRef, () => {
  if (props.open && !placing) place()
})

watch(
  () => props.open,
  async (isOpen) => {
    if (!isOpen) {
      placed.value = false
      return
    }
    await nextTick()
    place()
  },
)
</script>

<template>
  <Teleport to="body">
    <div
      v-if="open"
      ref="panelRef"
      class="toolbar-overflow-menu"
      :style="{
        top: `${top}px`,
        left: `${left}px`,
        visibility: placed ? 'visible' : 'hidden',
      }"
      tabindex="-1"
      role="menu"
      @keydown.escape.stop="close()"
    >
      <slot />
    </div>
  </Teleport>
</template>

<style scoped>
/* A floating sheet, so it is one that casts a shadow (in this app only floating panels do).
   Same surface, frame and z-index rationale as the nav rail's overflow menu: teleporting to
   the body puts it against the app's body-level layers, not the toolbar's own stack. */
.toolbar-overflow-menu {
  position: fixed;
  z-index: 9999;
  display: flex;
  flex-direction: column;
  min-width: 180px;
  padding: 4px 0;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.18);
  direction: rtl;
  outline: none;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}
/* Rows arrive through the slot as the toolbar's own controls, so the panel gives them their
   shape rather than each control carrying a copy of it - and it has to be the panel that
   does: the slot content is teleported out of the toolbar's subtree with this sheet, where
   the toolbar's own scoped rules no longer reach it. `:deep` from the panel, which IS in
   this component's scope, does.

   One shape for every row, whether the control that wears it is a button (export, sync,
   diacritics) or a strip of its own (the zoom pair): full width, one height, and the icon
   leading the label. */
.toolbar-overflow-menu :deep(.overflow-row) {
  display: flex;
  align-items: center;
  justify-content: flex-start;
  gap: 10px;
  width: 100%;
  height: 32px;
  padding: 0 10px;
  border-radius: 0;
  font-size: 13px;
  color: var(--text-primary);
  text-align: right;
  white-space: nowrap;
}
/* A whole-row hover reads as "this row does something", so only the rows that ARE one
   action get it. The zoom row is a heading beside two buttons, which light up separately. */
.toolbar-overflow-menu :deep(button.overflow-row:hover) {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
/* Fixed at the toolbar's icon size: these glyphs are drawn for 16 and would otherwise take
   whatever intrinsic size their own viewBox implies, which is what leaves a row's label
   sitting at a different place from the row above it. */
.toolbar-overflow-menu :deep(.overflow-row svg) {
  width: 16px;
  height: 16px;
  flex-shrink: 0;
}
/* The label takes the rest of the row, so every row is the same width and a hover band
   covers all of it. */
.toolbar-overflow-menu :deep(.overflow-row > span) {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
}
/* The zoom pair: its heading takes the row and the two buttons sit at the end, square and
   the size they are on the toolbar, so the row reads as a label with a control rather than
   as three things in a line. */
.toolbar-overflow-menu :deep(.overflow-row-zoom) {
  gap: 4px;
}
.toolbar-overflow-menu :deep(.overflow-row-zoom > button) {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  flex-shrink: 0;
  border-radius: 4px;
}
.toolbar-overflow-menu :deep(.overflow-row-zoom > button:disabled) {
  opacity: 0.4;
  cursor: not-allowed;
}
</style>
