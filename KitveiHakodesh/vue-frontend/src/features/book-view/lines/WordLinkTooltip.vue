<script setup lang="ts">
/**
 * Hover preview panel for the reading views. Two callers: the word-link preview
 * (useWordLinkTooltip — target line content headed by the target book's title) and
 * the user-note preview (useNoteTooltip — the note's own text, `interactive: false`).
 *
 * Positioning follows the BookViewAbbrevTooltip pattern:
 * Teleported to body, rendered hidden first so real dimensions can be measured,
 * then fixed beside the anchor — on whichever side has more room, with the height
 * capped to that side so no part of the panel can ever fall outside the viewport.
 * See computePosition for why the height is capped rather than the panel nudged.
 *
 * The parent keys this component by hover id so a new target remounts and
 * re-measures. Content is trusted seforim-DB HTML (same trust level as the book
 * lines themselves); the divine-name censor is applied at render, mirroring the
 * FTS snippet renderer.
 *
 * Long content scrolls inside the tooltip, so unlike a pure decoration this one
 * accepts the pointer: it emits `pointer-enter`/`pointer-leave` and the host
 * composable keeps it open while the pointer is inside. A `::before` on the root
 * spans the MARGIN between anchor and tooltip so travelling there never crosses
 * dead space where a `mouseout` would look like leaving — it hangs off whichever
 * edge faces the anchor, hence the `is-above`/`is-below` class.
 */
import { ref, computed, onMounted, nextTick } from 'vue'
import { useEventListener } from '@vueuse/core'
import { IconDismiss20Regular } from '@iconify-prerendered/vue-fluent'
import { useSettingsStore } from '@/stores/settingsStore'
import { censorDivineNames } from '@/utils/censorDivineNames'
import type { WordLinkTooltipData } from './useWordLinkTooltip'

const props = withDefaults(
  defineProps<{
    data: WordLinkTooltipData
    /**
     * Whether the panel accepts the pointer. True (the default) for the word-link
     * preview, whose long content scrolls and whose text is selectable. False for
     * the user-note preview: its content is short, its editable original is one
     * click away, and capturing the pointer there would fight the marker's own
     * click-to-edit. A static panel needs no gap bridge either.
     */
    interactive?: boolean
  }>(),
  { interactive: true },
)
const emit = defineEmits<{
  'pointer-enter': []
  'pointer-leave': []
  /** A mousedown inside — possibly the start of a selection drag. */
  'select-start': []
  /** The close button was pressed — dismiss unconditionally, ignoring hover state. */
  close: []
}>()

const settingsStore = useSettingsStore()
const html = computed(() => censorDivineNames(props.data.html, settingsStore.censorOptions))

/**
 * The target's TOC section, one entry per line, with the cited line flagged. Every
 * line goes through the same censor as the single-line path — the section is book
 * content on exactly the same footing, and censoring only the focus line would leak
 * through its neighbours.
 *
 * Null for a single-line preview, which the template renders from `html` instead.
 */
const sectionLines = computed(() => {
  const section = props.data.section
  if (!section) return null
  return section.lines.map((line, index) => ({
    id: line.id,
    html: censorDivineNames(line.html, settingsStore.censorOptions),
    isFocus: index === section.focusIndex,
  }))
})

/**
 * Book title followed by the target line's full TOC path. Either part may be
 * absent — a line with no line_toc row yields no path — so both are filtered
 * before joining, and the header is hidden entirely when nothing is left.
 */
const heading = computed(() => {
  const parts = [props.data.bookTitle, props.data.tocPath].filter(Boolean)
  return parts.join(' — ')
})

const tooltipRef = ref<HTMLElement | null>(null)
const bodyRef = ref<HTMLElement | null>(null)
const resolvedTop = ref<number | null>(null)
const resolvedLeft = ref<number | null>(null)
/** Which edge faces the anchor — the gap bridge is drawn on that side. */
const placement = ref<'above' | 'below'>('above')

const MARGIN = 8
const MAX_WIDTH = 360

/**
 * The smallest height the panel can actually occupy: border + padding + the header
 * line, none of which shrink. Asking for less via max-height is ignored by layout,
 * so the placement maths must work in terms of this floor or it computes positions
 * for a panel size that cannot exist — which is how a "contained" panel ends up
 * hanging off the edge in a short viewport.
 */
const MIN_HEIGHT = 40

/**
 * Hard ceiling on the panel height, resolved against the space actually available
 * on the chosen side. The panel must never render even partly outside the viewport,
 * so this is applied as an inline `max-height` that overrides the stylesheet's
 * preferred cap — the preferred size is a comfort target, this is a constraint.
 */
const resolvedMaxHeight = ref<number | null>(null)

/**
 * Places the panel so that no part of it falls outside the viewport, under any
 * anchor position or content size.
 *
 * Side choice is by available room, not merely by clipping: picking "above unless
 * the top clips" hands a link near the top a below-placement that may be the more
 * cramped of the two. Whichever side has more room wins, ties going to above (the
 * habitual reading position for these previews).
 *
 * The height is then capped to that side's room rather than the panel being pushed
 * back inside, because pushing would slide the panel over its own anchor — the link
 * would end up underneath the preview, and moving the pointer to reach the panel
 * would cross the link again. Capping keeps the anchor clear; the content scrolls,
 * which it already does.
 *
 * Every value is finally clamped, so even a viewport too small for the minimum
 * usable panel yields an in-bounds rect rather than a negative offset.
 */
function computePosition() {
  const rect = props.data.anchorRect
  const viewportW = window.innerWidth
  const viewportH = window.innerHeight
  const width = tooltipRef.value?.offsetWidth ?? MAX_WIDTH
  const height = tooltipRef.value?.offsetHeight ?? 60

  // Center horizontally on the link, then clamp. Math.max runs LAST so that a panel
  // wider than the viewport pins to the near edge instead of resolving to a negative
  // left — with the two clamps the other way round, the upper bound wins and the
  // panel starts off-screen.
  let left = rect.left + rect.width / 2 - width / 2
  left = Math.max(MARGIN, Math.min(left, viewportW - width - MARGIN))

  // Room between the anchor and each viewport edge, once the gap and margin are paid.
  const roomAbove = rect.top - MARGIN * 2
  const roomBelow = viewportH - rect.bottom - MARGIN * 2

  const placeAbove = roomAbove >= roomBelow
  placement.value = placeAbove ? 'above' : 'below'

  // The panel cannot actually be squeezed below MIN_HEIGHT: the header does not
  // shrink (it is flex-shrink: 0 and always present on an interactive panel), and
  // the border and padding are fixed. So a max-height under that floor would be
  // silently ignored and the panel would overhang the edge it was being fitted to.
  // Clamp the height to what the panel can really be, and let the final top clamp
  // below decide where a panel that large actually fits.
  const room = Math.max(0, placeAbove ? roomAbove : roomBelow)
  const constrainedHeight = Math.max(MIN_HEIGHT, Math.min(height, room))

  // Anchored to the near edge of the link on the chosen side, then clamped to the
  // viewport. The clamp uses the REAL height, so when the preferred side is too
  // small the panel slides to where it fits rather than overhanging — at the cost
  // of overlapping the anchor, which beats rendering outside the viewport.
  let top = placeAbove ? rect.top - MARGIN - constrainedHeight : rect.bottom + MARGIN
  top = Math.max(MARGIN, Math.min(top, viewportH - constrainedHeight - MARGIN))

  // A viewport too short for even the minimum panel has no in-bounds placement;
  // pin to the top edge so the overflow is at the bottom, where it is at least
  // predictable, rather than splitting across both edges.
  if (viewportH < constrainedHeight + MARGIN * 2) top = MARGIN

  resolvedMaxHeight.value = Math.max(MIN_HEIGHT, room)
  resolvedTop.value = top
  resolvedLeft.value = left
}

const style = computed(() => {
  if (resolvedTop.value === null) {
    // Not yet measured: render invisible so dimensions can be read on mount
    return {
      position: 'fixed' as const,
      top: '-9999px',
      left: '-9999px',
      maxWidth: `${Math.min(MAX_WIDTH, window.innerWidth - MARGIN * 2)}px`,
      zIndex: '9998',
      visibility: 'hidden' as const,
    }
  }
  return {
    position: 'fixed' as const,
    top: `${resolvedTop.value}px`,
    left: `${resolvedLeft.value}px`,
    maxWidth: `${Math.min(MAX_WIDTH, window.innerWidth - MARGIN * 2)}px`,
    // Overrides the stylesheet's preferred cap. That one is a comfort target; this
    // is the constraint that keeps the panel inside the viewport, so it has to win
    // — hence inline, and hence unconditional rather than only when it bites.
    maxHeight: `${resolvedMaxHeight.value}px`,
    zIndex: '9998',
  }
})

/**
 * True when a STATIC panel's content is taller than the panel. Such a panel cannot be
 * scrolled — it never takes the pointer — so the overflow is faded out to read as "there
 * is more of this note" instead of as the note's end. Measured rather than assumed: a
 * short note must not get a faded last line.
 */
const clipped = ref(false)

/**
 * Bring the cited line to the top of the scroll area, so the citation reads as the
 * subject with its section continuing below it. Scrolled by measured `offsetTop`
 * rather than `scrollIntoView`, which walks up the DOM and would scroll ancestors —
 * and which `content-visibility: auto` on the lines makes unreliable anyway, since a
 * skipped line reports its `contain-intrinsic-size` estimate instead of its real
 * height. Setting `scrollTop` moves only this container, and the browser renders the
 * lines it lands on before paint, so the estimate never reaches the screen.
 *
 * A focus line at the very start needs no scroll at all, which is also the common
 * case — a citation usually opens its section.
 */
/**
 * Frames the correction loop below is allowed to run for. Each pass renders the
 * lines it lands among, which shifts the offsets above it, so the target converges
 * rather than being known up front. Convergence is normally 2-3 passes; the cap is
 * a backstop against a layout that never settles, not an expected cost.
 */
const MAX_SCROLL_PASSES = 8

/**
 * Whether two scroll positions are the same position on screen.
 *
 * NOT `===`: `offsetTop` is a rounded integer while `scrollTop` reads back as a
 * fractional double once the browser snaps it to the device pixel grid — which it
 * does at any non-integer display scaling, and this app runs in WebView2 where
 * 125%/150% Windows scaling is the norm. Exact equality would then be permanently
 * false, the convergence test would never fire, and the pass cap would silently
 * become the only exit — load-bearing rather than the backstop it is meant to be.
 */
function isSamePosition(a: number, b: number): boolean {
  return Math.abs(a - b) < 1
}

function scrollToFocusLine() {
  const body = bodyRef.value
  if (!body || !props.data.section) return
  const line = body.querySelector<HTMLElement>('[data-focus-line]')
  if (!line) return

  // The lines above the focus line are `content-visibility: auto`, so until they
  // render they contribute their `contain-intrinsic-size` estimate and `offsetTop`
  // is an estimate too — one scroll lands near the target, not on it. Scrolling
  // there forces the lines around the landing point to render, which corrects the
  // offsets above and moves the target again.
  //
  // So iterate rather than correcting once: re-read and re-scroll until the offset
  // stops changing. Runs in rAF, before paint, so the intermediate positions are
  // never shown — the panel appears already at the cited line.
  let passes = 0
  // What this loop last wrote, so a pass can tell its own effect apart from the
  // user's. null until the first write.
  let written: number | null = null
  const settle = () => {
    const el = bodyRef.value
    if (!el) return
    // The user took over — scrolled the panel themselves since the last pass. Their
    // position wins: re-asserting the target here would yank the panel back under
    // them. Compared against what this loop wrote rather than against the target,
    // so a pass that simply has not converged yet is not mistaken for a user scroll.
    if (written !== null && !isSamePosition(el.scrollTop, written)) return
    // Clamped to what the container can actually reach: a focus line near the end
    // of the section cannot be brought to the top, and comparing against the raw
    // offset would then never match and burn every pass on an unreachable target.
    const target = Math.min(line.offsetTop, el.scrollHeight - el.clientHeight)
    if (isSamePosition(el.scrollTop, target) || ++passes > MAX_SCROLL_PASSES) return
    el.scrollTop = target
    written = el.scrollTop
    requestAnimationFrame(settle)
  }
  settle()
}

onMounted(() => {
  nextTick(async () => {
    // Measures the panel at its natural (stylesheet-capped) size and resolves the
    // placement, which may impose a SMALLER max-height for the side it chose.
    computePosition()

    // That new cap is a reactive style binding, so it is not on the element yet.
    // Everything below measures the scroll area, and measuring it at the pre-cap
    // height would read a viewport the panel never actually has — so wait for the
    // style to flush before touching clientHeight or any offset within it.
    await nextTick()

    const body = bodyRef.value
    if (!body) return
    clipped.value = !props.interactive && body.scrollHeight > body.clientHeight + 1
    scrollToFocusLine()
  })
})

/**
 * A viewport that changes under an open panel would strand it outside the bounds
 * computed on mount. Re-placing is NOT the fix: `anchorRect` is a snapshot taken
 * when the hover began, and a resize reflows the scroller underneath, so the link
 * has moved but the rect has not — the panel would re-place itself against where
 * the link used to be, and the gap bridge would then span empty space.
 *
 * So dismiss instead, exactly as scrolling the underlying view already does (see
 * the scroll listener in useWordLinkTooltip). A hover preview is cheap to
 * re-summon and has no state worth preserving across a layout change.
 */
useEventListener(() => window, 'resize', () => emit('close'))
</script>

<template>
  <Teleport to="body">
    <div
      ref="tooltipRef"
      class="word-link-tooltip"
      :class="[`is-${placement}`, { 'is-static': !interactive, 'is-clipped': clipped }]"
      :style="style"
      dir="rtl"
      @mouseenter="emit('pointer-enter')"
      @mouseleave="emit('pointer-leave')"
      @mousedown.left="emit('select-start')"
    >
      <!-- Shown when there is a heading OR a close button to hold; an interactive
           panel always has the latter, so it always keeps its header line. -->
      <div v-if="heading || interactive" class="word-link-tooltip-title">
        <span class="word-link-tooltip-heading">{{ heading }}</span>
        <button
          v-if="interactive"
          class="word-link-tooltip-close"
          type="button"
          title="סגור"
          aria-label="סגור"
          @click="emit('close')"
          @mousedown.left.stop
        >
          <IconDismiss20Regular />
        </button>
      </div>
      <div ref="bodyRef" class="word-link-tooltip-body">
        <template v-if="sectionLines">
          <!-- eslint-disable-next-line vue/no-v-html -->
          <div
            v-for="line in sectionLines"
            :key="line.id"
            class="word-link-tooltip-line"
            :class="{ 'is-focus': line.isFocus }"
            :data-focus-line="line.isFocus ? '' : undefined"
            v-html="line.html"
          />
        </template>
        <!-- eslint-disable-next-line vue/no-v-html -->
        <div v-else v-html="html" />
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.word-link-tooltip {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 3px;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.12);
  padding: 6px 0;
  direction: rtl;
  font-family: 'Segoe UI Variable', 'Segoe UI', system-ui, sans-serif;
  font-size: 12.5px;
  line-height: 1.7;
  color: var(--text-primary);
  /* Long previews scroll, and the user must be able to reach that scrollbar — so
     this accepts the pointer, stated outright rather than left to the initial
     value, because the usual tooltip default here is pointer-events: none. */
  pointer-events: auto;
  display: flex;
  flex-direction: column;
  /* Roughly square: the panel is MAX_WIDTH (360px) wide, so capping the height near
     that keeps a section preview comfortable to read without growing into a column
     that dominates the screen. The vh term keeps it inside short viewports; whichever
     is smaller wins. A full section is normally longer than this — it scrolls, and
     the cited line is scrolled to, so the cap costs no context. */
  max-height: min(380px, 60vh);
  min-height: 0;
}

.word-link-tooltip-title {
  padding-inline: 10px;
  font-weight: 600;
  font-size: 12px;
  color: var(--accent-color);
  padding-bottom: 4px;
  margin-bottom: 4px;
  border-bottom: 1px solid var(--border-color);
  display: flex;
  align-items: center;
  gap: 6px;
  /* The header must not scroll away with the content, and must not be squeezed
     out when the body fills the panel. */
  flex-shrink: 0;
}

/* Takes the slack so the close button is pushed to the end edge — which under
   this panel's `dir="rtl"` is the visual LEFT. Logical properties throughout, so
   the button stays on the correct side without hard-coding a direction. */
.word-link-tooltip-heading {
  flex: 1;
  min-width: 0;
  /* A long book+path heading must not push the close button off the panel; it
     clips instead, matching the breadcrumb convention of clipping over ellipsis. */
  overflow: hidden;
  white-space: nowrap;
}

.word-link-tooltip-close {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 18px;
  height: 18px;
  padding: 0;
  margin-inline-end: -4px;
  border: none;
  border-radius: 3px;
  background: none;
  cursor: pointer;
  /* theme.css pins `svg { color: ... }`, which severs currentColor inheritance —
     the icon needs the colour handed to it explicitly. */
  color: var(--text-secondary);
}

.word-link-tooltip-close:hover {
  background: var(--bg-hover, rgba(127, 127, 127, 0.18));
  color: var(--text-primary);
}

.word-link-tooltip-close svg {
  width: 14px;
  height: 14px;
  color: inherit;
}

/* Bridges the MARGIN gap between anchor and tooltip so the pointer never crosses
   dead space on its way in. Lives on the root, which does NOT scroll — an
   overflow container would clip it. Sits on whichever edge faces the anchor. */
.word-link-tooltip::before {
  content: '';
  position: absolute;
  left: 0;
  right: 0;
  height: 10px;
}

.word-link-tooltip.is-above::before {
  top: 100%;
}

.word-link-tooltip.is-below::before {
  bottom: 100%;
}

/* Read-only preview: never takes the pointer, so it cannot swallow a click meant
   for the marker underneath, and needs no bridge to travel into. */
.word-link-tooltip.is-static {
  pointer-events: none;
}
.word-link-tooltip.is-static::before {
  content: none;
}

/* A preview taller than the panel cannot be scrolled — the pointer can never reach the
   scrollbar (see is-static above), and entering the panel would end the hover anyway. So
   don't offer one: clip, and fade the last line out so the cut is legible as "there is
   more here" rather than as the end of the note. The full text is one click away in the
   editable bubble. `is-clipped` is measured on mount, so a short note keeps a crisp last
   line. */
.word-link-tooltip.is-static.is-clipped .word-link-tooltip-body {
  overflow: hidden;
  mask-image: linear-gradient(to bottom, #000 calc(100% - 1.7em), transparent 100%);
}

.word-link-tooltip-body {
  /* The scroll container: keeping overflow off the root lets ::before escape it.
     Horizontal padding lives here rather than on the root so the scrollbar
     itself renders flush with the tooltip edge, with the gap on its inner side. */
  overflow-y: auto;
  min-height: 0;
  /* Makes this the offsetParent of the section lines, so their `offsetTop` is
     measured against the scroll container and can be assigned to `scrollTop`
     directly. Without it the nearest positioned ancestor is the fixed root, and
     every offset would carry the title bar's height as a constant overshoot.
     The gap bridge is unaffected — it hangs off the root, not this element. */
  position: relative;
  padding-inline: 10px;
  text-align: justify;
  /* Selectable — the global `* { user-select: none }` reset is opted out of in
     main.css, which this teleported element needs by name. */
  cursor: text;
  scrollbar-width: thin;
  scrollbar-color: color-mix(in srgb, var(--text-secondary) 30%, transparent) transparent;
}

/* One line of the target's TOC section.

   `content-visibility: auto` skips layout and paint for the lines scrolled out of
   view — a section runs to hundreds of lines and only a handful are ever on screen,
   and the panel is built on hover, where that work is paid for at the worst possible
   moment. `contain-intrinsic-size` supplies the placeholder height for the skipped
   ones, so the scrollbar stays stable instead of jumping as lines render; the value
   is a typical single wrapped line at this font-size and line-height, not a guess at
   any particular line. */
.word-link-tooltip-line {
  content-visibility: auto;
  contain-intrinsic-size: auto 44px;
}

/* The cited line — the reason the preview opened. Marked with a start-edge rule
   rather than a background so it reads as a margin annotation and leaves the text
   itself untouched. It is scrolled to the top of the panel on mount, so this marks
   which line that is once the user scrolls away from it.

   Never skipped: it is measured on mount to scroll to, and a skipped line reports
   its intrinsic-size estimate instead of its real offset. */
.word-link-tooltip-line.is-focus {
  content-visibility: visible;
  border-inline-start: 2px solid var(--accent-color);
  padding-inline-start: 6px;
  margin-inline-start: -8px;
}
</style>
