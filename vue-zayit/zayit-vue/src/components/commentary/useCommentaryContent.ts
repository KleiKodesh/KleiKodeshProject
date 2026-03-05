import { ref, computed } from 'vue'
import { dbService } from '@/data/services/dbService'

export interface CommentaryMetadata {
    groupName: string
    targetBookId?: number
    targetLineIndex?: number
    isLoaded: boolean
    links: Array<{ text: string; html: string }>
}

export function useCommentaryContent() {
    const commentaryGroups = ref<CommentaryMetadata[]>([])
    const isLoadingMetadata = ref(false)
    const loadingGroupIndices = ref<Set<number>>(new Set())

    /**
     * Load commentary metadata (titles and IDs) without content
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

            // Load links with metadata only (we'll use the title and IDs)
            const links = await dbService.getLinks(lineId, '', bookId, connectionTypeId)

            // Group by title to get unique commentaries
            const grouped = new Map<string, {
                targetBookId?: number
                targetLineIndex?: number
            }>()

            links.forEach(link => {
                const groupName = link.title || 'אחר'
                if (!grouped.has(groupName)) {
                    grouped.set(groupName, {
                        targetBookId: link.targetBookId,
                        targetLineIndex: link.lineIndex
                    })
                }
            })

            // Convert to metadata array
            commentaryGroups.value = Array.from(grouped.entries()).map(([groupName, data]) => ({
                groupName,
                targetBookId: data.targetBookId,
                targetLineIndex: data.targetLineIndex,
                isLoaded: false,
                links: []
            }))
        } catch (error) {
            console.error('Failed to load commentary metadata:', error)
            commentaryGroups.value = []
        } finally {
            isLoadingMetadata.value = false
        }
    }

    /**
     * Load content for a specific commentary group
     */
    async function loadGroupContent(
        groupIndex: number,
        bookId: number,
        lineIndex: number,
        connectionTypeId?: number
    ): Promise<void> {
        if (groupIndex < 0 || groupIndex >= commentaryGroups.value.length) return

        const group = commentaryGroups.value[groupIndex]
        if (!group || group.isLoaded || loadingGroupIndices.value.has(groupIndex)) return

        loadingGroupIndices.value.add(groupIndex)

        try {
            const lineId = await dbService.getLineId(bookId, lineIndex)
            if (!lineId) return

            // Load all links and filter for this specific group
            const links = await dbService.getLinks(lineId, '', bookId, connectionTypeId)

            const groupLinks = links
                .filter(link => (link.title || 'אחר') === group.groupName)
                .map(link => ({
                    text: link.content || '',
                    html: link.content || ''
                }))

            // Update the group with loaded content
            group.links = groupLinks
            group.isLoaded = true
        } catch (error) {
            console.error(`Failed to load content for group ${group.groupName}:`, error)
        } finally {
            loadingGroupIndices.value.delete(groupIndex)
        }
    }

    const isLoadingAnyGroup = computed(() => loadingGroupIndices.value.size > 0)

    return {
        commentaryGroups,
        isLoadingMetadata,
        isLoadingAnyGroup,
        loadCommentaryMetadata,
        loadGroupContent
    }
}
