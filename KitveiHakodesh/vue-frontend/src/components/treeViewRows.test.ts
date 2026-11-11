import { describe, expect, it } from 'vitest'
import type { TreeNodeItem } from './treeTypes'
import { ancestorPath, changedSpan, changedSpans, expandAncestorsOf, flattenVisible, nearestShownIndex } from './treeViewRows'

// 1 > (2 > (3, 4), 5), 6 > 7
function node(id: number, parentId: number | null, level: number, hasChildren = false): TreeNodeItem {
  return { id, parentId, level, hasChildren, text: `n${id}` } as TreeNodeItem
}
const nodes = [node(1, null, 0, true), node(2, 1, 1, true), node(3, 2, 2), node(4, 2, 2), node(5, 1, 1), node(6, null, 0, true), node(7, 6, 1)]
const byId = new Map(nodes.map((n) => [n.id, n]))
const ids = (list: TreeNodeItem[]) => list.map((n) => n.id)

describe('treeViewRows', () => {
  it('flattens only rows whose ancestors are all expanded', () => {
    expect(ids(flattenVisible(nodes, new Set()))).toEqual([1, 6])
    expect(ids(flattenVisible(nodes, new Set([1])))).toEqual([1, 2, 5, 6])
    expect(ids(flattenVisible(nodes, new Set([2])))).toEqual([1, 6])
    expect(ids(flattenVisible(nodes, new Set([1, 2, 6])))).toEqual([1, 2, 3, 4, 5, 6, 7])
  })

  it('an expand or a collapse is one changed span', () => {
    const closed = flattenVisible(nodes, new Set([1]))
    const open = flattenVisible(nodes, new Set([1, 2]))
    expect(changedSpan(closed, open)).toEqual({ at: 2, removed: 0, inserted: 2 })
    expect(changedSpan(open, closed)).toEqual({ at: 2, removed: 2, inserted: 0 })
    expect(changedSpan(open, open)).toBeNull()
  })

  it('expanding nested ancestors at once is still one span', () => {
    const before = flattenVisible(nodes, new Set())
    const expanded = new Set<number>()
    expandAncestorsOf(4, byId, expanded)
    expect([...expanded].sort()).toEqual([1, 2])
    expect(changedSpan(before, flattenVisible(nodes, expanded))).toEqual({ at: 1, removed: 0, inserted: 4 })
  })

  it('the sticky header path is the ancestors, root first', () => {
    expect(ids(ancestorPath(byId.get(4), byId))).toEqual([1, 2])
    expect(ancestorPath(byId.get(1), byId)).toEqual([])
    expect(ancestorPath(undefined, byId)).toEqual([])
  })

  it('a hidden node maps to its nearest shown ancestor', () => {
    const shown = flattenVisible(nodes, new Set([1]))
    const indexById = new Map(shown.map((n, i) => [n.id, i]))
    expect(nearestShownIndex(4, byId, indexById)).toBe(1)
    expect(nearestShownIndex(5, byId, indexById)).toBe(2)
    expect(nearestShownIndex(99, byId, indexById)).toBeUndefined()
  })

  it('a subtree in several runs (nodes not in tree order) is several spans', () => {
    // 1's children 2 and 3 sit apart, with root 9 between them in the array.
    const list = [node(1, null, 0, true), node(2, 1, 1), node(9, null, 0), node(3, 1, 1)]
    const closed = flattenVisible(list, new Set())
    const open = flattenVisible(list, new Set([1]))
    expect(ids(closed)).toEqual([1, 9])
    expect(changedSpans(closed, open)).toEqual([{ at: 1, removed: 0, inserted: 1 }, { at: 3, removed: 0, inserted: 1 }])
    expect(changedSpans(open, closed)).toEqual([{ at: 3, removed: 1, inserted: 0 }, { at: 1, removed: 1, inserted: 0 }])
    expect(changedSpans(open, open)).toEqual([])
  })
})
