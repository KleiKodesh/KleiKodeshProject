<script setup lang="ts">
// Filter pills: an "all" pill plus one toggle pill per item. Nothing selected = all.
// Wide: each pill is as wide as its text and rows wrap.
// Narrow (container under NARROW_WIDTH): one row; pills that don't fit go behind a "+N"
// pill whose dropdown lists them.
import { ref, computed, nextTick, watch } from 'vue'
import { useElementSize, useResizeObserver } from '@vueuse/core'
import { IconCheckmark16Regular } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'

export interface FilterPillItem {
  key: string
  label: string
  title?: string
}

const props = defineProps<{
  items: FilterPillItem[]
  allLabel: string
  itemTestId?: string
}>()
const selected = defineModel<string[]>({ required: true })

const NARROW_WIDTH = 480
const GAP = 6
/** Room kept for the "+N" pill when some pills overflow. */
const MORE_PILL_WIDTH = 52

const rootRef = ref<HTMLElement | null>(null)
const measureRef = ref<HTMLElement | null>(null)
const { width: rootWidth } = useElementSize(rootRef)
const isNarrow = computed(() => rootWidth.value > 0 && rootWidth.value < NARROW_WIDTH)

// Widths of every pill ("all" first), read from an invisible copy of the full row.
const pillWidths = ref<number[]>([])
function measure() {
  const el = measureRef.value
  if (!el) return
  pillWidths.value = [...el.children].map((c) => (c as HTMLElement).offsetWidth)
}
useResizeObserver(measureRef, measure)
watch(() => props.items, () => nextTick(measure), { deep: true })

/** How many items (after "all") fit in one row; the rest go to the dropdown. */
const visibleCount = computed(() => {
  if (!isNarrow.value) return props.items.length
  const widths = pillWidths.value
  if (widths.length !== props.items.length + 1) return props.items.length
  const total = widths.reduce((sum, w) => sum + w + GAP, -GAP)
  if (total <= rootWidth.value) return props.items.length
  const widthAt = (i: number) => widths[i] ?? 0
  let used = widthAt(0) + GAP + MORE_PILL_WIDTH
  let count = 0
  while (count < props.items.length && used + widthAt(count + 1) + GAP <= rootWidth.value) {
    used += widthAt(count + 1) + GAP
    count++
  }
  return count
})
const visibleItems = computed(() => props.items.slice(0, visibleCount.value))
const hiddenItems = computed(() => props.items.slice(visibleCount.value))
const hiddenSelectedCount = computed(
  () => hiddenItems.value.filter((i) => selected.value.includes(i.key)).length,
)

function toggle(key: string) {
  selected.value = selected.value.includes(key)
    ? selected.value.filter((k) => k !== key)
    : [...selected.value, key]
}

const isMoreOpen = ref(false)
const moreButtonRef = ref<HTMLElement | null>(null)
const dropdownRef = ref<HTMLElement | null>(null)
const { justClosed } = useDropdownClose(dropdownRef, () => (isMoreOpen.value = false), {
  toggleButton: moreButtonRef,
})
function toggleMore() {
  if (justClosed.value) return
  isMoreOpen.value = !isMoreOpen.value
}
watch(hiddenItems, (items) => { if (!items.length) isMoreOpen.value = false })
</script>

<template>
  <div ref="rootRef" class="filter-pills" :class="{ narrow: isNarrow }" role="group">
    <button
      class="filter-pill"
      :class="{ active: !selected.length }"
      @mousedown.prevent
      @click="selected = []"
    >{{ allLabel }}</button>
    <button
      v-for="item in visibleItems"
      :key="item.key"
      class="filter-pill"
      :class="{ active: selected.includes(item.key) }"
      :title="item.title ?? item.label"
      :data-testid="itemTestId"
      @mousedown.prevent
      @click="toggle(item.key)"
    >{{ item.label }}</button>
    <div v-if="hiddenItems.length" class="more-host">
      <button
        ref="moreButtonRef"
        class="filter-pill more-pill"
        :class="{ active: hiddenSelectedCount > 0, open: isMoreOpen }"
        aria-haspopup="menu"
        :aria-expanded="isMoreOpen"
        @mousedown.prevent
        @click="toggleMore"
      >+{{ hiddenItems.length }}</button>
      <div v-if="isMoreOpen" ref="dropdownRef" class="more-dropdown" role="menu">
        <button
          v-for="item in hiddenItems"
          :key="item.key"
          class="option-row"
          role="menuitemcheckbox"
          :aria-checked="selected.includes(item.key)"
          :title="item.title ?? item.label"
          :data-testid="itemTestId"
          @mousedown.prevent
          @click="toggle(item.key)"
        >
          <IconCheckmark16Regular class="check" :class="{ shown: selected.includes(item.key) }" />
          <span class="option-label">{{ item.label }}</span>
        </button>
      </div>
    </div>

    <!-- Invisible copy of the full row, for measuring pill widths. -->
    <div ref="measureRef" class="measure" aria-hidden="true">
      <span class="filter-pill">{{ allLabel }}</span>
      <span v-for="item in items" :key="item.key" class="filter-pill">{{ item.label }}</span>
    </div>
  </div>
</template>

<style scoped>
.filter-pills {
  position: relative;
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.filter-pills.narrow {
  flex-wrap: nowrap;
}
.filter-pill {
  display: inline-flex;
  align-items: center;
  flex-shrink: 0;
  max-width: 260px;
  height: 28px;
  padding: 0 14px;
  font-size: 13px;
  font-family: inherit;
  line-height: 1;
  border: none;
  border-radius: 999px;
  background: var(--bg-secondary);
  color: var(--text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  cursor: pointer;
}
.filter-pill:hover,
.filter-pill.open { background: var(--hover-bg); color: var(--text-primary); }
/* Same active look as the toolbar / commentary-nav toggle buttons. */
.filter-pill.active,
.filter-pill.active:hover {
  background: var(--active-bg);
  color: var(--text-primary);
}

.measure {
  position: absolute;
  visibility: hidden;
  pointer-events: none;
  display: flex;
  gap: 6px;
  inset-inline-start: 0;
  top: 0;
  width: max-content;
}

.more-host {
  position: relative;
  flex-shrink: 0;
}
/* Same size rules as every dropdown: 180-300 wide, up to 320 high, 28 px rows. */
.more-dropdown {
  position: absolute;
  top: calc(100% + 4px);
  inset-inline-end: 0;
  min-width: 180px;
  max-width: 300px;
  max-height: 320px;
  overflow-y: auto;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.25);
  z-index: 100;
  padding: 2px 0;
}
.option-row {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  height: 28px;
  padding: 0 10px;
  border: none;
  background: transparent;
  color: var(--text-primary);
  font: inherit;
  font-size: 13px;
  text-align: start;
  cursor: pointer;
}
.option-row:hover { background: var(--hover-bg); }
.option-label {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.check {
  flex-shrink: 0;
  visibility: hidden;
}
.check.shown { visibility: visible; }
</style>
