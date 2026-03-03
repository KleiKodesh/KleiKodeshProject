/**
 * Book Line Viewer Service
 * 
 * Manages line loading with smart streaming prioritization.
 * 
 * Architecture:
 * 1. Immediate scaffolding: Fill lines.value with placeholders for instant navigation
 * 2. Smart streaming: Load batches with priority queue (user needs first)
 */

import { ref, type Ref } from 'vue'
import { dbService, type LineLoadResult } from './dbService'

const PADDING_LINES = 200
const BATCH_SIZE = 200

export class BookLineViewerService {
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
            this.totalLines.value = await dbService.getTotalLines(bookId)

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
     * Load and merge lines associated with a TOC entry
     */
    async loadTocLines(bookId: number, tocEntryId: number): Promise<void> {
        if (!this.bookId || this.bookId !== bookId) {
            console.warn('Cannot load TOC lines: book not loaded or mismatch')
            return
        }

        try {
            const tocLines = await dbService.getLinesByTocEntry(bookId, tocEntryId)

            if (tocLines.length === 0) {
                return
            }

            console.log(`✅ Loaded ${tocLines.length} lines for TOC section`)

            // Merge loaded lines into existing lines
            const updatedLines = { ...this.lines.value }
            tocLines.forEach(line => {
                updatedLines[line.lineIndex] = line.content
            })
            this.lines.value = updatedLines

            // Prioritize the area around the first line of this TOC entry
            if (tocLines.length > 0) {
                await this.prioritizeLines(tocLines[0]!.lineIndex, PADDING_LINES)
            }
        } catch (error) {
            console.error('❌ Failed to load TOC lines:', error)
        }
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

            const loadedLines = await dbService.loadLineRange(this.bookId, start, end)

            // Update lines with proper reactivity - create new object to trigger Vue's reactivity
            const updatedLines = { ...this.lines.value }
            loadedLines.forEach(line => {
                updatedLines[line.lineIndex] = line.content
            })
            this.lines.value = updatedLines

        } catch (error) {
            console.error(`❌ Failed to load batch ${batchIndex}:`, error)
        } finally {
            this.pendingBatches.delete(batchIndex)
        }
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