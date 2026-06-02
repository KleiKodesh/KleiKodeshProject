export interface BookRow {
  id: number
  categoryId: number
  title: string
  hasTeamim?: number | null // 1 if the book has cantillation marks, 0/null if not
  authors?: string | null
  treeOrder?: number
  parentPath?: string // category path without the book title — used for display in search results
  period?: string // Chronological period: תנ"ך, ספרות חז"ל, גאונים, ראשונים, אחרונים, etc.
  rootCategory?: string // First-tier category title
}
export interface CategoryRow {
  id: number
  parentId: number | null
  title: string
  level: number
}
export interface CategoryNode extends CategoryRow {
  children: CategoryNode[]
  books: BookRow[]
}

/** Custom (user-defined) entries have negative IDs and always sort after DB entries. */
function customLast(a: { id: number }, b: { id: number }): number {
  const aCustom = a.id < 0 ? 1 : 0
  const bCustom = b.id < 0 ? 1 : 0
  return aCustom - bCustom
}

export function buildTree(categories: CategoryRow[], books: BookRow[]): CategoryNode[] {
  const map = new Map<number, CategoryNode>()
  for (const cat of categories) map.set(cat.id, { ...cat, children: [], books: [] })

  const orphanedBooks: BookRow[] = []
  for (const book of books) {
    const node = map.get(book.categoryId)
    if (node) node.books.push(book)
    else orphanedBooks.push(book)
  }

  const roots: CategoryNode[] = []
  for (const node of map.values())
    node.parentId == null ? roots.push(node) : (map.get(node.parentId)?.children.push(node) ?? roots.push(node))

  // Sort custom entries (negative IDs) to the end at every level
  for (const node of map.values()) {
    node.children.sort(customLast)
    node.books.sort(customLast)
  }
  roots.sort(customLast)

  // Attach orphaned books (missing category row) to a synthetic node at the end
  if (orphanedBooks.length > 0) {
    roots.push({
      id: -999999,
      parentId: null,
      title: 'ספרים נוספים',
      level: 0,
      children: [],
      books: orphanedBooks,
    })
  }

  return roots
}

export function assignFullPaths(
  nodes: CategoryNode[],
  parentPath = '',
  counter = { n: 0 },
  orderedBooks: BookRow[] = [],
): BookRow[] {
  for (const node of nodes) {
    const nodePath = parentPath ? `${parentPath} / ${node.title}` : node.title
    for (const book of node.books) {
      book.treeOrder = counter.n++
      book.parentPath = nodePath
      orderedBooks.push(book)
    }
    assignFullPaths(node.children, nodePath, counter, orderedBooks)
  }
  return orderedBooks
}

const PERIOD_KEYWORDS: [string, string][] = [
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

/** Single-pass traversal: returns a meaningful commentary group label and root category.
 *  Mirrors the resolveGroupLabel logic so the result is computed once at catalog load. */
export function findCategoryMeta(
  categoryId: number,
  map: Map<number, CategoryNode>,
): {
  period: string | null
  root: string | null
} {
  const visited = new Set<number>()
  let rootCat: CategoryNode | undefined
  let mifarsheimParentTitle: string | null = null
  let fallbackPeriod: string | null = null

  let currentId: number | null | undefined = categoryId
  while (currentId != null) {
    if (visited.has(currentId)) break
    visited.add(currentId)
    const cat = map.get(currentId)
    if (!cat) break

    if (cat.parentId == null) {
      rootCat = cat
      break
    }

    const title = cat.title

    // חברותא
    if (title === 'ביאור חברותא' || title === 'הערות על ביאור חברותא')
      return { period: 'חברותא', root: rootCat?.title ?? null }

    // "על ה..." commentary buckets
    if (
      title.includes('על התנ״ך') ||
      title.includes('על התנ"ך') ||
      title.includes('על התלמוד') ||
      title.includes('על המשנה') ||
      title.includes('על המשניות') ||
      title.includes('על הש"ס') ||
      title.includes('על השס')
    )
      return { period: title, root: rootCat?.title ?? null }

    // Broad families
    if (title.includes('חסידות')) return { period: 'חסידות', root: rootCat?.title ?? null }
    if (title.includes('מילונים')) return { period: 'מילונים', root: rootCat?.title ?? null }
    if (title === 'מחברי זמננו') return { period: 'מחברי זמננו', root: rootCat?.title ?? null }
    if (title === 'ראשונים') return { period: 'ראשונים', root: rootCat?.title ?? null }

    // מפרשים → "מפרשים על <parent>" — capture once, keep walking for a more specific match
    if (title === 'מפרשים' && mifarsheimParentTitle === null) {
      const parent = cat.parentId != null ? map.get(cat.parentId) : null
      mifarsheimParentTitle = parent?.title ?? ''
    }

    // Classic period fallback
    if (!fallbackPeriod) fallbackPeriod = detectPeriod(title)

    currentId = cat.parentId
  }

  if (mifarsheimParentTitle !== null)
    return { period: `מפרשים על ${mifarsheimParentTitle}`, root: rootCat?.title ?? null }

  return {
    period: fallbackPeriod ?? (rootCat ? rootCat.title : null),
    root: rootCat?.title ?? null,
  }
}
