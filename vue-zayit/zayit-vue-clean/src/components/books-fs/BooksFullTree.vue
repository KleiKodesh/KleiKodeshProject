<script setup lang="ts">
import { computed, ref } from 'vue'
import { useBooksDataStore } from '@/stores/booksDataStore'
import TreeView from '@/components/common/TreeView.vue'
import type { TreeNodeItem } from '@/components/common/TreeNode.vue'
import type { CategoryNode, BookRow } from './booksFsTree'

const emit = defineEmits<{ selectBook: [book: BookRow] }>()

const store = useBooksDataStore()
const treeViewRef = ref<InstanceType<typeof TreeView> | null>(null)

// Flatten the full category+book tree into a sorted TreeNodeItem list.
// Categories get negative IDs (-(categoryId+1)) to avoid collisions with book IDs.
// Books get their real positive IDs.
interface FlatNode extends TreeNodeItem {
  _book?: BookRow
}

function catId(id: number) { return -(id + 1) }

function flatten(nodes: CategoryNode[], parentId: number | null, level: number, out: FlatNode[]) {
  for (const node of nodes) {
    const id = catId(node.id)
    const hasChildren = node.children.length > 0 || node.books.length > 0
    out.push({ id, parentId, level, hasChildren, text: node.title })
    flatten(node.children, id, level + 1, out)
    for (const book of node.books) {
      out.push({ id: book.id, parentId: id, level: level + 1, hasChildren: false, text: book.title, _book: book })
    }
  }
}

const flatNodes = computed<FlatNode[]>(() => {
  const out: FlatNode[] = []
  flatten(store.ROOT.children, null, 0, out)
  return out
})

function onSelect(node: TreeNodeItem) {
  const flat = node as FlatNode
  if (flat._book) emit('selectBook', flat._book)
  else treeViewRef.value?.toggleNode(node)
}

function reset() { treeViewRef.value?.reset() }
defineExpose({ reset })
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
