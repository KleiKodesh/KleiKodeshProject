import { normalize } from '@/utils/normalize'

export interface BookRow {
  id: number; categoryId: number; title: string; heShortDesc: string | null
  orderIndex: number; treeOrder?: number; fullPath?: string; searchPath?: string
  period?: string          // Chronological period: תנ"ך, ספרות חז"ל, גאונים, ראשונים, אחרונים, etc.
  rootCategory?: string    // First-tier category title
  secondaryCategory?: string // Second-tier category title (if exists)
  rootCategoryOrder?: number
  secondaryCategoryOrder?: number
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

export function findCategoryHierarchy(categoryId: number, map: Map<number, CategoryNode>): {
  root: string | null; secondary: string | null; rootOrder: number | null; secondaryOrder: number | null
} {
  const visited = new Set<number>()
  let rootCat: CategoryNode | undefined
  let secondaryCat: CategoryNode | undefined

  function traverse(id: number): void {
    if (visited.has(id)) return
    visited.add(id)
    const cat = map.get(id)
    if (!cat) return
    if (cat.parentId == null) { rootCat = cat; return }
    const parent = map.get(cat.parentId)
    if (parent && parent.parentId == null) secondaryCat = cat
    if (cat.parentId) traverse(cat.parentId)
  }

  traverse(categoryId)
  return {
    root: rootCat ? rootCat.title : null,
    secondary: secondaryCat ? secondaryCat.title : null,
    rootOrder: rootCat ? rootCat.orderIndex : null,
    secondaryOrder: secondaryCat ? secondaryCat.orderIndex : null,
  }
}

export function findCategoryPeriod(categoryId: number, map: Map<number, CategoryNode>): string | null {
  const visited = new Set<number>()
  let rootCat: CategoryNode | undefined

  function traverse(id: number): string | null {
    if (visited.has(id)) return null
    visited.add(id)
    const cat = map.get(id)
    if (!cat) return null
    if (cat.parentId == null) rootCat = cat
    const title = cat.title.toLowerCase()
    if (title.includes('גאונים')) return 'גאונים'
    if (title.includes('ראשונים')) return 'ראשונים'
    if (title.includes('אחרונים')) return 'אחרונים'
    if (cat.parentId) return traverse(cat.parentId)
    return null
  }

  const period = traverse(categoryId)
  if (!period && rootCat) return rootCat.title
  return period
}
