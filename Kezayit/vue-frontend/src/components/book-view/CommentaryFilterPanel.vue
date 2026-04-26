<script setup lang="ts">
import { computed } from 'vue'
import CommentaryTreeViewNode from './CommentaryTreeViewNode.vue'
import {
  buildCommentaryTree,
  getLegacyCommentaryBookKey,
  isCommentaryGroupHidden,
} from './useCommentary'
import type { CommentaryGroup } from './useCommentary'

const props = defineProps<{
  groups: CommentaryGroup[]
  hiddenBookIds?: Set<string>
}>()

const emit = defineEmits<{
  'update:hiddenBookIds': [value: Set<string>]
}>()

const tree = computed(() => buildCommentaryTree(props.groups))
const hiddenKeys = computed(() => props.hiddenBookIds ?? new Set<string>())

const allState = computed<'checked' | 'unchecked' | 'indeterminate'>(() => {
  const hidden = props.groups.filter((group) => isCommentaryGroupHidden(hiddenKeys.value, group)).length
  if (hidden === 0) return 'checked'
  if (hidden === props.groups.length) return 'unchecked'
  return 'indeterminate'
})

function toggleAll() {
  const next = new Set(props.hiddenBookIds ?? [])
  const shouldHideAll = allState.value === 'checked'
  props.groups.forEach((group) => {
    if (shouldHideAll) next.add(group.filterKey)
    else next.delete(group.filterKey)
    next.delete(getLegacyCommentaryBookKey(group.bookId))
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
      :key="node.filterKey ?? node.label"
      :node="node"
      :hidden-book-ids="hiddenBookIds"
      @update:hidden-book-ids="emit('update:hiddenBookIds', $event)"
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
  flex-shrink: 0;
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
