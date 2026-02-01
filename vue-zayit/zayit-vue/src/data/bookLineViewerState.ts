/**
 * BookLineViewerState
 * 
 * Manages line loading with smart streaming prioritization.
 * 
 * Architecture:
 * 1. Immediate scaffolding: Fill lines.value with placeholders for instant navigation
 * 2. Smart streaming: Load batches with priority queue (user needs first)
 */

import { ref, type Ref } from 'vue'
import { dbManager, type LineLoadResult } from './dbManager'

const PADDING_LINES = 200
const BATCH_SIZE = 200

export class BookLineViewerState {
    // Public reactive state - single source of truth
    lines: Ref<Record<number, string>> = ref({})
    totalLines: Ref<number> = ref(0)
    isInitialLoad = true

    // Private streaming state
    private bookId: number | null = null
    private streamingAbort: (() => void) | null = null
    private pendingBatches: Set<number> = new Set()
    private priorityQueue: number[] = []

    /**
     * Load a new book - immediate scaffolding + start streaming
     */
    async loadBook(bookId: number, isRestore: boolean, initialLineIndex?: number): Promise<void> {
        console.log('📚 Loading book:', bookId, 'isRestore:', isRestore, 'initialLineIndex:', initialLineIndex)

        this.cleanup()
        this.bookId = bookId
        this.isInitialLoad = true

        try {
            this.totalLines.value = await dbManager.getTotalLines(bookId)

            // Stage 1: Immediate scaffolding with placeholders
            const placeholders: Record<number, string> = {}
            for (let i = 0; i < this.totalLines.value; i++) {
                placeholders[i] = '\u00A0' // Hard space placeholder
            }
            this.lines.value = placeholders

            // Stage 2: Start smart streaming
            this.startSmartStreaming()

            // Load initial content if restoring
            if (isRestore && initialLineIndex !== undefined) {
                await this.prioritizeLines(initialLineIndex)
            }

            this.isInitialLoad = false
        } catch (error) {
            console.error('❌ Failed to load book:', error)
            this.totalLines.value = 0
            throw error
        }
    }

    /**
     * Prioritize loading lines around a specific point (TOC navigation, scroll-to-line)
     */
    async prioritizeLines(centerLine: number, padding = PADDING_LINES): Promise<void> {
        const start = Math.max(0, centerLine - padding)
        const end = Math.min(this.totalLines.value - 1, centerLine + padding)

        // Calculate which batches contain these lines
        const neededBatches = new Set<number>()
        for (let i = start; i <= end; i++) {
            const batchIndex = Math.floor(i / BATCH_SIZE)
            neededBatches.add(batchIndex)
        }

        // Move needed batches to front of priority queue
        const batchArray = Array.from(neededBatches).sort((a, b) => {
            // Sort by distance from center batch
            const centerBatch = Math.floor(centerLine / BATCH_SIZE)
            return Math.abs(a - centerBatch) - Math.abs(b - centerBatch)
        })

        // Remove from existing queue and add to front
        this.priorityQueue = this.priorityQueue.filter(batch => !neededBatches.has(batch))
        this.priorityQueue.unshift(...batchArray)

        // Load the most critical batch immediately
        if (batchArray.length > 0) {
            await this.loadBatch(batchArray[0]!)
        }
    }

    /**
     * Handle TOC selection - prioritize the selected area
     */
    async handleTocSelection(lineIndex: number): Promise<void> {
        await this.prioritizeLines(lineIndex, PADDING_LINES)
    }

    /**
     * Start smart streaming - loads batches in priority order
     */
    private startSmartStreaming() {
        if (!this.bookId || this.totalLines.value === 0) return

        // Initialize priority queue with all batches (default order)
        const totalBatches = Math.ceil(this.totalLines.value / BATCH_SIZE)
        this.priorityQueue = Array.from({ length: totalBatches }, (_, i) => i)
        this.pendingBatches.clear()

        // Start streaming
        this.processQueue()
    }

    /**
     * Process the priority queue - loads next batch
     */
    private async processQueue() {
        if (!this.bookId || this.priorityQueue.length === 0) return

        const nextBatch = this.priorityQueue.shift()!
        if (this.pendingBatches.has(nextBatch)) {
            // Skip if already loading
            setTimeout(() => this.processQueue(), 10)
            return
        }

        await this.loadBatch(nextBatch)

        // Continue with next batch after short delay
        setTimeout(() => this.processQueue(), 50)
    }

    /**
     * Load a specific batch of lines
     */
    private async loadBatch(batchIndex: number): Promise<void> {
        if (!this.bookId || this.pendingBatches.has(batchIndex)) return

        this.pendingBatches.add(batchIndex)

        try {
            const start = batchIndex * BATCH_SIZE
            const end = Math.min(start + BATCH_SIZE - 1, this.totalLines.value - 1)

            const loadedLines = await dbManager.loadLineRange(this.bookId, start, end)

            // Update lines directly
            loadedLines.forEach(line => {
                this.lines.value[line.lineIndex] = line.content
            })


        } catch (error) {
            console.error(`❌ Failed to load batch ${batchIndex}:`, error)
        } finally {
            this.pendingBatches.delete(batchIndex)
        }
    }

    /**
     * Get search data - search loaded lines
     */
    async getSearchData(): Promise<Array<{ index: number, content: string }>> {
        const allLines: Array<{ index: number, content: string }> = []

        Object.entries(this.lines.value).forEach(([indexStr, content]) => {
            if (content && content !== '\u00A0') {
                allLines.push({
                    index: Number(indexStr),
                    content
                })
            }
        })

        return allLines.sort((a, b) => a.index - b.index)
    }

    /**
     * Progressive search - loads and searches content in batches
     * Returns a promise that resolves with search results as they become available
     */
    async searchProgressively(
        query: string,
        onProgressUpdate: (matches: Array<{ itemIndex: number, occurrence: number, totalInItem: number }>) => void
    ): Promise<Array<{ itemIndex: number, occurrence: number, totalInItem: number }>> {
        if (!this.bookId || !query.trim()) {
            return []
        }

        const allMatches: Array<{ itemIndex: number, occurrence: number, totalInItem: number }> = []
        const normalizedQuery = this.removeDiacritics(query.toLowerCase())

        // First, search already loaded lines for immediate results
        const loadedMatches = this.searchInLoadedLines(query)
        if (loadedMatches.length > 0) {
            allMatches.push(...loadedMatches)
            onProgressUpdate([...allMatches])
        }

        // Then search remaining batches progressively
        const totalBatches = Math.ceil(this.totalLines.value / BATCH_SIZE)
        const searchedBatches = new Set<number>()

        // Mark already loaded batches as searched
        Object.keys(this.lines.value).forEach(indexStr => {
            const lineIndex = Number(indexStr)
            if (this.lines.value[lineIndex] !== '\u00A0') {
                const batchIndex = Math.floor(lineIndex / BATCH_SIZE)
                searchedBatches.add(batchIndex)
            }
        })

        // Search remaining batches
        for (let batchIndex = 0; batchIndex < totalBatches; batchIndex++) {
            if (searchedBatches.has(batchIndex)) continue

            try {
                // Load the batch if not already loaded
                await this.loadBatch(batchIndex)

                // Search the newly loaded batch
                const batchMatches = this.searchBatch(batchIndex, normalizedQuery)
                if (batchMatches.length > 0) {
                    allMatches.push(...batchMatches)
                    // Update occurrence counts for all matches
                    this.updateOccurrenceCounts(allMatches)
                    onProgressUpdate([...allMatches])
                }

                searchedBatches.add(batchIndex)

                // Small delay to prevent blocking the UI
                await new Promise(resolve => setTimeout(resolve, 10))

            } catch (error) {
                console.error(`Failed to search batch ${batchIndex}:`, error)
            }
        }

        return allMatches
    }

    /**
     * Search in currently loaded lines
     */
    private searchInLoadedLines(query: string): Array<{ itemIndex: number, occurrence: number, totalInItem: number }> {
        const matches: Array<{ itemIndex: number, occurrence: number, totalInItem: number }> = []
        const normalizedQuery = this.removeDiacritics(query.toLowerCase())

        Object.entries(this.lines.value).forEach(([indexStr, content]) => {
            if (content && content !== '\u00A0') {
                const lineIndex = Number(indexStr)
                const lineMatches = this.searchInLine(lineIndex, content, normalizedQuery)
                matches.push(...lineMatches)
            }
        })

        this.updateOccurrenceCounts(matches)
        return matches
    }

    /**
     * Search a specific batch
     */
    private searchBatch(batchIndex: number, normalizedQuery: string): Array<{ itemIndex: number, occurrence: number, totalInItem: number }> {
        const matches: Array<{ itemIndex: number, occurrence: number, totalInItem: number }> = []
        const start = batchIndex * BATCH_SIZE
        const end = Math.min(start + BATCH_SIZE - 1, this.totalLines.value - 1)

        for (let lineIndex = start; lineIndex <= end; lineIndex++) {
            const content = this.lines.value[lineIndex]
            if (content && content !== '\u00A0') {
                const lineMatches = this.searchInLine(lineIndex, content, normalizedQuery)
                matches.push(...lineMatches)
            }
        }

        return matches
    }

    /**
     * Search within a single line
     */
    private searchInLine(lineIndex: number, content: string, normalizedQuery: string): Array<{ itemIndex: number, occurrence: number, totalInItem: number }> {
        const matches: Array<{ itemIndex: number, occurrence: number, totalInItem: number }> = []
        const normalizedContent = this.removeDiacritics(this.stripHtml(content).toLowerCase())

        let startIndex = 0
        let occurrence = 0

        while (true) {
            const index = normalizedContent.indexOf(normalizedQuery, startIndex)
            if (index === -1) break

            matches.push({
                itemIndex: lineIndex,
                occurrence,
                totalInItem: 0 // Will be updated later
            })

            occurrence++
            startIndex = index + 1
        }

        return matches
    }

    /**
     * Update occurrence counts for matches in the same line
     */
    private updateOccurrenceCounts(matches: Array<{ itemIndex: number, occurrence: number, totalInItem: number }>) {
        const lineGroups = new Map<number, Array<{ itemIndex: number, occurrence: number, totalInItem: number }>>()

        matches.forEach(match => {
            if (!lineGroups.has(match.itemIndex)) {
                lineGroups.set(match.itemIndex, [])
            }
            lineGroups.get(match.itemIndex)!.push(match)
        })

        lineGroups.forEach(lineMatches => {
            const totalInLine = lineMatches.length
            lineMatches.forEach(match => {
                match.totalInItem = totalInLine
            })
        })
    }

    /**
     * Helper methods for search
     */
    private stripHtml(html: string): string {
        const tempDiv = document.createElement('div')
        tempDiv.innerHTML = html
        return tempDiv.textContent || ''
    }

    private removeDiacritics(text: string): string {
        // Remove Hebrew diacritics (nikkud and cantillation marks)
        return text.replace(/[\u0591-\u05C7]/g, '')
    }

    /**
     * Cleanup resources
     */
    cleanup() {
        if (this.streamingAbort) {
            this.streamingAbort()
            this.streamingAbort = null
        }
        this.pendingBatches.clear()
        this.priorityQueue = []
    }
}