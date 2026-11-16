<script setup lang="ts">
/**
 * Compact abbreviation-expansion tooltip, anchored to the current text selection.
 *
 * Shows all dictionary senses for the selected abbreviation on one wrapped
 * line, divider-separated. Sense labels arrive display-ready from the
 * composable (headword-prefixed when a fallback matched a different entry).
 *
 * Positioning follows the BookViewNoteBubble pattern: Teleported to body,
 * rendered hidden first so real dimensions can be measured, then fixed above
 * the selection (flipped below when clipped by the viewport top).
 *
 * The parent keys this component by lookup id so a new lookup remounts and
 * re-measures. mousedown is prevented so clicking the tooltip doesn't
 * collapse the selection (which would immediately dismiss it).
 */
import { ref, computed, onMounted, nextTick } from 'vue'
import type { AbbrevTooltipData } from './useBookViewAbbrevTooltip'

const props = defineProps<{ data: AbbrevTooltipData }>()

const tooltipRef = ref<HTMLElement | null>(null)
const resolvedTop = ref<number | null>(null)
const resolvedLeft = ref<number | null>(null)

const MARGIN = 8
const MAX_WIDTH = 460

function computePosition() {
  const rect = props.data.anchorRect
  const width = tooltipRef.value?.offsetWidth ?? MAX_WIDTH
  const height = tooltipRef.value?.offsetHeight ?? 40

  // Center horizontally on the selection, clamped to the viewport
  let left = rect.left + rect.width / 2 - width / 2
  left = Math.min(Math.max(MARGIN, left), window.innerWidth - width - MARGIN)

  // Prefer above the selection; flip below when clipped by the top edge
  let top = rect.top - height - MARGIN
  if (top < MARGIN) top = rect.bottom + MARGIN

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
    <div ref="tooltipRef" class="abbrev-tooltip" :style="style" dir="rtl" @mousedown.prevent>
      <template v-for="(sense, senseIndex) in data.senses" :key="senseIndex">
        <span v-if="senseIndex > 0" class="abbrev-divider">|</span>
        <span class="abbrev-sense">{{ sense }}</span>
      </template>
    </div>
  </Teleport>
</template>

<style scoped>
.abbrev-tooltip {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  box-shadow:
    0 2px 8px rgba(0, 0, 0, 0.15),
    0 8px 24px rgba(0, 0, 0, 0.1);
  padding: 5px 10px;
  direction: rtl;
  font-family: var(--text-font);
  font-size: 12.5px;
  line-height: 1.6;
  color: var(--text-primary);
  max-height: 40vh;
  overflow-y: auto;
  user-select: none;
}

.abbrev-divider {
  opacity: 0.35;
  margin-inline: 7px;
}
</style>
