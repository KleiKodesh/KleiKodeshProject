import { describe, expect, it } from 'vitest'
import type { CategoryNode } from '@/features/book-catalog/bookCatalogTree'
import type { BookRow } from '@/webview-host/queries.types'
import { categoryCheckStates, categoryResultTotals, createFilterTreeRows, expandableCategoryRowIds } from './fullTextSearchFilterTree'

const book = (id: number) => ({ id, title: `b${id}` }) as BookRow
const cat = (id: number, children: CategoryNode[], books: BookRow[]) =>
  ({ id, title: `c${id}`, children, books }) as unknown as CategoryNode

// c1 { c2 { b10, b11 }, b12 }, c3 { b13 }
const tree = () => [cat(1, [cat(2, [], [book(10), book(11)])], [book(12)]), cat(3, [], [book(13)])]

describe('fullTextSearchFilterTree', () => {
  it('sums results over each subtree', () => {
    const totals = categoryResultTotals(tree(), new Map([[10, 2], [12, 5]]))
    expect([...totals]).toEqual([[2, 2], [1, 7], [3, 0]])
  })

  it('gives each category its tri-state', () => {
    const states = categoryCheckStates(tree(), new Set([10, 11, 13]))
    expect(states.get(2)).toEqual({ checked: true, indet: false })
    expect(states.get(1)).toEqual({ checked: false, indet: true })
    expect(states.get(3)).toEqual({ checked: true, indet: false })
  })

  it('rolls computer files and unopened folders up to every folder above', () => {
    const folder = (id: number, children: CategoryNode[], files: string[]) =>
      ({ id, title: `f${id}`, children, books: [], files: files.map((path) => ({ path })) }) as unknown as CategoryNode
    // f1 { f2 { a, b }, f3 (unopened) }
    const roots = () => [folder(1, [folder(2, [], ['a', 'b']), folder(3, [], [])], [])]
    const fileOff = categoryCheckStates(roots(), new Set(), new Set(['b']), new Set())
    expect(fileOff.get(2)).toEqual({ checked: false, indet: true })
    expect(fileOff.get(1)).toEqual({ checked: false, indet: true })
    const folderOff = categoryCheckStates(roots(), new Set(), new Set(['a', 'b']), new Set([3]))
    expect(folderOff.get(3)).toEqual({ checked: false, indet: false })
    expect(folderOff.get(1)).toEqual({ checked: false, indet: true })
    const allOn = categoryCheckStates(roots(), new Set(), new Set(['a', 'b']), new Set())
    expect(allOn.get(1)).toEqual({ checked: true, indet: false })
  })

  it('lists rows in tree order, hiding what has no results after a search', () => {
    const roots = tree()
    const counts = new Map([[10, 2], [12, 5]])
    const build = createFilterTreeRows()
    const all = build(roots, false, counts, categoryResultTotals(roots, counts))
    expect(all.map((r) => r.text)).toEqual(['c1', 'c2', 'b10', 'b11', 'b12', 'c3', 'b13'])
    expect(expandableCategoryRowIds(all)).toHaveLength(3)
    const searched = build(roots, true, counts, categoryResultTotals(roots, counts))
    expect(searched.map((r) => r.text)).toEqual(['c1', 'c2', 'b10', 'b12'])
    // Same rows keep their objects, so TreeView sees the change as spans.
    expect(searched[0]).toBe(all[0])
    expect(searched[2]).toBe(all[2])
  })
})
