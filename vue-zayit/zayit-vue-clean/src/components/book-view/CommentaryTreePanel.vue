<script setup lang="ts">
import { computed, ref, watch } from 'vue'
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

const tree = computed(() => buildCommentaryTree(props.groups))

const allBookIds = computed(() => props.groups.map((g) => g.bookId))

const allState = computed<'checked' | 'unchecked' | 'indeterminate'>(() => {
  const hidden = allBookIds.value.filter((id) => props.hiddenBookIds?.has(id)).length
  if (hidden === 0) return 'checked'
  if (hidden === allBookIds.value.length) return 'unchecked'
  return 'indeterminate'
})

const allCbEl = ref<HTMLInputElement | null>(null)
watch(
  allState,
  (s) => {
    if (allCbEl.value) allCbEl.value.indeterminate = s === 'indeterminate'
  },
  { immediate: true, flush: 'post' },
)

function toggleAll() {
  if (allState.value === 'indeterminate') {
    // show all hidden
    allBookIds.value
      .filter((id) => props.hiddenBookIds?.has(id))
      .forEach((id) => emit('toggle', id))
  } else {
    allBookIds.value.forEach((id) => emit('toggle', id))
  }
}
</script>

<template>
  <div class="tree-backdrop" @click.self="emit('close')" />
  <div class="commentary-tree-panel">
    <div class="all-row" @click="toggleAll">
      <span class="expander-placeholder" />
      <input
        ref="allCbEl"
        type="checkbox"
        class="row-checkbox"
        :checked="allState === 'checked'"
        @click.stop="toggleAll"
        @change.stop
      />
      <span class="all-label">הצג הכל</span>
    </div>
    <div v-if="!tree.length" class="empty">אין מפרשים זמינים</div>
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
.tree-backdrop {
  position: absolute;
  inset: 0;
  z-index: 9;
}
.commentary-tree-panel {
  position: absolute;
  top: 32px;
  right: 0;
  bottom: 0;
  width: max-content;
  min-width: 140px;
  max-width: 30%;
  z-index: 10;
  overflow-y: auto;
  overflow-x: hidden;
  background: var(--bg-secondary);
  border-inline-start: 1px solid var(--border-color);
  box-shadow: -4px 0 12px rgba(0, 0, 0, 0.2);
  direction: rtl;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}
.all-row {
  display: flex;
  align-items: center;
  gap: 6px;
  min-height: 28px;
  padding-inline-end: 8px;
  background: var(--bg-secondary);
  border-bottom: 1px solid var(--border-color);
  direction: rtl;
  font-size: 12px;
  font-weight: 700;
  cursor: pointer;
  position: sticky;
  top: 0;
  z-index: 2;
  color: var(--text-primary);
}
.all-row:hover {
  background: color-mix(in srgb, var(--text-primary) 6%, var(--bg-secondary));
}
.expander-placeholder {
  width: 28px;
  height: 28px;
  flex-shrink: 0;
}
.all-label {
  flex: 1;
}
.row-checkbox {
  flex-shrink: 0;
  width: 13px;
  height: 13px;
  margin: 0;
  cursor: pointer;
  accent-color: var(--accent-color);
}
.empty {
  padding: 12px 8px;
  text-align: center;
  color: var(--text-secondary);
  font-size: 11px;
  font-style: italic;
}
</style>
