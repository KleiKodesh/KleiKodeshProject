<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
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
export interface FastDocViewTocState {
  searchQuery?: string
  expanded?: number[]
  scrollTop?: number
}

const props = defineProps<{
  outline: FastDocViewOutlineNode[]
  activeNodeId?: number
  isRightToLeft: boolean
  /** The panel's state when it last closed / the tab was saved - applied on mount, as the book view's TOC restoreState. */
  savedState?: FastDocViewTocState
}>()
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

// ── Persistence (as BookViewTocTree) ───────────────────────────────────────
// The panel is v-if'd away on close and on tab switch, so the page snapshots this on close /
// save and hands it back through savedState. Expanded ids land before the scroll offset:
// expanding changes the scroll height.
function snapshotState(): FastDocViewTocState {
  return {
    searchQuery: query.value || undefined,
    expanded: sectionRef.value?.getExpanded() ?? [],
    scrollTop: sectionRef.value?.containerRef()?.scrollTop ?? 0,
  }
}
function applyState(saved: FastDocViewTocState) {
  if (saved.searchQuery) query.value = saved.searchQuery
  if (saved.expanded?.length) sectionRef.value?.setExpanded(saved.expanded)
  nextTick(() => {
    const list = sectionRef.value?.containerRef()
    if (list && saved.scrollTop != null) list.scrollTop = saved.scrollTop
  })
}
onMounted(() => {
  const saved = props.savedState
  if (!saved) return
  // The headings can arrive after the panel opens (restored open on a fresh tab).
  if (props.outline.length) return applyState(saved)
  const stop = watch(
    () => props.outline.length,
    (count) => {
      if (!count) return
      stop()
      nextTick(() => applyState(saved))
    },
  )
})
defineExpose({ snapshotState })
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
