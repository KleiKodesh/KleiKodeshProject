<script setup lang="ts">
/**
 * A tree on screen is a flat list (`visibleNodes`), drawn through the app's
 * WindowedScroller: only a window of rows is in the DOM, as real flow content with
 * their own heights (long titles wrap), so a tree of 30k rows expands, restores and
 * scrolls like a tree of 30. The list is recomputed on expand / collapse / filter
 * only, never per scroll.
 *
 * An expand or collapse changes the list in one place; the scroller is told
 * (`replaceRows`) before it renders, so the reader's row stays pixel-still whatever
 * opened or closed above it. Anything else (a new filter, new nodes, a restored
 * shape) is a new list: the scroller is re-created at its start row (`reopenAt`).
 *
 * The search results (filter mode, at most 100 rows with path subtitles) go through
 * the same scroller: one code path for keys, focus and the bar, and rows of any
 * height are what it is built for.
 */
import { ref, computed, watch, onMounted } from 'vue'
import TreeNode from './TreeNode.vue'
import WindowedScroller from './WindowedScroller.vue'
import type { TreeNodeItem } from './treeTypes'
import { useListKeys } from '@/composables/useListKeyNav'
import { SegmentSearchTree, type TreeSearcher } from '@/utils/segmentSearchTree'
import { useElementSize } from '@vueuse/core'
import {
  ancestorPath,
  changedSpans,
  expandAncestorsOf,
  flattenVisible,
  nearestShownIndex,
  type TreeViewScrollPosition,
} from './treeViewRows'

// Vue casts an absent boolean prop to false, so the on-by-default flag needs an explicit default.
const props = withDefaults(defineProps<{
  nodes: TreeNodeItem[]
  filter?: string
  activeNodeId?: number
  indent?: number
  /** Added to every row's level when indenting (see TreeNode). */
  levelOffset?: number
  rowHeight?: number
  fontSize?: string
  stickyHeaders?: boolean
  searchTree?: TreeSearcher
  /** Expanded ids the tree is CREATED with (a restored session). Read once, at mount. */
  initialExpanded?: readonly number[]
  /** The row the tree is created at (a restored session), instead of the active entry. Read once, at mount. */
  initialPosition?: TreeViewScrollPosition | null
  /**
   * New `nodes` are the same tree updated, not another tree: rows shown or hidden, a
   * folder's contents loaded. Each node keeps its object, so the change reaches the
   * scroller as spans (the reader's row stays still), instead of re-opening the tree.
   */
  nodesUpdateInPlace?: boolean
}>(), { stickyHeaders: true })

// The optional event lets consumers honour Ctrl/⌘-click (mouse) or Ctrl/⌘+Enter
// (keyboard) — both flow through wantsNewTab. It is undefined only for
// programmatic selection.
const emit = defineEmits<{
  select: [node: TreeNodeItem, event?: MouseEvent | KeyboardEvent]
  /** The reader opened or closed a node (its chevron, or a sticky row's). */
  toggle: [node: TreeNodeItem, expanded: boolean]
}>()

/**
 * A caller's own row instead of TreeNode, in the list and in the sticky header alike.
 * `toggle` and `select` are what TreeNode's chevron and title do; `header` is true for a
 * sticky copy of an ancestor row.
 */
defineSlots<{
  row?: (props: {
    node: TreeNodeItem
    expanded: boolean
    focused: boolean
    header: boolean
    toggle: () => void
    select: (event?: MouseEvent | KeyboardEvent) => void
  }) => unknown
}>()

const expanded = ref<Set<number>>(new Set())
const rootRef = ref<HTMLElement | null>(null)
const scrollerRef = ref<InstanceType<typeof WindowedScroller> | null>(null)
/** The scroll element (focus, keys) - the scroller's viewport. */
const containerRef = computed<HTMLElement | null>(() => scrollerRef.value?.scrollerElement ?? null)

const nodeById = computed(() => {
  const map = new Map<number, TreeNodeItem>()
  for (const n of props.nodes) map.set(n.id, n)
  return map
})

// Use the passed-in tree or build one lazily only when a filter is active and no
// external tree was provided — avoids constructing segment maps on every load.
const internalTree = computed(() =>
  !props.searchTree && props.filter ? new SegmentSearchTree(props.nodes) : null,
)
const activeTree = computed<TreeSearcher>(() => props.searchTree ?? internalTree.value ?? new SegmentSearchTree([]))

const visibleNodes = computed(() =>
  props.filter
    ? (activeTree.value.search(props.nodes, props.filter, 100) as TreeNodeItem[])
    : flattenVisible(props.nodes, expanded.value),
)
const visibleIndexById = computed(() => new Map(visibleNodes.value.map((n, i) => [n.id, i])))

// ── The scroller and where it opens ─────────────────────────────────────────

/**
 * The row the scroller is created at. Null until the tree is mounted and knows it
 * (centring the active entry needs the pane's height), so the scroller is never
 * created at the top and then moved.
 */
const opening = ref<TreeViewScrollPosition | null>(null)
const scrollerKey = ref(0)

function reopenAt(position: TreeViewScrollPosition) {
  opening.value = position
  scrollerKey.value++
}

/** The offset that centres a row of this height in the pane. */
function centringOffset(rowHeight: number) {
  const height = containerRef.value?.clientHeight ?? rootRef.value?.clientHeight ?? 0
  return -Math.max(0, height / 2 - rowHeight / 2)
}

/** The list is about to be replaced as a whole: the next change is not a mid-list span. */
let isListReplaced = false

watch(() => props.nodes, () => { if (!props.nodesUpdateInPlace) isListReplaced = true })
watch(() => [props.filter, activeTree.value], () => { isListReplaced = true })

// A pre-flush watcher: runs before the scroller patches the new list.
watch(visibleNodes, (next, previous) => {
  if (!opening.value) return
  if (isListReplaced) {
    isListReplaced = false
    // A new search shows its results from the top; leaving it goes back to the active entry.
    reopenAt(props.filter ? { index: 0, offsetPx: 0 } : startForActive())
    return
  }
  for (const span of changedSpans(previous, next)) scrollerRef.value?.replaceRows(span.at, span.removed, span.inserted)
})

/**
 * What to do once the scroller has drawn the list's new shape (a reveal after an
 * expand, the landing after a collapse): it needs the scroller's new count and rows.
 * Run by the post-flush watcher below - Vue's own render order, not a wait.
 */
let afterListRendered: (() => void) | null = null

/** Runs `then` once the list `change` makes has been drawn - at once when it changes nothing. */
function changeList(change: () => void, then: () => void) {
  const before = visibleNodes.value
  change()
  if (visibleNodes.value === before) then()
  else afterListRendered = then
}

watch(
  () => [visibleNodes.value, props.filter],
  () => {
    const then = afterListRendered
    afterListRendered = null
    then?.()
    updateHeader()
  },
  { flush: 'post' },
)

/** Where the tree opens on its own: the active entry centred (ancestors expanded), else the top. */
function startForActive(): TreeViewScrollPosition {
  const id = props.activeNodeId
  positionedNodeId = id
  if (id == null || props.filter) return { index: 0, offsetPx: 0 }
  expandAncestorsOf(id, nodeById.value, expanded.value)
  const index = visibleIndexById.value.get(id) ?? 0
  return { index, offsetPx: centringOffset(props.rowHeight ?? 28) }
}

onMounted(() => {
  if (props.initialExpanded?.length) expanded.value = new Set(props.initialExpanded)
  if (props.initialPosition) {
    // Opened where the reader left it - in the tree, or in the search results when it
    // is created with its query (the position was saved on that same list). The active
    // entry counts as reached, so its sync does not move the tree a second time.
    positionedNodeId = props.activeNodeId
    opening.value = props.initialPosition
    return
  }
  opening.value = startForActive()
})

// ── Following the active entry ──────────────────────────────────────────────

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

function revealCentred(id: number) {
  const index = visibleIndexById.value.get(id)
  const scroller = scrollerRef.value
  if (index == null || !scroller) return
  // Already fully on screen (below the sticky header): leave the pane where it is.
  if (scroller.isRowFullyVisible(index, { topInsetPx: headerHeight.value })) return
  const rowHeight = scroller.rowElement(index)?.offsetHeight ?? props.rowHeight ?? 28
  void scroller.scrollToIndex(index, { offsetPx: centringOffset(rowHeight) })
}

function syncToNode(id: number | undefined) {
  // Already here — nothing to do. Repeat announcements land here and stop.
  if (id === positionedNodeId) return
  positionedNodeId = id
  if (!opening.value || props.filter) return
  // No entry covers the position (the reader is above the tree's first entry):
  // follow them back to the top rather than stay parked on the last one.
  if (id == null) {
    void scrollerRef.value?.scrollToIndex(0)
    return
  }
  changeList(() => expandAncestorsOf(id, nodeById.value, expanded.value), () => revealCentred(id))
}

watch(() => props.activeNodeId, syncToNode)

// ── Expand / collapse ───────────────────────────────────────────────────────

function toggle(node: TreeNodeItem) {
  const opening = !expanded.value.has(node.id)
  if (opening) expanded.value.add(node.id)
  else expanded.value.delete(node.id)
  emit('toggle', node, opening)
}

/** Collapse everything and go back to the top. */
function reset() {
  expanded.value = new Set()
  if (opening.value) reopenAt({ index: 0, offsetPx: 0 })
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

/** The first visible row and how far into it, for the caller to save. Null before the tree has opened. */
function getScrollPosition(): TreeViewScrollPosition | null {
  const visible = scrollerRef.value?.readFirstVisible()
  return visible ? { index: visible.index, offsetPx: visible.offsetWithinRow } : null
}

/**
 * A new shape - a restore, or every node expanded at once. The tree re-opens at
 * `position` (a saved one), else at the row that was at the top, wherever it now is.
 */
function setExpanded(ids: readonly number[], position?: TreeViewScrollPosition | null): void {
  const top = getScrollPosition()
  const topId = top ? visibleNodes.value[top.index]?.id : undefined
  expanded.value = new Set(ids)
  if (!opening.value) return
  if (position) {
    reopenAt(position)
    return
  }
  const index = topId == null ? undefined : nearestShownIndex(topId, nodeById.value, visibleIndexById.value)
  reopenAt(index == null ? { index: 0, offsetPx: 0 } : { index, offsetPx: top!.offsetPx })
}

defineExpose({ toggleNode: toggle, reset, containerRef, getExpanded, setExpanded, getScrollPosition })

// ── Sticky header ───────────────────────────────────────────────────────────

/**
 * Sticky header: the ancestor path of the row at the top of the scroll area (not
 * just its direct parent), drawn as real tree rows in an overlay above the scroller -
 * but only the ancestors whose own rows have scrolled ABOVE the viewport's top. A row
 * still showing needs no sticky copy, and a copy over it would cover the rows below
 * it (at the very top of the tree, the header would sit over the list's first rows).
 * Read from the data: the scroller names the first visible rows, the node names its
 * parents. Recomputed on each of the scroller's moves.
 *
 * The header's height for a path is known before it is drawn: its rows are the
 * path's nodes, the same rows the tree draws (one or two lines - a long title wraps),
 * at the same width. A node already in the header is measured there; otherwise its
 * own row in the scroller when mounted; otherwise one line (`rowHeight`).
 */
const headerRef = ref<HTMLElement | null>(null)
const headerPath = ref<TreeNodeItem[]>([])
// The scroller's drawn bar - the header stops short of it so the bar stays usable.
const scrollbarWidth = ref(0)
const { height: headerHeight } = useElementSize(headerRef, undefined, { box: 'border-box' })

function headerRowHeight(node: TreeNodeItem): number {
  const shownAt = headerPath.value.findIndex((n) => n.id === node.id)
  const shown = shownAt >= 0 ? (headerRef.value?.children[shownAt] as HTMLElement | undefined) : undefined
  if (shown) return shown.offsetHeight
  const index = visibleIndexById.value.get(node.id)
  const scroller = scrollerRef.value
  const row = index != null && scroller?.isRowRendered(index) ? scroller.rowElement(index) : null
  return row?.offsetHeight ?? props.rowHeight ?? 28
}

/** The sticky header's height while it shows `path`. */
function headerHeightFor(path: readonly TreeNodeItem[]): number {
  let height = 0
  for (const node of path) height += headerRowHeight(node)
  return height
}

/**
 * The header path for the row at the top below a header `insetPx` tall: its ancestors
 * whose rows lie above the viewport (scrolled past, wholly or in part). Ancestors come
 * before their descendants in the list, so this is always a leading part of the path.
 * Null when none shows.
 */
function headerPathBelow(insetPx: number): TreeNodeItem[] | null {
  const scroller = scrollerRef.value
  const top = scroller?.readFirstVisible(insetPx)
  const edge = scroller?.readFirstVisible(0)
  if (!top || !edge) return null
  // The row at the viewport's very top: rows before it are above; it is above in part
  // once scrolled into (it starts above the top edge).
  const firstShowing = edge.offsetWithinRow > 0 ? edge.index + 1 : edge.index
  const path = ancestorPath(visibleNodes.value[top.index], nodeById.value)
    .filter((node) => (visibleIndexById.value.get(node.id) ?? Infinity) < firstShowing)
  return path.length ? path : null
}

function updateHeader() {
  const scroller = scrollerRef.value
  if (!scroller || props.filter || props.stickyHeaders === false) {
    headerPath.value = []
    return
  }
  let path = headerPathBelow(headerHeightFor(headerPath.value))
  // Read below the CURRENT header; a header of another height covers another row, so
  // read once more below the new path's own height (once - a row exactly at the edge
  // must not flip-flop).
  if (path) {
    const height = headerHeightFor(path)
    if (height !== headerHeightFor(headerPath.value)) path = headerPathBelow(height) ?? path
  }
  if (!path) {
    headerPath.value = []
    return
  }
  scrollbarWidth.value = scroller.scrollbarElement?.offsetWidth ?? 0
  const same =
    path.length === headerPath.value.length && path.every((n, i) => n.id === headerPath.value[i]!.id)
  if (same) return
  headerPath.value = path
}


/**
 * The sticky rows are the ancestor rows themselves: same look, same chevron, same click.
 * Collapsing one folds the heading away and brings it to the top, where it now sits
 * as an ordinary row (below its own ancestors' header).
 */
function collapseFromHeader(node: TreeNodeItem) {
  // Its header once it sits at the top is its own ancestors - the rows above it in
  // the header now, measured before they change.
  const shownAt = headerPath.value.findIndex((n) => n.id === node.id)
  const insetPx = headerHeightFor(headerPath.value.slice(0, Math.max(0, shownAt)))
  changeList(() => {
    expanded.value.delete(node.id)
    emit('toggle', node, false)
  }, () => {
    const index = visibleIndexById.value.get(node.id)
    // The header follows from the landing's moves and its moveend.
    if (index != null) void scrollerRef.value?.scrollToIndex(index, { offsetPx: -insetPx })
  })
}

function selectFromHeader(node: TreeNodeItem, event?: MouseEvent | KeyboardEvent) {
  positionedNodeId = node.id
  emit('select', node, event)
}

// ── Keys and clicks ─────────────────────────────────────────────────────────

const { focusedIndex, containerFocused } = useListKeys(
  containerRef,
  () => visibleNodes.value.length,
  // Forward the KeyboardEvent so consumers see Ctrl/⌘+Enter (via wantsNewTab),
  // mirroring the MouseEvent they already get from a click.
  (i, _openInNewTab, event) => emit('select', visibleNodes.value[i]!, event),
  { reveal: revealRow },
)

/**
 * Brings a row the keys moved to into view below the sticky header. Once it has
 * scrolled to the top, the header there is its own ancestors - possibly more rows than
 * show now - so a row landed from above lands below THAT header, never under it.
 */
function revealRow(index: number) {
  const scroller = scrollerRef.value
  if (!scroller) return
  const shownInset = headerPath.value.length ? headerHeight.value : 0
  if (scroller.isRowFullyVisible(index, { topInsetPx: shownInset })) return
  const hasHeader = !props.filter && props.stickyHeaders !== false
  const itsHeader = hasHeader ? headerHeightFor(ancestorPath(visibleNodes.value[index], nodeById.value)) : 0
  scroller.revealIndex(index, { topInsetPx: Math.max(shownInset, itsHeader), landMounted: true })
}

function selectNode(i: number, node: TreeNodeItem, event?: MouseEvent | KeyboardEvent) {
  focusedIndex.value = i
  // Select first, scroll second — the same order every sync follows. The row is
  // already under the pointer, so being "positioned" on it is simply true, and
  // recording that here is what makes the sync coming back a no-op instead of a
  // scroll that yanks the row to the centre.
  positionedNodeId = node.id
  emit('select', node, event)
}
</script>

<template>
  <div ref="rootRef" class="tree-view">
    <div v-if="headerPath.length" ref="headerRef" class="tree-sticky-header" :style="{ insetInlineEnd: `${scrollbarWidth}px` }">
      <template v-for="node in headerPath" :key="node.id">
      <slot
        v-if="$slots.row"
        name="row"
        :node="node"
        :expanded="true"
        :focused="false"
        :header="true"
        :toggle="() => collapseFromHeader(node)"
        :select="(event?: MouseEvent | KeyboardEvent) => selectFromHeader(node, event)"
      />
      <TreeNode
        v-else
        :node="node"
        :expanded="true"
        :active="node.id === activeNodeId"
        :focused="false"
        :filtered="false"
        :indent="indent"
        :level-offset="levelOffset"
        :row-height="rowHeight"
        :font-size="fontSize"
        @toggle="collapseFromHeader(node)"
        @select="selectFromHeader(node, $event)"
      />
      </template>
    </div>
    <WindowedScroller
      v-if="opening"
      :key="scrollerKey"
      ref="scrollerRef"
      class="tree-entries"
      thin
      tabindex="0"
      :count="visibleNodes.length"
      :initial-index="opening.index"
      :initial-offset-px="opening.offsetPx"
      :style="{ scrollPaddingTop: `${headerPath.length ? headerHeight : 0}px` }"
      @move="updateHeader"
      @moveend="updateHeader"
      @opened="updateHeader"
    >
      <template #row="{ index }">
        <slot
          v-if="$slots.row && visibleNodes[index]"
          name="row"
          :node="visibleNodes[index]!"
          :expanded="expanded.has(visibleNodes[index]!.id)"
          :focused="containerFocused && focusedIndex === index"
          :header="false"
          :toggle="() => toggle(visibleNodes[index]!)"
          :select="(event?: MouseEvent | KeyboardEvent) => selectNode(index, visibleNodes[index]!, event)"
        />
        <TreeNode
          v-else-if="visibleNodes[index]"
          :node="visibleNodes[index]!"
          :expanded="expanded.has(visibleNodes[index]!.id)"
          :active="visibleNodes[index]!.id === activeNodeId"
          :focused="containerFocused && focusedIndex === index"
          :filtered="!!filter"
          :indent="indent"
          :level-offset="levelOffset"
          :row-height="rowHeight"
          :font-size="fontSize"
          @toggle="toggle(visibleNodes[index]!)"
          @select="selectNode(index, visibleNodes[index]!, $event)"
        >
          {{ filter ? (activeTree.displayPaths.get(visibleNodes[index]!.id) ?? visibleNodes[index]!.text) : visibleNodes[index]!.text }}
        </TreeNode>
      </template>
    </WindowedScroller>
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
  background: var(--tree-bg, var(--bg-primary));
}
.tree-view > .windowed-scroller {
  flex: 1;
  min-height: 0;
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
</style>
