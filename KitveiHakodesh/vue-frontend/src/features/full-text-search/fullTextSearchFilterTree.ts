import type { CategoryNode } from '@/features/book-catalog/bookCatalogTree'
import type { BookRow } from '@/webview-host/queries.types'
import type { TreeNodeItem } from '@/components/treeTypes'

/**
 * The FTS filter tree as TreeView rows: one flat list in tree order (a category, its
 * subcategories, its books, its computer files), and the per-category totals and check
 * states the rows show - each worked out ONCE per change, in one pass over the tree,
 * never per row.
 */

type ComputerFile = NonNullable<CategoryNode['files']>[number]

export type FilterTreeItem =
  | { kind: 'category'; category: CategoryNode }
  | { kind: 'book'; book: BookRow }
  | { kind: 'file'; file: ComputerFile }

/** A row of the filter tree. `item` is the live catalog object behind it. */
export interface FilterTreeRow extends TreeNodeItem {
  /** Stable for the life of the tree: the same category, book or file keeps its row object. */
  key: string
  item: FilterTreeItem
}

/** Results per category, summed over its whole subtree. */
export function categoryResultTotals(roots: readonly CategoryNode[], resultCounts: ReadonlyMap<number, number>): Map<number, number> {
  const totals = new Map<number, number>()
  const visit = (node: CategoryNode): number => {
    let total = 0
    for (const child of node.children) total += visit(child)
    for (const book of node.books) total += resultCounts.get(book.id) ?? 0
    totals.set(node.id, total)
    return total
  }
  for (const root of roots) visit(root)
  return totals
}

export interface CategoryCheckState {
  checked: boolean
  indet: boolean
}

/**
 * The tri-state box of every category: its subtree's books plus its own computer files
 * (a computer folder's files count only once it has been opened). A computer folder not
 * opened yet has nothing to count: its own checked state stands.
 */
export function categoryCheckStates(
  roots: readonly CategoryNode[],
  checkedBookIds: ReadonlySet<number>,
  checkedFilePaths?: ReadonlySet<string>,
  uncheckedFolderIds?: ReadonlySet<number>,
): Map<number, CategoryCheckState> {
  const states = new Map<number, CategoryCheckState>()
  const visit = (node: CategoryNode): { books: number; checkedBooks: number } => {
    let books = node.books.length
    let checkedBooks = 0
    for (const book of node.books) if (checkedBookIds.has(book.id)) checkedBooks++
    for (const child of node.children) {
      const sub = visit(child)
      books += sub.books
      checkedBooks += sub.checkedBooks
    }
    const files = node.files ?? []
    let checkedFiles = 0
    for (const file of files) if (checkedFilePaths?.has(file.path)) checkedFiles++
    const items = books + files.length
    const checkedItems = checkedBooks + checkedFiles
    const isUnopenedFolder = node.files !== undefined && items === 0
    states.set(node.id, {
      checked: isUnopenedFolder ? !uncheckedFolderIds?.has(node.id) : items > 0 && checkedItems === items,
      indet: checkedItems > 0 && checkedItems < items,
    })
    return { books, checkedBooks }
  }
  for (const root of roots) visit(root)
  return states
}

/** Every category with something to reveal - what "expand all" opens. */
export function expandableCategoryRowIds(rows: readonly FilterTreeRow[]): number[] {
  return rows.filter((row) => row.item.kind === 'category' && row.hasChildren).map((row) => row.id)
}

/**
 * Builds the filter tree's rows, reusing row objects across calls so an update (new
 * counts, a folder loaded) changes only the rows it shows or hides.
 */
export function createFilterTreeRows() {
  const rowsByKey = new Map<string, FilterTreeRow>()
  let nextId = 1

  function row(key: string, parentId: number | null, level: number, hasChildren: boolean, text: string, item: FilterTreeItem): FilterTreeRow {
    const known = rowsByKey.get(key)
    if (known && known.parentId === parentId && known.level === level && known.hasChildren === hasChildren && known.text === text) {
      // The live object can be a new one with the same identity (a reloaded folder).
      known.item = item
      return known
    }
    const made: FilterTreeRow = { id: known?.id ?? nextId++, key, parentId, level, hasChildren, text, item }
    rowsByKey.set(key, made)
    return made
  }

  /**
   * After a search, only categories and books with results are shown. Every node is
   * listed, collapsed or not (TreeView shows what its expanded ancestors reveal).
   */
  return function build(
    roots: readonly CategoryNode[],
    hasSearched: boolean,
    resultCounts: ReadonlyMap<number, number>,
    totals: ReadonlyMap<number, number>,
  ): FilterTreeRow[] {
    const rows: FilterTreeRow[] = []
    const visit = (node: CategoryNode, parentId: number | null, level: number) => {
      if (hasSearched && !((totals.get(node.id) ?? 0) > 0)) return
      // `files` is set on every computer folder, loaded or not: it can always be opened.
      const hasChildren = node.children.length > 0 || node.books.length > 0 || node.files !== undefined
      const own = row(`c${node.id}`, parentId, level, hasChildren, node.title, { kind: 'category', category: node })
      rows.push(own)
      for (const child of node.children) visit(child, own.id, level + 1)
      for (const book of node.books) {
        if (hasSearched && !resultCounts.get(book.id)) continue
        rows.push(row(`b${node.id}/${book.id}`, own.id, level + 1, false, book.title, { kind: 'book', book }))
      }
      for (const file of node.files ?? []) {
        rows.push(row(`f${node.id}/${file.path}`, own.id, level + 1, false, file.title || file.name, { kind: 'file', file }))
      }
    }
    for (const root of roots) visit(root, null, 0)
    return rows
  }
}
