/**
 * Book Line Loader - Pure Service (Framework-Agnostic)
 * 
 * Manages line loading with smart streaming prioritization.
 * No Vue dependencies - pure data operations.
 */

import { dbService } from './dbService';

const PADDING_LINES = 200;
const BATCH_SIZE = 200;

export interface LineData {
    lineIndex: number;
    content: string;
}

export interface BookLineState {
    lines: Record<number, string>;
    totalLines: number;
    isInitialLoad: boolean;
}

export class BookLineLoader {
    // Private state
    private bookId: number | null = null;
    private lines: Record<number, string> = {};
    private totalLines: number = 0;
    private isInitialLoad = true;
    private pendingBatches: Set<number> = new Set();
    private priorityQueue: number[] = [];
    private streamingActive = false;
    private onStateChange?: (state: BookLineState) => void;

    /**
     * Set callback for state changes
     */
    setOnStateChange(callback: (state: BookLineState) => void) {
        this.onStateChange = callback;
    }

    /**
     * Get current state
     */
    getState(): BookLineState {
        return {
            lines: { ...this.lines },
            totalLines: this.totalLines,
            isInitialLoad: this.isInitialLoad,
        };
    }

    /**
     * Notify state change
     */
    private notifyStateChange() {
        if (this.onStateChange) {
            this.onStateChange(this.getState());
        }
    }

    /**
     * Load a new book - immediate scaffolding + start streaming
     */
    async loadBook(bookId: number, initialLineIndex?: number): Promise<void> {
        this.cleanup();
        this.bookId = bookId;
        this.isInitialLoad = true;

        try {
            this.totalLines = await dbService.getTotalLines(bookId);

            // Stage 1: Immediate scaffolding with placeholders
            const placeholders: Record<number, string> = {};
            for (let i = 0; i < this.totalLines; i++) {
                placeholders[i] = '\u00A0'; // Hard space placeholder
            }
            this.lines = placeholders;
            this.notifyStateChange();

            // Stage 2: Start smart streaming
            this.startSmartStreaming();

            // Load initial content if provided
            if (initialLineIndex !== undefined) {
                await this.prioritizeLines(initialLineIndex);
            }

            this.isInitialLoad = false;
            this.notifyStateChange();
        } catch (error) {
            console.error('❌ Failed to load book:', error);
            this.totalLines = 0;
            throw error;
        }
    }

    /**
     * Prioritize loading lines around a specific point
     */
    async prioritizeLines(centerLine: number, padding = PADDING_LINES): Promise<void> {
        const start = Math.max(0, centerLine - padding);
        const end = Math.min(this.totalLines - 1, centerLine + padding);

        const neededBatches = new Set<number>();
        for (let i = start; i <= end; i++) {
            const batchIndex = Math.floor(i / BATCH_SIZE);
            neededBatches.add(batchIndex);
        }

        const batchArray = Array.from(neededBatches).sort((a, b) => {
            const centerBatch = Math.floor(centerLine / BATCH_SIZE);
            return Math.abs(a - centerBatch) - Math.abs(b - centerBatch);
        });

        this.priorityQueue = this.priorityQueue.filter(batch => !neededBatches.has(batch));
        this.priorityQueue.unshift(...batchArray);

        if (batchArray.length > 0) {
            await this.loadBatch(batchArray[0]!);
        }
    }

    /**
     * Load lines for a TOC entry
     */
    async loadTocLines(bookId: number, tocEntryId: number): Promise<void> {
        if (!this.bookId || this.bookId !== bookId) {
            console.warn('Cannot load TOC lines: book not loaded or mismatch');
            return;
        }

        try {
            const tocLines = await dbService.getLinesByTocEntry(bookId, tocEntryId);
            if (tocLines.length === 0) return;

            tocLines.forEach(line => {
                this.lines[line.lineIndex] = line.content;
            });
            this.notifyStateChange();

            if (tocLines.length > 0) {
                await this.prioritizeLines(tocLines[0]!.lineIndex, PADDING_LINES);
            }
        } catch (error) {
            console.error('❌ Failed to load TOC lines:', error);
        }
    }

    /**
     * Start smart streaming
     */
    private startSmartStreaming() {
        if (!this.bookId || this.totalLines === 0) return;

        const totalBatches = Math.ceil(this.totalLines / BATCH_SIZE);
        this.priorityQueue = Array.from({ length: totalBatches }, (_, i) => i);
        this.pendingBatches.clear();
        this.streamingActive = true;

        this.processQueue();
    }

    /**
     * Process the priority queue
     */
    private async processQueue() {
        if (!this.streamingActive || !this.bookId || this.priorityQueue.length === 0) return;

        const nextBatch = this.priorityQueue.shift()!;
        if (this.pendingBatches.has(nextBatch)) {
            setTimeout(() => this.processQueue(), 10);
            return;
        }

        await this.loadBatch(nextBatch);
        setTimeout(() => this.processQueue(), 50);
    }

    /**
     * Load a specific batch of lines
     */
    private async loadBatch(batchIndex: number): Promise<void> {
        if (!this.bookId || this.pendingBatches.has(batchIndex)) return;

        this.pendingBatches.add(batchIndex);

        try {
            const start = batchIndex * BATCH_SIZE;
            const end = Math.min(start + BATCH_SIZE - 1, this.totalLines - 1);

            const loadedLines = await dbService.loadLineRange(this.bookId, start, end);

            loadedLines.forEach(line => {
                this.lines[line.lineIndex] = line.content;
            });
            this.notifyStateChange();
        } catch (error) {
            console.error(`❌ Failed to load batch ${batchIndex}:`, error);
        } finally {
            this.pendingBatches.delete(batchIndex);
        }
    }

    /**
     * Cleanup resources
     */
    cleanup() {
        this.streamingActive = false;
        this.pendingBatches.clear();
        this.priorityQueue = [];
        this.lines = {};
        this.totalLines = 0;
    }
}
