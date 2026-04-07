<script setup lang="ts">
import { computed, ref } from 'vue'
import { onClickOutside } from '@vueuse/core'
import CommentaryTreeViewNode from './CommentaryTreeViewNode.vue'
import { buildCommentaryTree } from './useCommentary'
import type { CommentaryGroup } from './useCommentary'

const props = defineProps<{
  groups: CommentaryGroup[]
  hiddenBookIds?: Set<number>
}>()

const emit = defineEmits<{
  toggle: [bookId: number]
  close: []
}>()

const panelEl = ref<HTMLElement | null>(null)
onClickOutside(panelEl, () => emit('close'))

const tree = computed(() => buildCommentaryTree(props.groups))
const allBookIds = computed(() => props.groups.map((g) => g.bookId))

const allState = computed<'checked' | 'unchecked' | 'indeterminate'>(() => {
  const hidden = allBookIds.value.filter((id) => props.hiddenBookIds?.has(id)).length
  if (hidden === 0) return 'checked'
  if (hidden === allBookIds.value.length) return 'unchecked'
  return 'indeterminate'
})

function toggleAll() {
  if (allState.value === 'indeterminate') {
    allBookIds.value
      .filter((id) => props.hiddenBookIds?.has(id))
      .forEach((id) => emit('toggle', id))
  } else {
    allBookIds.value.forEach((id) => emit('toggle', id))
  }
}
</script>

<template>
  <div ref="panelEl" class="filter-panel">
    <div
      class="all-row"
      :class="{ checked: allState === 'checked', indeterminate: allState === 'indeterminate' }"
      @click="toggleAll"
    >
      <span class="check-col">
        <span class="check-mark">✓</span>
        <span class="dash-mark">–</span>
      </span>
      <span class="row-label">הצג הכל</span>
    </div>
    <CommentaryTreeViewNode
      v-for="node in tree"
      :key="node.label"
      :node="node"
      :hidden-book-ids="hiddenBookIds"
      @toggle="emit('toggle', $event)"
    />
  </div>
</template>

<style scoped>
.filter-panel {
  position: absolute;
  top: 32px;
  right: 0;
  width: fit-content;
  min-width: 140px;
  max-width: 50%;
  max-height: calc(100% - 40px);
  z-index: 10;
  overflow-y: auto;
  overflow-x: hidden;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.18);
  direction: rtl;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  padding-block: 0;
}
.all-row {
  display: flex;
  align-items: center;
  gap: 4px;
  height: 28px;
  padding-inline: 6px 10px;
  cursor: pointer;
  font-size: 12px;
  font-weight: 600;
  color: var(--text-primary);
  border-bottom: 1px solid var(--border-color);
}
.all-row:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
.check-col {
  width: 16px;
  height: 16px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 11px;
  color: var(--accent-color);
}
.check-mark {
  display: none;
}
.dash-mark {
  display: none;
}
.all-row.checked .check-mark {
  display: block;
}
.all-row.indeterminate .dash-mark {
  display: block;
}
.row-label {
  flex: 1;
  white-space: nowrap;
}
</style>
