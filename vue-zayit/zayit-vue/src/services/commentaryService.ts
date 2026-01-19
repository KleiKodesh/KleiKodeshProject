/**
 * Commentary Service
 * 
 * Centralized service for all commentary-related operations.
 * Handles loading, filtering, and processing of commentary links.
 */

import { dbManager } from '../data/dbManager'
import { useConnectionTypesStore } from '../stores/connectionTypesStore'
import type { Link } from '../types/Link'
import type { Book } from '../types/Book'

export interface CommentaryLinkGroup {
    groupName: string
    targetBookId?: number
    targetLineIndex?: number
    links: Array<{ text: string; html: string }>
}

export interface CommentaryFilterOptions {
    connectionTypeId?: number
    defaultConnectionType?: string // e.g., 'COMMENTARY' for מפרשים as default
}

export class CommentaryService {
    private static instance: CommentaryService
    
    static getInstance(): CommentaryService {
        if (!CommentaryService.instance) {
            CommentaryService.instance = new CommentaryService()
        }
        return CommentaryService.instance
    }

    /**
     * Get available filter options for a book based on its connection flags
     */
    getAvailableFilterOptions(book: Book): Array<{ label: string; value: number | undefined; isDefault?: boolean }> {
        const connectionTypesStore = useConnectionTypesStore()
        
        if (!connectionTypesStore.isLoaded) {
            console.warn('Connection types not loaded yet')
            return []
        }

        const options: Array<{ label: string; value: number | undefined; isDefault?: boolean }> = [
            { label: 'הצג הכל', value: undefined }
        ]

        // Add available connection types based on book flags
        const connectionTypeMap = [
            { flag: book.hasCommentaryConnection, name: 'COMMENTARY', isDefault: true }, // Default to מפרשים
            { flag: book.hasSourceConnection, name: 'SOURCE' },
            { flag: book.hasTargumConnection, name: 'TARGUM' },
            { flag: book.hasReferenceConnection, name: 'REFERENCE' },
            { flag: book.hasOtherConnection, name: 'OTHER' }
        ]

        connectionTypeMap.forEach(({ flag, name, isDefault }) => {
            if (flag > 0) {
                const connectionTypeId = connectionTypesStore.getConnectionTypeId(name)
                if (connectionTypeId) {
                    options.push({
                        label: connectionTypesStore.getHebrewLabel(name),
                        value: connectionTypeId,
                        isDefault: isDefault || false
                    })
                }
            }
        })

        return options
    }

    /**
     * Get default filter for a book (prefers COMMENTARY if available)
     */
    getDefaultFilter(book: Book): number | undefined {
        const options = this.getAvailableFilterOptions(book)
        const defaultOption = options.find(opt => opt.isDefault && opt.value !== undefined)
        return defaultOption?.value
    }

    /**
     * Load and group commentary links for a specific book line with filtering
     */
    async loadCommentaryLinks(
        bookId: number, 
        lineIndex: number, 
        tabId: string, 
        filterOptions?: CommentaryFilterOptions
    ): Promise<CommentaryLinkGroup[]> {
        try {
            // Get the actual line ID from the database
            const lineId = await dbManager.getLineId(bookId, lineIndex)
            if (!lineId) {
                console.warn(`No line ID found for book ${bookId}, lineIndex ${lineIndex}`)
                return []
            }

            // Apply filtering at SQL level
            const connectionTypeId = filterOptions?.connectionTypeId
            const links = await dbManager.getLinks(lineId, tabId, bookId, connectionTypeId)

            // Group links by title and store first targetBookId/lineIndex
            const grouped = new Map<string, {
                links: Array<{ text: string; html: string }>,
                targetBookId?: number,
                targetLineIndex?: number
            }>()

            links.forEach(link => {
                const groupName = link.title || 'אחר'
                if (!grouped.has(groupName)) {
                    grouped.set(groupName, {
                        links: [],
                        targetBookId: link.targetBookId,
                        targetLineIndex: link.lineIndex
                    })
                }
                grouped.get(groupName)!.links.push({
                    text: link.content || '',
                    html: link.content || ''
                })
            })

            // Convert to array format
            const linkGroups = Array.from(grouped.entries()).map(([groupName, data]) => ({
                groupName,
                targetBookId: data.targetBookId,
                targetLineIndex: data.targetLineIndex,
                links: data.links
            }))

            return linkGroups
        } catch (error) {
            console.error('❌ Failed to load commentary links:', error)
            return []
        }
    }

    /**
     * Check if a book has multiple connection types (determines if filter should be shown)
     */
    shouldShowFilter(book: Book): boolean {
        const connectionCount = [
            book.hasSourceConnection,
            book.hasCommentaryConnection,
            book.hasTargumConnection,
            book.hasReferenceConnection,
            book.hasOtherConnection
        ].filter(flag => flag > 0).length

        return connectionCount > 1
    }
}

export const commentaryService = CommentaryService.getInstance()