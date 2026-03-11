import { ref, computed, type Ref } from 'vue'
import { dbService } from '@/data/services/dbService'

export interface CommentaryMetadata {
    groupName: string
    targetBookId?: number
    targetLineIndex?: number
    connectionTypeId?: number
    targetLineIds?: number[]  // Array of all line IDs for this commentary group
    isLoaded: boolean
    links: Array<{ text: string; html: string }>
}

/**
 * Load links metadata in batches (fast - no content)
 */
async function loadLinksMetadataInBatches(
    lineIds: number[],
    connectionTypeId: number | undefined,
    batchSize: number = 8
): Promise<any[]> {
    const allMetadata: any[] = []

    for (let i = 0; i < lineIds.length; i += batchSize) {
        const batch = lineIds.slice(i, i + batchSize)
        const batchPromises = batch.map(lineId =>
            dbService.getLinksMetadata(lineId, connectionTypeId)
        )
        const batchResults = await Promise.all(batchPromises)
        allMetadata.push(...batchResults.flat())
    }

    return allMetadata
}

/**
 * Group metadata by title (no content yet)
 */
function groupMetadata(metadata: any[]): Map<string, {
    targetBookId?: number
    targetLineIndex?: number
    connectionTypeId?: number
    targetLineIds: number[]  // Store ALL line IDs for this group
    linkCount: number
}> {
    const grouped = new Map<string, {
        targetBookId?: number
        targetLineIndex?: number
        connectionTypeId?: number
        targetLineIds: number[]
        linkCount: number
    }>()

    metadata.forEach(item => {
        const groupName = item.title || 'אחר'
        if (!grouped.has(groupName)) {
            grouped.set(groupName, {
                targetBookId: item.targetBookId,
                targetLineIndex: item.lineIndex,
                connectionTypeId: item.connectionTypeId,
                targetLineIds: [],
                linkCount: 0
            })
        }
        const group = grouped.get(groupName)!
        if (!group.targetLineIds.includes(item.targetLineId)) {
            group.targetLineIds.push(item.targetLineId)
        }
        group.linkCount++
    })

    return grouped
}

/**
 * Convert grouped metadata to metadata array (no content loaded yet)
 */
function metadataToCommentaryMetadata(grouped: Map<string, {
    targetBookId?: number
    targetLineIndex?: number
    connectionTypeId?: number
    targetLineIds: number[]
    linkCount: number
}>): CommentaryMetadata[] {
    return Array.from(grouped.entries()).map(([groupName, data]) => ({
        groupName,
        targetBookId: data.targetBookId,
        targetLineIndex: data.targetLineIndex,
        connectionTypeId: data.connectionTypeId,
        targetLineIds: data.targetLineIds,  // Pass the array of line IDs
        isLoaded: false,
        links: []
    }))
}

/**
 * Progressively render groups in batches to avoid UI blocking
 * Uses smaller batches and requestIdleCallback for better responsiveness
 */
async function renderGroupsProgressively(
    allGroups: CommentaryMetadata[],
    commentaryGroupsRef: Ref<CommentaryMetadata[]>,
    isLoadingMoreRef: Ref<boolean>,
    initialBatchSize: number = 30,
    subsequentBatchSize: number = 20
): Promise<void> {
    if (allGroups.length === 0) {
        commentaryGroupsRef.value = []
        return
    }

    // Show first batch immediately for fast initial render
    const firstBatch = allGroups.slice(0, initialBatchSize)
    commentaryGroupsRef.value = firstBatch

    if (allGroups.length <= initialBatchSize) {
        return // All done
    }

    // Load remaining groups in batches using requestIdleCallback for better UI responsiveness
    isLoadingMoreRef.value = true
    let currentIndex = initialBatchSize

    const addNextBatch = () => {
        if (currentIndex >= allGroups.length) {
            isLoadingMoreRef.value = false
            return
        }

        const nextBatch = allGroups.slice(currentIndex, currentIndex + subsequentBatchSize)

        // Use push to add items without triggering full re-render
        commentaryGroupsRef.value.push(...nextBatch)

        currentIndex += subsequentBatchSize

        // Schedule next batch when browser is idle
        if ('requestIdleCallback' in window) {
            requestIdleCallback(addNextBatch, { timeout: 100 })
        } else {
            setTimeout(addNextBatch, 16) // ~60fps fallback
        }
    }

    // Start the progressive loading
    if ('requestIdleCallback' in window) {
        requestIdleCallback(addNextBatch, { timeout: 100 })
    } else {
        setTimeout(addNextBatch, 16)
    }
}

export function useCommentaryContent() {
    const commentaryGroups = ref<CommentaryMetadata[]>([])
    const isLoadingMetadata = ref(false)
    const isLoadingMore = ref(false)
    const loadingQueue = ref<Array<{ bookId: number; lineIndex: number }>>([])
    const isProcessingQueue = ref(false)
    const loadingProgress = ref(0) // 0-100 percentage
    const totalGroupsToLoad = ref(0)

    /**
     * Load commentary metadata (fast - no content)
     * If tocEntryId is provided, loads metadata for all lines in that TOC section
     */
    async function loadCommentaryMetadata(
        bookId: number,
        lineIndex: number,
        connectionTypeId?: number,
        tocEntryId?: number,
        isVisible: boolean = true
    ): Promise<void> {
        // Don't load if commentary pane is not visible
        if (!isVisible) {
            commentaryGroups.value = []
            return
        }

        isLoadingMetadata.value = true
        loadingProgress.value = 5 // Start at 5%

        // Simulate progress during metadata load
        const progressInterval = setInterval(() => {
            if (loadingProgress.value < 18) {
                loadingProgress.value += 1
            }
        }, 50) // Update every 50ms

        try {
            let metadata: any[] = []

            if (tocEntryId !== undefined) {
                // TOC mode: Load metadata for all lines in the TOC section
                const lineIdResults = await dbService.getLineIdsByTocEntry(bookId, tocEntryId)
                const lineIds = lineIdResults.map(r => r.lineId)

                if (lineIds.length === 0) {
                    commentaryGroups.value = []
                    return
                }

                // Load metadata in batches (fast - no content)
                metadata = await loadLinksMetadataInBatches(lineIds, connectionTypeId, 8)
            } else {
                // Single line mode: Load metadata for just the selected line
                const lineId = await dbService.getLineId(bookId, lineIndex)

                if (!lineId) {
                    commentaryGroups.value = []
                    return
                }

                metadata = await dbService.getLinksMetadata(lineId, connectionTypeId)
            }

            // Group by title (no content yet)
            const grouped = groupMetadata(metadata)
            const allGroups = metadataToCommentaryMetadata(grouped)

            // Set all groups at once (fast since no content)
            commentaryGroups.value = allGroups

            // Clear progress simulation and set to 20%
            clearInterval(progressInterval)
            loadingProgress.value = 20

            // Queue all groups for loading (non-priority)
            allGroups.forEach(group => {
                if (group.targetBookId && group.targetLineIndex !== undefined) {
                    queueGroupLoad(group.targetBookId, group.targetLineIndex, false)
                }
            })

            // Initialize progress tracking for content loading (20% to 100%)
            totalGroupsToLoad.value = allGroups.length
        } catch (error) {
            clearInterval(progressInterval)
            console.error('Failed to load commentary metadata:', error)
            commentaryGroups.value = []
        } finally {
            isLoadingMetadata.value = false
        }
    }

    /**
     * Process the loading queue in batches for better performance
     */
    async function processLoadingQueue() {
        if (isProcessingQueue.value || loadingQueue.value.length === 0) {
            return
        }

        isProcessingQueue.value = true
        const batchSize = 20 // Load 20 groups at a time

        while (loadingQueue.value.length > 0) {
            // Get next batch
            const batch = loadingQueue.value.splice(0, batchSize)

            // Load batch with single batched SQL query
            await loadGroupContentBatch(batch)

            // Small delay to keep UI responsive between batches
            await new Promise(resolve => setTimeout(resolve, 10))
        }

        isProcessingQueue.value = false
    }

    /**
     * Load content for multiple groups in a single batched SQL query
     */
    async function loadGroupContentBatch(batch: Array<{ bookId: number; lineIndex: number }>): Promise<void> {
        try {
            const contentStart = performance.now()

            // Collect all groups and their line IDs
            const groupsToLoad: Array<{
                group: CommentaryMetadata
                lineIds: number[]
            }> = []

            const allLineIds: number[] = []

            for (const item of batch) {
                const group = commentaryGroups.value.find(
                    g => g.targetBookId === item.bookId && g.targetLineIndex === item.lineIndex
                )

                if (!group) continue

                const lineIds = group.targetLineIds || []
                if (lineIds.length === 0) continue

                groupsToLoad.push({ group, lineIds })
                allLineIds.push(...lineIds)
            }

            if (allLineIds.length === 0) return

            // Single batched SQL query for all line IDs at once
            const contentMap = await dbService.getLineContentByIds(allLineIds)

            // Assign content to each group
            for (const { group, lineIds } of groupsToLoad) {
                const links = lineIds
                    .map(lineId => contentMap.get(lineId))
                    .filter(content => content !== undefined)
                    .map(content => ({
                        text: content!,
                        html: content!
                    }))

                if (links.length > 0) {
                    group.links = links
                    group.isLoaded = true
                }
            }

            // Update progress (scale from 20% to 90%, then jump to 100% when all loaded)
            const loadedCount = commentaryGroups.value.filter(g => g.isLoaded).length
            const contentProgress = totalGroupsToLoad.value > 0 ? (loadedCount / totalGroupsToLoad.value) : 1

            if (contentProgress >= 1) {
                // All content loaded - set to 100% (rendering will happen in component)
                loadingProgress.value = 100
            } else {
                // Still loading - scale from 20% to 90%
                loadingProgress.value = Math.round(20 + (contentProgress * 70)) // 20% + (0-70%)
            }
        } catch (error) {
            console.error('Failed to load content batch:', error)
        }
    }

    /**
     * Add item to loading queue with priority (visible items first)
     */
    function queueGroupLoad(bookId: number, lineIndex: number, priority: boolean = false) {
        // Check if already in queue
        const existingIndex = loadingQueue.value.findIndex(
            item => item.bookId === bookId && item.lineIndex === lineIndex
        )

        if (existingIndex !== -1) {
            // If priority is true, move to front of queue
            if (priority) {
                const item = loadingQueue.value.splice(existingIndex, 1)[0]
                if (item) {
                    loadingQueue.value.unshift(item)
                }
            }
            return
        }

        // Add to queue (priority items go to front)
        if (priority) {
            loadingQueue.value.unshift({ bookId, lineIndex })
        } else {
            loadingQueue.value.push({ bookId, lineIndex })
        }

        // Start processing if not already running
        processLoadingQueue()
    }

    /**
     * Load content for a specific group on demand
     * Loads content for ALL lines associated with this commentary group
     */
    async function loadGroupContent(bookId: number, lineIndex: number): Promise<void> {
        try {
            const contentStart = performance.now()

            // Find the group
            const group = commentaryGroups.value.find(
                g => g.targetBookId === bookId && g.targetLineIndex === lineIndex
            )

            if (!group) {
                console.warn(`⚠️ [useCommentaryContent] Group not found for book ${bookId}, line ${lineIndex}`)
                return
            }

            // Load content for all line IDs in this group
            const lineIds = group.targetLineIds || []
            if (lineIds.length === 0) {
                console.warn(`⚠️ [useCommentaryContent] No line IDs found for group ${group.groupName}`)
                return
            }

            // Load all lines' content by lineId
            const contentPromises = lineIds.map(lineId =>
                dbService.getLineContentById(lineId)
            )
            const contents = await Promise.all(contentPromises)

            // Filter out null/empty content and create links
            const links = contents
                .filter(content => content !== null && content !== '')
                .map(content => ({
                    text: content!,
                    html: content!
                }))

            if (links.length === 0) {
                console.warn(`⚠️ [useCommentaryContent] No content found for group ${group.groupName}`)
                return
            }

            // Update the group
            group.links = links
            group.isLoaded = true
        } catch (error) {
            console.error(`Failed to load content for book ${bookId}, line ${lineIndex}:`, error)
        }
    }

    return {
        commentaryGroups,
        isLoadingMetadata,
        isLoadingMore,
        loadingProgress,
        loadCommentaryMetadata,
        loadGroupContent,
        queueGroupLoad
    }
}
