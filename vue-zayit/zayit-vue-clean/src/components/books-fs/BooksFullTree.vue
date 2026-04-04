<script setup lang="ts">
import { ref, computed } from 'vue'
import { useBooksDataStore } from '@/stores/booksDataStore'
import TreeView from '@/components/common/TreeView.vue'
import type { TreeNodeItem } from '@/components/common/TreeNode.vue'
import type { CategoryNode, BookRow } from './booksCategoryTree'

const emit = defineEmits<{ selectBook: [BookRow] }>()
const store = useBooksDataStore()
const treeViewRef = ref<InstanceType<typeof TreeView> | null>(null)

interface FlatNode extends TreeNodeItem {
  _book?: BookRow
}

function flatten(nodes: CategoryNode[], parentId: number | null, level: number, out: FlatNode[]) {
  for (const node of nodes) {
    const id = -(node.id + 1)
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
        id: book.id,
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

function onSelect(node: TreeNodeItem) {
  const flat = node as FlatNode
  flat._book ? emit('selectBook', flat._book) : treeViewRef.value?.toggleNode(node)
}

const reset = () => treeViewRef.value?.reset()
defineExpose({ reset, containerRef: computed(() => treeViewRef.value?.containerRef ?? null) })
</script>

<template>
  <TreeView
    ref="treeViewRef"
    :nodes="flatNodes"
    :row-height="38"
    font-size="14px"
    @select="onSelect"
  />
</template>
