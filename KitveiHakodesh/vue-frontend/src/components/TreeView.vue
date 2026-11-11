<script setup lang="ts">
import { ref, computed, watch, nextTick, onMounted } from 'vue'
import TreeNode from './TreeNode.vue'
import type { TreeNodeItem } from './treeTypes'
import { useListKeys } from '@/composables/useListKeyNav'
import { SegmentSearchTree, type TreeSearcher } from '@/utils/segmentSearchTree'
import { useElementSize } from '@vueuse/core'

// Vue casts an absent boolean prop to false, so the on-by-default flag needs an explicit default.
const props = withDefaults(defineProps<{
  nodes: TreeNodeItem[]
  filter?: string
  activeNodeId?: number
  indent?: number
  rowHeight?: number
  fontSize?: string
  stickyHeaders?: boolean
  searchTree?: TreeSearcher
}>(), { stickyHeaders: true })

// The optional event lets consumers honour Ctrl/⌘-click (mouse) or Ctrl/⌘+Enter
// (keyboard) — both flow through wantsNewTab. It is undefined only for
// programmatic selection.
const emit = defineEmits<{ select: [node: TreeNodeItem, event?: MouseEvent | KeyboardEvent] }>()

const expanded = ref<Set<number>>(new Set())
const containerRef = ref<HTMLElement | null>(null)

/**
 * The row element of a visible node. Rows are the container's children in
 * visibleNodes order, so no per-row refs are kept (a ref callback per row is
 * re-run for every row on every render).
 */
function rowElAt(index: number): HTMLElement | null {
  return (containerRef.value?.children[index] as HTMLElement | undefined) ?? null
}
function rowEl(id: number): HTMLElement | null {
  const index = visibleIndexById.value.get(id)
  return index == null ? null : rowElAt(index)
}

function expandAncestors(id: number) {
  const map = nodeById.value
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
  const el = rowEl(id)
  const container = containerRef.value
  if (!el || !container) return
  // Already fully on screen (below the sticky header): leave the pane where it is.
  const rowTopInPane = el.offsetTop - container.offsetTop
  const viewTop = container.scrollTop + (headerRef.value?.offsetHeight ?? 0)
  if (rowTopInPane >= viewTop && rowTopInPane + el.offsetHeight <= container.scrollTop + container.clientHeight) return
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
 * do not arrive once. A jump is followed by several scroll and window updates, and
 * each of them re-derives the active entry and re-announces the SAME node. A
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
  // No entry covers the position (the reader is above the tree's first entry):
  // follow them back to the top rather than stay parked on the last one.
  if (id == null) {
    if (containerRef.value) containerRef.value.scrollTop = 0
    return
  }
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

/**
 * Sticky header: the full ancestor path of the row at the top of the scroll
 * area (not just its direct parent), drawn as real tree rows in an overlay above the
 * scroller. Recomputed once per frame on scroll.
 */
const headerRef = ref<HTMLElement | null>(null)
const headerPath = ref<TreeNodeItem[]>([])
// The scroller's scrollbar width - the header stops short of it so the bar stays usable.
const scrollbarWidth = ref(0)
const nodeById = computed(() => {
  const map = new Map<number, TreeNodeItem>()
  for (const n of props.nodes) map.set(n.id, n)
  return map
})

function rowTop(el: HTMLElement, container: HTMLElement) {
  return el.offsetTop - container.offsetTop
}

/** First visible row whose bottom is below `y` (binary search - rows are in order). */
function rowAt(y: number): TreeNodeItem | undefined {
  const container = containerRef.value
  const list = visibleNodes.value
  if (!container) return undefined
  let lo = 0
  let hi = list.length - 1
  let found: TreeNodeItem | undefined
  while (lo <= hi) {
    const mid = (lo + hi) >> 1
    const el = rowElAt(mid)
    if (!el) return undefined
    if (rowTop(el, container) + el.offsetHeight > y) {
      found = list[mid]
      hi = mid - 1
    } else lo = mid + 1
  }
  return found
}

function updateHeader() {
  const container = containerRef.value
  if (!container || props.filter || props.stickyHeaders === false || container.scrollTop <= 0) {
    headerPath.value = []
    return
  }
  scrollbarWidth.value = container.offsetWidth - container.clientWidth
  const top = rowAt(container.scrollTop + (headerRef.value?.offsetHeight ?? 0))
  const path: TreeNodeItem[] = []
  let parentId = top?.parentId ?? null
  while (parentId != null) {
    const parent = nodeById.value.get(parentId)
    if (!parent) break
    path.unshift(parent)
    parentId = parent.parentId
  }
  const same =
    path.length === headerPath.value.length && path.every((n, i) => n.id === headerPath.value[i]!.id)
  if (!same) headerPath.value = path
}

let headerFrame = 0
function onScroll() {
  if (headerFrame) return
  headerFrame = requestAnimationFrame(() => {
    headerFrame = 0
    updateHeader()
  })
}

/**
 * The sticky rows are the ancestor rows themselves: same look, same chevron, same click.
 * Collapsing one folds the heading away and brings it to the top, where it now sits
 * as an ordinary row.
 */
function collapseFromHeader(node: TreeNodeItem) {
  expanded.value.delete(node.id)
  nextTick(() => {
    const el = rowEl(node.id)
    const container = containerRef.value
    if (!el || !container) return
    container.scrollTop = rowTop(el, container)
    updateHeader()
    nextTick(() => {
      container.scrollTop = rowTop(el, container) - (headerRef.value?.offsetHeight ?? 0)
    })
  })
}

function selectFromHeader(node: TreeNodeItem, event?: MouseEvent) {
  positionedNodeId = node.id
  emit('select', node, event)
}

// Keyboard nav / reveal-active scroll rows into view: keep them out from under the header.
const { height: headerHeight } = useElementSize(headerRef, undefined, { box: 'border-box' })

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
const activeTree = computed<TreeSearcher>(() => props.searchTree ?? internalTree.value ?? new SegmentSearchTree([]))

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
const visibleIndexById = computed(() => new Map(visibleNodes.value.map((n, i) => [n.id, i])))
// After visibleNodes is declared - the getter reads it during setup.
watch(
  () => [visibleNodes.value, props.filter],
  () => nextTick(updateHeader),
)
</script>

<template>
  <div class="tree-view">
  <div v-if="headerPath.length" ref="headerRef" class="tree-sticky-header" :style="{ insetInlineEnd: `${scrollbarWidth}px` }">
    <TreeNode
      v-for="node in headerPath"
      :key="node.id"
      :node="node"
      :expanded="true"
      :active="node.id === activeNodeId"
      :focused="false"
      :filtered="false"
      :indent="indent"
      :row-height="rowHeight"
      :font-size="fontSize"
      @toggle="collapseFromHeader(node)"
      @select="selectFromHeader(node, $event)"
    />
  </div>
  <div ref="containerRef" class="tree-entries toc-thin-scroll" tabindex="0" :style="{ scrollPaddingTop: `${headerPath.length ? headerHeight : 0}px` }" @scroll.passive="onScroll">
    <TreeNode
      v-for="(node, i) in visibleNodes"
      :key="node.id"
      v-memo="[node, i, expanded.has(node.id), node.id === activeNodeId, containerFocused && focusedIndex === i, filter, activeTree, indent, rowHeight, fontSize]"
      :node="node"
      :expanded="expanded.has(node.id)"
      :active="node.id === activeNodeId"
      :focused="containerFocused && focusedIndex === i"
      :filtered="!!filter"
      :indent="indent"
      :row-height="rowHeight"
      :font-size="fontSize"
      @toggle="toggle(node)"
      @select="selectNode(i, node, $event)"
    >
      {{ filter ? (activeTree.displayPaths.get(node.id) ?? node.text) : node.text }}
    </TreeNode>
  </div>
  </div>
</template>

<style scoped>
.tree-view {
  position: relative;
  flex: 1;
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
/* Overlay, not in the flow: its height changes with the path and must not shift the rows. */
.tree-sticky-header {
  position: absolute;
  inset-inline-start: 0;
  top: 0;
  z-index: 2;
  display: flex;
  flex-direction: column;
  background: var(--tree-bg, var(--bg-primary));
}
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
.tree-entries .tree-row.is-filtered {
  contain-intrinsic-size: auto 56px;
}
</style>
