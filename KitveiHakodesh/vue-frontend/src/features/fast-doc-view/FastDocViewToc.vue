<script setup lang="ts">
import { computed, ref } from 'vue'
import TocTreeLayout from '@/components/TocTreeLayout.vue'
import TocTreeSection from '@/components/TocTreeSection.vue'
import type { TreeNodeItem } from '@/components/treeTypes'
import { SegmentSearchTree } from '@/utils/segmentSearchTree'
import type { FastDocViewOutlineNode } from './fastDocViewOutline'

/**
 * The FastDocView table of contents: the book view's TOC panel (the shared TocTreeLayout +
 * TocTreeSection - same rows, search box and states), fed the document's headings. The search
 * is the book view TOC's own (TreeView filter over a SegmentSearchTree of the heading paths);
 * `activeNodeId` is the heading at the reader's position, highlighted as in the book view.
 */
const props = defineProps<{ outline: FastDocViewOutlineNode[]; activeNodeId?: number; isRightToLeft: boolean }>()
const emit = defineEmits<{ select: [node: FastDocViewOutlineNode] }>()

const query = ref('')
const sectionRef = ref<InstanceType<typeof TocTreeSection> | null>(null)
const searchTree = computed(() => new SegmentSearchTree(props.outline))

function focusList() {
  sectionRef.value?.containerRef?.()?.focus()
}
function onSelect(node: TreeNodeItem) {
  emit('select', node as FastDocViewOutlineNode)
}
</script>

<template>
  <TocTreeLayout v-model:query="query" :dir="isRightToLeft ? 'rtl' : 'ltr'" @focus-list="focusList">
    <TocTreeSection
      ref="sectionRef"
      class="fast-doc-view-toc-section"
      :title="null"
      :entries="outline"
      :filter="query"
      :active-entry-id="activeNodeId"
      :search-tree="searchTree"
      @select="onSelect"
    />
  </TocTreeLayout>
</template>

<style scoped>
.fast-doc-view-toc-section {
  flex: 1;
  min-height: 0;
}
</style>
