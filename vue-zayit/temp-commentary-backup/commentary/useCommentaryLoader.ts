/**
 * Commentary Loader Composable
 * Handles loading and managing commentary links
 */

import { ref, type Ref } from 'vue'
import { bookCommentaryService, type CommentaryLinkGroup } from '@/data/services/bookCommentaryService'
import { dbService } from '@/data/services/dbService'
import { useConnectionTypesStore } from '@/data/stores/connectionTypesStore'
import type { Book } from '@/data/types/Book'

export function useCommentaryLoader() {
    const linkGroups = ref<CommentaryLinkGroup[]>([])
    const isLoading = ref(false)
    const commentaryBookIds = ref<number[]>([])

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
            const connectionTypesStore = useConnectionTypesStore()

            console.log('📊 [CommentaryLoader] Starting load:', {
                bookId,
                lineIndex,
                selectedConnectionTypeId,
                tocEntryId,
                storeLoaded: connectionTypesStore.isLoaded
            })

            // Ensure connection types are loaded
            if (!connectionTypesStore.isLoaded) {
                console.log('⏳ [CommentaryLoader] Waiting for connection types to load...')
                await connectionTypesStore.loadConnectionTypes()
                console.log('✅ [CommentaryLoader] Connection types loaded')
            }

            const commentaryTypeId = connectionTypesStore.getConnectionTypeId('COMMENTARY')
            const isCommentarySelected = selectedConnectionTypeId === commentaryTypeId

            console.log('🔍 [CommentaryLoader] Connection type info:', {
                commentaryTypeId,
                selectedConnectionTypeId,
                isCommentarySelected
            })

            if (tocEntryId !== undefined) {
                // Load links for TOC entry
                const lineIdResults = await dbService.getLineIdsByTocEntry(bookId, tocEntryId)
                const lineIds = lineIdResults.map(r => r.lineId)

                if (lineIds.length > 0) {
                    // Always load commentary book IDs for categories (regardless of selected type)
                    if (commentaryTypeId) {
                        console.log('📚 [CommentaryLoader] Loading commentary book IDs for TOC entry...')
                        const commentaryBookIdsPromises = lineIds.map(lineId =>
                            dbService.getLinkBookIds(lineId, commentaryTypeId)
                        )
                        const allCommentaryBookIdsArrays = await Promise.all(commentaryBookIdsPromises)
                        const allCommentaryBookIds = allCommentaryBookIdsArrays.flat()
                        commentaryBookIds.value = [...new Set(allCommentaryBookIds)]
                        console.log('✅ [CommentaryLoader] Commentary book IDs loaded:', commentaryBookIds.value)
                    } else {
                        console.warn('⚠️ [CommentaryLoader] No commentaryTypeId, skipping book IDs load')
                    }

                    // Load full data for selected connection type
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
                    commentaryBookIds.value = []
                }
            } else {
                // Load links for single line
                const lineId = await dbService.getLineId(bookId, lineIndex)
                if (!lineId) {
                    console.warn('⚠️ [CommentaryLoader] No lineId found')
                    linkGroups.value = []
                    commentaryBookIds.value = []
                    return
                }

                console.log('📝 [CommentaryLoader] Loading for single line, lineId:', lineId)

                // Always load commentary book IDs for categories (regardless of selected type)
                if (commentaryTypeId) {
                    console.log('📚 [CommentaryLoader] Loading commentary book IDs...')
                    const bookIds = await dbService.getLinkBookIds(lineId, commentaryTypeId)
                    commentaryBookIds.value = bookIds
                    console.log('✅ [CommentaryLoader] Commentary book IDs loaded:', commentaryBookIds.value)
                } else {
                    console.warn('⚠️ [CommentaryLoader] No commentaryTypeId, skipping book IDs load')
                }

                // Load full data for selected connection type
                console.log('📖 [CommentaryLoader] Loading full data for connection type:', selectedConnectionTypeId)
                linkGroups.value = await bookCommentaryService.loadCommentaryLinks(
                    bookId,
                    lineIndex,
                    tabId,
                    { connectionTypeId: selectedConnectionTypeId }
                )
                console.log('✅ [CommentaryLoader] Link groups loaded:', linkGroups.value.length)
            }
        } catch (error) {
            console.error('❌ Failed to load commentary links:', error)
            linkGroups.value = []
            commentaryBookIds.value = []
        } finally {
            isLoading.value = false
        }
    }

    return {
        linkGroups,
        isLoading,
        commentaryBookIds,
        loadCommentaryLinks
    }
}
