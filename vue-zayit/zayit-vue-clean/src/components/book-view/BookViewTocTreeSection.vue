<script setup lang="ts">
import TreeView from '@/components/common/TreeView.vue'
import type { TreeNodeItem } from '@/components/common/TreeNode.vue'
import type { TocEntry } from './useToc'

defineProps<{ title: string | null; entries: TocEntry[]; filter: string; activeEntryId?: number; visible?: boolean; suppressScroll?: boolean }>()
defineEmits<{ select: [TocEntry] }>()
</script>

<template>
  <div class="toc-section">
    <div v-if="title" class="section-title">{{ title }}</div>
    <TreeView
      :nodes="entries as unknown as TreeNodeItem[]"
      :filter="filter"
      :active-node-id="activeEntryId"
      :visible="visible"
      :suppress-scroll="suppressScroll"
      @select="$emit('select', $event as unknown as TocEntry)"
    />
  </div>
</template>

<style scoped>
.toc-section { display: flex; flex-direction: column; min-height: 0; min-width: 120px; flex: 1; }
.section-title { font-size: 0.7rem; font-weight: 600; color: var(--text-secondary); text-transform: uppercase; letter-spacing: 0.06em; padding: 4px 10px 3px; border-bottom: 1px solid var(--border-color); flex-shrink: 0; }
</style>
