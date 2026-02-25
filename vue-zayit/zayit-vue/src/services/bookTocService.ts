/**
 * TOC Service
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

export class TocService {
    private static instance: TocService

    static getInstance(): TocService {
        if (!TocService.instance) {
            TocService.instance = new TocService()
        }
        return TocService.instance
    }

    /**
     * Build TOC tree from flat data and create alt TOC lookup map
     */
    buildTocFromFlat(tocEntriesFlat: TocEntry[]): {
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
        const regularTree = this.buildTocChildren(undefined, regularEntries)
        const altTree = this.buildTocChildren(undefined, altEntries)

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
                isLastChild: false,
                hasChildren: true,
                text: 'חלוקה נוספת',
                isAltToc: 1,
                path: '',
                children: altTree,
                isExpanded: false
            }
            tree.unshift(altRootNode) // Add to beginning instead of end
        }

        // Set first regular root item to be expanded by default
        // Find the first non-alt TOC entry in the tree
        const firstRegularEntry = tree.find(entry => !entry.isAltToc)
        if (firstRegularEntry) {
            firstRegularEntry.isExpanded = true
        }

        return { tree, allTocs: allEntries, altTocByLineIndex }
    }

    private buildTocChildren(parentId: number | undefined | null, items: TocEntry[]): TocEntry[] {
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
                    child.path = parent.path + ' - ' + parent.text
                } else {
                    child.path = parent.text
                }
            }

            // Recursively build children
            if (child.hasChildren) {
                child.children = this.buildTocChildren(child.id, items)
            }
        }

        return children
    }

    /**
     * Find TOC entry by line index
     */
    findTocEntryByLineIndex(tocEntries: TocEntry[], lineIndex: number): TocEntry | null {
        for (const entry of tocEntries) {
            if (entry.lineIndex === lineIndex) {
                return entry
            }
            if (entry.children && entry.children.length > 0) {
                const found = this.findTocEntryByLineIndex(entry.children, lineIndex)
                if (found) return found
            }
        }
        return null
    }

    /**
     * Get all TOC entries as flat array
     */
    flattenTocTree(tocEntries: TocEntry[]): TocEntry[] {
        const flattened: TocEntry[] = []

        const flatten = (entries: TocEntry[]) => {
            for (const entry of entries) {
                flattened.push(entry)
                if (entry.children && entry.children.length > 0) {
                    flatten(entry.children)
                }
            }
        }

        flatten(tocEntries)
        return flattened
    }

    /**
     * Get TOC path as string
     */
    getTocPath(entry: TocEntry): string {
        if (entry.path) {
            return entry.path + ' - ' + entry.text
        }
        return entry.text
    }
}

// Export singleton instance and legacy function
export const bookTocService = TocService.getInstance()
export const buildTocFromFlat = bookTocService.buildTocFromFlat.bind(bookTocService)