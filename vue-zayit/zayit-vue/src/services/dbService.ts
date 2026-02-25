/**
 * Unified Database Service
 * 
 * Consolidated database manager that handles all database operations.
 * Supports both C# WebView bridge and development server fallbacks.
 */

import type { Category } from '../types/BookCategoryTree'
import type { Book } from '../types/Book'
import type { TocEntry } from '../types/BookToc'
import type { Link } from '../types/Link'
import type { ConnectionType } from '../types/ConnectionType'
import { webviewBridge } from './webviewBridge'
import { censorDivineNames } from '../utils/censorDivineNames'
import { useSettingsStore } from '../stores/settingsStore'
import { SqlQueries } from './dbQueries'

export interface LineLoadResult {
    lineIndex: number
    content: string
    tocEntryId?: number | null
}

/**
 * Execute SQL query via Vite dev server API (development only)
 */
async function devQuery<T = any>(sql: string, params: any[] = []): Promise<T[]> {
    if (import.meta.env.PROD) {
        throw new Error('SQLite API is only available in development mode')
    }

    try {
        const response = await fetch('/__db/query', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ query: sql, params })
        })

        const result = await response.json()

        if (!result.success) {
            throw new Error(result.error)
        }

        return result.data
    } catch (error) {
        console.error('❌ Database query failed:', error)
        throw error
    }
}

class DatabaseService {
    private static instance: DatabaseService

    static getInstance(): DatabaseService {
        if (!DatabaseService.instance) {
            DatabaseService.instance = new DatabaseService()
        }
        return DatabaseService.instance
    }

    private isWebViewAvailable(): boolean {
        return webviewBridge.isAvailable()
    }

    private isDevServerAvailable(): boolean {
        return import.meta.env.DEV
    }

    // ============================================================================
    // Tree Data
    // ============================================================================

    async getTree(): Promise<{ categoriesFlat: Category[], booksFlat: Book[] }> {
        console.log('[DatabaseService] getTree called')

        if (this.isWebViewAvailable()) {
            console.log('[DatabaseService] Using WebView bridge')
            return await webviewBridge.call('GetTree', SqlQueries.getAllCategories, SqlQueries.getAllBooks)
        } else if (this.isDevServerAvailable()) {
            console.log('[DatabaseService] Using dev server fallback')
            const categoriesFlat = await devQuery<Category>(SqlQueries.getAllCategories)
            const booksFlat = await devQuery<Book>(SqlQueries.getAllBooks)
            return { categoriesFlat, booksFlat }
        } else {
            throw new Error('No database source available')
        }
    }

    // ============================================================================
    // TOC Data
    // ============================================================================

    async getToc(bookId: number): Promise<{ tocEntriesFlat: TocEntry[] }> {
        if (this.isWebViewAvailable()) {
            return await webviewBridge.call('GetToc', bookId, SqlQueries.getToc(bookId))
        } else if (this.isDevServerAvailable()) {
            const tocEntriesFlat = await devQuery<TocEntry>(SqlQueries.getToc(bookId))
            return { tocEntriesFlat }
        } else {
            throw new Error('No database source available')
        }
    }

    // ============================================================================
    // Connection Types & Links
    // ============================================================================

    async getConnectionTypes(): Promise<ConnectionType[]> {
        if (this.isWebViewAvailable()) {
            return await webviewBridge.call('GetConnectionTypes', SqlQueries.getConnectionTypes)
        } else if (this.isDevServerAvailable()) {
            return await devQuery<ConnectionType>(SqlQueries.getConnectionTypes)
        } else {
            throw new Error('No database source available')
        }
    }

    async getLinks(lineId: number, tabId: string, bookId: number, connectionTypeId?: number): Promise<Link[]> {
        let links: Link[]

        if (this.isWebViewAvailable()) {
            const queryObj = SqlQueries.getLinks(lineId, connectionTypeId)
            links = await webviewBridge.call('GetLinks', lineId, tabId, bookId, queryObj.query, queryObj.params)
        } else if (this.isDevServerAvailable()) {
            const queryObj = SqlQueries.getLinks(lineId, connectionTypeId)
            links = await devQuery<Link>(queryObj.query, queryObj.params)
        } else {
            throw new Error('No database source available')
        }

        // Apply censoring if enabled
        if (useSettingsStore().censorDivineNames) {
            links = links.map(link => ({
                ...link,
                content: link.content ? censorDivineNames(link.content) : link.content
            }))
        }

        return links
    }

    // ============================================================================
    // Line Operations
    // ============================================================================

    async getTotalLines(bookId: number): Promise<number> {
        if (this.isWebViewAvailable()) {
            return await webviewBridge.call('GetTotalLines', bookId, SqlQueries.getBookLineCount(bookId))
        } else if (this.isDevServerAvailable()) {
            const result = await devQuery<{ totalLines: number }>(SqlQueries.getBookLineCount(bookId))
            return result[0]?.totalLines || 0
        } else {
            throw new Error('No database source available')
        }
    }

    async getLineContent(bookId: number, lineIndex: number): Promise<string | null> {
        let content: string | null

        if (this.isWebViewAvailable()) {
            content = await webviewBridge.call('GetLineContent', bookId, lineIndex, SqlQueries.getLineContent(bookId, lineIndex))
        } else if (this.isDevServerAvailable()) {
            const result = await devQuery<{ content: string }>(SqlQueries.getLineContent(bookId, lineIndex))
            content = result[0]?.content ?? null
        } else {
            throw new Error('No database source available')
        }

        // Apply censoring if enabled
        if (content && useSettingsStore().censorDivineNames) {
            content = censorDivineNames(content)
        }

        return content
    }

    async getLineId(bookId: number, lineIndex: number): Promise<number | null> {
        if (this.isWebViewAvailable()) {
            return await webviewBridge.call('GetLineId', bookId, lineIndex, SqlQueries.getLineId(bookId, lineIndex))
        } else if (this.isDevServerAvailable()) {
            const result = await devQuery<{ id: number }>(SqlQueries.getLineId(bookId, lineIndex))
            return result[0]?.id ?? null
        } else {
            throw new Error('No database source available')
        }
    }

    async loadLineRange(bookId: number, start: number, end: number): Promise<LineLoadResult[]> {
        let lines: LineLoadResult[]

        if (this.isWebViewAvailable()) {
            lines = await webviewBridge.call('GetLineRange', bookId, start, end, SqlQueries.getLineRange(bookId, start, end))
        } else if (this.isDevServerAvailable()) {
            lines = await devQuery<LineLoadResult>(SqlQueries.getLineRange(bookId, start, end))
        } else {
            throw new Error('No database source available')
        }

        // Apply censoring if enabled
        if (useSettingsStore().censorDivineNames) {
            lines = lines.map(line => ({
                ...line,
                content: censorDivineNames(line.content)
            }))
        }

        return lines
    }

    // ============================================================================
    // Search Operations
    // ============================================================================

    async searchLines(bookId: number, searchTerm: string): Promise<LineLoadResult[]> {
        let lines: LineLoadResult[]

        if (this.isWebViewAvailable()) {
            lines = await webviewBridge.call('SearchLines', bookId, searchTerm, SqlQueries.searchLines(bookId, searchTerm))
        } else if (this.isDevServerAvailable()) {
            lines = await devQuery<LineLoadResult>(SqlQueries.searchLines(bookId, searchTerm))
        } else {
            throw new Error('No database source available')
        }

        // Apply censoring if enabled
        if (useSettingsStore().censorDivineNames) {
            lines = lines.map(line => ({
                ...line,
                content: censorDivineNames(line.content)
            }))
        }

        return lines
    }

    // ============================================================================
    // Background Loading
    // ============================================================================

    startBackgroundLoad(
        bookId: number,
        totalLines: number,
        batchSize: number,
        onBatch: (lines: LineLoadResult[]) => void,
        onComplete?: () => void
    ): () => void {
        let aborted = false
        const abort = () => {
            aborted = true
        }

        const loadNextBatch = async (currentBatch: number) => {
            if (aborted) return

            const start = currentBatch * batchSize
            if (start >= totalLines) {
                onComplete?.()
                return
            }

            const end = Math.min(start + batchSize - 1, totalLines - 1)

            try {
                const lines = await this.loadLineRange(bookId, start, end)
                if (!aborted) {
                    onBatch(lines)
                    loadNextBatch(currentBatch + 1)
                }
            } catch (error) {
                console.error('Background load failed:', error)
            }
        }

        loadNextBatch(0)
        return abort
    }

    // ============================================================================
    // TOC-based Line Loading
    // ============================================================================

    async getLinesByTocEntry(bookId: number, tocEntryId: number): Promise<LineLoadResult[]> {
        // First get all line IDs associated with this TOC entry
        let lineIdResults: { lineId: number }[]

        if (this.isWebViewAvailable()) {
            lineIdResults = await webviewBridge.call('GetLineIdsByTocEntry', tocEntryId, SqlQueries.getLineIdsByTocEntry(tocEntryId))
        } else if (this.isDevServerAvailable()) {
            lineIdResults = await devQuery<{ lineId: number }>(SqlQueries.getLineIdsByTocEntry(tocEntryId))
        } else {
            throw new Error('No database source available')
        }

        if (lineIdResults.length === 0) {
            return []
        }

        const lineIds = lineIdResults.map(r => r.lineId)

        // Now get the actual line content
        let lines: LineLoadResult[]

        if (this.isWebViewAvailable()) {
            lines = await webviewBridge.call('GetLinesByIds', bookId, lineIds, SqlQueries.getLinesByIds(bookId, lineIds))
        } else if (this.isDevServerAvailable()) {
            lines = await devQuery<LineLoadResult>(SqlQueries.getLinesByIds(bookId, lineIds))
        } else {
            throw new Error('No database source available')
        }

        // Apply censoring if enabled
        if (useSettingsStore().censorDivineNames) {
            lines = lines.map(line => ({
                ...line,
                content: censorDivineNames(line.content)
            }))
        }

        return lines
    }

    async getLineIdsByTocEntry(bookId: number, tocEntryId: number): Promise<{ lineId: number }[]> {
        if (this.isWebViewAvailable()) {
            return await webviewBridge.call('GetLineIdsByTocEntry', tocEntryId, SqlQueries.getLineIdsByTocEntry(tocEntryId))
        } else if (this.isDevServerAvailable()) {
            return await devQuery<{ lineId: number }>(SqlQueries.getLineIdsByTocEntry(tocEntryId))
        } else {
            throw new Error('No database source available')
        }
    }

    async getLineIndexFromLineId(lineId: number): Promise<{ lineIndex: number; bookId: number } | null> {
        if (this.isWebViewAvailable()) {
            const result = await webviewBridge.call<Array<{ lineIndex: number; bookId: number }>>('GetLineIndexFromLineId', lineId, SqlQueries.getLineIndexFromLineId(lineId))
            return result?.[0] || null
        } else if (this.isDevServerAvailable()) {
            const result = await devQuery<{ lineIndex: number; bookId: number }>(SqlQueries.getLineIndexFromLineId(lineId))
            return result[0] || null
        } else {
            throw new Error('No database source available')
        }
    }

    // ============================================================================
    // Topic Data
    // ============================================================================

    async getTopicsForBooks(bookIds: number[]): Promise<Array<{ id: number; name: string }>> {
        if (bookIds.length === 0) return []

        if (this.isWebViewAvailable()) {
            const queryObj = SqlQueries.getTopicsForBooks(bookIds)
            return await webviewBridge.call('GetTopicsForBooks', bookIds, queryObj.query, queryObj.params)
        } else if (this.isDevServerAvailable()) {
            const queryObj = SqlQueries.getTopicsForBooks(bookIds)
            return await devQuery<{ id: number; name: string }>(queryObj.query, queryObj.params)
        } else {
            throw new Error('No database source available')
        }
    }

    async getBookTopics(bookIds: number[]): Promise<Array<{ bookId: number; topicId: number }>> {
        if (bookIds.length === 0) return []

        if (this.isWebViewAvailable()) {
            const queryObj = SqlQueries.getBookTopics(bookIds)
            return await webviewBridge.call('GetBookTopics', bookIds, queryObj.query, queryObj.params)
        } else if (this.isDevServerAvailable()) {
            const queryObj = SqlQueries.getBookTopics(bookIds)
            return await devQuery<{ bookId: number; topicId: number }>(queryObj.query, queryObj.params)
        } else {
            throw new Error('No database source available')
        }
    }
}

// Export singleton instance
export const dbService = DatabaseService.getInstance()

// Legacy exports for backward compatibility
export const databaseService = dbService
export const unifiedDbManager = dbService
export const dbManager = dbService
