import { normalize } from '@/utils/normalize'

export interface BookRow {
  id: number; categoryId: number; title: string; heShortDesc: string | null
  orderIndex: number; treeOrder?: number; fullPath?: string; searchPath?: string
}
export interface CategoryRow { id: number; parentId: number | null; title: string; level: number; orderIndex: number }
export interface CategoryNode extends CategoryRow { children: CategoryNode[]; books: BookRow[] }

export function buildTree(categories: CategoryRow[], books: BookRow[]): CategoryNode[] {
  const map = new Map<number, CategoryNode>()
  for (const cat of categories) map.set(cat.id, { ...cat, children: [], books: [] })
  for (const book of books) map.get(book.categoryId)?.books.push(book)
  const roots: CategoryNode[] = []
  for (const node of map.values())
    node.parentId == null ? roots.push(node) : map.get(node.parentId)?.children.push(node)
  return roots
}

export function assignFullPaths(nodes: CategoryNode[], parentPath = '', counter = { n: 0 }): void {
  for (const node of nodes) {
    const nodePath = parentPath ? `${parentPath} / ${node.title}` : node.title
    for (const book of node.books) {
      book.treeOrder = counter.n++
      book.fullPath = `${nodePath} / ${book.title}`
      book.searchPath = normalize(book.fullPath)
    }
    assignFullPaths(node.children, nodePath, counter)
  }
}
