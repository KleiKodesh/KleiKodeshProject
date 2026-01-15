/**
 * TOC Builder Utility
 * 
 * Builds TOC tree from flat JSON data returned by database queries.
 * Used by both C# and TypeScript to ensure consistent TOC structure.
 */

import type { TocEntry } from '../types/BookToc'

/**
 * Alt TOC entry for line display
 */
export interface AltTocLineEntry {
  text: string
  level: number
  lineIndex: number
}

/**
 * Build TOC tree from flat data and create alt TOC lookup map
 */
export function buildTocFromFlat(tocEntriesFlat: TocEntry[]): {
  tree: TocEntry[]
  allTocs: TocEntry[]
  altTocByLineIndex: Map<number, AltTocLineEntry[]>
} {
  // Convert flat entries to TocEntry objects with tree properties
  const allEntries: TocEntry[] = tocEntriesFlat.map(flat => ({
    ...flat,
    path: '',
    children: []
  }))

  // Separate regular and alt TOC entries
  const regularEntries = allEntries.filter(e => !e.isAltToc)
  const altEntries = allEntries.filter(e => e.isAltToc)

  // Build separate trees
  const regularTree = buildTocChildren(undefined, regularEntries)
  const altTree = buildTocChildren(undefined, altEntries)

  // Create alt TOC lookup map for efficient line display - supporting multiple entries per line
  const altTocByLineIndex = new Map<number, AltTocLineEntry[]>()
  altEntries.forEach(entry => {
    if (entry.lineIndex !== undefined && entry.text) {
      const lineIndex = entry.lineIndex
      const altTocEntry: AltTocLineEntry = {
        text: entry.text,
        level: entry.level,
        lineIndex: entry.lineIndex
      }
      
      if (!altTocByLineIndex.has(lineIndex)) {
        altTocByLineIndex.set(lineIndex, [])
      }
      altTocByLineIndex.get(lineIndex)!.push(altTocEntry)
    }
  })

  // Wrap alt TOC in a synthetic root node if it exists
  const tree = [...regularTree]
  if (altTree.length > 0) {
    const altRootNode: TocEntry = {
      id: -1,
      bookId: altEntries[0]?.bookId || 0,
      parentId: undefined,
      level: 0,
      lineId: 0,
      lineIndex: 0,
      isLastChild: true,
      hasChildren: true,
      text: 'כותרות נוספות',
      isAltToc: 1,
      path: '',
      children: altTree,
      isExpanded: false
    }
    tree.push(altRootNode)
  }

  return { tree, allTocs: allEntries, altTocByLineIndex }
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
