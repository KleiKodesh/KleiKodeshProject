<script setup lang="ts">
import { ref, computed } from 'vue'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { storeToRefs } from 'pinia'
import TreeView from '@/components/TreeView.vue'
import type { TreeNodeItem } from '@/components/treeTypes'
import type { CategoryNode, BookRow } from '@/features/book-catalog/bookCatalogTree'
import { wantsNewTab } from '@/composables/useOpenInNewTab'

const emit = defineEmits<{ selectBook: [BookRow, boolean?] }>()
const store = useBooksDataStore()
const { compactMode } = storeToRefs(useSettingsStore())
const treeViewRef = ref<InstanceType<typeof TreeView> | null>(null)

const catalogRowHeight = computed(() => (compactMode.value ? 32 : 38))

interface FlatNode extends TreeNodeItem {
  _book?: BookRow
}

function encodeCategoryId(categoryId: number): number {
  return categoryId * 2 - 1
}

function encodeBookId(bookId: number): number {
  return bookId * 2
}

function flatten(nodes: CategoryNode[], parentId: number | null, level: number, out: FlatNode[]) {
  for (const node of nodes) {
    const id = encodeCategoryId(node.id)
    out.push({
      id,
      parentId,
      level,
      hasChildren: node.children.length > 0 || node.books.length > 0,
      text: node.title,
    })
    flatten(node.children, id, level + 1, out)
    for (const book of node.books)
      out.push({
        id: encodeBookId(book.id),
        parentId: id,
        level: level + 1,
        hasChildren: false,
        text: book.title,
        _book: book,
      })
  }
}

const flatNodes = computed<FlatNode[]>(() => {
  const out: FlatNode[] = []
  flatten(store.ROOT.children, null, 0, out)
  return out
})

function onSelect(node: TreeNodeItem, event?: MouseEvent | KeyboardEvent) {
  const flat = node as FlatNode
  flat._book
    ? emit('selectBook', flat._book, wantsNewTab(event))
    : treeViewRef.value?.toggleNode(node)
}

function focusContainer() {
  treeViewRef.value?.containerRef?.focus()
}

function reset() {
  treeViewRef.value?.reset()
}

defineExpose({ focusContainer, reset })
</script>

<template>
  <TreeView
    ref="treeViewRef"
    :nodes="flatNodes"
    :row-height="catalogRowHeight"
    :sticky-headers="false"
    font-size="14px"
    @select="onSelect"
  />
  <!-- @select forwards (node, MouseEvent) so Ctrl/⌘-click opens in a new tab -->
</template>
