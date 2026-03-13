/**
 * TOC Service
 * 
 * Builds TOC tree from flat JSON data returned by database queries.
 * Used by both C# and TypeScript to ensure consistent TOC structure.
 */

import type { TocEntry } from '@/data/types/BookToc'

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
     * If bookTitle is provided and root TOC matches it, elevate children to roots
     */
    buildTocFromFlat(tocEntriesFlat: TocEntry[], bookTitle?: string): {
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

        // Check if we should skip root during path building
        let skipRootId: number | undefined
        console.log('[TOC Service] Starting root check - bookTitle:', bookTitle, 'regularEntries:', regularEntries.length)
        if (bookTitle && regularEntries.length > 0) {
            const potentialRoot = regularEntries.find(e => !e.parentId || e.parentId === 0)
            console.log('[TOC Service] Potential root found:', potentialRoot?.text, 'id:', potentialRoot?.id)
            if (potentialRoot &&
                (potentialRoot.level === 0 || !potentialRoot.parentId) &&
                potentialRoot.text.trim().toLowerCase() === bookTitle.trim().toLowerCase()) {
                skipRootId = potentialRoot.id
                console.log('[TOC Service] ✅ Root matches book title - will elevate. skipRootId:', skipRootId)
            } else if (potentialRoot) {
                console.log('[TOC Service] ❌ Root does NOT match:')
                console.log('  Root text:', `"${potentialRoot.text.trim().toLowerCase()}"`)
                console.log('  Book title:', `"${bookTitle.trim().toLowerCase()}"`)
                console.log('  Match:', potentialRoot.text.trim().toLowerCase() === bookTitle.trim().toLowerCase())
            }
        }

        // Build separate trees
        let regularTree = this.buildTocChildren(undefined, regularEntries, skipRootId)
        const altTree = this.buildTocChildren(undefined, altEntries)

        // Elevate children if single root matches book title (only for regular TOC, not alt TOC)
        if (skipRootId !== undefined && regularTree.length === 1) {
            const rootEntry = regularTree[0]
            console.log('[TOC Service] Elevating - root has', rootEntry?.children?.length || 0, 'children')
            if (rootEntry && rootEntry.children && rootEntry.children.length > 0) {
                regularTree = rootEntry.children
                console.log('[TOC Service] ✅ Children elevated. New root count:', regularTree.length)
            }
        }

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

        // Combine regular and alt trees without wrapping
        const tree = [...regularTree, ...altTree]

        // Set first regular root item to be expanded by default
        if (regularTree.length > 0 && regularTree[0]) {
            regularTree[0].isExpanded = true
        }

        // Filter out the skipped root from allTocs if it was elevated
        const allTocs = skipRootId !== undefined
            ? allEntries.filter(e => e.id !== skipRootId)
            : allEntries

        console.log('[TOC Service] Final - tree:', tree.length, 'allTocs:', allTocs.length, 'filtered:', skipRootId !== undefined)

        return { tree, allTocs, altTocByLineIndex }
    }

    private buildTocChildren(parentId: number | undefined | null, items: TocEntry[], skipRootId?: number): TocEntry[] {
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
            // Skip adding the root node to paths if it matches the book title
            if (parent && parent.id !== skipRootId) {
                if (parent.path) {
                    child.path = parent.path + ' - ' + parent.text
                } else {
                    child.path = parent.text
                }
            }

            // Recursively build children
            if (child.hasChildren) {
                child.children = this.buildTocChildren(child.id, items, skipRootId)
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