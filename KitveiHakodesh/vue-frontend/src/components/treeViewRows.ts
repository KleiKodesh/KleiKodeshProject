import type { TreeNodeItem } from './treeTypes'

/**
 * A tree on screen is a flat list: the rows TreeView shows, in order, and the pure
 * questions it asks of them. Kept apart from the component so each is testable.
 */

/** The rows on screen: every node whose ancestors are all expanded, in tree order. */
export function flattenVisible(nodes: readonly TreeNodeItem[], expanded: ReadonlySet<number>): TreeNodeItem[] {
  const result: TreeNodeItem[] = []
  const hidden = new Set<number>()
  for (const node of nodes) {
    if (node.parentId !== null && hidden.has(node.parentId)) {
      hidden.add(node.id)
      continue
    }
    result.push(node)
    if (node.hasChildren && !expanded.has(node.id)) hidden.add(node.id)
  }
  return result
}

/**
 * Where two row lists differ, as ONE span: the `removed` rows from `at` became
 * `inserted` rows. An expand or a collapse is exactly one such span (the subtree
 * that appeared or went). Null when the lists are the same.
 */
export function changedSpan(
  before: readonly TreeNodeItem[],
  after: readonly TreeNodeItem[],
): { at: number; removed: number; inserted: number } | null {
  const shorter = Math.min(before.length, after.length)
  let at = 0
  while (at < shorter && before[at] === after[at]) at++
  if (at === before.length && at === after.length) return null
  let tail = 0
  while (tail < shorter - at && before[before.length - 1 - tail] === after[after.length - 1 - tail]) tail++
  return { at, removed: before.length - at - tail, inserted: after.length - at - tail }
}

/**
 * Where two row lists differ, as separate spans, LAST first (so applying them in
 * order never shifts the index of one still to apply). An expand or collapse is a
 * pure insert or a pure removal, but not always in one place: a node's rows need
 * not be contiguous in `nodes` (an outline whose groups were merged in later), so
 * its subtree can surface in several runs. A change that both adds and removes
 * rows is returned as one span covering it.
 */
export function changedSpans(
  before: readonly TreeNodeItem[],
  after: readonly TreeNodeItem[],
): { at: number; removed: number; inserted: number }[] {
  const single = changedSpan(before, after)
  if (!single || before.length === after.length) return single ? [single] : []
  const isInsert = after.length > before.length
  const longer = isInsert ? after : before
  const shorter = isInsert ? before : after
  const spans: { at: number; removed: number; inserted: number }[] = []
  // Indexes are in AFTER-list terms for inserts, BEFORE-list terms for removals;
  // applied last-first, both are the index the span has when it is applied.
  let j = 0
  let i = 0
  while (i < longer.length) {
    if (j < shorter.length && longer[i] === shorter[j]) { i++; j++; continue }
    const start = i
    while (i < longer.length && !(j < shorter.length && longer[i] === shorter[j])) i++
    spans.push(isInsert ? { at: start, removed: 0, inserted: i - start } : { at: start, removed: i - start, inserted: 0 })
  }
  if (j !== shorter.length) return [single]
  // Inserts: `at` in the after-list equals the index in a list where only the runs
  // before it have been inserted - apply first-first. Removals: apply last-first.
  return isInsert ? spans : spans.reverse()
}

/** The node's ancestors, root first (the sticky header's rows). */
export function ancestorPath(node: TreeNodeItem | undefined, nodeById: ReadonlyMap<number, TreeNodeItem>): TreeNodeItem[] {
  const path: TreeNodeItem[] = []
  let parentId = node?.parentId ?? null
  while (parentId != null) {
    const parent = nodeById.get(parentId)
    if (!parent) break
    path.unshift(parent)
    parentId = parent.parentId
  }
  return path
}

/** Adds every ancestor of `id` to `expanded`, so its row is on screen. */
export function expandAncestorsOf(id: number, nodeById: ReadonlyMap<number, TreeNodeItem>, expanded: Set<number>) {
  for (const ancestor of ancestorPath(nodeById.get(id), nodeById)) {
    if (!expanded.has(ancestor.id)) expanded.add(ancestor.id)
  }
}

/** The nearest row on screen for a node that may be hidden: itself, else its closest shown ancestor. */
export function nearestShownIndex(
  id: number,
  nodeById: ReadonlyMap<number, TreeNodeItem>,
  indexById: ReadonlyMap<number, number>,
): number | undefined {
  let current = nodeById.get(id)
  while (current) {
    const index = indexById.get(current.id)
    if (index != null) return index
    current = current.parentId == null ? undefined : nodeById.get(current.parentId)
  }
  return undefined
}

/** Where a tree's scroll position is kept: the first visible row and how far into it - as the book view's. */
export interface TreeViewScrollPosition {
  index: number
  offsetPx: number
}
