/**
 * BookLineViewerState
 * 
 * Manages line loading state and strategy for a single BookLineViewer instance.
 * 
 * Architecture:
 * - Virtualization ON: DB is source of truth, no buffer, load only visible content
 * - Virtualization OFF: Buffer is source of truth, progressive loading, search buffer
 */

import { ref, type Ref } from 'vue'
import { bookLinesLoader, type LineLoadResult } from './bookLinesManager'
import { dbManager } from './dbManager'

const PADDING_LINES = 200

export class BookLineViewerState {
    // Public reactive state
    lines: Ref<Record<number, string>> = ref({})
    totalLines: Ref<number> = ref(0)
    bufferUpdateCount: Ref<number> = ref(0) // Reactive counter for buffer updates
    isInitialLoad = true

    // Private state
    private lineBuffer: Record<number, string> = {}
    private backgroundAbort: (() => void) | null = null
    private bookId: number | null = null
    private virtualizationEnabled = false

    /**
     * Set virtualization mode
     * When enabled, DB becomes source of truth and buffer is cleared
     * When disabled, buffer becomes source of truth
     */
    setVirtualizationMode(enabled: boolean) {
        const wasEnabled = this.virtualizationEnabled
        this.virtualizationEnabled = enabled

        if (enabled && !wasEnabled) {
            // Switching to virtualization mode: Clear buffer, DB becomes source of truth
            this.lineBuffer = {}
            this.bufferUpdateCount.value++
            console.log('📚 Virtualization enabled: Buffer cleared, DB is source of truth')
        } else if (!enabled && wasEnabled) {
            // Switching from virtualization mode: Buffer will become source of truth
            // Don't clear anything here - let startProgressiveLoading handle the transition
            console.log('📚 Virtualization disabled: Buffer will become source of truth')
        }
    }

    /**
     * Clean up non-visible lines to free memory
     * Removes from UI but keeps in buffer (buffer is always complete)
     */
    cleanupNonVisibleLines(visibleLines: Set<number>, bufferSize = PADDING_LINES) {
        const linesToKeep = new Set<number>()

        // Calculate which lines to keep in UI (visible + buffer)
        visibleLines.forEach(lineIndex => {
            for (let i = Math.max(0, lineIndex - bufferSize);
                i <= Math.min(this.totalLines.value - 1, lineIndex + bufferSize);
                i++) {
                linesToKeep.add(i)
            }
        })

        // Remove non-visible lines from UI (but keep in buffer)
        Object.keys(this.lines.value).forEach(indexStr => {
            const lineIndex = Number(indexStr)
            if (!linesToKeep.has(lineIndex)) {
                const content = this.lines.value[lineIndex]
                if (content && content !== '\u00A0') {
                    // Content already in buffer, just remove from UI
                    delete this.lines.value[lineIndex]
                }
            }
        })
    }

    /**
     * Load a new book
     */
    async loadBook(bookId: number, isRestore: boolean, initialLineIndex?: number): Promise<void> {
        console.log('📚 Loading book:', bookId, 'isRestore:', isRestore, 'initialLineIndex:', initialLineIndex, 'virtualization:', this.virtualizationEnabled)

        this.cleanup()

        this.bookId = bookId
        this.isInitialLoad = true
        this.totalLines.value = 0
        this.lines.value = {}

        try {
            this.totalLines.value = await bookLinesLoader.getTotalLines(bookId)

            // Only start background loading if virtualization is OFF
            if (!this.virtualizationEnabled) {
                this.startBackgroundLoading()
            }

            // Only load initial content if restoring
            if (isRestore && initialLineIndex !== undefined) {
                await this.loadLinesAround(initialLineIndex)
            }

            this.isInitialLoad = false
        } catch (error) {
            console.error('❌ Failed to load book:', error)
            this.totalLines.value = 0
            throw error
        }
    }

    /**
     * Load lines around a center point
     * Virtualization ON: Efficient batched loading directly to UI
     * Virtualization OFF: Load to both UI and buffer
     */
    async loadLinesAround(centerLine: number, padding = PADDING_LINES): Promise<void> {
        const start = Math.max(0, centerLine - padding)
        const end = Math.min(this.totalLines.value - 1, centerLine + padding)

        if (this.virtualizationEnabled) {
            // Virtualization ON: Efficient batched loading
            await this.loadRangeEfficiently(start, end)
        } else {
            // Virtualization OFF: Use buffer-first approach
            // First check buffer for already loaded lines
            for (let i = start; i <= end; i++) {
                const bufferedLine = this.lineBuffer[i]
                if (this.lines.value[i] === undefined && bufferedLine !== undefined) {
                    this.lines.value[i] = bufferedLine
                    // Keep in buffer too - don't delete
                }
            }

            // Then load missing lines from DB
            const needsLoading: number[] = []
            for (let i = start; i <= end; i++) {
                if (this.lines.value[i] === undefined && this.lineBuffer[i] === undefined) {
                    needsLoading.push(i)
                }
            }

            if (needsLoading.length > 0 && this.bookId !== null) {
                const firstLine = needsLoading[0]!
                const lastLine = needsLoading[needsLoading.length - 1]!
                const loadedLines = await bookLinesLoader.loadLineRange(this.bookId, firstLine, lastLine)
                loadedLines.forEach(line => {
                    // Add to both UI and buffer
                    this.lines.value[line.lineIndex] = line.content
                    this.lineBuffer[line.lineIndex] = line.content
                })
            }
        }
    }

    /**
     * Efficiently load a range of lines in virtualization mode
     * Batches requests to minimize DB calls and avoid loading already loaded lines
     */
    private async loadRangeEfficiently(start: number, end: number): Promise<void> {
        if (!this.bookId) return

        // Find continuous ranges that need loading
        const rangesToLoad: Array<{ start: number, end: number }> = []
        let rangeStart: number | null = null

        for (let i = start; i <= end; i++) {
            const needsLoading = this.lines.value[i] === undefined

            if (needsLoading) {
                if (rangeStart === null) {
                    rangeStart = i
                }
            } else {
                if (rangeStart !== null) {
                    rangesToLoad.push({ start: rangeStart, end: i - 1 })
                    rangeStart = null
                }
            }
        }

        // Handle final range if it extends to the end
        if (rangeStart !== null) {
            rangesToLoad.push({ start: rangeStart, end })
        }

        // Load all ranges in parallel for maximum efficiency
        if (rangesToLoad.length > 0) {
            console.log(`📚 Loading ${rangesToLoad.length} ranges in virtualization mode:`, rangesToLoad)

            const loadPromises = rangesToLoad.map(async (range) => {
                try {
                    const loadedLines = await dbManager.loadLineRange(this.bookId!, range.start, range.end)
                    return loadedLines
                } catch (error) {
                    console.error(`❌ Failed to load range ${range.start}-${range.end}:`, error)
                    return []
                }
            })

            const results = await Promise.all(loadPromises)

            // Apply all loaded lines to UI
            results.flat().forEach(line => {
                this.lines.value[line.lineIndex] = line.content
            })

            console.log(`✅ Loaded ${results.flat().length} lines in ${rangesToLoad.length} batched requests`)
        }
    }

    /**
     * Handle TOC selection - stop background loading, load selected area, apply buffer
     */
    async handleTocSelection(lineIndex: number): Promise<void> {
        // Stop background loading
        this.stopBackgroundLoading()

        // Load selected ± padding (from buffer if available, else DB)
        await this.loadLinesAround(lineIndex, PADDING_LINES)

        // Apply remaining buffer to UI (non-blocking)
        setTimeout(() => {
            Object.assign(this.lines.value, this.lineBuffer)
            this.lineBuffer = {}
        }, 0)
    }

    /**
     * Start background loading of all lines (only when virtualization is OFF)
     */
    private startBackgroundLoading() {
        if (this.bookId === null || this.totalLines.value === 0 || this.virtualizationEnabled) return

        // Delay start to let UI render first
        setTimeout(() => {
            if (this.bookId === null || this.virtualizationEnabled) return

            this.backgroundAbort = bookLinesLoader.startBackgroundLoad(
                this.bookId,
                this.totalLines.value,
                (loadedLines: LineLoadResult[]) => {
                    // In buffer mode (virtualization OFF), update both buffer and visible lines
                    let hasNewContent = false
                    loadedLines.forEach(line => {
                        if (this.lineBuffer[line.lineIndex] === undefined) {
                            // Add to buffer (source of truth for buffer mode)
                            this.lineBuffer[line.lineIndex] = line.content
                            hasNewContent = true
                        }

                        // Also update visible lines if not already loaded
                        if (this.lines.value[line.lineIndex] === undefined) {
                            this.lines.value[line.lineIndex] = line.content
                        }
                    })

                    // Trigger reactive update for search
                    if (hasNewContent) {
                        this.bufferUpdateCount.value++
                    }
                }
            )
        }, 100)
    }

    /**
     * Stop background loading
     */
    private stopBackgroundLoading() {
        if (this.backgroundAbort) {
            this.backgroundAbort()
            this.backgroundAbort = null
        }
    }

    /**
     * Get search data based on virtualization mode
     * Virtualization ON: Search DB directly
     * Virtualization OFF: Search buffer (source of truth)
     */
    async getSearchData(): Promise<Array<{ index: number, content: string }>> {
        if (this.virtualizationEnabled) {
            // Virtualization ON: Search DB directly
            if (!this.bookId) return []

            console.log('🔍 Searching DB directly (virtualization mode)')
            // For now, return empty array - search will be handled by DB search
            return []
        } else {
            // Virtualization OFF: Search buffer
            const allLines: Array<{ index: number, content: string }> = []

            console.log('Buffer size for search:', Object.keys(this.lineBuffer).length)

            Object.entries(this.lineBuffer).forEach(([indexStr, content]) => {
                if (content && content !== '\u00A0') {
                    allLines.push({
                        index: Number(indexStr),
                        content
                    })
                }
            })

            console.log('Search lines found:', allLines.length)
            return allLines.sort((a, b) => a.index - b.index)
        }
    }

    /**
     * Search lines in DB (for virtualization mode)
     */
    async searchInDB(searchTerm: string): Promise<Array<{ index: number, content: string }>> {
        if (!this.bookId || !this.virtualizationEnabled) return []

        try {
            console.log('🔍 Searching DB for term:', searchTerm)
            const results = await dbManager.searchLines(this.bookId, searchTerm)
            return results.map(result => ({
                index: result.lineIndex,
                content: result.content
            }))
        } catch (error) {
            console.error('❌ DB search failed:', error)
            return []
        }
    }

    /**
     * Move all buffer content to UI (for non-virtualized mode)
     */
    moveBufferToUI(): void {
        Object.assign(this.lines.value, this.lineBuffer)
    }

    /**
     * Start progressive loading when switching from virtualization ON to OFF
     * Preserves currently loaded lines and starts background loading
     */
    async startProgressiveLoading(): Promise<void> {
        if (!this.bookId || this.virtualizationEnabled) return

        console.log('📚 Starting progressive loading after virtualization switch')

        // Copy currently loaded UI lines to buffer to preserve them
        Object.entries(this.lines.value).forEach(([indexStr, content]) => {
            if (content && content !== '\u00A0') {
                this.lineBuffer[Number(indexStr)] = content
            }
        })

        // Start background loading to fill the rest
        this.startBackgroundLoading()

        console.log('✅ Progressive loading started, preserved', Object.keys(this.lineBuffer).length, 'lines')
    }

    /**
     * Cleanup resources
     */
    cleanup() {
        this.stopBackgroundLoading()
        this.lineBuffer = {}
    }
}