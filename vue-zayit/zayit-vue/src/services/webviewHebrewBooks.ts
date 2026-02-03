/**
 * WebView Hebrew Books Service - Unified Hebrew Books Operations
 * 
 * Handles Hebrew Books download and viewing using C# WebView bridge.
 * Uses static global SetVirtualHostNameToFolderMapping for all Hebrew books.
 * 
 * Pipeline: Vue → webviewBridge → C# ServiceProvider → HebrewBooksService → Download capture + Cache
 */

import { webviewBridge } from './webviewBridge'

export class WebviewHebrewBooksService {
    private static instance: WebviewHebrewBooksService

    static getInstance(): WebviewHebrewBooksService {
        if (!WebviewHebrewBooksService.instance) {
            WebviewHebrewBooksService.instance = new WebviewHebrewBooksService()
        }
        return WebviewHebrewBooksService.instance
    }

    /**
     * Prepare Hebrew book for viewing
     * C# will handle download capture, cache management, and virtual URL creation
     */
    async prepareForViewing(bookId: string, title: string): Promise<{ success: boolean; cached?: boolean; fileName?: string; url?: string }> {
        console.log('[WebviewHebrewBooks] Preparing book for viewing:', bookId, title)

        try {
            const result = await webviewBridge.call<{ success: boolean; cached?: boolean; fileName?: string; url?: string }>('PrepareHebrewBookDownload', bookId, title, 'view')
            console.log('[WebviewHebrewBooks] Prepare result:', result)
            return result
        } catch (error) {
            console.error('[WebviewHebrewBooks] Failed to prepare book for viewing:', error)
            return { success: false }
        }
    }

    /**
     * Prepare Hebrew book for download
     * C# will show save dialog and handle download capture
     */
    async prepareForDownload(bookId: string, title: string): Promise<{ success: boolean; cancelled?: boolean; filePath?: string }> {
        console.log('[WebviewHebrewBooks] Preparing book for download:', bookId, title)

        try {
            const result = await webviewBridge.call<{ success: boolean; cancelled?: boolean; filePath?: string }>('PrepareHebrewBookDownload', bookId, title, 'download')
            console.log('[WebviewHebrewBooks] Download prepare result:', result)
            return result
        } catch (error) {
            console.error('[WebviewHebrewBooks] Failed to prepare book for download:', error)
            return { success: false }
        }
    }

    /**
     * Notify C# that a Hebrew book tab was closed (for cache cleanup)
     */
    async notifyTabClosed(fileName: string): Promise<void> {
        try {
            await webviewBridge.call('HandleHebrewBookTabClosed', fileName)
            console.log('[WebviewHebrewBooks] Notified C# of tab closure:', fileName)
        } catch (error) {
            console.error('[WebviewHebrewBooks] Failed to notify tab closure:', error)
        }
    }

    /**
     * Get Hebrew books cache statistics
     */
    async getCacheStats(): Promise<{ totalFiles: number; activeFiles: number; totalSizeMB: number; maxFiles: number }> {
        try {
            const stats = await webviewBridge.call<{ totalFiles: number; activeFiles: number; totalSizeMB: number; maxFiles: number }>('GetHebrewBooksCacheStats')
            return stats || { totalFiles: 0, activeFiles: 0, totalSizeMB: 0, maxFiles: 10 }
        } catch (error) {
            console.error('[WebviewHebrewBooks] Failed to get cache stats:', error)
            return { totalFiles: 0, activeFiles: 0, totalSizeMB: 0, maxFiles: 10 }
        }
    }

    /**
     * Clear Hebrew books cache
     */
    async clearCache(): Promise<void> {
        try {
            await webviewBridge.call('ClearHebrewBooksCache')
            console.log('[WebviewHebrewBooks] Cache cleared')
        } catch (error) {
            console.error('[WebviewHebrewBooks] Failed to clear cache:', error)
        }
    }

    /**
     * Trigger browser download (used internally by C# flow)
     */
    triggerBrowserDownload(bookId: string, title: string): void {
        console.log('[WebviewHebrewBooks] Triggering browser download:', bookId, title)

        const url = `https://download.hebrewbooks.org/downloadhandler.ashx?req=${bookId}`
        const link = document.createElement('a')
        link.href = url
        link.download = `${title}.pdf`
        link.style.display = 'none'
        document.body.appendChild(link)
        link.click()
        document.body.removeChild(link)
    }

    /**
     * Check if service is available
     */
    isAvailable(): boolean {
        return webviewBridge.isAvailable()
    }
}

// Export singleton instance
export const webviewHebrewBooks = WebviewHebrewBooksService.getInstance()
