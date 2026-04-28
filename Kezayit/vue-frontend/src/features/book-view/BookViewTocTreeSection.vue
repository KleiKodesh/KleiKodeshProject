<script setup lang="ts">
import { ref } from 'vue'
import TreeView from '@/components/TreeView.vue'
import type { TocEntry } from './useBookViewToc'
import type { SearchableTree } from '@/utils/tocSearchUtils'

defineProps<{
  title: string | null
  entries: TocEntry[]
  filter: string
  activeEntryId?: number
  searchTree?: SearchableTree
}>()
defineEmits<{ select: [TocEntry] }>()

const treeViewRef = ref<InstanceType<typeof TreeView> | null>(null)
defineExpose({ containerRef: () => treeViewRef.value?.containerRef ?? null })
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
      @select="$emit('select', $event as TocEntry)"
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
