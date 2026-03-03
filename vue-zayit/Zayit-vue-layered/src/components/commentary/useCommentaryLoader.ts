/**
 * Commentary Loader Composable
 * Handles loading and managing commentary links
 */

import { ref, type Ref } from 'vue'
import { bookCommentaryService, type CommentaryLinkGroup } from '@/data/services/bookCommentaryService'
import { dbService } from '@/data/services/dbService'
import type { Book } from '@/data/types/Book'

export function useCommentaryLoader() {
    const linkGroups = ref<CommentaryLinkGroup[]>([])
    const isLoading = ref(false)
    const availableFilterOptions = ref<Array<{ label: string; value: number }>>([])

    /**
     * Load commentary links for a specific line
     */
    async function loadCommentaryLinks(
        bookId: number,
        lineIndex: number,
        tabId: string,
        selectedConnectionTypeId: number | undefined,
        tocEntryId: number | undefined,
        book: Book | undefined
    ): Promise<void> {
        isLoading.value = true

        try {
            if (tocEntryId !== undefined) {
                // Load links for TOC entry
                const lineIdResults = await dbService.getLineIdsByTocEntry(bookId, tocEntryId)
                const lineIds = lineIdResults.map(r => r.lineId)

                if (lineIds.length > 0) {
                    const linksPromises = lineIds.map(lineId =>
                        dbService.getLinks(lineId, tabId, bookId, selectedConnectionTypeId)
                    )
                    const allLinksArrays = await Promise.all(linksPromises)
                    const allLinks = allLinksArrays.flat()

                    const grouped = new Map<string, {
                        links: Array<{ text: string; html: string }>,
                        targetBookId?: number,
                        targetLineIndex?: number
                    }>()

                    allLinks.forEach(link => {
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

                    linkGroups.value = Array.from(grouped.entries()).map(([groupName, data]) => ({
                        groupName,
                        links: data.links,
                        targetBookId: data.targetBookId,
                        targetLineIndex: data.targetLineIndex
                    }))
                } else {
                    linkGroups.value = []
                }

                // Set available filter options
                if (book) {
                    availableFilterOptions.value = bookCommentaryService.getAvailableFilterOptions(book)
                }
            } else {
                // Load links for single line
                linkGroups.value = await bookCommentaryService.loadCommentaryLinks(
                    bookId,
                    lineIndex,
                    tabId,
                    { connectionTypeId: selectedConnectionTypeId }
                )

                // Compute available filter options
                await computeAvailableFilterOptions(bookId, lineIndex, tabId, book)
            }
        } catch (error) {
            console.error('❌ Failed to load commentary links:', error)
            linkGroups.value = []
        } finally {
            isLoading.value = false
        }
    }

    /**
     * Compute available filter options for a line
     */
    async function computeAvailableFilterOptions(
        bookId: number,
        lineIndex: number,
        tabId: string,
        book: Book | undefined
    ): Promise<void> {
        availableFilterOptions.value = []
        if (!book) return

        const baseOptions = bookCommentaryService.getAvailableFilterOptions(book)
        if (!baseOptions || baseOptions.length === 0) return

        const results: Array<{ label: string; value: number }> = []

        for (const opt of baseOptions) {
            try {
                const groups = await bookCommentaryService.loadCommentaryLinks(
                    bookId,
                    lineIndex,
                    tabId,
                    { connectionTypeId: opt.value }
                )
                if (groups && groups.length > 0) {
                    results.push({ label: opt.label, value: opt.value })
                }
            } catch (e) {
                // Ignore errors
            }
        }

        availableFilterOptions.value = results
    }

    return {
        linkGroups,
        isLoading,
        availableFilterOptions,
        loadCommentaryLinks
    }
}
