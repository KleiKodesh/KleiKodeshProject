export interface BookRow {
  id: number
  categoryId: number
  title: string
  heShortDesc: string | null
  orderIndex: number
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
  for (const node of map.values()) {
    if (node.parentId == null) roots.push(node)
    else map.get(node.parentId)?.children.push(node)
  }
  return roots
}
