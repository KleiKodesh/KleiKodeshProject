/**
 * TOC Builder Utility
 * 
 * Builds TOC tree from flat JSON data returned by database queries.
 * Used by both C# and TypeScript to ensure consistent TOC structure.
 */

import type { TocEntry } from '../types/BookToc'

/**
 * Build TOC tree from flat data
 */
export function buildTocFromFlat(tocEntriesFlat: TocEntry[]): {
  tree: TocEntry[]
  allTocs: TocEntry[]
} {
  // Convert flat entries to TocEntry objects with tree properties
  const allEntries: TocEntry[] = tocEntriesFlat.map(flat => ({
    ...flat,
    path: '',
    children: []
  }))

  // Build tree structure
  const tree = buildTocChildren(undefined, allEntries)

  // Automatically expand the first root item
  if (tree.length > 0 && tree[0] && tree[0].hasChildren) {
    tree[0].isExpanded = true
  }

  return { tree, allTocs: allEntries }
}

function buildTocChildren(parentId: number | undefined | null, items: TocEntry[]): TocEntry[] {
  const parent = items.find(t => t.id === parentId)
  const children = items.filter(t => {
    // Match null, undefined, or 0 as root level
    if (parentId === undefined || parentId === null) {
      return t.parentId === null || t.parentId === undefined || t.parentId === 0
    }
    return t.parentId === parentId
  })

  for (const child of children) {
    // Build path from parent's path + parent's text (no trailing separator)
    if (parent) {
      if (parent.path) {
        child.path = parent.path + ' > ' + parent.text
      } else {
        child.path = parent.text
      }
    }

    // Recursively build children
    if (child.hasChildren) {
      child.children = buildTocChildren(child.id, items)
    }
  }

  return children
}
