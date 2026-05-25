<script setup lang="ts">
import { ref, computed, watch, nextTick, onMounted } from 'vue'
import TreeNode from './TreeNode.vue'
import type { TreeNodeItem } from './treeTypes'
import { useListKeys } from '@/composables/useListKeyNav'
import { SearchableTree } from '@/features/book-view/toc/tocSearchUtils'

const props = defineProps<{
  nodes: TreeNodeItem[]
  filter?: string
  activeNodeId?: number
  indent?: number
  rowHeight?: number
  fontSize?: string
  stickyHeaders?: boolean
  searchTree?: SearchableTree
}>()

const emit = defineEmits<{ select: [node: TreeNodeItem] }>()

const expanded = ref<Set<number>>(new Set())
const rowRefs = ref<Map<number, HTMLElement>>(new Map())
const containerRef = ref<HTMLElement | null>(null)

function setRowRef(el: unknown, id: number) {
  const dom = (el as any)?.$el ?? (el instanceof HTMLElement ? el : null)
  if (dom) rowRefs.value.set(id, dom)
  else rowRefs.value.delete(id)
}

function expandAncestors(id: number) {
  // Build a local map on demand — only called on active-entry change, not on every render
  const map = new Map<number, TreeNodeItem>()
  for (const n of props.nodes) map.set(n.id, n)
  const node = map.get(id)
  if (!node) return
  let current = node
  while (current.parentId != null) {
    expanded.value.add(current.parentId)
    const parent = map.get(current.parentId)
    if (!parent) break
    current = parent
  }
}

function scrollIntoView(id: number) {
  const el = rowRefs.value.get(id)
  const container = containerRef.value
  if (!el || !container) return
  container.scrollTop = el.offsetTop - container.clientHeight / 2 + el.offsetHeight / 2
}

watch(
  () => props.activeNodeId,
  (id) => {
    if (id != null) {
      expandAncestors(id)
      nextTick(() => scrollIntoView(id))
    }
  },
)

onMounted(() => {
  if (props.activeNodeId != null) {
    expandAncestors(props.activeNodeId)
    nextTick(() => scrollIntoView(props.activeNodeId!))
  }
})

function toggle(node: TreeNodeItem) {
  if (expanded.value.has(node.id)) expanded.value.delete(node.id)
  else expanded.value.add(node.id)
}

function reset() {
  expanded.value = new Set()
}

defineExpose({ toggleNode: toggle, reset, containerRef })

const { focusedIndex, containerFocused } = useListKeys(
  containerRef,
  () => visibleNodes.value.length,
  (i) => emit('select', visibleNodes.value[i]!),
)

function selectNode(i: number, node: TreeNodeItem) {
  focusedIndex.value = i
  emit('select', node)
}

// Use the passed-in SearchableTree or build one lazily only when a filter is active
// and no external tree was provided — avoids constructing segment maps on every load.
const internalTree = computed(() =>
  !props.searchTree && props.filter ? new SearchableTree(props.nodes) : null,
)
const activeTree = computed(() => props.searchTree ?? internalTree.value ?? new SearchableTree([]))

const visibleNodes = computed(() => {
  if (props.filter) {
    return activeTree.value.search(props.nodes, props.filter, 100) as TreeNodeItem[]
  }

  const result: TreeNodeItem[] = []
  const hidden = new Set<number>()

  for (const node of props.nodes) {
    if (node.parentId !== null && hidden.has(node.parentId)) {
      hidden.add(node.id)
      continue
    }
    result.push(node)
    if (node.hasChildren && !expanded.value.has(node.id)) hidden.add(node.id)
  }
  return result
})
</script>

<template>
  <div ref="containerRef" class="tree-entries toc-thin-scroll" tabindex="0">
    <TreeNode
      v-for="(node, i) in visibleNodes"
      :key="node.id"
      :ref="(el) => setRowRef(el, node.id)"
      :node="node"
      :expanded="expanded.has(node.id)"
      :active="node.id === activeNodeId"
      :focused="containerFocused && focusedIndex === i"
      :filtered="!!filter"
      :indent="indent"
      :row-height="rowHeight"
      :font-size="fontSize"
      :sticky-headers="stickyHeaders !== false"
      @toggle="toggle(node)"
      @select="selectNode(i, node)"
    >
      {{ filter ? (activeTree.displayPaths.get(node.id) ?? node.text) : node.text }}
    </TreeNode>
  </div>
</template>

<style scoped>
.tree-entries {
  flex: 1;
  height: 100%;
  overflow: auto;
  min-height: 0;
  background: var(--tree-bg, var(--bg-primary));
}
</style>

<style>
.tree-entries .tree-row {
  content-visibility: auto;
  contain-intrinsic-size: auto 28px;
}
.tree-entries .tree-row.is-sticky {
  content-visibility: visible;
}
.tree-entries .tree-row.is-filtered {
  contain-intrinsic-size: auto 56px;
}
</style>
