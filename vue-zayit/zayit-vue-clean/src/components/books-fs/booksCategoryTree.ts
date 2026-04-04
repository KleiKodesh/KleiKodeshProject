import { normalize } from '@/utils/normalizeText'

export interface BookRow {
  id: number
  categoryId: number
  title: string
  treeOrder?: number
  fullPath?: string
  searchPath?: string
  period?: string // Chronological period: תנ"ך, ספרות חז"ל, גאונים, ראשונים, אחרונים, etc.
  rootCategory?: string // First-tier category title
}
export interface CategoryRow {
  id: number
  parentId: number | null
  title: string
  level: number
  orderIndex: number
}
export interface CategoryNode extends CategoryRow {
  children: CategoryNode[]
  books: BookRow[]
}

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

const PERIOD_KEYWORDS: [string, string][] = [
  ['גאונים', 'גאונים'],
  ['ראשונים', 'ראשונים'],
  ['אחרונים', 'אחרונים'],
  ['מדרש', 'מדרש'],
  ['תלמוד', 'תלמוד'],
  ['תוספתא', 'תוספתא'],
  ['תנ"ך', 'תנ"ך'],
  ['מקרא', 'תנ"ך'],
]

function detectPeriod(title: string): string | null {
  if (title === 'משנה') return 'משנה'
  for (const [keyword, period] of PERIOD_KEYWORDS) if (title.includes(keyword)) return period
  return null
}

/** Single-pass traversal: returns period and root category info. */
export function findCategoryMeta(
  categoryId: number,
  map: Map<number, CategoryNode>,
): {
  period: string | null
  root: string | null
} {
  const visited = new Set<number>()
  let rootCat: CategoryNode | undefined
  let period: string | null = null

  function traverse(id: number): void {
    if (visited.has(id)) return
    visited.add(id)
    const cat = map.get(id)
    if (!cat) return
    if (cat.parentId == null) {
      rootCat = cat
      return
    }
    if (!period) period = detectPeriod(cat.title)
    if (cat.parentId) traverse(cat.parentId)
  }

  traverse(categoryId)
  return {
    period: period ?? (rootCat ? rootCat.title : null),
    root: rootCat?.title ?? null,
  }
}
