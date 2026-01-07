/**
 * Book Lines Loader
 * 
 * Utility for loading book lines from database.
 * Simple and reusable - components manage their own state/cache.
 */

import { dbManager, type LineLoadResult } from './dbManager'

export type { LineLoadResult }

export class BookLinesLoader {
    private readonly batchSize = 200

    /**
     * Get total line count for a book
     */
    async getTotalLines(bookId: number): Promise<number> {
        return dbManager.getTotalLines(bookId)
    }

    /**
     * Load a range of lines from database
     * Returns array of loaded lines
     */
    async loadLineRange(bookId: number, start: number, end: number): Promise<LineLoadResult[]> {
        return dbManager.loadLineRange(bookId, start, end)
    }

    /**
     * Start background loading of all lines in batches
     * Returns abort function
     */
    startBackgroundLoad(
        bookId: number,
        totalLines: number,
        onBatch: (lines: LineLoadResult[]) => void,
        onComplete?: () => void
    ): () => void {
        return dbManager.startBackgroundLoad(bookId, totalLines, this.batchSize, onBatch, onComplete)
    }
}

// Export singleton instance
export const bookLinesLoader = new BookLinesLoader()
