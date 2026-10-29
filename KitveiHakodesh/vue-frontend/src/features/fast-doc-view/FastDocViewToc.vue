<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import TreeView from '@/components/TreeView.vue'
import type { TreeNodeItem } from '@/components/treeTypes'
import { normalize } from '@/utils/normalizeText'
import { buildTocSearchPaths, matchWords, normalizeTocWords } from '@/utils/tocSearchPaths'
import type { FastDocViewOutlineNode } from './fastDocViewOutline'

/**
 * The FastDocView table of contents: the headings tree, and under it a search box with the
 * book view TOC's matching (same normalisation, whole words in order over the heading's
 * path). While a query is typed the tree shows the matching headings, each with its path.
 */
const props = defineProps<{ outline: FastDocViewOutlineNode[]; isRightToLeft: boolean }>()
const emit = defineEmits<{ select: [node: FastDocViewOutlineNode] }>()

const query = ref('')
const searchInput = ref<HTMLInputElement | null>(null)
onMounted(() => searchInput.value?.focus({ preventScroll: true }))

const searchPaths = computed(() =>
  buildTocSearchPaths(props.outline.map((node) => ({ id: node.id, parentId: node.parentId, bookId: 0, text: node.text, lineIndex: node.blockIndex }))),
)

const shownNodes = computed<TreeNodeItem[]>(() => {
  const words = normalizeTocWords(normalize(query.value).split(/\s+/).filter(Boolean))
  if (words.length === 0) return props.outline
  const byId = new Map(props.outline.map((node) => [node.id, node]))
  const matches: FastDocViewOutlineNode[] = []
  for (const path of searchPaths.value) {
    const node = byId.get(path.id)
    if (node && matchWords(path.tocSearchPath, words)) {
      matches.push({ ...node, parentId: null, level: 0, hasChildren: false, text: path.tocDisplayPath })
    }
  }
  return matches
})

function onSelect(node: TreeNodeItem) {
  emit('select', node as FastDocViewOutlineNode)
}
</script>

<template>
  <div class="fast-doc-view-toc-body" :dir="isRightToLeft ? 'rtl' : 'ltr'">
    <TreeView class="fast-doc-view-toc-tree" :dir="isRightToLeft ? 'rtl' : 'ltr'" :nodes="shownNodes" @select="onSelect" />
    <div class="fast-doc-view-toc-search">
      <input
        ref="searchInput"
        v-model="query"
        type="search"
        class="fast-doc-view-toc-search-input"
        :placeholder="$t('components.searchPlaceholder')"
      />
    </div>
  </div>
</template>

<style scoped>
.fast-doc-view-toc-body {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
}
.fast-doc-view-toc-tree {
  flex: 1;
  min-height: 0;
}
.fast-doc-view-toc-search {
  padding: 5px 6px 6px;
  border-top: 1px solid var(--border-color);
  flex-shrink: 0;
  background: var(--tree-bg, var(--bg-primary));
}
.fast-doc-view-toc-search-input {
  width: 100%;
  padding: 4px 8px;
  background: none;
  border: none;
  outline: none;
  color: var(--text-primary);
  font: inherit;
}
</style>
