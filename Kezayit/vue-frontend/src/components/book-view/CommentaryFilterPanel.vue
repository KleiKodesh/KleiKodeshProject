<script setup lang="ts">
import { computed } from 'vue'
import CommentaryTreeViewNode from './CommentaryTreeViewNode.vue'
import { buildCommentaryTree } from './useCommentary'
import type { CommentaryGroup } from './useCommentary'

const props = defineProps<{
  groups: CommentaryGroup[]
  hiddenBookIds?: Set<number>
}>()

const emit = defineEmits<{
  'update:hiddenBookIds': [value: Set<number>]
}>()

const tree = computed(() => buildCommentaryTree(props.groups))
const allBookIds = computed(() => props.groups.map((g) => g.bookId))

const allState = computed<'checked' | 'unchecked' | 'indeterminate'>(() => {
  const hidden = allBookIds.value.filter((id) => props.hiddenBookIds?.has(id)).length
  if (hidden === 0) return 'checked'
  if (hidden === allBookIds.value.length) return 'unchecked'
  return 'indeterminate'
})

function toggleBookVisibility(bookId: number) {
  const next = new Set(props.hiddenBookIds ?? [])
  if (next.has(bookId)) next.delete(bookId)
  else next.add(bookId)
  emit('update:hiddenBookIds', next)
}

function toggleAll() {
  const next = new Set(props.hiddenBookIds ?? [])
  const shouldHideAll = allState.value === 'checked'
  allBookIds.value.forEach((id) => {
    if (shouldHideAll) next.add(id)
    else next.delete(id)
  })
  emit('update:hiddenBookIds', next)
}
</script>

<template>
  <div class="filter-panel">
    <div
      class="all-row"
      :class="{ checked: allState === 'checked', indeterminate: allState === 'indeterminate' }"
      @click="toggleAll"
    >
      <span class="check-col">
        <span class="check-mark">&#10003;</span>
        <span class="dash-mark">&#8211;</span>
      </span>
      <span class="row-label">&#x5D4;&#x5E6;&#x5D2; &#x5D4;&#x5DB;&#x5DC;</span>
    </div>
    <CommentaryTreeViewNode
      v-for="node in tree"
      :key="node.label"
      :node="node"
      :hidden-book-ids="hiddenBookIds"
      @toggle="toggleBookVisibility"
    />
  </div>
</template>

<style scoped>
.filter-panel {
  display: flex;
  flex-direction: column;
  height: 100%;
  width: fit-content;
  min-width: 140px;
  max-width: 100%;
  overflow-y: auto;
  overflow-x: hidden;
  background: var(--bg-secondary);
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
