<script setup lang="ts">
/**
 * Hover preview for a word-level link: the target line's content, headed by the
 * target book's title. Positioning follows the BookViewAbbrevTooltip pattern:
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
 * composable keeps it open while the pointer is inside. A `.word-link-tooltip-gap`
 * pseudo-element spans the MARGIN between anchor and tooltip so travelling there
 * never crosses dead space where a `mouseout` would look like leaving.
 */
import { ref, computed, onMounted, nextTick } from 'vue'
import { useSettingsStore } from '@/stores/settingsStore'
import { censorDivineNames } from '@/utils/censorDivineNames'
import type { WordLinkTooltipData } from './useWordLinkTooltip'

const props = defineProps<{ data: WordLinkTooltipData }>()
const emit = defineEmits<{
  'pointer-enter': []
  'pointer-leave': []
  /** A mousedown inside — possibly the start of a selection drag. */
  'select-start': []
}>()

const settingsStore = useSettingsStore()
const html = computed(() => censorDivineNames(props.data.html, settingsStore.censorOptions))

const tooltipRef = ref<HTMLElement | null>(null)
const resolvedTop = ref<number | null>(null)
const resolvedLeft = ref<number | null>(null)
/** Which edge faces the anchor — the gap bridge is drawn on that side. */
const placement = ref<'above' | 'below'>('above')

const MARGIN = 8
const MAX_WIDTH = 460

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

onMounted(() => {
  nextTick(computePosition)
})
</script>

<template>
  <Teleport to="body">
    <div
      ref="tooltipRef"
      class="word-link-tooltip"
      :class="`is-${placement}`"
      :style="style"
      dir="rtl"
      @mouseenter="emit('pointer-enter')"
      @mouseleave="emit('pointer-leave')"
      @mousedown="emit('select-start')"
    >
      <div v-if="data.bookTitle" class="word-link-tooltip-title">{{ data.bookTitle }}</div>
      <!-- eslint-disable-next-line vue/no-v-html -->
      <div class="word-link-tooltip-body" v-html="html" />
    </div>
  </Teleport>
</template>

<style scoped>
.word-link-tooltip {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  box-shadow:
    0 2px 8px rgba(0, 0, 0, 0.15),
    0 8px 24px rgba(0, 0, 0, 0.1);
  padding: 6px 10px;
  direction: rtl;
  font-family: var(--text-font);
  font-size: 13px;
  line-height: 1.7;
  color: var(--text-primary);
  /* Long previews scroll, and the user must be able to reach that scrollbar —
     so this accepts the pointer instead of the usual tooltip pointer-events: none. */
  display: flex;
  flex-direction: column;
  max-height: 40vh;
  min-height: 0;
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

.word-link-tooltip-body {
  /* The scroll container: keeping overflow off the root lets ::before escape it. */
  overflow-y: auto;
  min-height: 0;
  /* Selectable — the global `* { user-select: none }` reset is opted out of in
     main.css, which this teleported element needs by name. */
  cursor: text;
}

.word-link-tooltip-title {
  font-weight: 600;
  font-size: 12px;
  color: var(--accent-color);
  margin-bottom: 2px;
}
</style>
