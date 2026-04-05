<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import TreeNode, { type TreeNodeItem } from './TreeNode.vue'
import { useListKeys } from '@/composables/useListKeyNav'

const props = defineProps<{
  nodes: TreeNodeItem[]
  filter?: string
  activeNodeId?: number
  visible?: boolean
  indent?: number
  rowHeight?: number
  fontSize?: string
  suppressScroll?: boolean
  stickyHeaders?: boolean
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

const nodeMap = computed(() => {
  const map = new Map<number, TreeNodeItem>()
  for (const n of props.nodes) map.set(n.id, n)
  return map
})

function expandAncestors(id: number) {
  const node = props.nodes.find((n) => n.id === id)
  if (!node) return
  let current = node
  while (current.parentId != null) {
    expanded.value.add(current.parentId)
    const parent = nodeMap.value.get(current.parentId)
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
    if (id == null || props.suppressScroll) return
    expandAncestors(id)
    nextTick(() => scrollIntoView(id))
  },
)

watch(
  () => props.visible,
  (val) => {
    if (val && props.activeNodeId != null) nextTick(() => scrollIntoView(props.activeNodeId!))
  },
)

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

function getPath(node: TreeNodeItem): string {
  const parts: string[] = []
  let current: TreeNodeItem | undefined = node
  while (current) {
    parts.unshift(current.text)
    current = current.parentId != null ? nodeMap.value.get(current.parentId) : undefined
  }
  return parts.join(' / ')
}

const visibleNodes = computed(() => {
  if (props.filter) return props.nodes.filter((n) => n.text.includes(props.filter!))

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
      {{ filter ? getPath(node) : node.text }}
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
