import { ref, computed } from 'vue'
import { dbService } from '@/data/services/dbService'

export interface CommentaryMetadata {
    groupName: string
    targetBookId?: number
    targetLineIndex?: number
    connectionTypeId?: number
    isLoaded: boolean
    links: Array<{ text: string; html: string }>
}

export function useCommentaryContent() {
    const commentaryGroups = ref<CommentaryMetadata[]>([])
    const isLoadingMetadata = ref(false)

    /**
     * Load commentary metadata and content in one call
     */
    async function loadCommentaryMetadata(
        bookId: number,
        lineIndex: number,
        connectionTypeId?: number
    ): Promise<void> {
        isLoadingMetadata.value = true

        try {
            const lineId = await dbService.getLineId(bookId, lineIndex)
            if (!lineId) {
                commentaryGroups.value = []
                return
            }

            // Load links once - we get both metadata AND content
            const links = await dbService.getLinks(lineId, '', bookId, connectionTypeId)

            // Group by title and cache the links for each group
            const grouped = new Map<string, {
                targetBookId?: number
                targetLineIndex?: number
                connectionTypeId?: number
                links: Array<{ text: string; html: string }>
            }>()

            links.forEach(link => {
                const groupName = link.title || 'אחר'
                if (!grouped.has(groupName)) {
                    grouped.set(groupName, {
                        targetBookId: link.targetBookId,
                        targetLineIndex: link.lineIndex,
                        connectionTypeId: link.connectionTypeId,
                        links: []
                    })
                }

                // Store original content without filtering
                grouped.get(groupName)!.links.push({
                    text: link.content || '',
                    html: link.content || ''
                })
            })

            // Convert to metadata array with preloaded content
            commentaryGroups.value = Array.from(grouped.entries()).map(([groupName, data]) => ({
                groupName,
                targetBookId: data.targetBookId,
                targetLineIndex: data.targetLineIndex,
                connectionTypeId: data.connectionTypeId,
                isLoaded: true,
                links: data.links
            }))
        } catch (error) {
            console.error('Failed to load commentary metadata:', error)
            commentaryGroups.value = []
        } finally {
            isLoadingMetadata.value = false
        }
    }

    return {
        commentaryGroups,
        isLoadingMetadata,
        loadCommentaryMetadata
    }
}
