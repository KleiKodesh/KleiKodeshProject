<script setup lang="ts">
import { ref } from 'vue'
import TreeView from './TreeView.vue'
import type { TreeNodeItem } from './treeTypes'
import type { SegmentSearchTree } from '@/utils/segmentSearchTree'

/**
 * One table-of-contents tree (title row optional) - the book view's TOC and the FastDocView
 * outline share it, so both look the same by construction. Nodes are TreeView rows.
 */

defineProps<{
  title: string | null
  entries: TreeNodeItem[]
  filter: string
  activeEntryId?: number
  searchTree?: SegmentSearchTree
}>()
defineEmits<{ select: [TreeNodeItem] }>()

const treeViewRef = ref<InstanceType<typeof TreeView> | null>(null)
defineExpose({
  containerRef: () => treeViewRef.value?.containerRef ?? null,
  getExpanded: () => treeViewRef.value?.getExpanded() ?? [],
  setExpanded: (ids: readonly number[]) => treeViewRef.value?.setExpanded(ids),
})
</script>

<template>
  <div class="toc-section">
    <div v-if="title" class="section-title">{{ title }}</div>
    <TreeView
      ref="treeViewRef"
      :nodes="entries"
      :filter="filter"
      :active-node-id="activeEntryId"
      :search-tree="searchTree"
      @select="$emit('select', $event)"
    />
  </div>
</template>

<style scoped>
.toc-section {
  display: flex;
  flex-direction: column;
  min-height: 0;
  min-width: 120px;
  flex: 1;
}
.section-title {
  font-size: 0.7rem;
  font-weight: 600;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.06em;
  padding: 4px 10px 3px;
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
}
</style>
