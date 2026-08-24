<script setup lang="ts">
import { ref, computed, watch, nextTick, onMounted } from 'vue'
import TreeNode from './TreeNode.vue'
import type { TreeNodeItem } from './treeTypes'
import { useListKeys } from '@/composables/useListKeyNav'
import { SegmentSearchTree } from '@/utils/segmentSearchTree'

const props = defineProps<{
  nodes: TreeNodeItem[]
  filter?: string
  activeNodeId?: number
  indent?: number
  rowHeight?: number
  fontSize?: string
  stickyHeaders?: boolean
  searchTree?: SegmentSearchTree
}>()

// The optional event lets consumers honour Ctrl/⌘-click (mouse) or Ctrl/⌘+Enter
// (keyboard) — both flow through wantsNewTab. It is undefined only for
// programmatic selection.
const emit = defineEmits<{ select: [node: TreeNodeItem, event?: MouseEvent | KeyboardEvent] }>()

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
  // offsetTop is measured from the nearest positioned ancestor, and the scroll
  // container isn't one — so subtract the container's own offset to get the row's
  // position within it. Without this the second tree of a SplitPane scrolls too
  // far by the height of the pane above it. Rects would be simpler but report a
  // stuck sticky header at its pinned position rather than its layout position.
  const top = el.offsetTop - container.offsetTop
  container.scrollTop = top - container.clientHeight / 2 + el.offsetHeight / 2
}

/**
 * The node this tree has already positioned itself on.
 *
 * Every sync is select-then-scroll: record the target, then move to it. A sync for
 * the node we are ALREADY on returns early, having nothing to do — and that is the
 * whole gate.
 *
 * It has to be a gate on identity rather than a flag on timing because the syncs
 * do not arrive once. A jump makes the virtualizer settle over many frames, and
 * each settle re-derives the active entry and re-announces the SAME node. A
 * one-shot flag is consumed by the first of those and lets every later one
 * through, which is why suppression worked only some of the time. Comparing
 * against the node we are on ignores all of them, however many arrive.
 *
 * It also covers the click case for free: selectNode records the node before the
 * change goes out, so when the sync comes back it matches and no scroll happens.
 * The row stays exactly where the reader clicked it.
 */
let positionedNodeId: number | undefined

function syncToNode(id: number | undefined) {
  // Already here — nothing to do. Repeat announcements land here and stop.
  if (id === positionedNodeId) return
  positionedNodeId = id
  if (id == null) return
  expandAncestors(id)
  nextTick(() => scrollIntoView(id))
}

watch(() => props.activeNodeId, syncToNode)

onMounted(() => {
  syncToNode(props.activeNodeId)
})

function toggle(node: TreeNodeItem) {
  if (expanded.value.has(node.id)) expanded.value.delete(node.id)
  else expanded.value.add(node.id)
}

function reset() {
  expanded.value = new Set()
}

/**
 * Expanded-node access for callers that persist their tree's shape (the book
 * view's TOC). Kept as plain ids so the caller never has to know the tree's
 * structure, and exposed rather than a prop so nothing changes for the callers
 * that are happy with the default expand-the-active-ancestors behaviour.
 */
function getExpanded(): number[] {
  return [...expanded.value]
}

function setExpanded(ids: readonly number[]): void {
  expanded.value = new Set(ids)
}

defineExpose({ toggleNode: toggle, reset, containerRef, getExpanded, setExpanded })

const { focusedIndex, containerFocused } = useListKeys(
  containerRef,
  () => visibleNodes.value.length,
  // Forward the KeyboardEvent so consumers see Ctrl/⌘+Enter (via wantsNewTab),
  // mirroring the MouseEvent they already get from a click.
  (i, _openInNewTab, event) => emit('select', visibleNodes.value[i]!, event),
)

function selectNode(i: number, node: TreeNodeItem, event?: MouseEvent) {
  focusedIndex.value = i
  // Select first, scroll second — the same order every sync follows. The row is
  // already under the pointer, so being "positioned" on it is simply true, and
  // recording that here is what makes the sync coming back a no-op instead of a
  // scroll that yanks the row to the centre.
  positionedNodeId = node.id
  emit('select', node, event)
}

// Use the passed-in tree or build one lazily only when a filter is active and no
// external tree was provided — avoids constructing segment maps on every load.
const internalTree = computed(() =>
  !props.searchTree && props.filter ? new SegmentSearchTree(props.nodes) : null,
)
const activeTree = computed(() => props.searchTree ?? internalTree.value ?? new SegmentSearchTree([]))

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
      @select="selectNode(i, node, $event)"
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
