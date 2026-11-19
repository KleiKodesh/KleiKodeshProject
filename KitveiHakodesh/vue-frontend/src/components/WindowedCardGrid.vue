<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useElementSize } from '@vueuse/core'
import WindowedScroller from './WindowedScroller.vue'
import { useCardGridLayout } from '@/composables/useCardGridLayout'
import type { CardsViewMode } from '@/stores/settingsStore'

/**
 * WindowedCardGrid: a card / tile / list grid of any length through WindowedScroller.
 * Each scroller row is ONE LINE of cards, so only the lines near the viewport are in
 * the DOM - the cards' grid look, the list's windowing.
 *
 * Columns come from useCardGridLayout (whole columns that fit; cards shrink to fit
 * one more, never stretch past their max) - the home page's flow. A new column count
 * re-opens the scroller at the line holding the card that was on top, so a resize or
 * a view switch keeps the reader's place.
 *
 * Keyboard: the caller keeps its own useInputListNavigation and wires
 * `getColumnsPerRow: () => grid.columns` and `scrollIntoView: (i) => grid.revealIndex(i)`
 * (exposed below) - most cards have no element, so the scroller does the landing.
 */
const props = withDefaults(
  defineProps<{
    count: number
    view: CardsViewMode
    /** Changes when the items are a NEW list (new results): the grid re-opens at the top. */
    listKey?: unknown
    /** Caps the grid's width (the home page's 720, the websites page's 960). */
    maxWidth?: number
    /** Side padding of the scroll area, in px (left + right each). */
    paddingInline?: number
  }>(),
  { listKey: undefined, maxWidth: Infinity, paddingInline: 12 },
)

const hostRef = ref<HTMLElement | null>(null)
const { width: hostWidth } = useElementSize(hostRef)
const layout = useCardGridLayout(
  () => Math.max(0, hostWidth.value - 2 * props.paddingInline),
  () => props.view,
  props.maxWidth,
)

const columns = computed(() => (props.view === 'list' ? 1 : layout.value.columns))
const rowCount = computed(() => Math.ceil(props.count / columns.value))
const rowIndexes = (row: number) => {
  const indexes: number[] = []
  for (let i = row * columns.value; i < Math.min(props.count, (row + 1) * columns.value); i++) indexes.push(i)
  return indexes
}

const scrollerRef = ref<InstanceType<typeof WindowedScroller> | null>(null)
// The start row is an argument of the scroller's creation: re-key it to open elsewhere.
const opening = ref({ key: 0, row: 0 })
watch(
  () => props.listKey,
  () => {
    opening.value = { key: opening.value.key + 1, row: 0 }
  },
)
watch(columns, (now, before) => {
  // No scroller yet (width not measured, `before` meaningless): nothing to keep.
  const firstVisibleRow = scrollerRef.value?.readFirstVisible()?.index
  const firstCard = firstVisibleRow == null || !Number.isFinite(before) ? 0 : firstVisibleRow * before
  opening.value = { key: opening.value.key + 1, row: Math.floor(firstCard / now) }
})

defineExpose({
  /** The grid's width in px - a search bar above it lines up with its edges. */
  get width() {
    return layout.value.width
  },
  /** Cards per line - for useInputListNavigation's getColumnsPerRow. */
  get columns() {
    return columns.value
  },
  /** Brings card `index`'s line into view ('nearest': nothing while it is fully visible). */
  revealIndex: (index: number) => scrollerRef.value?.revealIndex(Math.floor(index / columns.value)),
  scrollerElement: computed(() => scrollerRef.value?.scrollerElement ?? null),
})
</script>

<template>
  <div
    ref="hostRef"
    class="windowed-card-grid"
    :style="{
      '--grid-width': `${layout.width}px`,
      '--grid-columns': columns,
      '--grid-padding-inline': `${paddingInline}px`,
    }"
  >
    <!-- Built once the width is measured: before that the column count is unknown
         (with no maxWidth it would be infinite), and the start row depends on it. -->
    <WindowedScroller v-if="hostWidth > 0" :key="opening.key" ref="scrollerRef" class="windowed-card-grid__scroller" thin :count="rowCount" :initial-index="opening.row">
      <template #row="{ index }">
        <div class="windowed-card-grid__line" :class="`windowed-card-grid__line--${view}`">
          <template v-for="itemIndex in rowIndexes(index)" :key="itemIndex">
            <slot name="item" :index="itemIndex" />
          </template>
        </div>
      </template>
    </WindowedScroller>
  </div>
</template>

<style scoped>
.windowed-card-grid {
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.windowed-card-grid__scroller {
  flex: 1;
  min-height: 0;
}
.windowed-card-grid :deep(.windowed-scroller-content) {
  padding: 12px var(--grid-padding-inline) 16px;
}
/* One line of cards, centered at the grid's width. Gaps match useCardGridLayout's. */
.windowed-card-grid__line {
  display: grid;
  grid-template-columns: repeat(var(--grid-columns), minmax(0, 1fr));
  column-gap: 16px;
  width: 100%;
  max-width: var(--grid-width);
  margin-inline: auto;
  padding-bottom: 4px;
}
.windowed-card-grid__line--list {
  grid-template-columns: 1fr;
  padding-bottom: 0;
}
.windowed-card-grid__line--tiles {
  grid-template-columns: repeat(var(--grid-columns), 72px);
  column-gap: 6px;
  padding-bottom: 6px;
  justify-items: center;
}
</style>
