/**
 * LRU (Least Recently Used) storage manager for localStorage
 * Automatically evicts oldest entries when limit is reached
 * 
 * Features:
 * - Generic key-value storage with automatic eviction
 * - Multiple independent caches with different prefixes
 * - Configurable max entries per cache
 * - Automatic cleanup of orphaned entries
 * - Quota exceeded error handling
 * 
 * Usage:
 * ```ts
 * const cache = new LRUStorage('my-feature-', 500)
 * cache.setItem('key1', 'value1')
 * const value = cache.getItem('key1')
 * ```
 */

interface LRUMetadata {
    [prefix: string]: {
        keys: string[] // Ordered by access time (most recent last)
        lastCleanup: number
    }
}

const METADATA_KEY = '__lru_metadata__'
const CLEANUP_INTERVAL_MS = 60000 // Clean up at most once per minute

export class LRUStorage {
    private prefix: string
    private maxEntries: number

    constructor(prefix: string, maxEntries: number = 1000) {
        this.prefix = prefix
        this.maxEntries = maxEntries
    }

    private getAllMetadata(): LRUMetadata {
        try {
            const raw = localStorage.getItem(METADATA_KEY)
            if (!raw) {
                return {}
            }
            return JSON.parse(raw) as LRUMetadata
        } catch {
            return {}
        }
    }

    private getMetadata(): { keys: string[]; lastCleanup: number } {
        const allMetadata = this.getAllMetadata()
        return allMetadata[this.prefix] || { keys: [], lastCleanup: Date.now() }
    }

    private saveMetadata(metadata: { keys: string[]; lastCleanup: number }): void {
        try {
            const allMetadata = this.getAllMetadata()
            allMetadata[this.prefix] = metadata
            localStorage.setItem(METADATA_KEY, JSON.stringify(allMetadata))
        } catch (e) {
            console.warn('[LRUStorage] Failed to save metadata:', e)
        }
    }

    private updateAccessTime(key: string): void {
        const metadata = this.getMetadata()

        // Remove key if it exists (we'll add it to the end)
        const index = metadata.keys.indexOf(key)
        if (index !== -1) {
            metadata.keys.splice(index, 1)
        }

        // Add to end (most recently used)
        metadata.keys.push(key)

        // Check if we need to evict
        if (metadata.keys.length > this.maxEntries) {
            const toEvict = metadata.keys.shift() // Remove oldest
            if (toEvict) {
                try {
                    localStorage.removeItem(toEvict)
                    console.log('[LRUStorage] 🗑️ EVICTED:', { key: toEvict })
                } catch (e) {
                    console.warn('[LRUStorage] Failed to evict:', toEvict, e)
                }
            }
        }

        this.saveMetadata(metadata)
    }

    private shouldCleanup(): boolean {
        const metadata = this.getMetadata()
        return Date.now() - metadata.lastCleanup > CLEANUP_INTERVAL_MS
    }

    private cleanup(): void {
        const metadata = this.getMetadata()

        // Remove keys that no longer exist in localStorage
        const validKeys = metadata.keys.filter(key => {
            try {
                return localStorage.getItem(key) !== null
            } catch {
                return false
            }
        })

        if (validKeys.length !== metadata.keys.length) {
            console.log('[LRUStorage] 🧹 CLEANUP:', {
                before: metadata.keys.length,
                after: validKeys.length,
                removed: metadata.keys.length - validKeys.length
            })
            metadata.keys = validKeys
            metadata.lastCleanup = Date.now()
            this.saveMetadata(metadata)
        }
    }

    /**
     * Get item from storage and update access time
     */
    getItem(key: string): string | null {
        const fullKey = this.prefix + key

        try {
            const value = localStorage.getItem(fullKey)
            if (value !== null) {
                this.updateAccessTime(fullKey)
            }
            return value
        } catch (e) {
            console.warn('[LRUStorage] Failed to get item:', fullKey, e)
            return null
        }
    }

    /**
     * Set item in storage and update access time
     */
    setItem(key: string, value: string): void {
        const fullKey = this.prefix + key

        try {
            localStorage.setItem(fullKey, value)
            this.updateAccessTime(fullKey)

            // Periodic cleanup
            if (this.shouldCleanup()) {
                this.cleanup()
            }
        } catch (e) {
            console.warn('[LRUStorage] Failed to set item:', fullKey, e)

            // If quota exceeded, force cleanup and retry
            if (e instanceof DOMException && e.name === 'QuotaExceededError') {
                console.log('[LRUStorage] 💾 QUOTA EXCEEDED, forcing cleanup...')
                this.cleanup()

                // Try again after cleanup
                try {
                    localStorage.setItem(fullKey, value)
                    this.updateAccessTime(fullKey)
                } catch (retryError) {
                    console.error('[LRUStorage] Failed even after cleanup:', retryError)
                }
            }
        }
    }

    /**
     * Remove item from storage
     */
    removeItem(key: string): void {
        const fullKey = this.prefix + key

        try {
            localStorage.removeItem(fullKey)

            const metadata = this.getMetadata()
            const index = metadata.keys.indexOf(fullKey)
            if (index !== -1) {
                metadata.keys.splice(index, 1)
                this.saveMetadata(metadata)
            }
        } catch (e) {
            console.warn('[LRUStorage] Failed to remove item:', fullKey, e)
        }
    }

    /**
     * Get current number of tracked entries for this cache
     */
    getSize(): number {
        return this.getMetadata().keys.length
    }

    /**
     * Get total number of entries across all LRU caches
     */
    static getTotalSize(): number {
        try {
            const raw = localStorage.getItem(METADATA_KEY)
            if (!raw) return 0

            const allMetadata = JSON.parse(raw) as LRUMetadata
            return Object.values(allMetadata).reduce((sum, meta) => sum + meta.keys.length, 0)
        } catch {
            return 0
        }
    }

    /**
     * Get statistics for all LRU caches
     */
    static getStats(): { prefix: string; entries: number }[] {
        try {
            const raw = localStorage.getItem(METADATA_KEY)
            if (!raw) return []

            const allMetadata = JSON.parse(raw) as LRUMetadata
            return Object.entries(allMetadata).map(([prefix, meta]) => ({
                prefix,
                entries: meta.keys.length
            }))
        } catch {
            return []
        }
    }

    /**
     * Clear all entries with this prefix
     */
    clear(): void {
        const metadata = this.getMetadata()

        metadata.keys.forEach(key => {
            try {
                localStorage.removeItem(key)
            } catch (e) {
                console.warn('[LRUStorage] Failed to remove during clear:', key, e)
            }
        })

        // Remove this prefix from metadata
        const allMetadata = this.getAllMetadata()
        delete allMetadata[this.prefix]

        try {
            localStorage.setItem(METADATA_KEY, JSON.stringify(allMetadata))
        } catch (e) {
            console.warn('[LRUStorage] Failed to update metadata during clear:', e)
        }

        console.log('[LRUStorage] 🧹 CLEARED:', {
            prefix: this.prefix,
            removed: metadata.keys.length
        })
    }

    /**
     * Clear entries matching a pattern (e.g., all entries for a specific tab)
     */
    clearMatching(pattern: string | RegExp): number {
        const metadata = this.getMetadata()
        const regex = typeof pattern === 'string' ? new RegExp(pattern) : pattern

        let removed = 0
        const keysToKeep: string[] = []

        metadata.keys.forEach(key => {
            // Remove prefix to get the actual key
            const actualKey = key.startsWith(this.prefix) ? key.slice(this.prefix.length) : key

            if (regex.test(actualKey)) {
                try {
                    localStorage.removeItem(key)
                    removed++
                } catch (e) {
                    console.warn('[LRUStorage] Failed to remove during clearMatching:', key, e)
                }
            } else {
                keysToKeep.push(key)
            }
        })

        if (removed > 0) {
            metadata.keys = keysToKeep
            this.saveMetadata(metadata)

            console.log('[LRUStorage] 🧹 CLEARED MATCHING:', {
                prefix: this.prefix,
                pattern: pattern.toString(),
                removed
            })
        }

        return removed
    }

    /**
     * Clear all LRU caches (useful for debugging or reset)
     */
    static clearAll(): void {
        try {
            const raw = localStorage.getItem(METADATA_KEY)
            if (!raw) return

            const allMetadata = JSON.parse(raw) as LRUMetadata
            let totalRemoved = 0

            Object.values(allMetadata).forEach(meta => {
                meta.keys.forEach(key => {
                    try {
                        localStorage.removeItem(key)
                        totalRemoved++
                    } catch (e) {
                        console.warn('[LRUStorage] Failed to remove during clearAll:', key, e)
                    }
                })
            })

            localStorage.removeItem(METADATA_KEY)

            console.log('[LRUStorage] 🧹 CLEARED ALL:', {
                totalRemoved,
                caches: Object.keys(allMetadata).length
            })
        } catch (e) {
            console.warn('[LRUStorage] Failed to clear all:', e)
        }
    }
}
