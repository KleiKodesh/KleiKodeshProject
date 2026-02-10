import { webviewBridge } from './webviewBridge'
import { bloomSearchCacheService } from './bloomSearchCacheService'
import type { BloomSearchResult, IndexingProgress } from '../types/BloomSearch'

class BloomSearchService {
    async isReady(): Promise<boolean> {
        try {
            if (!webviewBridge.isAvailable()) {
                console.warn('[BloomSearchService] WebView bridge not available')
                return false
            }
            return await webviewBridge.isBloomSearchReady()
        } catch (error) {
            console.error('[BloomSearchService] Error checking if ready:', error)
            return false
        }
    }

    async getIndexingProgress(): Promise<IndexingProgress> {
        try {
            if (!webviewBridge.isAvailable()) {
                return {
                    isReady: false,
                    isIndexing: false
                }
            }
            return await webviewBridge.getBloomIndexingProgress()
        } catch (error) {
            console.error('[BloomSearchService] Error getting indexing progress:', error)
            return {
                isReady: false,
                isIndexing: false
            }
        }
    }

    async getLineIndexFromLineId(lineId: number): Promise<{ lineIndex: number; bookId: number } | null> {
        try {
            if (!webviewBridge.isAvailable()) {
                console.warn('[BloomSearchService] WebView bridge not available')
                return null
            }

            const result = await webviewBridge.getLineIndexFromLineId(lineId)
            if (result.lineIndex === -1 || result.bookId === -1) {
                return null
            }
            return result
        } catch (error) {
            console.error('[BloomSearchService] Error getting line index:', error)
            return null
        }
    }

    async clearCache(): Promise<void> {
        await bloomSearchCacheService.clear()
    }

    async getCacheStats(): Promise<{ count: number; maxSize: number }> {
        return await bloomSearchCacheService.getStats()
    }
}

export const bloomSearchService = new BloomSearchService()
export { bloomSearchCacheService }
