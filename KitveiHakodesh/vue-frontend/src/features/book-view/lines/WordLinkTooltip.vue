<script setup lang="ts">
/**
 * Hover preview panel for the reading views. Two callers: the word-link preview
 * (useWordLinkTooltip — target line content headed by the target book's title) and
 * the user-note preview (useNoteTooltip — the note's own text, `interactive: false`).
 *
 * Positioning follows the BookViewAbbrevTooltip pattern:
 * Teleported to body, rendered hidden first so real dimensions can be measured,
 * then fixed above the anchor (flipped below when clipped by the viewport top).
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
}>()

const settingsStore = useSettingsStore()
const html = computed(() => censorDivineNames(props.data.html, settingsStore.censorOptions))

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

function computePosition() {
  const rect = props.data.anchorRect
  const width = tooltipRef.value?.offsetWidth ?? MAX_WIDTH
  const height = tooltipRef.value?.offsetHeight ?? 60

  // Center horizontally on the link, clamped to the viewport
  let left = rect.left + rect.width / 2 - width / 2
  left = Math.min(Math.max(MARGIN, left), window.innerWidth - width - MARGIN)

  // Prefer above the link; flip below when clipped by the top edge
  let top = rect.top - height - MARGIN
  placement.value = 'above'
  if (top < MARGIN) {
    top = rect.bottom + MARGIN
    placement.value = 'below'
  }

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

onMounted(() => {
  nextTick(() => {
    computePosition()
    const body = bodyRef.value
    clipped.value = !props.interactive && !!body && body.scrollHeight > body.clientHeight + 1
  })
})
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
      <div v-if="heading" class="word-link-tooltip-title">{{ heading }}</div>
      <!-- eslint-disable-next-line vue/no-v-html -->
      <div ref="bodyRef" class="word-link-tooltip-body" v-html="html" />
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
  max-height: 40vh;
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
  padding-inline: 10px;
  text-align: justify;
  /* Selectable — the global `* { user-select: none }` reset is opted out of in
     main.css, which this teleported element needs by name. */
  cursor: text;
  scrollbar-width: thin;
  scrollbar-color: color-mix(in srgb, var(--text-secondary) 30%, transparent) transparent;
}
</style>
