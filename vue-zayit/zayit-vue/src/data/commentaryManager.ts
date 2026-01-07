/**
 * Commentary Manager
 * 
 * Handles loading and processing of commentary links for book lines.
 */

import { dbManager } from './dbManager'
import type { Link } from '../types/Link'

export interface CommentaryLinkGroup {
    groupName: string
    targetBookId?: number
    targetLineIndex?: number
    links: Array<{ text: string; html: string }>
}

export class CommentaryManager {
    /**
     * Load and group commentary links for a specific book line
     */
    async loadCommentaryLinks(bookId: number, lineIndex: number, tabId: string): Promise<CommentaryLinkGroup[]> {
        try {
            // Get the actual line ID from the database
            const lineId = await dbManager.getLineId(bookId, lineIndex)
            if (!lineId) {
                console.warn(`No line ID found for book ${bookId}, lineIndex ${lineIndex}`)
                return []
            }

            const links = await dbManager.getLinks(lineId, tabId, bookId)

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

            console.log(`✅ Loaded ${linkGroups.length} link groups with ${links.length} total links`)
            return linkGroups
        } catch (error) {
            console.error('❌ Failed to load commentary links:', error)
            return []
        }
    }
}

export const commentaryManager = new CommentaryManager()