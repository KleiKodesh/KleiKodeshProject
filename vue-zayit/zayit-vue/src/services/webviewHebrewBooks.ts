/**
 * WebView Hebrew Books Service - Thin Helper for Hebrew Books Operations
 * 
 * Simple helper that delegates all WebView logic to the bridge.
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

    isAvailable(): boolean {
        return webviewBridge.isAvailable()
    }

    // Flow 1: Prepare book for viewing (cache if needed, no SaveAs dialog)
    async prepareForViewing(bookId: string, title: string): Promise<{ success: boolean; cached?: boolean; fileName?: string; url?: string }> {
        return await webviewBridge.prepareHebrewBookForViewing(bookId, title)
    }

    // Flow 2: Download book with SaveAs dialog (user chooses location)
    async prepareForDownload(bookId: string, title: string): Promise<{ success: boolean; cancelled?: boolean; filePath?: string }> {
        return await webviewBridge.prepareHebrewBookForDownload(bookId, title)
    }

    async notifyTabClosed(fileName: string): Promise<void> {
        return await webviewBridge.notifyHebrewBookTabClosed(fileName)
    }

    async getCacheStats(): Promise<{ totalFiles: number; activeFiles: number; totalSizeMB: number; maxFiles: number }> {
        return await webviewBridge.getHebrewBooksCacheStats()
    }

    async clearCache(): Promise<void> {
        return await webviewBridge.clearHebrewBooksCache()
    }
}

export const webviewHebrewBooks = WebviewHebrewBooksService.getInstance()
