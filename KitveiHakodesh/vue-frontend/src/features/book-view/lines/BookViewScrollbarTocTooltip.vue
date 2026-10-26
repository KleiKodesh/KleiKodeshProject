<script setup lang="ts">
/**
 * The label showing the reader's current book and TOC path, shown beside the pointer
 * while it hovers the scrollbar track (see useBookViewScrollbarTocTooltip for how it
 * resolves). The title leads on its own line — it is the coarser answer, and it is
 * what remains when no TOC entry covers the current line.
 *
 * A plain label, not a panel: Teleported to body so no ancestor's overflow can clip
 * it, `pointer-events: none` so it never intercepts the scrollbar drag underneath,
 * and clamped to the viewport vertically. It hangs off the scrollbar's inner edge,
 * so it reads into the page rather than off the window edge.
 */
import { ref, computed } from 'vue'
import { useElementSize } from '@vueuse/core'
import type { ScrollbarTocTooltipData } from './useBookViewScrollbarTocTooltip'

const props = defineProps<{ data: ScrollbarTocTooltipData }>()

const labelEl = ref<HTMLElement | null>(null)
// Observed rather than measured once: this component is NOT remounted per update
// (there is no changing key), and the box resizes on its own as a drag swaps in
// paths that wrap to a different number of lines. A one-shot onMounted read would
// centre every later path against the first one's height.
//
// The BORDER box is what the centring wants (it is the full painted height), and
// asking for it explicitly also avoids a visible jump: useElementSize seeds its refs
// from offsetHeight — the border box — and only switches to the requested box once
// the ResizeObserver first fires. Left at the content-box default, the label would
// paint centred on the first frame, then drop by half its 12px of chrome on the next
// and stay 6px low from then on. Same trap, same fix as useBookViewToolbarOverflow.
const { height } = useElementSize(labelEl, undefined, { box: 'border-box' })

const GAP_PX = 8
const VIEWPORT_MARGIN_PX = 4

const style = computed(() => {
  const top = Math.min(
    Math.max(VIEWPORT_MARGIN_PX, props.data.pointerY - height.value / 2),
    Math.max(VIEWPORT_MARGIN_PX, window.innerHeight - height.value - VIEWPORT_MARGIN_PX),
  )
  const style: Record<string, string> = {
    top: `${top}px`,
    visibility: height.value ? 'visible' : 'hidden',
  }
  // The label extends INTO the page from the scrollbar's inner edge, so it is
  // anchored by that edge: `left` when the scrollbar is on the left, `right`
  // measured from the viewport's right when it is on the right.
  if (props.data.side === 'left') {
    style.left = `${props.data.edgeX + GAP_PX}px`
  } else {
    style.right = `${Math.max(0, window.innerWidth - props.data.edgeX) + GAP_PX}px`
  }
  return style
})
</script>

<template>
  <Teleport to="body">
    <div dir="rtl" ref="labelEl" class="scrollbar-toc-tooltip" :class="{ 'follows-thumb': data.followsThumb }" :style="style">
      <div v-if="data.bookTitle" class="book-title">{{ data.bookTitle }}</div>
      <div v-if="data.path" class="toc-path">{{ data.path }}</div>
    </div>
  </Teleport>
</template>

<style scoped>
.scrollbar-toc-tooltip {
  position: fixed;
  z-index: 3000;
  /* Never intercepts the pointer: the scrollbar it sits beside is being dragged. */
  pointer-events: none;
  max-width: min(280px, 40vw);
  /* DMScrollBar's info label: a dark rounded bubble with light text that fades and
     grows in beside the bar, inverted against the page in every theme. */
  padding: 2px 8px;
  border-radius: 6px;
  background: color-mix(in srgb, var(--text-primary) 78%, transparent);
  box-shadow: 0 2px 6px rgba(0, 0, 0, 0.12);
  color: var(--bg-primary);
  animation: scrollbar-toc-tooltip-in 160ms ease-out;
  font-family: var(--ui-font, inherit);
  font-size: 11px;
  line-height: 1.35;
  /* The title and path are Hebrew text — RTL, and wrapped rather than ellipsised
     so a long path stays readable (the same call the breadcrumb makes). */
  direction: rtl;
  text-align: start;
  white-space: normal;
  overflow-wrap: anywhere;
}
/* Dark theme: not inverted (a white bubble glares on a dark page) — a raised dark
   surface with light text and a hairline border, as browsers and VS Code do. */
:global(:root.dark) .scrollbar-toc-tooltip {
  background: color-mix(in srgb, var(--bg-secondary) 94%, transparent);
  border: 1px solid var(--border-color);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.4);
  color: var(--text-primary);
}
/* Following the thumb through a scroll: the thumb's own ease-out glide
   (WindowedScrollerBar), so the two move as one. Hover follows the pointer, unanimated. */
.scrollbar-toc-tooltip.follows-thumb {
  transition: top 220ms cubic-bezier(0.33, 1, 0.68, 1);
}
/* The book leads: it is the coarser answer, and the one that survives when no TOC
   entry covers the current line. */
.book-title {
  font-weight: 600;
}
.toc-path {
  opacity: 0.75;
}
@keyframes scrollbar-toc-tooltip-in {
  from {
    opacity: 0;
    transform: scale(0.92);
  }
}
/* Only when both are present — a lone title must not carry a hanging gap. */
.book-title + .toc-path {
  margin-block-start: 0;
}
</style>
