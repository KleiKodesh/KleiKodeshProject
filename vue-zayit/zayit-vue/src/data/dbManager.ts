/**
 * Database Manager
 * 
 * Unified router for all database requests.
 * Routes to C# webview (production) or dev server (development).
 */

import type { Category } from '../types/BookCategoryTree'
import type { Book } from '../types/Book'
import type { TocEntry } from '../types/BookToc'
import type { Link } from '../types/Link'
import type { ConnectionType } from '../types/ConnectionType'
import * as sqliteDb from './sqliteDb'
import type { LineLoadResult } from './sqliteDb'
import { CSharpBridge } from './csharpBridge'
import { censorDivineNames } from '../utils/censorDivineNames'
import { useSettingsStore } from '../stores/settingsStore'
import { SqlQueries } from './sqlQueries'

export type { LineLoadResult }

class DatabaseManager {
    private csharp = CSharpBridge.getInstance()

    private isWebViewAvailable(): boolean {
        return this.csharp.isAvailable()
    }

    private isDevServerAvailable(): boolean {
        return import.meta.env.DEV
    }

    // --------------------------------------------------------------------------
    // Public API - Tree Data
    // --------------------------------------------------------------------------

    async getTree(): Promise<{ categoriesFlat: Category[], booksFlat: Book[] }> {
        console.log('[DbManager] getTree called')
        if (this.isWebViewAvailable()) {
            console.log('[DbManager] Using WebView bridge')
            const promise = this.csharp.createRequest<{ categoriesFlat: Category[], booksFlat: Book[] }>('GetTree')
            this.csharp.send('GetTree', [SqlQueries.getAllCategories, SqlQueries.getAllBooks])
            console.log('[DbManager] Waiting for GetTree response...')
            const result = await promise
            console.log('[DbManager] GetTree response received:', result)
            return result
        } else if (this.isDevServerAvailable()) {
            console.log('[DbManager] Using dev server fallback')
            const categoriesFlat = await sqliteDb.getAllCategories()
            const booksFlat = await sqliteDb.getBooks()
            return { categoriesFlat, booksFlat }
        } else {
            console.error('[DbManager] No database source available')
            throw new Error('No database source available')
        }
    }

    // --------------------------------------------------------------------------
    // Public API - TOC Data
    // --------------------------------------------------------------------------

    async getToc(bookId: number): Promise<{ tocEntriesFlat: TocEntry[] }> {
        if (this.isWebViewAvailable()) {
            const promise = this.csharp.createRequest<{ tocEntriesFlat: TocEntry[] }>(`GetToc:${bookId}`)
            this.csharp.send('GetToc', [bookId, SqlQueries.getToc(bookId)])
            return promise
        } else if (this.isDevServerAvailable()) {
            return await sqliteDb.getToc(bookId)
        } else {
            throw new Error('No database source available')
        }
    }

    // --------------------------------------------------------------------------
    // Public API - Links
    // --------------------------------------------------------------------------

    async getConnectionTypes(): Promise<ConnectionType[]> {
        if (this.isWebViewAvailable()) {
            const promise = this.csharp.createRequest<ConnectionType[]>('GetConnectionTypes')
            this.csharp.send('GetConnectionTypes', [SqlQueries.getConnectionTypes])
            return promise
        } else if (this.isDevServerAvailable()) {
            return await sqliteDb.getConnectionTypes()
        } else {
            throw new Error('No database source available')
        }
    }

    async getLinks(lineId: number, tabId: string, bookId: number, connectionTypeId?: number): Promise<Link[]> {
        let links: Link[]

        if (this.isWebViewAvailable()) {
            const queryObj = SqlQueries.getLinks(lineId, connectionTypeId)
            const promise = this.csharp.createRequest<Link[]>(`GetLinks:${tabId}:${bookId}`)
            this.csharp.send('GetLinks', [lineId, tabId, bookId, queryObj.query])
            links = await promise
        } else if (this.isDevServerAvailable()) {
            links = await sqliteDb.getLinks(lineId, connectionTypeId)
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

    // --------------------------------------------------------------------------
    // Public API - Line Operations
    // --------------------------------------------------------------------------

    async getTotalLines(bookId: number): Promise<number> {
        if (this.isWebViewAvailable()) {
            const promise = this.csharp.createRequest<number>(`GetTotalLines:${bookId}`)
            this.csharp.send('GetTotalLines', [bookId, SqlQueries.getBookLineCount(bookId)])
            return promise
        } else if (this.isDevServerAvailable()) {
            return await sqliteDb.getTotalLines(bookId)
        } else {
            throw new Error('No database source available')
        }
    }

    async getLineContent(bookId: number, lineIndex: number): Promise<string | null> {
        let content: string | null

        if (this.isWebViewAvailable()) {
            const promise = this.csharp.createRequest<string | null>(`GetLineContent:${bookId}:${lineIndex}`)
            this.csharp.send('GetLineContent', [bookId, lineIndex, SqlQueries.getLineContent(bookId, lineIndex)])
            content = await promise
        } else if (this.isDevServerAvailable()) {
            content = await sqliteDb.getLineContent(bookId, lineIndex)
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
            const promise = this.csharp.createRequest<number | null>(`GetLineId:${bookId}:${lineIndex}`)
            this.csharp.send('GetLineId', [bookId, lineIndex, SqlQueries.getLineId(bookId, lineIndex)])
            return promise
        } else if (this.isDevServerAvailable()) {
            return await sqliteDb.getLineId(bookId, lineIndex)
        } else {
            throw new Error('No database source available')
        }
    }

    async loadLineRange(bookId: number, start: number, end: number): Promise<LineLoadResult[]> {
        let lines: LineLoadResult[]

        if (this.isWebViewAvailable()) {
            const promise = this.csharp.createRequest<LineLoadResult[]>(`GetLineRange:${bookId}:${start}:${end}`)
            this.csharp.send('GetLineRange', [bookId, start, end, SqlQueries.getLineRange(bookId, start, end)])
            lines = await promise
        } else if (this.isDevServerAvailable()) {
            lines = await sqliteDb.loadLineRange(bookId, start, end)
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

    // --------------------------------------------------------------------------
    // Public API - Search Operations
    // --------------------------------------------------------------------------

    async searchLines(bookId: number, searchTerm: string): Promise<LineLoadResult[]> {
        let lines: LineLoadResult[]

        if (this.isWebViewAvailable()) {
            const promise = this.csharp.createRequest<LineLoadResult[]>(`SearchLines:${bookId}:${searchTerm}`)
            this.csharp.send('SearchLines', [bookId, searchTerm, SqlQueries.searchLines(bookId, searchTerm)])
            lines = await promise
        } else if (this.isDevServerAvailable()) {
            lines = await sqliteDb.searchLines(bookId, searchTerm)
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

    // --------------------------------------------------------------------------
    // Public API - PDF Operations
    // --------------------------------------------------------------------------

    async openPdfFilePicker(): Promise<{ filePath: string | null, fileName: string | null, dataUrl: string | null }> {
        if (this.isWebViewAvailable()) {
            const promise = this.csharp.createRequest<{ filePath: string | null, fileName: string | null, dataUrl: string | null }>('OpenPdfFilePicker')
            this.csharp.send('OpenPdfFilePicker', [])
            return promise
        } else {
            // Fallback to browser file picker
            return { filePath: null, fileName: null, dataUrl: null }
        }
    }

    async loadPdfFromPath(filePath: string): Promise<string | null> {
        if (this.isWebViewAvailable()) {
            const promise = this.csharp.createRequest<string | null>(`LoadPdfFromPath:${filePath}`)
            this.csharp.send('LoadPdfFromPath', [filePath])
            return promise
        } else {
            // Cannot load from file path in browser
            return null
        }
    }

    // --------------------------------------------------------------------------
    // Utility - Background Loading
    // --------------------------------------------------------------------------

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
}

export const dbManager = new DatabaseManager()
