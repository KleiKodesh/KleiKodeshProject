import { describe, expect, it } from 'vitest'
import { assignFullPaths, buildTree } from './bookCatalogTree'
import type { BookRow, CategoryRow } from '@/webview-host/queries.types'

// treeOrder is the SERVER's number (SeforimDbBookTreeOrder.cs). The frontend must not
// renumber it: these rows deliberately carry a treeOrder that contradicts folder position
// (the base text 100 sits in folder 2 but is numbered BEFORE the subfolder's book 102).
const categories: CategoryRow[] = [
  { id: 1, parentId: null, title: 'Halacha', level: 0 },
  { id: 2, parentId: 1, title: 'Tur', level: 1 },
  { id: 3, parentId: 2, title: 'Mefarshim', level: 2 },
]

function rows(): BookRow[] {
  return [
    { id: 102, categoryId: 3, title: 'Beit Yosef', treeOrder: 1 },
    { id: 100, categoryId: 2, title: 'Tur', treeOrder: 0 },
    { id: 101, categoryId: 2, title: 'Notes', treeOrder: 2 },
  ]
}

describe('assignFullPaths', () => {
  it('orders books by the server treeOrder, not by folder position', () => {
    const ordered = assignFullPaths(buildTree(categories, rows()))
    expect(ordered.map((b) => b.id)).toEqual([100, 102, 101])
  })

  it('fills parentPath from the tree', () => {
    const ordered = assignFullPaths(buildTree(categories, rows()))
    expect(ordered.find((b) => b.id === 102)?.parentPath).toBe('Halacha / Tur / Mefarshim')
    expect(ordered.find((b) => b.id === 100)?.parentPath).toBe('Halacha / Tur')
  })

  it('numbers books without a treeOrder (custom, orphaned) after the last server value', () => {
    const books = rows()
    books.push({ id: -5, categoryId: 2, title: 'Custom', treeOrder: undefined })
    books.push({ id: 900, categoryId: 4242, title: 'Orphan' })
    const ordered = assignFullPaths(buildTree(categories, books))
    expect(ordered.map((b) => b.id)).toEqual([100, 102, 101, -5, 900])
    expect(ordered.map((b) => b.treeOrder)).toEqual([0, 1, 2, 3, 4])
  })
})
